using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GangFight
{
    internal sealed class CombatSmoke
    {
        private readonly Arena arena;
        private readonly RunOptions options = new() { Smoke = true, Roles = new[] { "passive", "passive" } };
        private readonly List<string> events = new();
        private Fighter Player => arena.Get(0);
        private Fighter Rival => arena.Get(1);

        internal CombatSmoke(Transform parent)
        {
            arena = new Arena(parent, new Vector3(0, 0, 42), options, 2, events.Add);
        }

        internal void Run()
        {
            Require(Player.Root.GetComponentsInChildren<ConfigurableJoint>().Length == 5, "five joints");
            float mass = Player.Root.GetComponentsInChildren<Rigidbody>().Sum(body => body.mass);
            Require(Mathf.Abs(mass - 8.60f) < .001f, "lighter rig mass");
            Require(Mathf.Abs(4 * arena.HalfSize * arena.HalfSize - 150f) < .001f, "arena area is 150 m²");
            foreach (BoxCollider boot in Player.Root.GetComponentsInChildren<BoxCollider>())
            {
                var legJoint = boot.attachedRigidbody.GetComponent<ConfigurableJoint>();
                float length = legJoint.anchor.y - boot.transform.localPosition.y + boot.size.y / 2;
                Require(Mathf.Abs(length - .93f * .70f) < .001f, "hip-to-sole length is 30 percent shorter");
            }

            CheckStrike("punch_left", FighterButtons.LeftGrip, true);
            CheckStrike("punch_right", FighterButtons.RightGrip, true);
            CheckStrike("headbutt", FighterButtons.Headbutt, false);
            CheckStrike("kick", FighterButtons.Kick, false);
            CheckAir();
            CheckRaisedArms();
            CheckTurn();
            CheckHeadbuttLunge();
            CheckGrabs();
            CheckOverheadCarry();
            CheckThrows();
            CheckKnockout();
            CheckRoundReset();
            CheckObservations();
            Debug.Log("GANGFIGHT_COMBAT_OK");
        }

        private float[] Capture(Fighter observer)
        {
            var values = new float[FighterAgent.ObservationCount];
            Require(FighterAgent.WriteObservations(arena, observer, values) == values.Length,
                "V4 observation layout length");
            foreach (float value in values)
                Require(!float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= 1.00001f,
                    "observations are finite and bounded");
            return values;
        }

        private void CheckObservations()
        {
            Place(4.6f);
            Require(FighterAgent.ObservationCount == 526, "V4 contract has 526 observations");
            Rigidbody arm = Player.Root.GetComponentsInChildren<Rigidbody>().Single(body => body.name == "Brazo 0");
            arm.position = Player.Position + new Vector3(-.4f, .3f, .2f);
            arm.rotation = Quaternion.Euler(12, 45, 0);
            arm.linearVelocity = new Vector3(4, 8, -16);
            arm.angularVelocity = new Vector3(6, 12, -24);
            float[] values = Capture(Player);
            int index = FighterAgent.OverviewObservationCount + 2 * FighterAgent.BodyObservationCount;
            Require(Mathf.Abs(values[index] + .2f) < .0001f &&
                Mathf.Abs(values[index + 1] - .15f) < .0001f && Mathf.Abs(values[index + 2] - .1f) < .0001f,
                "left arm position is relative to its trunk");
            Require(Mathf.Abs(values[index + 9] - .25f) < .0001f &&
                Mathf.Abs(values[index + 10] - .5f) < .0001f && Mathf.Abs(values[index + 11] + 1) < .0001f,
                "limb linear velocity is observable");
            Require(Mathf.Abs(values[index + 12] - .25f) < .0001f &&
                Mathf.Abs(values[index + 13] - .5f) < .0001f && Mathf.Abs(values[index + 14] + 1) < .0001f,
                "limb angular velocity is observable");
            Vector3 forward = arm.rotation * Vector3.forward;
            Require(Vector3.Distance(new Vector3(values[index + 3], values[index + 4], values[index + 5]), forward) < .0001f,
                "limb orientation uses the fighter's viewing frame");

            Vector3 translation = new(42, 3, -42);
            foreach (Fighter fighter in new[] { Player, Rival })
                foreach (Rigidbody body in fighter.Root.GetComponentsInChildren<Rigidbody>())
                    body.position += translation;
            float[] translated = Capture(Player);
            for (int i = FighterAgent.OverviewObservationCount; i < values.Length; i++)
                Require(Mathf.Abs(values[i] - translated[i]) < .0001f,
                    "detailed state is independent of arena world position");

            int absent = FighterAgent.OverviewObservationCount + 2 * FighterAgent.FighterDetailCount;
            Require(values.Skip(absent).All(value => value == 0), "unused rival slots contain zeros");
            Rival.Eliminate();
            values = Capture(Player);
            Require(values.Skip(FighterAgent.OverviewObservationCount + FighterAgent.FighterDetailCount)
                .All(value => value == 0), "eliminated rival details contain zeros");

            var buttons = new[] { FighterButtons.LeftGrip, FighterButtons.RightGrip, FighterButtons.Headbutt, FighterButtons.Kick };
            int[] windups = { 2, 2, 4, 5 }, active = { 7, 7, 9, 7 }, durations = { 13, 13, 24, 22 };
            for (int attack = 0; attack < buttons.Length; attack++)
            {
                Place(4.6f);
                arena.Command(0, new FighterCommand(0, 0, buttons[attack]));
                Require(Player.ReadAttack(attack).Phase == Fighter.AttackPhase.Idle,
                    "queued controls do not reveal a future attack");
                Advance(buttons[attack], 1);
                if (attack < 2)
                    Advance(0, 1); // A short release starts a punch.
                for (int elapsed = 1; elapsed <= durations[attack]; elapsed++)
                {
                    var expected = elapsed >= durations[attack] ? Fighter.AttackPhase.Idle
                        : elapsed < windups[attack] ? Fighter.AttackPhase.Windup
                        : elapsed < windups[attack] + active[attack] ? Fighter.AttackPhase.Active
                        : Fighter.AttackPhase.Recovery;
                    float progress = expected == Fighter.AttackPhase.Idle ? 0 : (float)elapsed / durations[attack];
                    CheckObservedAttack(Player, 0, attack, expected, progress);
                    CheckObservedAttack(Rival, 1, attack, expected, progress);
                    if (elapsed < durations[attack])
                        Advance(attack < 2 ? 0 : buttons[attack], 1);
                }
            }
            Place(4.6f);
            Advance(FighterButtons.Headbutt, 1);
            Player.ReceiveImpact(10);
            for (int attack = 0; attack < Fighter.AttackCount; attack++)
                CheckObservedAttack(Rival, 1, attack, Fighter.AttackPhase.Idle, 0);
            Place(4.6f);
            Debug.Log("GANGFIGHT_OBSERVATIONS_V4_OK");
        }

        private void CheckObservedAttack(Fighter observer, int detailSlot, int attack, Fighter.AttackPhase phase, float progress)
        {
            float[] values = Capture(observer);
            int offset = FighterAgent.OverviewObservationCount + detailSlot * FighterAgent.FighterDetailCount +
                Fighter.BodyCount * FighterAgent.BodyObservationCount + attack * FighterAgent.AttackObservationCount;
            for (int i = 0; i < 4; i++)
                Require(values[offset + i] == (i == (int)phase ? 1 : 0), "current attack phase is visible to both fighters");
            Require(Mathf.Abs(values[offset + 4] - progress) < .0001f, "current attack progress is observable");
        }

        private void CheckRaisedArms()
        {
            Place(3.2f);
            var shapes = Player.Root.GetComponentsInChildren<SphereCollider>();
            var fists = shapes.Where(shape => shape.name == "Puño").ToArray();
            SphereCollider helmet = shapes.Single(shape => shape.name == "Casco");
            float restingHeight = fists.Max(fist => fist.bounds.center.y);
            float restingHelmet = helmet.bounds.max.y;
            Advance(FighterButtons.RaiseArms, 36);
            float raisedHeight = fists.Min(fist => fist.bounds.center.y);
            Debug.Log($"GangFight combat · fists rise to {raisedHeight:F2} m, helmet top {helmet.bounds.max.y:F2} m, upright {Player.Upright:F2}");
            Require(Player.ArmsRaised && raisedHeight > helmet.bounds.max.y &&
                helmet.bounds.max.y > restingHelmet - .16f && Player.Upright > .9f,
                "I raises both fists above the helmet");
            Advance(0, 24);
            Require(!Player.ArmsRaised && fists.Max(fist => fist.bounds.center.y) < restingHeight + .12f,
                "release I lowers both arms");
            Require(Count("headbutt") == 0 && Count("kick") == 0, "raising arms does not attack");
        }

        private void CheckTurn()
        {
            Place(4.6f);
            Advance(0, 42, 1);
            Require(Player.Velocity.x is > 3.6f and < 5.3f, "steady run stays within the original speed");
            Advance(0, 14, -1);
            Require(Player.Velocity.x < -.35f && Player.Facing.x < -.95f,
                "a reversal quickly changes both heading and movement");
            Vector3 facing = Player.ReadBody(0).Rotation * Vector3.forward;
            Debug.Log($"GangFight combat · unloaded heading after 0.28 s {facing}");
            Require(Vector3.Dot(facing, Vector3.left) > .82f, "unloaded reversal physically turns the trunk");
        }

        private void CheckHeadbuttLunge()
        {
            Place(4.6f);
            Vector3 origin = Player.Position;
            var trunk = Player.Root.GetComponentsInChildren<Rigidbody>().First(body => body.name == "Tronco");
            Advance(FighterButtons.Headbutt, 10);
            float distance = Player.Position.z - origin.z;
            Require(distance > .46f && Player.Velocity.z > 2.8f, "headbutt drives the whole body further forward");
            Require((trunk.rotation * Vector3.forward).y < -.33f, "the body pitches further forward into the headbutt");
            Advance(FighterButtons.Headbutt, 47);
            float recoveredFrom = Player.Position.z;
            Advance(FighterButtons.Headbutt, 20, 0, 1);
            Require(Player.Position.z - recoveredFrom > .3f && Player.KoTime == 0,
                "fighter walks again after a missed headbutt");
            Require(Count("headbutt") == 1 && Count("headbutt_hit") == 0, "a missed headbutt does not repeat when held");
            Debug.Log($"GangFight combat · headbutt lunges {distance:F2} m in 0.20 s and movement resumes");
            Place(1.15f);
            Advance(FighterButtons.Headbutt, 32);
            Require(Count("headbutt_hit") == 1, "headbutt connects from farther away");
        }

        private void CheckStrike(string attack, FighterButtons button, bool punch)
        {
            var distances = new[] { .74f, .86f, .62f, .98f };
            foreach (float distance in distances)
            {
                Place(distance);
                Advance(button, punch ? 2 : 32);
                if (punch)
                    Advance(0, 30);
                Require(Count(attack) == 1, attack + " triggers once");
                if (Count(attack + "_hit") != 0)
                {
                    Require(Count(attack + "_hit") == 1, attack + " counts one impact per rival");
                    Require(Rival.LatestHit == attack, attack + " identifies the impact");
                    Debug.Log($"GangFight combat · {attack} connects at {distance:F2} m");
                    return;
                }
            }
            Require(false, attack + " connects on contact");
        }

        private void CheckGrabs()
        {
            Place(.86f);
            float highestFeet = 0;
            var boots = Rival.Root.GetComponentsInChildren<BoxCollider>();
            for (int i = 0; i < 25; i++)
            {
                Advance(FighterButtons.LeftGrip | FighterButtons.RightGrip, 2);
                if (Player.Lifting)
                    highestFeet = Mathf.Max(highestFeet, boots.Min(boot => boot.bounds.min.y - arena.Center.y));
            }
            Debug.Log($"GangFight combat · rival lifts {highestFeet:F2} m");
            Require(Count("lift_with_grip") >= 1 && highestFeet > .09f, "two hands lift a rival clear of the floor");
            Require(Player.LeftGrip && Player.RightGrip, "both hands retain the rival");
            Debug.Log($"GangFight combat · carry starts upright {Player.Upright:F2}");
            float carriedFrom = Rival.Position.z;
            Advance(FighterButtons.LeftGrip | FighterButtons.RightGrip, 25, 0, -1);
            Vector3 carryFacing = Player.ReadBody(0).Rotation * Vector3.forward;
            Vector3 carryOffset = Rival.Position - Player.Position;
            Debug.Log($"GangFight combat · loaded heading {carryFacing}, rival offset {carryOffset}");
            Require(Player.Lifting && Rival.Position.z < carriedFrom - .1f, "walk while carrying a rival");
            Require(Player.Facing.z < -.95f && Vector3.Dot(carryFacing, Vector3.back) > .65f,
                "the trunk turns in the commanded direction while carrying");
            Require(Rival.Position.z < Player.Position.z - .15f, "the carried rival follows around the trunk");
            Advance(FighterButtons.RightGrip, 2);
            Require(!Player.LeftGrip && Player.RightGrip && Count("punch_left") == 0, "one hand releases without a punch");

            Place(3.2f);
            var block = arena.PracticeBlock;
            block.position = new Vector3(Player.Position.x, arena.Center.y + .53f, Player.Position.z + .74f);
            block.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            Advance(FighterButtons.LeftGrip | FighterButtons.RightGrip, 46);
            Debug.Log($"GangFight combat · block rises to {block.position.y - arena.Center.y:F2} m");
            Require(Player.LeftGrip && Player.RightGrip && Player.Lifting, "both hands grip the practice block");
            Require(block.position.y > arena.Center.y + .68f, "block rises off the platform");
            float carryHeight = block.position.y;
            Advance(FighterButtons.LeftGrip | FighterButtons.RightGrip | FighterButtons.RaiseArms, 28);
            Debug.Log($"GangFight combat · manual lift raises the block from {carryHeight:F2} m to {block.position.y:F2} m");
            Require(Player.LeftGrip && Player.RightGrip && block.position.y > carryHeight + .1f,
                "I raises a held object higher than the automatic lift");
        }

        private void CheckThrows()
        {
            float natural = ThrowBlock(1);
            float boosted = ThrowBlock(1.2f);
            options.ThrowMultiplier = RunOptions.DefaultThrowMultiplier;
            Debug.Log($"GangFight combat · throw tangential speed {natural:F2} m/s → {boosted:F2} m/s");
            Require(natural > .25f && boosted > natural * 1.12f, "released object gets the reported boost");

            Place(.86f);
            var bothHands = FighterButtons.LeftGrip | FighterButtons.RightGrip;
            Advance(bothHands, 40);
            Require(Player.Lifting, "rival is held before the spin");
            Advance(bothHands, 6, 1);
            Advance(0, 1, 1);
            Require(!Player.LeftGrip && !Player.RightGrip && Count("throw") == 1,
                "a rival is released as a single target during the spin");
        }

        private void CheckOverheadCarry()
        {
            Place(.86f);
            var overhead = FighterButtons.LeftGrip | FighterButtons.RightGrip | FighterButtons.RaiseArms;
            Advance(overhead, 40);
            Require(Player.Lifting && Player.ArmsRaised, "rival is held with arms raised");
            Advance(overhead, 18, 1);
            Require(Vector3.Dot(Player.ReadBody(0).Rotation * Vector3.forward, Vector3.right) > .7f &&
                Player.Lifting, "the fighter turns east while raising a rival");
            float carriedFrom = Rival.Position.z;
            Advance(overhead, 24, 0, 1);
            Require(Vector3.Dot(Player.ReadBody(0).Rotation * Vector3.forward, Vector3.forward) > .7f &&
                Player.Lifting && Rival.Position.z > carriedFrom + .15f, "W turns the fighter north and carries the rival along");
        }

        private float ThrowBlock(float multiplier)
        {
            options.ThrowMultiplier = multiplier;
            Place(3.2f);
            var block = arena.PracticeBlock;
            block.position = new Vector3(Player.Position.x, arena.Center.y + .53f, Player.Position.z + .74f);
            block.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            Advance(FighterButtons.LeftGrip | FighterButtons.RightGrip, 37);
            Require(Player.Lifting, "the block is held before the throw");
            Advance(FighterButtons.LeftGrip | FighterButtons.RightGrip, 5, 1);
            Advance(FighterButtons.RightGrip, 1, 1);
            Require(!Player.LeftGrip && Player.RightGrip && Count("throw") == 0, "one hand releases without a throw");
            Vector3 offset = block.worldCenterOfMass - Player.Position;
            Vector3 tangent = Vector3.Cross(Vector3.up, offset).normalized;
            Advance(0, 1, 1);
            Require(!Player.RightGrip && Count("throw") == (multiplier > 1 ? 1 : 0), "boost applies once on last release");
            return Vector3.Dot(block.linearVelocity - Player.Velocity, tangent);
        }

        private void CheckAir()
        {
            Place(3.2f);
            float jumpStart = Player.Position.y;
            float highest = jumpStart;
            for (int i = 0; i < 34; i++)
            {
                Advance(i == 0 ? FighterButtons.Jump : 0, 1);
                highest = Mathf.Max(highest, Player.Position.y);
            }
            Debug.Log($"GangFight combat · jump rises {highest - jumpStart:F2} m");
            Require(highest > jumpStart + 1.18f, "jump reaches the higher apex");

            Place(3.2f);
            Vector3 origin = Player.Position;
            Advance(FighterButtons.Jump | FighterButtons.Kick, 6, -1, 1);
            Require(!Player.Grounded && Player.Position.y > origin.y + .15f, "kick starts during a jump");
            Require(Player.Velocity.x < -.6f && Player.Velocity.z > .6f, "diagonal air impulse");
            Advance(0, 4, -1, 1);
            var trunk = Player.Root.GetComponentsInChildren<Rigidbody>().First(body => body.name == "Tronco");
            float backTilt = (trunk.rotation * Vector3.forward).y;
            Debug.Log($"GangFight combat · airborne backward pitch {Mathf.Asin(backTilt) * Mathf.Rad2Deg:F1}°");
            Require(backTilt > .21f, "the trunk tilts backward during the aerial kick");
            Advance(FighterButtons.Headbutt, 2, -1, 1);
            Advance(FighterButtons.LeftGrip, 2, -1, 1);
            Advance(0, 4, -1, 1);
            Advance(FighterButtons.RightGrip, 12, -1, 1);
            Require(Count("kick") == 1 && Count("headbutt") == 1 && Count("punch_left") == 1 &&
                Player.HandProgress(1) == 1 && Count("air_boost") == 1,
                "air attacks and grip share one impulse per jump");
            Require(Count("kick_hit") == 0 && Count("headbutt_hit") == 0 && Count("punch_left_hit") == 0,
                "missed strikes cause no impact");

            foreach (float distance in new[] { 1.05f, 1.2f, .9f, 1.35f })
            {
                Place(distance);
                Advance(FighterButtons.Jump | FighterButtons.Kick, 19, 0, 1);
                if (events.Any(line => line.StartsWith("{\"type\":\"kick_hit\",", StringComparison.Ordinal) &&
                    line.Contains("\"airborne\":true")))
                    return;
            }
            Require(false, "a directed flying kick hits a rival");
        }

        private void CheckKnockout()
        {
            Place(3.2f);
            Require(!Rival.ReceiveImpact(1.1f) && Rival.Stagger > 0 && Rival.KoTime == 0, "light impact builds stagger");
            Advance(0, 3);
            Require(!Rival.ReceiveImpact(1f) && Rival.ReceiveImpact(.5f), "successive attacks reach the threshold");
            Require(arena.AliveMask == 3 && Rival.KoTime > 0, "knockout preserves round participation");
            Advance(0, 105);
            Require(Rival.KoTime == 0 && !arena.Finished, "knockout recovers within the round");
            for (int expected = 3; expected <= 4; expected++)
            {
                Require(Rival.ReceiveImpact(10) && Rival.KoTime == expected && Rival.KoFraction == 1,
                    "knockout lasts one more second on each repeat");
                Require(!Rival.ReceiveImpact(10), "a hit on an unconscious rival does not add another knockout");
                Advance(0, expected * 50 + 5);
                Require(Rival.KoTime == 0 && !arena.Finished, "longer KO still expires");
            }
            Require(Player.ReceiveImpact(10) && Player.KoTime == 2, "knockout count is independent per fighter");
            arena.Restart();
            Require(Rival.ReceiveImpact(10) && Rival.KoTime == 2, "a new round clears the knockout count");
        }

        private void CheckRoundReset()
        {
            Place(.86f);
            Advance(FighterButtons.LeftGrip | FighterButtons.RightGrip | FighterButtons.RaiseArms, 39);
            Require(Player.Lifting, "reset test begins with a raised opponent");
            arena.Restart();
            CheckResetPose();
            Require(Count("throw") == 0, "a reset does not throw a held body");

            Advance(FighterButtons.RaiseArms, 36);
            options.Smoke = false;
            options.Session = true;
            Rival.SetTestPosition(arena.Center + Vector3.down * 3);
            Advance(0, 1);
            Require(arena.Finished, "test round finishes");
            for (int i = 0; i < 130 && arena.Finished; i++)
                Advance(0, 1);
            options.Smoke = true;
            options.Session = false;
            Require(!arena.Finished, "a finished round automatically restarts");
            CheckResetPose();
        }

        private void CheckResetPose()
        {
            Require(!Player.ArmsRaised && !Player.LeftGrip && !Player.RightGrip && !Player.Lifting, "reset clears arm commands and grips");
            for (int index = 2; index <= 4; index += 2)
            {
                var body = Player.ReadBody(index);
                Require(body.Position.y < Player.Position.y + .2f && body.Velocity.sqrMagnitude < .0001f &&
                    body.AngularVelocity.sqrMagnitude < .0001f &&
                    Vector3.Dot(body.Rotation * Vector3.up, Vector3.up) > .99f, "arms spawn down at rest");
            }
            Advance(0, 22);
            Advance(FighterButtons.RaiseArms, 36);
            var shapes = Player.Root.GetComponentsInChildren<SphereCollider>();
            Require(shapes.Where(shape => shape.name == "Puño").Min(shape => shape.bounds.center.y) >
                shapes.Single(shape => shape.name == "Casco").bounds.max.y, "I works again after the reset");
        }

        private void Place(float distance)
        {
            arena.Reset();
            Player.Reset(arena.Center + Vector3.back * (distance / 2), Vector3.forward);
            Rival.Reset(arena.Center + Vector3.forward * (distance / 2), Vector3.back);
            Physics.SyncTransforms();
            Advance(0, 22);
            events.Clear();
        }

        private void Advance(FighterButtons buttons, int steps, int x = 0, int z = 0)
        {
            for (int i = 0; i < steps; i++)
            {
                if (i == 0 || arena.Tick % Arena.DecisionPeriod == 0)
                    arena.Command(0, new FighterCommand(x, z, buttons));
                arena.Apply(.02f);
                Physics.Simulate(.02f);
                arena.Resolve(.02f);
            }
        }

        private int Count(string type)
        {
            string prefix = $"{{\"type\":\"{type}\",";
            return events.Count(line => line.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static void Require(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException($"GangFight combat smoke failed: {name}");
        }
    }
}
