using System;
using System.Globalization;
using System.Linq;

namespace GangFight
{
    public sealed class RunOptions
    {
        internal static readonly float DefaultHalfSize = 5f * MathF.Sqrt(1.5f);
        internal const float DefaultThrowMultiplier = 1.2f;
        public bool Session, SelfPlay, Evaluate, Smoke, Shaping, RecordCommands;
        public int Fighters = 2, Arenas = 1, Seed = 41;
        public float HalfSize = DefaultHalfSize, FanStrength, Timeout = 60;
        public float ThrowMultiplier = DefaultThrowMultiplier;
        public string[] Roles = Array.Empty<string>();
        public string[] OpponentPool = Array.Empty<string>();
        public string EventFile, ReplayFile;
        public bool ReplayAgent, InteractiveLayout;
        public int ReplayRound, SeatOffset;
        public bool Learning => !Session && !Evaluate && !Smoke;

        internal static RunOptions Parse(string[] args, bool inPlayScene)
        {
            var options = new RunOptions();
            string mode = Read(args, "--gf-mode", inPlayScene ? "play" : "passive");
            options.Session = mode == "play";
            options.Evaluate = mode == "evaluate";
            options.Smoke = mode == "smoke";
            options.SelfPlay = mode == "selfplay";
            options.EventFile = Read(args, "--gf-events");
            options.ReplayFile = Read(args, "--gf-replay");
            options.ReplayAgent = Array.IndexOf(args, "--gf-replay-agent") >= 0;
            options.InteractiveLayout = options.ReplayFile != null;
            options.SeatOffset = Integer(args, "--gf-seat-offset", 0, 0, 3);
            options.ReplayRound = Integer(args, "--gf-replay-round", 0, 0, int.MaxValue);
            options.RecordCommands = Array.IndexOf(args, "--gf-record-commands") >= 0;
            options.Shaping = Array.IndexOf(args, "--gf-shaping") >= 0;
            options.Seed = Integer(args, "--gf-seed", 41, int.MinValue, int.MaxValue);
            options.Arenas = Integer(args, "--gf-arenas", 1, 1, 8);
            options.Timeout = Number(args, "--gf-timeout", 60, 1, 600);
            options.HalfSize = Number(args, "--gf-half-size", DefaultHalfSize, 2.7f, 8);
            options.ThrowMultiplier = Number(args, "--gf-throw-multiplier", DefaultThrowMultiplier, 1, 3);
            options.FanStrength = Number(args, "--gf-fan", options.Session || options.InteractiveLayout ? 6 : 0, 0, 18);
            if (options.Shaping && mode != "passive")
                throw new ArgumentException("--gf-shaping requiere --gf-mode passive.");

            switch (mode)
            {
                case "play":
                    options.Roles = new[] { "keyboard" }.Concat(
                        Read(args, "--gf-opponents", "passive").Split(',')).ToArray();
                    break;
                case "smoke":
                    options.Roles = new[] { "passive", "passive", "passive", "passive" };
                    options.Arenas = 2;
                    break;
                case "selfplay":
                    options.Roles = new[] { "learn", "learn" };
                    break;
                case "evaluate":
                    options.Roles = Read(args, "--gf-roles", "chase,edge").Split(',');
                    break;
                case "ffa":
                    options.Roles = new[] { "learn" }.Concat(
                        Read(args, "--gf-opponents", "chase,edge").Split(',')).ToArray();
                    options.OpponentPool = Read(args, "--gf-opponent-pool", "").Split(
                        ',', StringSplitOptions.RemoveEmptyEntries);
                    break;
                case "passive":
                case "random":
                case "chase":
                case "edge":
                    options.Roles = new[] { "learn", mode };
                    break;
                default:
                    throw new ArgumentException($"Modo desconocido: {mode}.");
            }

            options.Fighters = options.Roles.Length;
            if (options.Fighters is < 2 or > 4 || options.Roles.Any(role => !ValidRole(role)))
                throw new ArgumentException("La arena acepta dos a cuatro roles válidos.");
            if (options.Roles.Skip(1).Concat(options.OpponentPool)
                .Any(role => !ValidRole(role) || role == "keyboard" || role == "learn" && !options.SelfPlay) ||
                options.Evaluate && options.Roles[0] is "learn" or "keyboard")
                throw new ArgumentException("Usa bots o modelos en los roles de evaluación y de rivales.");
            if (options.Evaluate || options.Session)
                options.Arenas = 1;
            if (options.Evaluate && string.IsNullOrEmpty(options.EventFile))
                throw new ArgumentException("--gf-mode evaluate requiere --gf-events.");
            if (options.RecordCommands && string.IsNullOrEmpty(options.EventFile))
                throw new ArgumentException("--gf-record-commands requiere --gf-events.");
            if (options.ReplayFile != null && !options.Evaluate)
                throw new ArgumentException("--gf-replay requiere --gf-mode evaluate.");
            return options;
        }

        private static bool ValidRole(string role) => role is
            "keyboard" or "learn" or "passive" or "random" or "chase" or "edge" ||
            role.StartsWith("model:", StringComparison.Ordinal) && role.Length > 6;

        internal static string Read(string[] args, string key, string fallback = null)
        {
            int index = Array.IndexOf(args, key);
            if (index < 0)
                return fallback;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Falta el valor de {key}.");
            return args[index + 1];
        }

        private static int Integer(string[] args, string key, int fallback, int min, int max)
        {
            string value = Read(args, key);
            if (value == null)
                return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ||
                number < min || number > max)
                throw new ArgumentException($"Valor inválido de {key}: {value}.");
            return number;
        }

        private static float Number(string[] args, string key, float fallback, float min, float max)
        {
            string value = Read(args, key);
            if (value == null)
                return fallback;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                float.IsNaN(number) || number < min || number > max)
                throw new ArgumentException($"Valor inválido de {key}: {value}.");
            return number;
        }
    }
}
