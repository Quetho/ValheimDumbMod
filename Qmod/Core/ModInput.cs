using BepInEx.Configuration;
using UnityEngine;

namespace Qmod
{
    internal static class ModInput
    {
        internal static void Tick()
        {
            if (Pressed(ThorLightning.MenuBind))
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

            if (DumpAllowed() && Pressed(ModConfig.ChestDump))
            {
                ChestDump.TryDump();
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
                CycleCameraMode();
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
                SwapActiveShoulder();
            }

            if (Pressed(ModConfig.ToggleUnarmedHudHide))
            {
                Toggle(ModConfig.UnarmedHudHideEnabled, "Masquage HUD mains vides");
            }

            if (Pressed(ModConfig.ToggleCultivateHarvest))
            {
                Toggle(ModConfig.CultivateHarvestEnabled, "Récolte cultivateur");
            }

            if (Pressed(MagicBush.ToggleBind))
            {
                MagicBush.Toggle();
            }

            if (Pressed(ThorLightning.StrikeBind))
            {
                ThorLightning.Strike();
            }

            if (Pressed(ThorLightning.AimBind))
            {
                ThorBolt.Cast();
            }

            if (Pressed(ModConfig.ToggleHugin))
            {
                Toggle(ModConfig.HuginDisabled, "Hugin coupé");
            }
        }

        // L'inventaire ouvert ne bloque pas le rangement. Le menu pause et le chat oui.
        private static bool DumpAllowed()
        {
            if (Menu.IsVisible() || Menu.IsActive())
            {
                return false;
            }

            Chat chat = Chat.instance;
            return !chat || !chat.HasFocus();
        }

        private static bool Pressed(ConfigEntry<KeyboardShortcut> entry)
        {
            return entry != null && Pressed(entry.Value);
        }

        private static bool Pressed(KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && shortcut.IsDown();
        }

        internal static bool Held(ConfigEntry<KeyboardShortcut> entry)
        {
            return entry != null && Held(entry.Value);
        }

        internal static bool Held(KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && shortcut.IsPressed();
        }

        private static void CycleCameraMode()
        {
            if (ModConfig.CameraMode == null)
            {
                return;
            }

            ShoulderCameraMode next;
            switch (ModConfig.CameraMode.Value)
            {
                case ShoulderCameraMode.Yotei:
                    next = ShoulderCameraMode.AutoShoulder;
                    break;
                case ShoulderCameraMode.AutoShoulder:
                    next = ShoulderCameraMode.Seidr;
                    break;
                case ShoulderCameraMode.Seidr:
                    next = ShoulderCameraMode.Off;
                    break;
                default:
                    next = ShoulderCameraMode.Yotei;
                    break;
            }

            ModConfig.CameraMode.Value = next;
            Jotunn.Logger.LogInfo("Caméra : " + next);
        }

        private static void SwapActiveShoulder()
        {
            if (ModConfig.CameraMode == null)
            {
                return;
            }

            if (ModConfig.CameraMode.Value == ShoulderCameraMode.AutoShoulder)
            {
                AutoShoulderCamera.ManualSwap();
            }
            else if (ModConfig.CameraMode.Value == ShoulderCameraMode.Seidr)
            {
                SeidrCamera.ManualSwap();
            }
            else if (ModConfig.CameraMode.Value == ShoulderCameraMode.Yotei)
            {
                YoteiCamera.ManualSwap();
            }
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
