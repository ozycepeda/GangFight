using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GangFight.Editor
{
    public static class BuildGangFight
    {
        private const string PlayScene = "Assets/GangFight/Scenes/Play.unity";
        private const string TrainingScene = "Assets/GangFight/Scenes/Training.unity";

        [MenuItem("GangFight/Abrir arena jugable")]
        public static void OpenArena()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(PlayScene);
        }

        [MenuItem("GangFight/Compilar/Linux para entrenamiento")]
        public static void Linux() => Build(BuildTarget.StandaloneLinux64, "builds/Linux/GangFight.x86_64", true);
        [MenuItem("GangFight/Compilar/macOS")]
        public static void Mac() => Build(BuildTarget.StandaloneOSX, "builds/macOS/GangFight.app", false);
        [MenuItem("GangFight/Compilar/macOS para entrenamiento")]
        public static void MacTraining() => Build(BuildTarget.StandaloneOSX, "builds/macOS-training/GangFight.app", true);
        [MenuItem("GangFight/Compilar/Windows")]
        public static void Windows() => Build(BuildTarget.StandaloneWindows64, "builds/Windows/GangFight.exe", false);

        private static void Build(BuildTarget target, string destination, bool training)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException($"Instala el módulo de compilación para {target}.");
            PlayerSettings.productName = "GangFight";
            PlayerSettings.companyName = "GangFight";
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            // Runtime-created primitive meshes and physics components must be available in player builds.
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedStrippingLevel.Disabled);
            if (target == BuildTarget.StandaloneOSX)
                PlayerSettings.SetArchitecture(UnityEditor.Build.NamedBuildTarget.Standalone, 1);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var request = new BuildPlayerOptions
            {
                target = target,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                scenes = training ? new[] { TrainingScene, PlayScene } : new[] { PlayScene, TrainingScene },
                locationPathName = destination,
                options = BuildOptions.StrictMode
            };
            BuildReport result = BuildPipeline.BuildPlayer(request);
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"La compilación de {target} terminó con {result.summary.result}.");
            Debug.Log($"GANGFIGHT_BUILD_OK {destination}");
        }
    }
}
