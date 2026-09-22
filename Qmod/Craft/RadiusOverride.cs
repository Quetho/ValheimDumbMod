using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    // Rayons réglables (section 11) : confort du reposé, zones de
    // construction établi / tailleur / artisan, et distance unique des
    // améliorations autour des stations. 0 = vanilla (pas touché).
    // Appliqué au spawn et réappliqué en direct au reload de la config.
    internal static class RadiusOverride
    {
        private static readonly Dictionary<string, float> vanillaRanges = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> vanillaUpgradeRanges = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Piece> comfortTemp = new List<Piece>();
        private static FieldInfo timerField;
        private static MethodInfo extensionsMethod;

        internal static void Apply(CraftingStation station)
        {
            if (!station)
            {
                return;
            }

            string prefab = Util.GetPrefabName(station.gameObject);
            if (string.IsNullOrEmpty(prefab))
            {
                return;
            }

            float? want = OverrideFor(prefab);
            if (!want.HasValue)
            {
                return;
            }

            if (!vanillaRanges.ContainsKey(prefab))
            {
                vanillaRanges[prefab] = station.m_rangeBuild;
            }

            float target = want.Value <= 0f ? vanillaRanges[prefab] : want.Value;
            if (Mathf.Approximately(station.m_rangeBuild, target))
            {
                return;
            }

            station.m_rangeBuild = target;
            ForceRecompute(station);
        }

        internal static void RefreshAll()
        {
            try
            {
                List<IMonoUpdater> stations = CraftingStation.Instances;
                if (stations != null)
                {
                    for (int i = 0; i < stations.Count; i++)
                    {
                        Apply(stations[i] as CraftingStation);
                    }
                }

                StationExtension[] extensions = UnityEngine.Object.FindObjectsByType<StationExtension>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < extensions.Length; i++)
                {
                    ApplyExtension(extensions[i]);
                }
            }
            catch
            {
            }
        }

        internal static void ApplyExtension(StationExtension extension)
        {
            if (!extension)
            {
                return;
            }

            string prefab = Util.GetPrefabName(extension.gameObject);
            if (string.IsNullOrEmpty(prefab))
            {
                return;
            }

            if (!vanillaUpgradeRanges.ContainsKey(prefab))
            {
                vanillaUpgradeRanges[prefab] = extension.m_maxStationDistance;
            }

            float want = Radius(ModConfig.UpgradeRadius);
            float target = want <= 0f ? vanillaUpgradeRanges[prefab] : want;
            if (!Mathf.Approximately(extension.m_maxStationDistance, target))
            {
                extension.m_maxStationDistance = target;
            }
        }

        private static float? OverrideFor(string prefab)
        {
            if (prefab.IndexOf("workbench", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Radius(ModConfig.WorkbenchRadius);
            }

            if (prefab.IndexOf("stonecutter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Radius(ModConfig.StonecutterRadius);
            }

            if (prefab.IndexOf("artisanstation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Radius(ModConfig.ArtisanRadius);
            }

            return null;
        }

        private static float Radius(ConfigEntry<float> entry)
        {
            return entry != null ? entry.Value : 0f;
        }

        // GetExtensions recalcule m_buildRange (avec bonus d'extensions),
        // l'anneau de marquage et le collider, mais pas plus d'une fois
        // toutes les 2 s : on force le timer pour une appli immédiate.
        private static void ForceRecompute(CraftingStation station)
        {
            try
            {
                if (timerField == null)
                {
                    timerField = typeof(CraftingStation).GetField("m_updateExtensionTimer",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }

                if (extensionsMethod == null)
                {
                    extensionsMethod = typeof(CraftingStation).GetMethod("GetExtensions",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }

                if (timerField == null || extensionsMethod == null)
                {
                    return;
                }

                timerField.SetValue(station, 99f);
                extensionsMethod.Invoke(station, null);
            }
            catch
            {
            }
        }

        [HarmonyPatch(typeof(CraftingStation), "Start")]
        private static class StationSpawnPatch
        {
            private static void Postfix(CraftingStation __instance)
            {
                Apply(__instance);
            }
        }

        [HarmonyPatch(typeof(StationExtension), "Awake")]
        private static class ExtensionSpawnPatch
        {
            private static void Postfix(StationExtension __instance)
            {
                ApplyExtension(__instance);
            }
        }

        // Le rayon confort vanilla est une constante compilée (10 m) :
        // on réimplémente la collecte avec le rayon configuré.
        [HarmonyPatch(typeof(SE_Rested), "GetNearbyComfortPieces")]
        private static class ComfortPatch
        {
            private static bool Prefix(Vector3 point, ref List<Piece> __result)
            {
                ConfigEntry<float> entry = ModConfig.ComfortRadius;
                if (entry == null || entry.Value <= 0f)
                {
                    return true;
                }

                comfortTemp.Clear();
                Piece.GetAllComfortPiecesInRadius(point, entry.Value, comfortTemp);
                __result = comfortTemp;
                return false;
            }
        }
    }
}
