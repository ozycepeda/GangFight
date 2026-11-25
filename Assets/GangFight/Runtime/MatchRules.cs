using System;

namespace GangFight
{
    // The arena submits a whole simulation tick at once.
    public sealed class MatchRules
    {
        public int Remaining { get; private set; }
        public int Winner { get; private set; } = -1;
        public bool Finished { get; private set; }
        public bool Interrupted { get; private set; }
        public int LastEliminated { get; private set; }

        public void Start(int count)
        {
            if (count < 2 || count > 4)
                throw new ArgumentOutOfRangeException(nameof(count));
            Remaining = (1 << count) - 1;
            LastEliminated = 0;
            Winner = -1;
            Finished = false;
            Interrupted = false;
        }

        public void Resolve(int fallen, bool timeLimit)
        {
            if (Finished)
                return;

            LastEliminated = Remaining & fallen;
            Remaining &= ~LastEliminated;
            if (Remaining == 0 || (Remaining & (Remaining - 1)) == 0)
            {
                Finished = true;
                for (int slot = 0; slot < 4; slot++)
                    if (Remaining == (1 << slot))
                        Winner = slot;
                return;
            }

            Finished = timeLimit;
            Interrupted = timeLimit;
        }

        public float Reward(int slot)
        {
            int bit = 1 << slot;
            if (Winner == slot)
                return 1;
            if ((LastEliminated & bit) != 0 && !(Finished && Remaining == 0))
                return -1;
            return 0;
        }
    }
}
