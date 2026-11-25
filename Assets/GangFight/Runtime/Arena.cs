using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using Unity.MLAgents;
using UnityEngine;

namespace GangFight
{
    public sealed class Arena
    {
        public const int FloorLayer = 8;
        public const int DecisionPeriod = 2;
        private static readonly Vector3 BlockSize = new(.66f, 1.04f, .66f);
        private const float DropY = -2;
        private const float Gamma = .995f;
        private readonly List<(Fighter attacker, Fighter rival, string type, Vector3 direction, bool airborne,
            float impulse, float stagger)> impacts = new();
        private readonly MatchRules rules = new();
        private readonly Fighter[] fighters;
        private readonly FighterAgent[] agents;
        private readonly string[] roles;
        private readonly Dictionary<string, ModelAsset> models = new();
        private readonly int[,] rivals;
        private readonly int[] spawnOrder;
        private readonly Transform floor;
        private readonly Transform fan;
        private readonly System.Random random;
        private readonly RunOptions options;
        private readonly Action<string> output;
        private readonly KeyboardCommands keyboard;
        private readonly CommandReplay replay;
        private float nextRound, potential;
        private int round, roundStart, learnerDecisions;
        private bool wonPreviousRound;

        public GameObject Root { get; }
        public Vector3 Center { get; }
        internal Rigidbody PracticeBlock { get; }
        public int Tick { get; private set; }
        public float HalfSize { get; private set; }
        internal float ThrowMultiplier => options.ThrowMultiplier;
        internal bool IsCarried(Fighter fighter)
        {
            foreach (Fighter carrier in fighters)
                if (carrier.Lifting && carrier.Holds(fighter, 0) && carrier.Holds(fighter, 1))
                    return true;
            return false;
        }
        public Vector3 FanCenter => fan.position;
        public Vector3 FanDirection => fan.forward;
        public float FanStrength { get; private set; }
        public bool Finished => rules.Finished;
        public int Winner => rules.Winner;
        public int AliveMask => rules.Remaining;
        public int Round => round;
        public int Count => fighters.Length;
        public Fighter Get(int slot) => fighters[slot];
        public bool HasPolicy => models.ContainsKey("model:Champion");
        public string RivalRole => roles[1];

        internal Arena(Transform parent, Vector3 center, RunOptions options, int arenaIndex, Action<string> output,
            KeyboardCommands keyboard = null, CommandReplay replay = null)
        {
            this.options = options;
            this.keyboard = keyboard;
            this.replay = replay;
            this.output = output;
            Center = center;
            random = new System.Random(unchecked(options.Seed + arenaIndex * 1087));
            Root = new GameObject($"Arena {arenaIndex + 1}");
            Root.transform.SetParent(parent, false);
            Root.transform.position = center;

            floor = new GameObject("Plataforma").transform;
            floor.SetParent(Root.transform, false);
            floor.gameObject.layer = FloorLayer;
            floor.gameObject.AddComponent<BoxCollider>();
            if (Visuals.Enabled)
            {
                Visuals.Part("Superficie", PrimitiveType.Cube, floor,
                    Vector3.zero, Vector3.one, Visuals.Paint(new Color(.21f, .32f, .39f)));
                Material warning = Visuals.Paint(new Color(.94f, .62f, .19f));
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Visuals.Part("Borde", PrimitiveType.Cube, floor, new(sign * .487f, .503f, 0), new(.024f, .007f, 1), warning);
                    Visuals.Part("Borde", PrimitiveType.Cube, floor, new(0, .503f, sign * .487f), new(1, .007f, .024f), warning);
                }
            }

            fan = new GameObject("Ventilador").transform;
            fan.SetParent(Root.transform, false);
            if (Visuals.Enabled)
            {
                Visuals.Part("Conducto", PrimitiveType.Cylinder, fan, new(0, .45f, -1.55f), new(.61f, .15f, .61f), Visuals.Ink)
                    .transform.localRotation = Quaternion.Euler(90, 0, 0);
                var fanTrim = Visuals.Paint(new Color(.43f, .85f, .88f));
                Visuals.Part("Rejilla", PrimitiveType.Cylinder, fan, new(0, .45f, -1.38f), new(.46f, .04f, .46f), fanTrim)
                    .transform.localRotation = Quaternion.Euler(90, 0, 0);
                for (int stripe = -1; stripe <= 1; stripe++)
                    Visuals.Part("Zona de aire", PrimitiveType.Cube, fan,
                        new(stripe * .32f, .012f, 0), new(.05f, .016f, 2.5f), fanTrim);
            }

            if (options.Session || options.InteractiveLayout || options.Smoke)
            {
                var block = new GameObject("Bloque agarrable");
                block.transform.SetParent(Root.transform, false);
                block.AddComponent<BoxCollider>().size = BlockSize;
                PracticeBlock = block.AddComponent<Rigidbody>();
                PracticeBlock.mass = 3;
                PracticeBlock.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                Visuals.Part("Bloque", PrimitiveType.Cube, block.transform, Vector3.zero, BlockSize,
                    Visuals.Paint(new Color(.53f, .57f, .71f)));
            }

            fighters = new Fighter[options.Fighters];
            agents = new FighterAgent[fighters.Length];
            roles = (string[])options.Roles.Clone();
            spawnOrder = new int[fighters.Length];
            rivals = new int[fighters.Length, fighters.Length - 1];
            foreach (string role in roles)
                LoadRequiredModel(role);
            foreach (string role in options.OpponentPool)
                LoadRequiredModel(role);
            if (keyboard != null && !HasPolicy)
            {
                var champion = Resources.Load<ModelAsset>(FighterAgent.ModelFolder + "/Champion");
                if (champion)
                    models.Add("model:Champion", champion);
            }

            for (int i = 0; i < fighters.Length; i++)
            {
                Color color = Visuals.ColorFor(i);
                fighters[i] = new Fighter(this, i, Visuals.Paint(color),
                    Visuals.Paint(Color.Lerp(color, new Color(.99f, .96f, .92f), .49f)));
                if (roles[i] == "learn")
                    agents[i] = FighterAgent.Create(this, fighters[i], options.SelfPlay ? i : 0);
                else
                {
                    models.TryGetValue(roles[i], out var model);
                    if (!model && i == 1 && keyboard != null)
                        models.TryGetValue("model:Champion", out model);
                    foreach (string candidate in options.OpponentPool)
                        if (!model && models.TryGetValue(candidate, out ModelAsset alternative))
                            model = alternative;
                    if (model)
                        agents[i] = FighterAgent.Create(this, fighters[i], i, model);
                }
            }
            Reset();
        }

        private void LoadRequiredModel(string role)
        {
            if (!role.StartsWith("model:", StringComparison.Ordinal) || models.ContainsKey(role))
                return;
            var model = Resources.Load<ModelAsset>($"{FighterAgent.ModelFolder}/{role.Substring(6)}");
            if (!model)
                throw new InvalidOperationException($"Falta el modelo para {role} en Resources/{FighterAgent.ModelFolder}.");
            models.Add(role, model);
        }

        private void AssignRole(int slot, string role)
        {
            if (roles[slot] == role)
                return;
            roles[slot] = role;
            if (models.TryGetValue(role, out ModelAsset model))
                agents[slot].UseModel(model);
        }

        public Fighter ObservationOpponent(int slot, int index) =>
            index < fighters.Length - 1 ? fighters[rivals[slot, index]] : null;

        public void SetRival(string role)
        {
            if (keyboard == null)
                return;
            InterruptOpenAgents();
            AssignRole(1, role);
            Reset();
        }

        public void Restart()
        {
            InterruptOpenAgents();
            Reset();
        }

        private void InterruptOpenAgents()
        {
            for (int i = 0; i < agents.Length; i++)
                if (UsesAgent(i) && !fighters[i].Eliminated && !Finished)
                    agents[i].EpisodeInterrupted();
        }

        public void Reset()
        {
            round++;
            roundStart = Tick;
            rules.Start(fighters.Length);
            HalfSize = Mathf.Clamp(Parameter("arena_half_size", options.HalfSize), 2.7f, 8);
            floor.localPosition = Vector3.down * .46f;
            floor.localScale = new Vector3(HalfSize * 2, .92f, HalfSize * 2);
            FanStrength = Mathf.Clamp(Parameter("fan_strength", options.FanStrength), 0, 18);
            fan.gameObject.SetActive(FanStrength > 0);
            fan.localPosition = new Vector3(-HalfSize * .43f, 0, HalfSize * .45f);
            fan.localRotation = Quaternion.Euler(0, options.Session || options.InteractiveLayout ? 90 : (round % 2 == 0 ? 90 : 180), 0);
            impacts.Clear();

            int offset = options.Session || options.InteractiveLayout ? 0 : (round - 1 + options.SeatOffset) % fighters.Length;
            for (int i = 0; i < fighters.Length; i++)
                spawnOrder[i] = (i + offset) % fighters.Length;
            if (options.OpponentPool.Length > 0)
                for (int slot = 1; slot < fighters.Length; slot++)
                    AssignRole(slot, options.OpponentPool[(slot + offset + round / fighters.Length - 1) % options.OpponentPool.Length]);
            float baseAngle = options.Session || options.InteractiveLayout ? Mathf.PI / 3 : (float)random.NextDouble() * Mathf.PI;
            float distance = Mathf.Min(1.85f, HalfSize * .48f);
            // Detach every grip before moving any of the bodies it might be connected to.
            foreach (Fighter fighter in fighters)
                fighter.PrepareForRoundReset();
            for (int i = 0; i < fighters.Length; i++)
            {
                float angle = baseAngle + spawnOrder[i] * (2 * Mathf.PI / fighters.Length);
                var away = new Vector3(Mathf.Sin(angle), 0, -Mathf.Cos(angle));
                fighters[i].Reset(Center + away * distance, -away);
                for (int j = 0, index = 0; j < fighters.Length; j++)
                    if (i != j)
                        rivals[i, index++] = j;
                for (int j = fighters.Length - 2; j > 0; j--)
                {
                    int swap = random.Next(j + 1);
                    (rivals[i, j], rivals[i, swap]) = (rivals[i, swap], rivals[i, j]);
                }
            }
            if (PracticeBlock)
            {
                PracticeBlock.gameObject.SetActive(true);
                PracticeBlock.position = Center + new Vector3(HalfSize * .63f, BlockSize.y * .5f + .01f, 0);
                PracticeBlock.rotation = Quaternion.identity;
                PracticeBlock.linearVelocity = Vector3.zero;
                PracticeBlock.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
            keyboard?.Clear();
            nextRound = 0;
            potential = Potential();
            wonPreviousRound = false;
            Record("round_start", -1);
            RecordLayout();
        }

        public void Prepare()
        {
            if (Finished || options.Smoke)
                return;
            if ((Tick - roundStart) % DecisionPeriod != 0)
                return;
            ShapeReward(false);
            for (int i = 0; i < fighters.Length; i++)
            {
                if (fighters[i].Eliminated)
                    continue;

                if (replay != null)
                    Command(i, replay.Read(Tick - roundStart, i, options.ReplayAgent));
                else if (UsesAgent(i))
                    agents[i].RequestDecision();
                else
                    Command(i, roles[i] == "keyboard" ? keyboard.Read() : Bot(i));
            }
        }

        public void Apply(float dt)
        {
            if (Finished)
                return;
            foreach (Fighter fighter in fighters)
            {
                fighter.Step(dt);
                if (!fighter.Eliminated && FanStrength > 0)
                    fighter.ApplyFan(FanCenter, FanDirection, FanStrength);
            }
        }

        public void Resolve(float dt)
        {
            if (Finished)
            {
                Tick++;
                if (!options.Evaluate && !options.Smoke && (nextRound += dt) >= (options.Session ? 2.5f : 0))
                    Reset();
                return;
            }

            foreach (var hit in impacts)
                hit.rival.ApplyStrike(hit.attacker, hit.type, hit.direction, hit.airborne, hit.impulse, hit.stagger);
            impacts.Clear();
            Tick++;

            if (PracticeBlock && PracticeBlock.gameObject.activeSelf && PracticeBlock.position.y < Center.y + DropY)
            {
                foreach (Fighter fighter in fighters)
                    fighter.ReleaseBlock();
                PracticeBlock.gameObject.SetActive(false);
            }

            int fallen = 0;
            foreach (Fighter fighter in fighters)
                if (!fighter.Eliminated && fighter.Position.y < Center.y + DropY)
                    fallen |= 1 << fighter.Slot;
            bool limit = !options.Session && Tick - roundStart >= Mathf.CeilToInt(options.Timeout / dt);
            rules.Resolve(fallen, limit);
            bool learnerEnded = rules.Finished || (rules.LastEliminated & 1) != 0;
            if (learnerEnded && !fighters[0].Eliminated)
                ShapeReward(!rules.Interrupted);

            for (int i = 0; i < fighters.Length; i++)
            {
                bool fell = (rules.LastEliminated & (1 << i)) != 0;
                if (fighters[i].Eliminated || !UsesAgent(i) || !(fell || Finished))
                    continue;
                agents[i].AddReward(rules.Reward(i));
                if (rules.Interrupted && !fell)
                    agents[i].EpisodeInterrupted();
                else
                    agents[i].EndEpisode();
            }

            foreach (Fighter fighter in fighters)
            {
                if ((rules.LastEliminated & (1 << fighter.Slot)) == 0)
                    continue;
                int credited = fighter.EjectionCredit(Tick);
                foreach (Fighter survivor in fighters)
                    survivor.ReleaseFrom(fighter);
                Record("elimination", fighter.Slot, credited);
                fighter.Eliminate();
            }

            if (Finished && !wonPreviousRound)
            {
                wonPreviousRound = true;
                string outcome = rules.Interrupted ? "timeout" : Winner < 0 ? "draw" : "winner";
                Record(outcome, Winner);
                if (Academy.IsInitialized && Academy.Instance.IsCommunicatorOn)
                {
                    var metrics = Academy.Instance.StatsRecorder;
                    metrics.Add("GangFight/Timeout", rules.Interrupted ? 1 : 0);
                    metrics.Add("GangFight/RoundSeconds", (Tick - roundStart) * dt);
                    if (!options.SelfPlay)
                        metrics.Add("GangFight/LearnerWin", Winner == 0 ? 1 : 0);
                }
            }
        }

        private FighterCommand Bot(int slot)
        {
            string role = roles[slot];
            if (role == "passive")
                return default;
            if (role == "random")
                return new FighterCommand(random.Next(-1, 2), random.Next(-1, 2), (FighterButtons)random.Next(64));

            Fighter fighter = fighters[slot];
            Fighter target = null;
            float distance = float.PositiveInfinity;
            foreach (Fighter rival in fighters)
            {
                if (rival == fighter || rival.Eliminated)
                    continue;
                float squared = (rival.Position - fighter.Position).sqrMagnitude;
                if (squared >= distance)
                    continue;
                target = rival;
                distance = squared;
            }

            if (target == null)
                return default;
            Vector3 move = target.Position - fighter.Position;
            FighterButtons buttons = 0;
            if (role == "edge")
            {
                Vector3 center = Center - fighter.Position;
                if (fighter.LeftGrip || fighter.RightGrip)
                {
                    Vector3 edge = target.Position - Center;
                    move = edge.sqrMagnitude > .5f ? edge : -center;
                    if (Mathf.Max(Mathf.Abs(center.x), Mathf.Abs(center.z)) < HalfSize - .9f)
                        buttons |= FighterButtons.LeftGrip | FighterButtons.RightGrip;
                    else
                        move = center;
                }
                else if (Mathf.Max(Mathf.Abs(center.x), Mathf.Abs(center.z)) > HalfSize - .78f)
                    move = center;
                else if (distance < 1.95f)
                    buttons |= FighterButtons.LeftGrip | FighterButtons.RightGrip;
            }
            int beat = ((Tick - roundStart) / DecisionPeriod) % 36;
            if (distance < 1.65f)
            {
                if (beat == 0)
                    buttons |= FighterButtons.Kick;
                if (beat == 12 && distance < 1.1f)
                    buttons |= FighterButtons.Headbutt;
                if (beat == 24 && role == "chase")
                    buttons |= FighterButtons.LeftGrip;
            }
            return new FighterCommand(Math.Abs(move.x) > .20f ? Math.Sign(move.x) : 0,
                Math.Abs(move.z) > .20f ? Math.Sign(move.z) : 0, buttons);
        }

        private bool UsesAgent(int i) =>
            agents[i] && (roles[i] == "learn" || roles[i].StartsWith("model:", StringComparison.Ordinal));

        private float Parameter(string key, float defaultValue) =>
            options.Learning ? Academy.Instance.EnvironmentParameters.GetWithDefault(key, defaultValue) : defaultValue;

        private float Potential()
        {
            Vector3 p = fighters[1].Position - Center;
            return Mathf.Clamp01(Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) / HalfSize);
        }

        private void ShapeReward(bool terminal)
        {
            if (!options.Shaping || fighters[0].Eliminated)
                return;
            float next = terminal ? 0 : Potential();
            if (Tick > roundStart)
                agents[0].AddReward(.10f * (Gamma * next - potential));
            potential = next;
        }

        internal void Command(int slot, FighterCommand command)
        {
            fighters[slot].SetCommand(command);
            if (roles[slot] == "learn")
                learnerDecisions++;
            if (options.RecordCommands)
            {
                Vector3 position = fighters[slot].Position - Center;
                output($"{{\"type\":\"command\",\"arena\":{Root.GetInstanceID()},\"round\":{round}," +
                    $"\"combat_version\":{FighterCommand.CombatVersion},\"elapsed_ticks\":{Tick - roundStart},\"slot\":{slot}," +
                    $"\"x\":{command.X},\"z\":{command.Z},\"buttons\":{(int)command.Buttons}," +
                    FormattableString.Invariant($"\"position\":[{position.x:R},{position.y:R},{position.z:R}]}}"));
            }
        }

        internal void CancelPlayerInput(int slot)
        {
            if (roles[slot] == "keyboard")
                keyboard?.Clear();
        }

        internal void QueueImpact(Fighter attacker, Fighter rival, string type, Vector3 direction, bool airborne,
            float impulse, float stagger)
        {
            if (!Finished)
                impacts.Add((attacker, rival, type, direction, airborne, impulse, stagger));
        }

        internal void RecordImpact(string type, int slot, int other, bool airborne, float impulse,
            float before, float after, bool knockout)
        {
            output?.Invoke(FormattableString.Invariant(
                $"{{\"type\":\"{type}\",\"arena\":{Root.GetInstanceID()},\"round\":{round},\"tick\":{Tick},\"elapsed_ticks\":{Tick - roundStart},\"slot\":{slot},\"other\":{other},\"airborne\":{(airborne ? "true" : "false")},\"impulse\":{impulse:R},\"stagger_before\":{before:R},\"stagger_after\":{after:R},\"ko\":{(knockout ? "true" : "false")}}}"));
        }

        internal int TakeLearnerDecisions()
        {
            int count = learnerDecisions;
            learnerDecisions = 0;
            return count;
        }

        private void RecordLayout()
        {
            if (output != null)
                output($"{{\"type\":\"layout\",\"arena\":{Root.GetInstanceID()},\"round\":{round}," +
                    $"\"order\":[{string.Join(",", spawnOrder)}]}}");
        }

        public void Record(string type, int slot, int other = -1)
        {
            if (output != null)
                output($"{{\"type\":\"{type}\",\"arena\":{Root.GetInstanceID()},\"round\":{round}," +
                    $"\"tick\":{Tick},\"elapsed_ticks\":{Tick - roundStart},\"slot\":{slot},\"other\":{other}}}");
        }
    }
}
