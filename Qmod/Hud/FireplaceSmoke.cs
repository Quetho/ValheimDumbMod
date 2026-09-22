using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    // Affiche l'évacuation de la fumée dans le survol des feux (même menu
    // que le combustible). Miroir exact de CheckUnderTerrain : enterré,
    // plafond trop bas (rayon 0.5 m) ou fumée du spawner bloquée.
    // Que des membres publics : le masque est reconstruit comme dans Awake.
    internal static class FireplaceSmoke
    {
        internal static bool IsEnabled =>
            ModConfig.FireplaceSmokeEnabled != null && ModConfig.FireplaceSmokeEnabled.Value;

        private static int solidMask;
        private static bool maskReady;

        internal static bool IsVentingBlocked(Fireplace fireplace)
        {
            Vector3 pos = fireplace.transform.position;
            float ground;
            if (Heightmap.GetHeight(pos, out ground) && ground > pos.y + fireplace.m_checkTerrainOffset)
            {
                return true;
            }

            if (Physics.Raycast(pos + Vector3.up * fireplace.m_coverCheckOffset, Vector3.up, 0.5f, SolidMask()))
            {
                return true;
            }

            return fireplace.m_smokeSpawner && fireplace.m_smokeSpawner.IsBlocked();
        }

        private static int SolidMask()
        {
            if (!maskReady)
            {
                maskReady = true;
                solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            }

            return solidMask;
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
        private static class HoverPatch
        {
            private static void Postfix(Fireplace __instance, ref string __result)
            {
                if (!IsEnabled || __instance == null || string.IsNullOrEmpty(__result))
                {
                    return;
                }

                if (__instance.m_disableCoverCheck)
                {
                    return;
                }

                __result += IsVentingBlocked(__instance)
                    ? "\nFumée : <color=red>bloquée</color>"
                    : "\nFumée : <color=green>évacuée</color>";
            }
        }
    }
}
