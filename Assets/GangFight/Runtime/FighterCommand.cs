using System;

namespace GangFight
{
    [Flags]
    public enum FighterButtons
    {
        Jump = 1, LeftGrip = 2, RightGrip = 4, RaiseArms = 8, Headbutt = 16, Kick = 32
    }

    public readonly struct FighterCommand
    {
        public const int CombatVersion = 3;
        public static int[] Branches => new[] { 3, 3, 2, 2, 2, 2, 2, 2 };
        public readonly int X;
        public readonly int Z;
        public readonly FighterButtons Buttons;

        public FighterCommand(int x, int z, FighterButtons buttons = 0)
        {
            X = Math.Sign(x);
            Z = Math.Sign(z);
            Buttons = buttons;
        }

        public bool Has(FighterButtons button) => (Buttons & button) != 0;
        public static int DecodeAxis(int value) => value == 1 ? -1 : value == 2 ? 1 : 0;
        public static int EncodeAxis(int value) => value < 0 ? 1 : value > 0 ? 2 : 0;
    }

    public sealed class FighterInput
    {
        public FighterButtons Previous { get; private set; }

        public FighterButtons Sample(FighterCommand command)
        {
            FighterButtons pressed = command.Buttons & ~Previous;
            Previous = command.Buttons;
            return pressed;
        }

        public void Reset() => Previous = 0;
    }

    internal struct HandGesture
    {
        // Eight 20 ms physics steps, including four full control intervals.
        internal const int HoldTicks = 8;
        private int? startTick;

        internal bool Reaching { get; private set; }
        internal float Progress(int tick) => startTick.HasValue
            ? Math.Min(1f, (float)(tick - startTick.Value) / HoldTicks)
            : 0f;

        // A short release requests a punch. An established reach ends on release.
        internal bool Sample(bool pressed, bool held, int tick)
        {
            if (pressed && held)
                startTick = tick;
            if (held)
            {
                Reaching = startTick.HasValue && tick - startTick.Value >= HoldTicks;
                return false;
            }

            bool punch = startTick.HasValue && tick - startTick.Value < HoldTicks;
            this = default;
            return punch;
        }
    }
}
