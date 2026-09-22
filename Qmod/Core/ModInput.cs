using BepInEx.Configuration;
using UnityEngine;

namespace Qmod
{
    internal static class ModInput
    {
        internal static void Tick()
        {
            if (Pressed(ModConfig.ToggleTpMenu))
            {
                ThorTp.Toggle();
                return;
            }

            if (ThorTp.IsOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    ThorTp.Close();
                }

                return;
            }

            if (Util.IsMenuBlocking())
            {
                return;
            }

            if (Pressed(ModConfig.ToggleStatusHud))
            {
                Toggle(ModConfig.StatusHudEnabled, "HUD effets");
            }

            if (Pressed(ModConfig.ToggleYoteiCamera))
            {
                Toggle(ModConfig.YoteiCameraEnabled, "Caméra Yotei");
            }

            if (Pressed(ModConfig.ToggleCinematicIdle))
            {
                Toggle(ModConfig.CinematicIdleEnabled, "Caméra cinématique idle");
            }

            if (Pressed(ModConfig.StartCinematic))
            {
                CinematicIdleCamera.ForceStart();
            }

            if (Pressed(ModConfig.ToggleSupersampling))
            {
                Toggle(ModConfig.SupersamplingEnabled, "Supersampling");
                SuperSampling.NotifyToggle();
            }

            if (Pressed(ModConfig.ToggleWaterShader))
            {
                Toggle(ModConfig.WaterShaderEnabled, "Eau");
                WaterShader.NotifyToggle();
            }

            if (Pressed(ModConfig.SwapShoulder))
            {
                YoteiCamera.ManualSwap();
            }

            if (Pressed(ModConfig.ToggleUnarmedHudHide))
            {
                Toggle(ModConfig.UnarmedHudHideEnabled, "Masquage HUD mains vides");
            }

            if (Pressed(ModConfig.ToggleCultivateHarvest))
            {
                Toggle(ModConfig.CultivateHarvestEnabled, "Récolte cultivateur");
            }

            if (Pressed(ModConfig.ToggleMagicBush))
            {
                MagicBush.Toggle();
            }

            if (Pressed(ModConfig.StrikeLightning))
            {
                ThorLightning.Strike();
            }

            if (Pressed(ModConfig.AimLightning))
            {
                ThorBolt.Cast();
            }

            if (Pressed(ModConfig.SpawnBoar))
            {
                BoarSpawn.Spawn();
            }

            if (Pressed(ModConfig.ToggleHugin))
            {
                Toggle(ModConfig.HuginDisabled, "Hugin coupé");
            }
        }

        private static bool Pressed(ConfigEntry<KeyboardShortcut> entry)
        {
            if (entry == null)
            {
                return false;
            }

            KeyboardShortcut shortcut = entry.Value;
            return shortcut.MainKey != KeyCode.None && shortcut.IsDown();
        }

        internal static bool Held(ConfigEntry<KeyboardShortcut> entry)
        {
            if (entry == null)
            {
                return false;
            }

            KeyboardShortcut shortcut = entry.Value;
            return shortcut.MainKey != KeyCode.None && shortcut.IsPressed();
        }

        private static void Toggle(ConfigEntry<bool> entry, string label)
        {
            if (entry == null)
            {
                return;
            }

            entry.Value = !entry.Value;
            Jotunn.Logger.LogInfo(label + ": " + (entry.Value ? "on" : "off"));
        }
    }
}
