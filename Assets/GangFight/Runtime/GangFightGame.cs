using System;
using System.IO;
using System.Linq;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GangFight
{
    public sealed class GangFightGame : MonoBehaviour
    {
        private const float StepSeconds = .02f;
        private RunOptions options;
        private Arena[] arenas;
        private KeyboardCommands keyboard;
        private Academy academy;
        private StreamWriter events;
        private int smokeStep;
        private Vector3 smokeOrigin;
        private int sentDecisions, physicsTicks;
        private float benchmarkStart, nextBenchmark;
        private SimulationMode previousSimulation;
        private bool configured;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string scene = SceneManager.GetActiveScene().path;
            bool playable = scene == "Assets/GangFight/Scenes/Play.unity";
            if (!playable && scene != "Assets/GangFight/Scenes/Training.unity")
                return;

            var driver = new GameObject("GangFight").AddComponent<GangFightGame>();
            if (!driver.Initialize(playable))
                driver.enabled = false;
        }

        private bool Initialize(bool playable)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                options = RunOptions.Parse(args, playable);
                previousSimulation = Physics.simulationMode;
                Physics.simulationMode = SimulationMode.Script;
                configured = true;
                Time.fixedDeltaTime = StepSeconds;
                Application.runInBackground = !options.Session;
                if (!options.Session)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = -1;
                }

                if (options.EventFile != null)
                {
                    string path = Path.GetFullPath(options.EventFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    events = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    {
                        AutoFlush = true
                    };
                }

                if (options.Session)
                    keyboard = new KeyboardCommands();
                var replay = options.ReplayFile != null ? new CommandReplay(options.ReplayFile, options.ReplayRound) : null;
                if (options.Learning)
                {
                    academy = Academy.Instance;
                    if (!academy.IsCommunicatorOn)
                        throw new InvalidOperationException("El simulador de entrenamiento requiere mlagents-learn.");
                    if (Array.IndexOf(args, "--gf-seed") < 0)
                        options.Seed = UnityEngine.Random.Range(0, int.MaxValue);
                }
                else if (options.Roles.Any(role => role.StartsWith("model:", StringComparison.Ordinal)) ||
                    options.Session && Resources.Load<Unity.InferenceEngine.ModelAsset>(FighterAgent.ModelFolder + "/Champion"))
                {
                    academy = Academy.Instance;
                    academy.InferenceSeed = options.Seed;
                }

                arenas = new Arena[options.Arenas];
                for (int i = 0; i < arenas.Length; i++)
                    arenas[i] = new Arena(transform, new Vector3(i * 42, 0, 0), options, i, Sink(), keyboard, replay);
                Debug.Log($"GANGFIGHT_INTERFACE {FighterAgent.Behavior} observations={FighterAgent.ObservationCount}");
                if (options.Session)
                {
                    var cameraHost = new GameObject("Cámara");
                    cameraHost.transform.SetParent(transform, false);
                    ConfigureView(cameraHost.AddComponent<Camera>(), arenas[0]);
                    gameObject.AddComponent<PlayHud>().Connect(arenas[0]);
                }
                else if (!options.Learning)
                    Time.timeScale = options.Smoke ? 6 : 20;

                if (Academy.IsInitialized)
                {
                    academy = Academy.Instance;
                    academy.AutomaticSteppingEnabled = false;
                    academy.EnvironmentStep();
                    if (options.Learning)
                        academy.OnEnvironmentReset += ResetArenas;
                }
                benchmarkStart = Time.realtimeSinceStartup;
                nextBenchmark = benchmarkStart + 60;
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Application.Quit(1);
                return false;
            }
        }

        private static void ConfigureView(Camera camera, Arena arena)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.086f, .116f, .17f);
            camera.fieldOfView = 32;
            camera.transform.position = arena.Center + new Vector3(0, 1.66f, -3.6f) * arena.HalfSize;
            Vector3 aim = arena.Center + Vector3.up * .85f;
            camera.transform.rotation = Quaternion.LookRotation(aim - camera.transform.position, Vector3.up);
        }

        private Action<string> Sink() => events == null ? null : events.WriteLine;

        private void ResetArenas()
        {
            foreach (Arena arena in arenas)
                arena.Reset();
        }

        private void Update()
        {
            keyboard?.Poll();
        }

        private void FixedUpdate()
        {
            try
            {
                foreach (Arena arena in arenas)
                    arena.Prepare();
                academy?.EnvironmentStep();
                foreach (Arena arena in arenas)
                    arena.Apply(StepSeconds);
                Physics.Simulate(StepSeconds);
                foreach (Arena arena in arenas)
                    arena.Resolve(StepSeconds);

                if (options.Evaluate && arenas[0].Finished)
                {
                    events?.Flush();
                    enabled = false;
                    Application.Quit();
                }
                else if (options.Smoke)
                    TestSimulation();
                else if (options.Learning)
                    Benchmark();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                enabled = false;
                Application.Quit(1);
            }
        }

        private void Benchmark()
        {
            physicsTicks += arenas.Length;
            foreach (Arena arena in arenas)
                sentDecisions += arena.TakeLearnerDecisions();
            float now = Time.realtimeSinceStartup;
            if (now < nextBenchmark)
                return;

            float elapsed = now - benchmarkStart;
            string quantity = options.SelfPlay ? "PolicyDecisionsPerSecond" : "LearnerDecisionsPerSecond";
            academy.StatsRecorder.Add($"GangFight/{quantity}", sentDecisions / elapsed);
            academy.StatsRecorder.Add("GangFight/ArenaTicksPerSecond", physicsTicks / elapsed);
            Debug.Log($"GangFight · {sentDecisions / elapsed:F1} decisiones/s, {physicsTicks / elapsed:F1} pasos físicos/s");
            sentDecisions = physicsTicks = 0;
            benchmarkStart = now;
            nextBenchmark = now + 60;
        }

        private void TestSimulation()
        {
            Arena first = arenas[0];
            Arena second = arenas[1];
            smokeStep++;
            if (smokeStep == 1)
            {
                var command = new FighterCommand(-1, 1, (FighterButtons)63);
                var values = new Unity.MLAgents.Actuators.ActionSegment<int>(new[] { 1, 2, 1, 1, 1, 1, 1, 1 });
                var decoded = FighterAgent.Decode(values);
                Require(command.X == decoded.X && command.Z == decoded.Z && command.Buttons == decoded.Buttons, "command parity");
                var valuesWritten = new float[FighterAgent.ObservationCount];
                Require(FighterAgent.WriteObservations(first, first.Get(0), valuesWritten) == FighterAgent.ObservationCount,
                    "observation count");
                for (int i = 0; i < valuesWritten.Length; i++)
                    Require(!float.IsNaN(valuesWritten[i]) && !float.IsInfinity(valuesWritten[i]), "finite observations");
                first.Get(0).ReceiveImpact(10);
                Require(first.Get(0).KoTime > 0 && first.AliveMask == 15, "KO without elimination");
            }
            if (smokeStep == 104)
            {
                Require(first.Get(0).KoTime == 0 && !first.Finished, "KO recovery");
                first.Get(0).SetTestPosition(first.Center + Vector3.down * 3);
                first.Get(1).SetTestPosition(first.Center + Vector3.down * 3);
            }
            if (smokeStep == 105)
            {
                Require(first.AliveMask == 12 && !first.Finished && second.AliveMask == 15, "two falls and arena isolation");
                first.Get(2).SetTestPosition(first.Center + Vector3.down * 3);
                second.Get(0).SetTestPosition(second.Center + Vector3.down * 3);
                second.Get(1).SetTestPosition(second.Center + Vector3.down * 3);
                second.Get(2).SetTestPosition(second.Center + Vector3.down * 3);
                second.Get(3).SetTestPosition(second.Center + Vector3.down * 3);
            }
            if (smokeStep == 106)
            {
                Require(first.Finished && first.Winner == 3, "last survivor");
                Require(second.Finished && second.Winner == -1, "simultaneous draw");
                first.Reset();
                Require(first.AliveMask == 15 && !first.Finished, "round reset");
                first.Get(0).Reset(first.Center, Vector3.right);
                for (int i = 1; i < first.Count; i++)
                    first.Get(i).SetTestPosition(first.Center + new Vector3(-3, 1.04f, (i - 2) * 1.5f));
            }
            if (smokeStep == 133)
            {
                smokeOrigin = first.Get(0).Position;
                first.Get(0).SetCommand(new FighterCommand(1, 0));
            }
            if (smokeStep == 160)
            {
                Require(first.Get(0).Position.x - smokeOrigin.x > .20f, "movement from shared controls");
                Require(first.Get(0).CanJump, "settled fighter grounded");
                smokeOrigin = first.Get(0).Position;
                first.Get(0).SetCommand(new FighterCommand(0, 0, FighterButtons.Jump));
            }
            if (smokeStep == 171)
            {
                Require(first.Get(0).Position.y - smokeOrigin.y > .15f, "jump from shared controls");
                var camera = new GameObject("Cámara de prueba").AddComponent<Camera>();
                camera.enabled = false;
                camera.aspect = 4f / 3;
                ConfigureView(camera, first);
                foreach (int x in new[] { -1, 1 })
                    foreach (int z in new[] { -1, 1 })
                    {
                        Vector3 corner = first.Center + new Vector3(x, 0, z) * first.HalfSize;
                        Vector3 visible = camera.WorldToViewportPoint(corner);
                        Require(visible.x is > 0 and < 1 && visible.y is > 0 and < 1 && visible.z > 0,
                            "fixed sideline camera frames the arena");
                    }
                Destroy(camera.gameObject);
                new CombatSmoke(transform).Run();
                Debug.Log("GANGFIGHT_SMOKE_OK");
                enabled = false;
                Application.Quit();
            }
        }

        private static void Require(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException($"GangFight smoke failed: {name}");
        }

        private void OnDestroy()
        {
            if (academy != null)
            {
                academy.OnEnvironmentReset -= ResetArenas;
                if (Academy.IsInitialized && !academy.AutomaticSteppingEnabled)
                    academy.AutomaticSteppingEnabled = true;
            }
            if (configured)
            {
                Physics.simulationMode = previousSimulation;
                Time.timeScale = 1;
            }
            events?.Dispose();
        }
    }
}
