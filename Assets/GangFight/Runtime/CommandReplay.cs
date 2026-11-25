using System;
using System.Collections.Generic;
using System.IO;
using Unity.MLAgents.Actuators;
using UnityEngine;

namespace GangFight
{
    internal sealed class CommandReplay
    {
        [Serializable]
        private sealed class Entry
        {
            public int round = 0, arena = 0, slot = 0, elapsed_ticks = 0, x = 0, z = 0, buttons = 0;
            public int combat_version = 0;
        }

        private readonly Dictionary<(int tick, int slot), FighterCommand> track = new();
        private readonly int startTick;

        public CommandReplay(string path, int requestedRound)
        {
            int? arena = null;
            int? selectedRound = requestedRound > 0 ? requestedRound : null;
            startTick = int.MaxValue;
            foreach (string line in File.ReadLines(path))
            {
                if (!line.Contains("\"type\":\"command\""))
                    continue;
                var entry = JsonUtility.FromJson<Entry>(line);
                selectedRound ??= entry.round;
                if (entry.round != selectedRound.Value)
                    continue;
                arena ??= entry.arena;
                if (entry.arena != arena.Value)
                    continue;
                if (entry.combat_version != FighterCommand.CombatVersion)
                    throw new ArgumentException("La grabación usa otra versión de los controles de combate.");
                track.Add((entry.elapsed_ticks, entry.slot),
                    new FighterCommand(entry.x, entry.z, (FighterButtons)entry.buttons));
                startTick = Math.Min(startTick, entry.elapsed_ticks);
            }
            if (track.Count == 0 || startTick != 0)
                throw new ArgumentException("La grabación debe contener una ronda completa que empiece en el paso cero.");
        }

        public FighterCommand Read(int tick, int slot, bool throughAgent) =>
            track.TryGetValue((tick, slot), out FighterCommand command)
                ? throughAgent ? ThroughAgent(command) : command
                : default;

        private static FighterCommand ThroughAgent(FighterCommand command)
        {
            int[] actions = new int[8];
            actions[0] = FighterCommand.EncodeAxis(command.X);
            actions[1] = FighterCommand.EncodeAxis(command.Z);
            for (int i = 2; i < actions.Length; i++)
                actions[i] = (command.Buttons & (FighterButtons)(1 << (i - 2))) != 0 ? 1 : 0;
            return FighterAgent.Decode(new ActionSegment<int>(actions));
        }
    }
}
