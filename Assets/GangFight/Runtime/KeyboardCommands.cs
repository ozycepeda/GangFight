using System.Collections.Generic;
using UnityEngine;

namespace GangFight
{
    public sealed class KeyboardCommands
    {
        private static readonly (KeyCode key, FighterButtons button)[] Bindings =
        {
            (KeyCode.Space, FighterButtons.Jump),
            (KeyCode.J, FighterButtons.LeftGrip),
            (KeyCode.L, FighterButtons.RightGrip),
            (KeyCode.I, FighterButtons.RaiseArms),
            (KeyCode.K, FighterButtons.Headbutt),
            (KeyCode.Comma, FighterButtons.Kick)
        };
        private readonly Queue<bool>[] transitions = new Queue<bool>[Bindings.Length];
        private FighterButtons physical, sampled, rearm;

        public KeyboardCommands()
        {
            for (int i = 0; i < Bindings.Length; i++)
                transitions[i] = new Queue<bool>();
        }

        public void Poll()
        {
            for (int i = 0; i < Bindings.Length; i++)
            {
                var (key, button) = Bindings[i];
                bool held = Input.GetKey(key);
                if ((rearm & button) != 0)
                {
                    if (!held)
                        rearm &= ~button;
                    continue;
                }
                if (Input.GetKeyDown(key))
                    Capture(i, button, true);
                if (Input.GetKeyUp(key))
                    Capture(i, button, false);
                Capture(i, button, held);
            }
        }

        private void Capture(int index, FighterButtons button, bool held)
        {
            if (((physical & button) != 0) == held)
                return;
            physical = held ? physical | button : physical & ~button;
            transitions[index].Enqueue(held);
        }

        public FighterCommand Read()
        {
            for (int i = 0; i < Bindings.Length; i++)
                if (transitions[i].TryDequeue(out bool held))
                {
                    FighterButtons button = Bindings[i].button;
                    sampled = held ? sampled | button : sampled & ~button;
                }
            return new FighterCommand(Axis(KeyCode.D, KeyCode.A), Axis(KeyCode.W, KeyCode.S), sampled);
        }

        public void Clear()
        {
            physical = sampled = rearm = 0;
            for (int i = 0; i < Bindings.Length; i++)
            {
                transitions[i].Clear();
                if (Input.GetKey(Bindings[i].key))
                    rearm |= Bindings[i].button;
            }
        }

        private static int Axis(KeyCode positive, KeyCode negative) =>
            (Input.GetKey(positive) ? 1 : 0) - (Input.GetKey(negative) ? 1 : 0);
    }
}
