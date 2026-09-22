using HarmonyLib;

namespace Qmod
{
    internal static class HuginMute
    {
        internal static bool IsMuted =>
            ModConfig.HuginDisabled != null && ModConfig.HuginDisabled.Value;

        [HarmonyPatch(typeof(Player), nameof(Player.ShowTutorial))]
        private static class ShowTutorialPatch
        {
            private static bool Prefix()
            {
                return !IsMuted;
            }
        }

        [HarmonyPatch(typeof(Tutorial), nameof(Tutorial.ShowText))]
        private static class ShowTextPatch
        {
            private static bool Prefix()
            {
                return !IsMuted;
            }
        }

        [HarmonyPatch(typeof(Tutorial), nameof(Tutorial.SpawnRaven))]
        private static class SpawnRavenPatch
        {
            private static bool Prefix()
            {
                return !IsMuted;
            }
        }

        [HarmonyPatch(typeof(Raven), nameof(Raven.CheckSpawn))]
        private static class CheckSpawnPatch
        {
            private static bool Prefix(Raven __instance)
            {
                if (!IsMuted || !__instance || __instance.m_isMunin)
                {
                    return true;
                }

                if (__instance.IsSpawned())
                {
                    __instance.FlyAway(true);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(Raven), nameof(Raven.Spawn))]
        private static class SpawnPatch
        {
            private static bool Prefix(Raven __instance)
            {
                if (!IsMuted || !__instance || __instance.m_isMunin)
                {
                    return true;
                }

                return false;
            }
        }
    }
}
