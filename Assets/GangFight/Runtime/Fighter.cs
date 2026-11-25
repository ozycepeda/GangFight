using System;
using System.Collections.Generic;
using UnityEngine;

namespace GangFight
{
    public sealed class Fighter
    {
        private const float InitialKoDuration = 2;
        internal const float KnockoutThreshold = 2.25f;
        internal const int BodyCount = 6;
        internal const int AttackCount = 4;

        internal enum AttackPhase { Idle, Windup, Active, Recovery }

        internal readonly struct BodyState
        {
            internal readonly Vector3 Position, Velocity, AngularVelocity;
            internal readonly Quaternion Rotation;

            internal BodyState(Rigidbody body)
            {
                Position = body.position;
                Rotation = body.rotation;
                Velocity = body.linearVelocity;
                AngularVelocity = body.angularVelocity;
            }
        }

        internal readonly struct AttackState
        {
            internal readonly AttackPhase Phase;
            internal readonly float Progress;

            internal AttackState(AttackPhase phase, float progress)
            {
                Phase = phase;
                Progress = progress;
            }
        }
        private const float RecoveryDuration = .44f;
        private const float WalkSpeed = 5.2f;
        private const float CarrySpeed = 3.4f;
        private const float BodyMassScale = .4f;
        private const float LegLengthScale = .70f;
        private const float OriginalHipHeight = .97f;
        private const float UpperBodyDrop = OriginalHipHeight * (1 - LegLengthScale);
        private const float StanceHeight = 1.04f - UpperBodyDrop;
        private const float TurnSpeed = 2100f;
        private const float ThrowTurnThreshold = 3f;
        private const float JumpSpeed = 5.65f;
        private const float GripReach = .29f;
        private const float HoistForcePerHand = 90f;
        private const float AirBoost = 2.5f;
        private const float AirSpeedLimit = 6.4f;
        private const float AirKickImpactMultiplier = 1.4f;
        private const float HeadbuttLungeSpeed = 5.2f;

        private sealed class Limb
        {
            public Rigidbody Body;
            public ConfigurableJoint Joint;
            public Vector3 Rest;
            public Collider Contact;
            public float RaisePitch;
        }

        private sealed class Grip
        {
            public ConfigurableJoint Joint;
            public Rigidbody Body;
            public Fighter Rival;
        }

        private sealed class Strike
        {
            public readonly string Name;
            private readonly int windup, active, duration;
            public readonly float ImpactScale;
            public readonly HashSet<Fighter> Contacts = new();
            public Collider Surface;
            public Vector3 Direction;
            public bool Airborne;
            private int? start;

            public Strike(string name, int windup, int active, int duration, float impactScale)
            {
                Name = name;
                this.windup = windup;
                this.active = active;
                this.duration = duration;
                ImpactScale = impactScale;
            }

            public bool Running(int tick) => start.HasValue && tick - start.Value < duration;
            public bool WindingUp(int tick) => start.HasValue && tick - start.Value < windup;
            public bool Active(int tick) => start.HasValue && tick - start.Value >= windup &&
                tick - start.Value < windup + active;

            public AttackState ReadState(int tick)
            {
                if (!start.HasValue || tick < start.Value || tick - start.Value >= duration)
                    return new AttackState(AttackPhase.Idle, 0);
                int elapsed = tick - start.Value;
                AttackPhase phase = elapsed < windup ? AttackPhase.Windup
                    : elapsed < windup + active ? AttackPhase.Active : AttackPhase.Recovery;
                return new AttackState(phase, (float)elapsed / duration);
            }

            public void Start(int tick, Collider surface, Vector3 direction, bool airborne)
            {
                start = tick;
                Surface = surface;
                Direction = direction;
                Airborne = airborne;
                Contacts.Clear();
            }

            public void Clear()
            {
                start = null;
                Surface = null;
                Direction = default;
                Airborne = false;
                Contacts.Clear();
            }
        }

        private readonly Limb core, head;
        private readonly Limb[] arms = new Limb[2], legs = new Limb[2];
        private readonly List<Limb> limbs = new(6);
        private readonly List<Collider> colliders = new(11);
        private readonly Grip[] grips = { new(), new() };
        private readonly HandGesture[] hands = new HandGesture[2];
        private readonly Strike[] punches = { new("punch_left", 2, 7, 13, .95f), new("punch_right", 2, 7, 13, .95f) };
        private readonly Strike headbutt = new("headbutt", 4, 9, 24, 1.15f);
        private readonly Strike kick = new("kick", 5, 7, 22, 1.05f);
        private readonly Strike[] strikes;
        private readonly FighterInput input = new();
        private readonly Collider[] nearby = new Collider[48];
        private readonly int[] touchTicks = new int[4];
        private readonly Arena arena;
        private FighterCommand command;
        private FighterButtons pending;
        private float gait, jumpTimer, stagger, recovery, yaw;
        private int kickSide, knockoutsThisRound;
        private bool airBoostSpent;

        public GameObject Root { get; }
        public int Slot { get; }
        public bool Eliminated { get; private set; }
        public float KoTime { get; private set; }
        internal float KoFraction => KoTime / (InitialKoDuration + Mathf.Max(0, knockoutsThisRound - 1));
        public bool Grounded { get; private set; }
        public bool CanJump => Grounded && Ready && jumpTimer <= 0;
        public bool CanKick => Ready && !kick.Running(arena.Tick);
        public bool CanHeadbutt => Ready && !headbutt.Running(arena.Tick);
        public bool LeftGrip => grips[0].Joint;
        public bool RightGrip => grips[1].Joint;
        public bool Lifting { get; private set; }
        public bool ArmsRaised => Ready && command.Has(FighterButtons.RaiseArms);
        public bool AirBoostAvailable => Ready && !airBoostSpent;
        public float Stagger => Mathf.Clamp01(stagger / KnockoutThreshold);
        public string LatestHit { get; private set; }
        public float HitFlashTime { get; private set; }
        public FighterButtons PreviousButtons => input.Previous;
        public Vector3 Position => core.Body.position;
        public Vector3 Velocity => core.Body.linearVelocity;
        public Vector3 Facing => Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        public float Upright => Vector3.Dot(core.Body.rotation * Vector3.up, Vector3.up);
        private bool Ready => !Eliminated && KoTime <= 0 && recovery <= 0;

        public Fighter(Arena arena, int slot, Material suit, Material trim)
        {
            this.arena = arena;
            Slot = slot;
            Root = new GameObject($"Luchador {slot + 1}");
            Root.transform.SetParent(arena.Root.transform, false);
            strikes = new[] { punches[0], punches[1], headbutt, kick };
            core = MakeLimb("Tronco", UpperBodyPoint(0, 1.04f, 0), 5.2f + 3.2f);
            MakeShape(core, "Pelvis", PrimitiveType.Capsule, core.Rest, new(.50f, .82f, .44f), suit);
            MakeShape(core, "Torso", PrimitiveType.Capsule, UpperBodyPoint(0, 1.40f, 0), new(.70f, .96f, .49f), suit);
            if (Visuals.Enabled)
                Visuals.Part("Placa", PrimitiveType.Cube, core.Body.transform,
                new(0, .38f, .248f), new(.27f, .145f, .025f), trim);

            head = MakeLimb("Casco", UpperBodyPoint(0, 1.84f, .01f), 1.5f, UpperBodyPoint(0, 1.62f, .015f),
                -76, 75);
            head.Contact = MakeShape(head, "Casco", PrimitiveType.Sphere, head.Rest, new(.53f, .52f, .52f), trim);
            if (Visuals.Enabled)
                Visuals.Part("Visor", PrimitiveType.Sphere, head.Body.transform,
                new(0, .015f, .206f), new(.41f, .20f, .15f), Visuals.Ink);

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1 : 1;
                Limb arm = arms[side] = MakeLimb($"Brazo {side}", UpperBodyPoint(sign * .413f, 1.22f, .025f),
                    1f + .8f + .55f, UpperBodyPoint(sign * .374f, 1.56f, .02f), -65, 178);
                MakeShape(arm, "Brazo", PrimitiveType.Capsule, arm.Rest, new(.24f, .59f, .24f), suit);
                arm.Contact = MakeShape(arm, "Puño", PrimitiveType.Sphere, UpperBodyPoint(sign * .428f, .94f, .05f),
                    new(.25f, .25f, .25f), trim);

                Limb leg = legs[side] = MakeLimb($"Pierna {side}", LegPoint(sign * .188f, .55f, -.01f),
                    1.5f + 1.1f + .85f, LegPoint(sign * .185f, OriginalHipHeight, 0), -98, 63);
                MakeShape(leg, "Pierna", PrimitiveType.Capsule, leg.Rest, LegPoint(.25f, .78f, .25f), suit);
                leg.Contact = MakeShape(leg, "Bota", PrimitiveType.Cube, LegPoint(sign * .191f, .155f, .066f),
                    LegPoint(.27f, .23f, .38f), trim);
            }
            IgnoreSelfCollisions();
        }

        private static Vector3 UpperBodyPoint(float x, float y, float z) => new(x, y - UpperBodyDrop, z);
        private static Vector3 LegPoint(float x, float y, float z) => new(x, y * LegLengthScale, z);

        private Limb MakeLimb(string name, Vector3 rest, float mass, Vector3? pivot = null,
            float lowPitch = 0, float highPitch = 0)
        {
            var part = new GameObject(name);
            part.transform.SetParent(Root.transform, false);
            part.transform.localPosition = rest;
            var body = part.AddComponent<Rigidbody>();
            body.mass = mass * BodyMassScale;
            body.linearDamping = .25f;
            body.angularDamping = 1.5f;
            body.maxAngularVelocity = 24;
            body.solverIterations = 14;
            body.solverVelocityIterations = 5;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            part.AddComponent<FighterContact>().Owner = this;
            var limb = new Limb { Body = body, Rest = rest };
            limbs.Add(limb);

            if (!pivot.HasValue)
                return limb;

            var joint = part.AddComponent<ConfigurableJoint>();
            joint.connectedBody = core.Body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = pivot.Value - rest;
            joint.connectedAnchor = pivot.Value - core.Rest;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = lowPitch };
            joint.highAngularXLimit = new SoftJointLimit { limit = highPitch };
            joint.angularYLimit = new SoftJointLimit { limit = 35 };
            joint.angularZLimit = new SoftJointLimit { limit = 40 };
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = .12f;
            joint.projectionAngle = 28;
            limb.Joint = joint;
            return limb;
        }

        private Collider MakeShape(Limb limb, string name, PrimitiveType shape, Vector3 rest,
            Vector3 size, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(limb.Body.transform, false);
            part.transform.localPosition = rest - limb.Rest;

            Collider collider;
            if (shape == PrimitiveType.Cube)
            {
                var box = part.AddComponent<BoxCollider>();
                box.size = size;
                collider = box;
            }
            else if (shape == PrimitiveType.Sphere)
            {
                var sphere = part.AddComponent<SphereCollider>();
                sphere.radius = size.x * .5f;
                collider = sphere;
            }
            else
            {
                var capsule = part.AddComponent<CapsuleCollider>();
                capsule.radius = Mathf.Min(size.x, size.z) * .5f;
                capsule.height = size.y;
                collider = capsule;
            }
            colliders.Add(collider);
            if (Visuals.Enabled)
                Visuals.Part(name, shape, part.transform, Vector3.zero, size, material);
            return collider;
        }

        internal void PrepareForRoundReset()
        {
            ResetCombat();
            Root.SetActive(false);
        }

        public void Reset(Vector3 position, Vector3 facing)
        {
            if (Root.activeSelf)
                PrepareForRoundReset();
            Eliminated = false;
            KoTime = HitFlashTime = 0;
            knockoutsThisRound = 0;
            LatestHit = null;
            Grounded = false;
            input.Reset();
            gait = jumpTimer = stagger = recovery = 0;
            Array.Fill(touchTicks, int.MinValue);
            kickSide = 0;
            airBoostSpent = false;
            yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            foreach (Limb limb in limbs)
            {
                limb.Body.transform.SetPositionAndRotation(position + rotation * limb.Rest, rotation);
                Pose(limb, Vector3.zero, 90);
            }
            Root.SetActive(true);
            IgnoreSelfCollisions();
            foreach (Limb limb in limbs)
            {
                limb.Body.position = position + rotation * limb.Rest;
                limb.Body.rotation = rotation;
                limb.Body.linearVelocity = Vector3.zero;
                limb.Body.angularVelocity = Vector3.zero;
                limb.Body.WakeUp();
            }
        }

        private void ResetCombat()
        {
            Release(0);
            Release(1);
            Lifting = false;
            command = default;
            pending = 0;
            Array.Clear(hands, 0, hands.Length);
            foreach (Strike strike in strikes)
                strike.Clear();
            foreach (Limb arm in arms)
                arm.RaisePitch = 0;
        }

        private void IgnoreSelfCollisions()
        {
            for (int i = 0; i < colliders.Count; i++)
                for (int j = i + 1; j < colliders.Count; j++)
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
        }

        public float HandProgress(int side) => hands[side].Progress(arena.Tick);
        public bool CanPunch(int side) => Ready && !punches[side].Running(arena.Tick) && !grips[side].Joint;

        // Stable observation order: trunk, head, left arm, left leg, right arm, right leg.
        internal BodyState ReadBody(int index) => new(limbs[index].Body);

        // Stable observation order: left punch, right punch, headbutt, kick.
        internal AttackState ReadAttack(int index) => strikes[index].ReadState(arena.Tick);

        public void SetCommand(FighterCommand next)
        {
            if (Eliminated)
                return;
            FighterButtons pressed = input.Sample(next);
            if (KoTime > 0)
                return;
            command = next;
            if (Ready)
                pending |= pressed;
        }

        public void Step(float dt)
        {
            if (Eliminated)
                return;

            jumpTimer = Mathf.Max(0, jumpTimer - dt);
            HitFlashTime = Mathf.Max(0, HitFlashTime - dt);
            stagger = Mathf.Max(0, stagger - dt * 1.05f);
            if (KoTime > 0)
            {
                KoTime = Mathf.Max(0, KoTime - dt);
                if (KoTime == 0)
                {
                    recovery = RecoveryDuration;
                    arena.Record("recovered", Slot);
                }
                foreach (Limb limb in limbs)
                    Pose(limb, Vector3.zero, 0);
                return;
            }

            recovery = Mathf.Max(0, recovery - dt);
            float motor = Mathf.Lerp(1, .15f, recovery / RecoveryDuration);
            bool support = Physics.Raycast(Position + Vector3.up * .24f, Vector3.down,
                out RaycastHit hit, 1.52f, 1 << Arena.FloorLayer, QueryTriggerInteraction.Ignore);
            float clearance = support ? hit.distance - .24f : 0;
            Grounded = support && clearance <= StanceHeight + .07f && Velocity.y < 2.1f && jumpTimer < .13f;
            if (Grounded)
                airBoostSpent = false;
            if (CanJump && (pending & FighterButtons.Jump) != 0)
            {
                jumpTimer = .34f;
                Grounded = false;
                ChangeVelocity(Vector3.up * JumpSpeed);
                arena.Record("jump", Slot);
            }

            Vector3 move = Vector3.ClampMagnitude(new(command.X, 0, command.Z), 1);
            if (move.sqrMagnitude > 0 &&
                !headbutt.WindingUp(arena.Tick) && !headbutt.Active(arena.Tick) &&
                !(kick.Airborne && (kick.WindingUp(arena.Tick) || kick.Active(arena.Tick))))
            {
                float target = Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, target, TurnSpeed * dt);
            }

            for (int side = 0; side < 2; side++)
            {
                FighterButtons button = side == 0 ? FighterButtons.LeftGrip : FighterButtons.RightGrip;
                bool reached = hands[side].Reaching;
                if (hands[side].Sample((pending & button) != 0, command.Has(button), arena.Tick) && CanPunch(side))
                    StartStrike(punches[side], arms[side].Contact, move);
                if (hands[side].Reaching && !reached && !Grounded)
                    AimAirAction(move);
                if (!hands[side].Reaching)
                    Release(side, deliberate: true);
            }
            if (CanHeadbutt && (pending & FighterButtons.Headbutt) != 0)
                StartStrike(headbutt, head.Contact, move);
            if (CanKick && (pending & FighterButtons.Kick) != 0)
            {
                kickSide = 1 - kickSide;
                StartStrike(kick, legs[kickSide].Contact, move);
            }
            pending = 0;

            Vector3 planarVelocity = Vector3.ProjectOnPlane(Velocity, Vector3.up);
            bool lunging = Grounded && !headbutt.Airborne && headbutt.Active(arena.Tick);
            Vector3 wanted = lunging ? headbutt.Direction * HeadbuttLungeSpeed
                : move * ((LeftGrip || RightGrip) ? CarrySpeed : WalkSpeed);
            if (headbutt.Active(arena.Tick) && !headbutt.Active(arena.Tick - 1))
            {
                if (lunging)
                {
                    float forwardSpeed = Vector3.Dot(planarVelocity, headbutt.Direction);
                    ChangeVelocity(headbutt.Direction * Mathf.Max(0, HeadbuttLungeSpeed - forwardSpeed));
                }
                core.Body.AddTorque(Vector3.Cross(Vector3.up, headbutt.Direction) * 5f, ForceMode.VelocityChange);
            }
            bool redirecting = !lunging && move.sqrMagnitude > 0 && planarVelocity.sqrMagnitude > .16f &&
                Vector3.Dot(move, planarVelocity.normalized) < .88f;
            Vector3 drive = Vector3.ClampMagnitude((wanted - planarVelocity) * (redirecting ? 28 : 11),
                redirecting ? 60 : 20);
            if (Grounded || move.sqrMagnitude > 0)
                core.Body.AddForce(drive * (Grounded ? 1 : .18f) * motor, ForceMode.Acceleration);
            if (support && jumpTimer <= .10f)
            {
                float spring = Mathf.Clamp(105 * (StanceHeight - clearance) - 16 * Velocity.y + 15, 0, 95);
                core.Body.AddForce(Vector3.up * spring * motor, ForceMode.Acceleration);
            }
            if (support && clearance < StanceHeight - .14f && recovery > 0)
                core.Body.AddForce(Vector3.up * 24f, ForceMode.Acceleration);

            PoseAttacks(move, motor, dt);
        }

        private void StartStrike(Strike strike, Collider surface, Vector3 move)
        {
            Vector3 direction = Grounded ? Facing : AimAirAction(move);
            strike.Start(arena.Tick, surface, direction, !Grounded);
            arena.Record(strike.Name, Slot);
        }

        private Vector3 AimAirAction(Vector3 move)
        {
            Vector3 direction = move.sqrMagnitude > 0 ? move.normalized : Facing;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (!airBoostSpent)
            {
                Vector3 planar = Vector3.ProjectOnPlane(Velocity, Vector3.up);
                Vector3 boosted = Vector3.ClampMagnitude(planar + direction * AirBoost,
                    Mathf.Max(AirSpeedLimit, planar.magnitude));
                ChangeVelocity(boosted - planar);
                airBoostSpent = true;
                arena.Record("air_boost", Slot);
            }
            return direction;
        }

        private void ChangeVelocity(Vector3 delta)
        {
            foreach (Limb limb in limbs)
                limb.Body.AddForce(delta, ForceMode.VelocityChange);
        }

        private void TipBackForAirKick()
        {
            Vector3 center = Vector3.zero;
            float mass = 0;
            foreach (Limb limb in limbs)
            {
                center += limb.Body.worldCenterOfMass * limb.Body.mass;
                mass += limb.Body.mass;
            }
            center /= mass;

            // Rotate the whole rig about its center of mass without adding another air boost.
            Vector3 rotation = Vector3.Cross(kick.Direction, Vector3.up) * 4.8f;
            RotateAround(center, rotation);
        }

        private void RotateAround(Vector3 center, Vector3 rotation)
        {
            foreach (Limb limb in limbs)
                RotateBodyAround(limb.Body, center, rotation);
        }

        private static void RotateBodyAround(Rigidbody body, Vector3 center, Vector3 rotation)
        {
            body.AddTorque(rotation, ForceMode.VelocityChange);
            body.AddForce(Vector3.Cross(rotation, body.worldCenterOfMass - center), ForceMode.VelocityChange);
        }

        private void PoseAttacks(Vector3 move, float motor, float dt)
        {
            int tick = arena.Tick;
            bool headWindup = headbutt.WindingUp(tick);
            bool headActive = headbutt.Active(tick);
            bool airKickWindup = kick.Airborne && kick.WindingUp(tick);
            bool airKick = kick.Airborne && kick.Active(tick);
            if (airKickWindup || airKick)
            {
                yaw = Mathf.Atan2(kick.Direction.x, kick.Direction.z) * Mathf.Rad2Deg;
                if (airKick && !kick.Active(tick - 1))
                    TipBackForAirKick();
            }
            float torsoPitch = airKick ? -42 : airKickWindup ? -26 :
                headActive ? 76 : headWindup ? -18 : headbutt.Running(tick) ? -21 : 0;
            Balance(torsoPitch, motor, dt);
            Pose(head, new(headActive ? 84 : headWindup ? -37 : 0, 0, 0),
                (headActive || headWindup ? 110 : 55) * motor);

            gait += dt * move.magnitude * 11;
            for (int side = 0; side < 2; side++)
            {
                float swing = Mathf.Sin(gait + side * Mathf.PI) * 25 * move.magnitude;
                bool punching = punches[side].Active(tick);
                bool punchWindup = punches[side].WindingUp(tick);
                bool reaching = hands[side].Reaching && !punches[side].Running(tick);
                bool raise = RaisingHand(side, tick);
                Limb arm = arms[side];
                arm.RaisePitch = raise ? Mathf.MoveTowards(arm.RaisePitch, -174, 1100 * dt) : reaching ? -80 : 0;
                float inward = side == 0 ? 1 : -1;
                float pitch = punching ? -104 : punchWindup ? 10 : raise ? arm.RaisePitch :
                    reaching ? -80 : -swing * .62f;
                Pose(arm, new(pitch, punching ? 30 * inward : 0, (raise ? -6 : 9) * inward),
                    (punching ? 175 : raise ? 145 : 80) * motor);
                if (punching)
                    arms[side].Body.AddForceAtPosition(punches[side].Direction * 55,
                        arms[side].Contact.bounds.center, ForceMode.Force);

                bool kicking = side == kickSide && kick.Active(tick);
                bool kickWindup = side == kickSide && kick.WindingUp(tick);
                float leg = kicking ? (airKick ? -46 : -78) : kickWindup ? 26 : swing;
                Pose(legs[side], new(leg, 0, 0), (kicking || kickWindup ? 175 : 104) * motor);
                if (kicking)
                    legs[side].Body.AddForce(kick.Direction * (airKick ? 42 : 15), ForceMode.Acceleration);
                UpdateGrip(side, reaching);
            }

            bool lift = LeftGrip && RightGrip &&
                (grips[0].Rival != null ? grips[0].Rival == grips[1].Rival : grips[0].Body == grips[1].Body);
            if (lift && !Lifting)
            {
                Fighter rival = grips[0].Rival;
                if (rival != null)
                    foreach (Grip grip in grips)
                    {
                        // The two-handed hold carries the trunk while preserving each contact point.
                        ConfigurableJoint joint = grip.Joint;
                        Vector3 anchor = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                        joint.connectedBody = rival.core.Body;
                        joint.connectedAnchor = joint.connectedBody.transform.InverseTransformPoint(anchor);
                    }
                arena.Record("lift_with_grip", Slot, rival?.Slot ?? -1);
            }
            Lifting = lift;
            for (int side = 0; side < arms.Length; side++)
                if (lift || (grips[side].Joint && RaisingHand(side, tick)))
                {
                    Limb arm = arms[side];
                    Vector3 palm = arm.Contact.bounds.center;
                    float shoulderHeight = arm.Body.transform.TransformPoint(arm.Joint.anchor).y;
                    float heightError = shoulderHeight + (RaisingHand(side, tick) ? .60f : .17f) - palm.y;
                    float power = Mathf.Clamp(heightError * 240 - arm.Body.GetPointVelocity(palm).y * 22,
                        0, HoistForcePerHand);
                    Vector3 force = Vector3.up * power;
                    arm.Body.AddForceAtPosition(force, palm);
                    core.Body.AddForce(-force);
                }
        }

        private bool RaisingHand(int side, int tick) =>
            ArmsRaised && !punches[side].Running(tick) && (!hands[side].Reaching || grips[side].Joint);

        private void Balance(float pitch, float strength, float dt)
        {
            Quaternion target = Quaternion.Euler(pitch, yaw, 0);
            Vector3 error = AngularError(target, core.Body.rotation);
            Vector3 angularVelocity = core.Body.angularVelocity;
            bool canSteer = Grounded || !arena.IsCarried(this);
            bool carrying = LeftGrip || RightGrip;
            bool fastGroundTurn = Grounded && pitch == 0 && (Mathf.Abs(error.y) > .14f || Mathf.Abs(angularVelocity.y) > 1.5f);
            if (canSteer && (carrying || fastGroundTurn))
            {
                SteerTurn(error, angularVelocity, dt, strength, carrying);
                return;
            }
            Vector3 tilt = Vector3.ProjectOnPlane(error * 45 - angularVelocity * 8, Vector3.up);
            float turn = canSteer ? Mathf.Clamp(error.y * 235 - angularVelocity.y * 20, -255, 255) : 0;
            core.Body.AddTorque((Vector3.ClampMagnitude(tilt, 55) + Vector3.up * turn) * strength,
                ForceMode.Acceleration);
        }

        private void SteerTurn(Vector3 headingError, Vector3 currentRate, float dt, float strength, bool carrying)
        {
            float rateLimit = carrying ? 10 : 17;
            float yawAcceleration = carrying ? 70 : 145;
            float wantedRate = Mathf.Clamp(headingError.y * (carrying ? 14 : 19), -rateLimit, rateLimit);
            float yawChange = Mathf.Clamp(wantedRate - currentRate.y, -yawAcceleration * dt, yawAcceleration * dt);
            Vector3 correction = Vector3.ProjectOnPlane(headingError * (carrying ? 12 : 16) - currentRate, Vector3.up);
            Vector3 tiltChange = Vector3.ClampMagnitude(correction, (carrying ? 70 : 100) * dt) * strength;

            // Lead with the carrier's trunk and give the gripped bodies the same turn around it.
            Vector3 center = core.Body.worldCenterOfMass;
            Vector3 turn = Vector3.up * (yawChange * strength);
            RotateAround(center, turn + tiltChange);
            if (!carrying || Mathf.Abs(yawChange) < .005f)
                return;
            for (int side = 0; side < grips.Length; side++)
            {
                Grip grip = grips[side];
                if (!grip.Joint || !grip.Body || (side == 1 && SharesTarget(grip, grips[0])))
                    continue;
                if (grip.Rival != null)
                    grip.Rival.RotateAround(center, turn);
                else
                    RotateBodyAround(grip.Body, center, turn);
            }
        }

        private static Vector3 AngularError(Quaternion target, Quaternion current)
        {
            Quaternion correction = target * Quaternion.Inverse(current);
            correction.ToAngleAxis(out float angle, out Vector3 axis);
            return axis * (Mathf.DeltaAngle(0, angle) * Mathf.Deg2Rad);
        }

        private static void Pose(Limb limb, Vector3 angles, float spring)
        {
            if (!limb.Joint)
                return;
            limb.Joint.targetRotation = Quaternion.Inverse(Quaternion.Euler(angles));
            limb.Joint.slerpDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = spring > 0 ? 10 : 1.4f,
                maximumForce = spring > 0 ? spring * 2.6f : 4
            };
        }

        private void UpdateGrip(int side, bool reaching)
        {
            Grip grip = grips[side];
            if (!grip.Joint && grip.Body)
                Release(side);
            if (!reaching)
                return;
            if (grip.Joint)
            {
                grip.Rival?.Touch(this);
                return;
            }

            Limb arm = arms[side];
            Vector3 palm = arm.Contact.transform.position;
            int count = Physics.OverlapSphereNonAlloc(palm, GripReach, nearby,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float distance = GripReach * GripReach;
            Rigidbody target = null;
            Fighter rival = null;
            Vector3 anchor = default;
            for (int i = 0; i < count; i++)
            {
                Collider candidate = nearby[i];
                Rigidbody body = candidate.attachedRigidbody;
                if (!body)
                    continue;
                Fighter owner = OwnerOf(body);
                if (owner == null ? body != arena.PracticeBlock : owner == this || owner.Eliminated || owner.arena != arena)
                    continue;
                Vector3 point = candidate.ClosestPoint(palm);
                float squared = (point - palm).sqrMagnitude;
                if (squared > distance || (squared == distance && target &&
                    body.GetInstanceID() >= target.GetInstanceID()))
                    continue;
                target = body;
                rival = owner;
                anchor = point;
                distance = squared;
            }
            if (!target)
                return;

            var joint = arm.Body.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = target;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = arm.Body.transform.InverseTransformPoint(palm);
            joint.connectedAnchor = target.transform.InverseTransformPoint(anchor);
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.breakForce = 520;
            joint.breakTorque = 310;
            joint.enableCollision = false;
            grip.Joint = joint;
            grip.Body = target;
            grip.Rival = rival;
            rival?.Touch(this);
            arena.Record(side == 0 ? "grab_left" : "grab_right", Slot, rival?.Slot ?? -1);
        }

        private static Fighter OwnerOf(Rigidbody body) =>
            body && body.TryGetComponent(out FighterContact contact) ? contact.Owner : null;

        public bool Holds(Fighter other, int side) => grips[side].Joint && grips[side].Rival == other;

        public void ReleaseFrom(Fighter other)
        {
            for (int side = 0; side < 2; side++)
                if (grips[side].Rival == other)
                    Release(side);
        }

        internal void ReleaseBlock()
        {
            for (int side = 0; side < 2; side++)
                if (grips[side].Body == arena.PracticeBlock)
                    Release(side);
        }

        private void Release(int side, bool deliberate = false)
        {
            Grip grip = grips[side];
            if (grip.Joint)
            {
                // Free the constraint now. Unity destroys the component at the end of the frame.
                grip.Joint.xMotion = grip.Joint.yMotion = grip.Joint.zMotion = ConfigurableJointMotion.Free;
                grip.Joint.enableCollision = true;
                grip.Joint.connectedBody = null;
                UnityEngine.Object.Destroy(grip.Joint);
                if (deliberate && grip.Body)
                    AddThrowImpulse(grip, grips[1 - side]);
            }
            if (grip.Body)
                arena.Record(side == 0 ? "release_left" : "release_right", Slot, grip.Rival?.Slot ?? -1);
            grip.Joint = null;
            grip.Body = null;
            grip.Rival = null;
        }

        private void AddThrowImpulse(Grip released, Grip otherHand)
        {
            float bonus = arena.ThrowMultiplier - 1;
            float turn = core.Body.angularVelocity.y;
            if (bonus <= 0 || Mathf.Abs(turn) < ThrowTurnThreshold || SharesTarget(released, otherHand))
                return;

            Fighter rival = released.Rival;
            Vector3 targetPosition = rival != null ? rival.Position : released.Body.worldCenterOfMass;
            Vector3 targetVelocity = rival != null ? rival.Velocity : released.Body.linearVelocity;
            Vector3 tangent = Vector3.Cross(Vector3.up * Mathf.Sign(turn), targetPosition - Position).normalized;
            float tangentialSpeed = Vector3.Dot(targetVelocity - Velocity, tangent);
            if (tangentialSpeed <= 0)
                return;

            Vector3 delta = tangent * (tangentialSpeed * bonus);
            if (rival != null)
                rival.ChangeVelocity(delta);
            else
                released.Body.AddForce(delta, ForceMode.VelocityChange);
            arena.Record("throw", Slot, rival?.Slot ?? -1);
        }

        private static bool SharesTarget(Grip grip, Grip other) => other.Joint &&
            (grip.Rival != null ? grip.Rival == other.Rival : grip.Body == other.Body);

        internal void OnCollision(Collision collision)
        {
            Fighter other = OwnerOf(collision.rigidbody);
            if (Eliminated || other == null || other == this || other.arena != arena || other.Eliminated)
                return;
            if (collision.relativeVelocity.sqrMagnitude > .48f)
                other.Touch(this);

            float impulse = collision.impulse.magnitude;
            if (impulse <= .35f || collision.relativeVelocity.sqrMagnitude < .64f)
                return;
            foreach (Strike strike in strikes)
            {
                if (!strike.Active(arena.Tick) || strike.Contacts.Contains(other))
                    continue;
                for (int i = 0; i < collision.contactCount; i++)
                {
                    if (collision.GetContact(i).thisCollider != strike.Surface)
                        continue;
                    strike.Contacts.Add(other);
                    float scale = strike.ImpactScale * (strike == kick && strike.Airborne ? AirKickImpactMultiplier : 1);
                    arena.QueueImpact(this, other, strike.Name, strike.Direction, strike.Airborne, impulse,
                        Mathf.Min(3.4f, (impulse - .35f) * scale));
                    break;
                }
            }
        }

        internal void ApplyStrike(Fighter attacker, string type, Vector3 direction, bool airborne,
            float impulse, float impact)
        {
            if (Eliminated)
                return;
            float before = Stagger;
            LatestHit = type;
            HitFlashTime = .5f;
            Touch(attacker);
            core.Body.AddForce(direction * Mathf.Min(.9f, impact * .40f), ForceMode.VelocityChange);
            bool knockedOut = ReceiveImpact(impact);
            arena.RecordImpact(type + "_hit", attacker.Slot, Slot, airborne, impulse, before,
                knockedOut ? 1f : Stagger, knockedOut);
            if (knockedOut)
                arena.Record("ko", Slot, attacker.Slot);
        }

        private void Touch(Fighter other) => touchTicks[other.Slot] = arena.Tick;

        public int EjectionCredit(int tick)
        {
            int source = -1;
            for (int slot = 0; slot < touchTicks.Length; slot++)
            {
                if (tick - (long)touchTicks[slot] > 100)
                    continue;
                if (source >= 0)
                    return -2;
                source = slot;
            }
            return source;
        }

        public void ApplyFan(Vector3 center, Vector3 direction, float strength)
        {
            Vector3 relative = Position - center;
            if (Mathf.Abs(Vector3.Dot(relative, direction)) > 1.4f || Mathf.Abs(Vector3.Dot(relative, Vector3.Cross(Vector3.up, direction))) > .93f ||
                relative.y > 2.1f || relative.y < -.1f)
                return;
            core.Body.AddForce(direction * strength, ForceMode.Acceleration);
        }

        internal void SetTestPosition(Vector3 position)
        {
            Vector3 delta = position - Position;
            foreach (Limb limb in limbs)
            {
                limb.Body.position += delta;
                limb.Body.linearVelocity = Vector3.zero;
                limb.Body.angularVelocity = Vector3.zero;
            }
        }

        internal bool ReceiveImpact(float impact)
        {
            if (Eliminated || KoTime > 0)
                return false;
            stagger = Mathf.Min(KnockoutThreshold, stagger + impact);
            if (stagger < KnockoutThreshold)
                return false;
            KoTime = InitialKoDuration + knockoutsThisRound++;
            Grounded = false;
            ResetCombat();
            stagger = 0;
            arena.CancelPlayerInput(Slot);
            return true;
        }

        public void Eliminate()
        {
            ResetCombat();
            Eliminated = true;
            Root.SetActive(false);
        }
    }
}
