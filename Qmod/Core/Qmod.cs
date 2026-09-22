using System.IO;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace Qmod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    internal class Qmod : BaseUnityPlugin
    {
        public const string PluginGUID = "com.aeons.qmod";
        public const string PluginName = "Qmod";
        public const string PluginVersion = "1.0.79";

        internal static Qmod Instance { get; private set; }

        private FileSystemWatcher configWatcher;
        private volatile bool configReloadPending;
        private float reloadAt;

        private void Awake()
        {
            Instance = this;
            ModConfig.Bind(Config);
            SuperSampling.Init();
            WaterShader.Init();
            WatchConfigFile();
            Harmony.CreateAndPatchAll(typeof(Qmod).Assembly, PluginGUID);
            bool navPatched = false;
            foreach (System.Reflection.MethodBase patched in Harmony.GetAllPatchedMethods())
            {
                if (patched.DeclaringType == typeof(BuildUi) && patched.Name == "NavigationUpdate")
                {
                    navPatched = true;
                    break;
                }
            }

            Jotunn.Logger.LogInfo("BuildPull: patch NavigationUpdate applique=" + navPatched);
            if (!GUIManager.IsHeadless())
            {
                GUIManager.OnCustomGUIAvailable += ThorTp.OnGuiAvailable;
            }

            Jotunn.Logger.LogInfo("Qmod has landed");
        }

        private void WatchConfigFile()
        {
            string directory = Path.GetDirectoryName(Config.ConfigFilePath);
            string fileName = Path.GetFileName(Config.ConfigFilePath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                return;
            }

            configWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            configWatcher.Changed += (_, __) =>
            {
                configReloadPending = true;
                reloadAt = Time.unscaledTime + 0.25f;
            };
            configWatcher.EnableRaisingEvents = true;
        }

        private void Update()
        {
            ModInput.Tick();

            if (!configReloadPending || Time.unscaledTime < reloadAt)
            {
                return;
            }

            configReloadPending = false;
            Config.Reload();
            WaterShader.Refresh();
            RadiusOverride.RefreshAll();
            Jotunn.Logger.LogInfo("Config Qmod rechargée");
        }

        private void OnDestroy()
        {
            SuperSampling.Shutdown();
            WaterShader.Shutdown();

            if (configWatcher != null)
            {
                configWatcher.Dispose();
                configWatcher = null;
            }

            if (!GUIManager.IsHeadless())
            {
                GUIManager.OnCustomGUIAvailable -= ThorTp.OnGuiAvailable;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
