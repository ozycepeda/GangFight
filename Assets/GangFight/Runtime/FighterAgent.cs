using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace GangFight
{
    public sealed class FighterAgent : Agent
    {
        public const string Behavior = "GangFightFighterV4";
        internal const int OverviewObservationCount = 86;
        internal const int BodyObservationCount = 15;
        internal const int AttackObservationCount = 5;
        internal const int FighterDetailCount = Fighter.BodyCount * BodyObservationCount +
            Fighter.AttackCount * AttackObservationCount;
        public const int ObservationCount = OverviewObservationCount + 4 * FighterDetailCount;
        internal const string ModelFolder = "Policies/V4";
        private Arena arena;
        private Fighter fighter;
        private ModelAsset activeModel;
        private readonly float[] observations = new float[ObservationCount];

        public static FighterAgent Create(Arena arena, Fighter fighter, int team, ModelAsset model = null)
        {
            var host = new GameObject($"Política {fighter.Slot + 1}");
            host.SetActive(false);
            host.transform.SetParent(arena.Root.transform, false);
            var settings = host.AddComponent<BehaviorParameters>();
            settings.BehaviorName = Behavior;
            settings.TeamId = team;
            settings.BrainParameters.VectorObservationSize = ObservationCount;
            settings.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(FighterCommand.Branches);
            if (model)
            {
                settings.Model = model;
                settings.BehaviorType = BehaviorType.InferenceOnly;
                settings.InferenceDevice = InferenceDevice.Burst;
            }

            var agent = host.AddComponent<FighterAgent>();
            agent.arena = arena;
            agent.fighter = fighter;
            agent.activeModel = model;
            agent.MaxStep = 0;
            host.SetActive(true);
            return agent;
        }

        internal void UseModel(ModelAsset model)
        {
            if (activeModel == model)
                return;
            SetModel(Behavior, model, InferenceDevice.Burst);
            activeModel = model;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            WriteObservations(arena, fighter, observations);
            sensor.AddObservation(observations);
        }

        internal static int WriteObservations(Arena arena, Fighter fighter, float[] values)
        {
            var sensor = new ObservationBuffer(values);
            Vector(ref sensor, fighter.Position - arena.Center, arena.HalfSize);
            Vector(ref sensor, fighter.Velocity, 8);
            Heading(ref sensor, fighter.Facing);
            sensor.AddObservation(fighter.Upright);
            sensor.AddObservation(fighter.Grounded);
            sensor.AddObservation(fighter.KoTime > 0);
            sensor.AddObservation(fighter.KoFraction);
            sensor.AddObservation(fighter.LeftGrip);
            sensor.AddObservation(fighter.RightGrip);
            sensor.AddObservation(fighter.Lifting);
            sensor.AddObservation(fighter.ArmsRaised);
            sensor.AddObservation(fighter.CanJump);
            sensor.AddObservation(fighter.CanKick);
            sensor.AddObservation((fighter.PreviousButtons & FighterButtons.Jump) != 0);
            sensor.AddObservation((fighter.PreviousButtons & FighterButtons.Kick) != 0);
            sensor.AddObservation(fighter.HandProgress(0));
            sensor.AddObservation(fighter.HandProgress(1));
            sensor.AddObservation(fighter.CanPunch(0));
            sensor.AddObservation(fighter.CanPunch(1));
            sensor.AddObservation(fighter.CanHeadbutt);
            sensor.AddObservation(fighter.Stagger);
            sensor.AddObservation(fighter.AirBoostAvailable);
            sensor.AddObservation((fighter.PreviousButtons & FighterButtons.Headbutt) != 0);

            for (int i = 0; i < 3; i++)
            {
                Fighter rival = arena.ObservationOpponent(fighter.Slot, i);
                if (rival == null || rival.Eliminated)
                {
                    for (int j = 0; j < 16; j++)
                        sensor.AddObservation(0f);
                    continue;
                }
                sensor.AddObservation(1f);
                Vector(ref sensor, rival.Position - fighter.Position, arena.HalfSize * 2);
                Vector(ref sensor, rival.Velocity - fighter.Velocity, 16);
                Heading(ref sensor, rival.Facing);
                sensor.AddObservation(rival.KoTime > 0);
                sensor.AddObservation(rival.LeftGrip);
                sensor.AddObservation(rival.RightGrip);
                sensor.AddObservation(rival.Grounded);
                sensor.AddObservation(fighter.Holds(rival, 0));
                sensor.AddObservation(fighter.Holds(rival, 1));
                sensor.AddObservation(rival.Stagger);
            }

            Vector3 position = fighter.Position - arena.Center;
            float size = arena.HalfSize;
            sensor.AddObservation(Clamp((size - position.x) / size));
            sensor.AddObservation(Clamp((size + position.x) / size));
            sensor.AddObservation(Clamp((size - position.z) / size));
            sensor.AddObservation(Clamp((size + position.z) / size));

            sensor.AddObservation(arena.FanStrength > 0);
            if (arena.FanStrength > 0)
            {
                Vector3 relative = arena.FanCenter - fighter.Position;
                sensor.AddObservation(Clamp(relative.x / size));
                sensor.AddObservation(Clamp(relative.z / size));
                Heading(ref sensor, arena.FanDirection);
                sensor.AddObservation(Mathf.Clamp01(arena.FanStrength / 18));
            }
            else
                for (int i = 0; i < 5; i++)
                    sensor.AddObservation(0f);

            // Preserve the overview's layout, then append self and rivals in the same order.
            Quaternion worldToView = Quaternion.Inverse(Quaternion.LookRotation(fighter.Facing, Vector3.up));
            WriteDetails(ref sensor, fighter, worldToView);
            for (int i = 0; i < 3; i++)
            {
                Fighter rival = arena.ObservationOpponent(fighter.Slot, i);
                WriteDetails(ref sensor, rival != null && !rival.Eliminated ? rival : null, worldToView);
            }
            return sensor.Count;
        }

        private static void WriteDetails(ref ObservationBuffer sensor, Fighter subject, Quaternion worldToView)
        {
            if (subject == null)
            {
                for (int i = 0; i < FighterDetailCount; i++)
                    sensor.AddObservation(0f);
                return;
            }

            for (int i = 0; i < Fighter.BodyCount; i++)
            {
                Fighter.BodyState body = subject.ReadBody(i);
                Vector(ref sensor, worldToView * (body.Position - subject.Position), 2);
                Quaternion orientation = worldToView * body.Rotation;
                // Two unit axes represent orientation without Euler wrap or quaternion sign flips.
                sensor.AddObservation(orientation * Vector3.forward);
                sensor.AddObservation(orientation * Vector3.up);
                Vector(ref sensor, worldToView * body.Velocity, 16);
                Vector(ref sensor, worldToView * body.AngularVelocity, 24);
            }

            for (int i = 0; i < Fighter.AttackCount; i++)
            {
                Fighter.AttackState attack = subject.ReadAttack(i);
                for (int phase = 0; phase < 4; phase++)
                    sensor.AddObservation((int)attack.Phase == phase);
                sensor.AddObservation(attack.Progress);
            }
        }

        private static float Clamp(float value) => Mathf.Clamp(value, -1, 1);

        private static void Vector(ref ObservationBuffer sensor, Vector3 v, float scale) =>
            sensor.AddObservation(new Vector3(Clamp(v.x / scale), Clamp(v.y / scale), Clamp(v.z / scale)));

        private static void Heading(ref ObservationBuffer sensor, Vector3 direction)
        {
            sensor.AddObservation(direction.x);
            sensor.AddObservation(direction.z);
        }

        private struct ObservationBuffer
        {
            private readonly float[] values;
            public int Count { get; private set; }

            public ObservationBuffer(float[] values)
            {
                this.values = values;
                Count = 0;
            }

            public void AddObservation(float value) => values[Count++] = value;
            public void AddObservation(bool value) => AddObservation(value ? 1f : 0f);
            public void AddObservation(Vector3 v)
            {
                AddObservation(v.x);
                AddObservation(v.y);
                AddObservation(v.z);
            }
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            arena.Command(fighter.Slot, Decode(actions.DiscreteActions));
        }

        internal static FighterCommand Decode(ActionSegment<int> actions)
        {
            FighterButtons buttons = 0;
            for (int i = 2; i < actions.Length; i++)
                if (actions[i] != 0)
                    buttons |= (FighterButtons)(1 << (i - 2));
            return new FighterCommand(FighterCommand.DecodeAxis(actions[0]), FighterCommand.DecodeAxis(actions[1]), buttons);
        }
    }
}
