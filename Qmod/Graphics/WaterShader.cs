using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    // Look visuel sur Custom/Water. Pas de déplacement de vertices, pas de tessellation.
    // Sans Unity Editor on ne peut pas compiler un shader de remplacement : on pousse
    // les propriétés fragment du shader vanilla (normals, réfraction, foam, gloss).
    internal static class WaterShader
    {
        private static readonly int IdNormalPower = Shader.PropertyToID("_NormalPower");
        private static readonly int IdNormalScale = Shader.PropertyToID("_NormalScale");
        private static readonly int IdGlossiness = Shader.PropertyToID("_Glossiness");
        private static readonly int IdWaveVel = Shader.PropertyToID("_WaveVel");
        private static readonly int IdRefractionScale = Shader.PropertyToID("_RefractionScale");
        private static readonly int IdRefractionMax = Shader.PropertyToID("_RefractionMax");
        private static readonly int IdFoamDepth = Shader.PropertyToID("_FoamDepth");
        private static readonly int IdShoreFade = Shader.PropertyToID("_ShoreFade");
        private static readonly int IdDepthFade = Shader.PropertyToID("_DepthFade");
        private static readonly int IdFoamColor = Shader.PropertyToID("_FoamColor");
        private static readonly int IdSurfaceColor = Shader.PropertyToID("_SurfaceColor");

        private static readonly Dictionary<int, Snapshot> originals = new Dictionary<int, Snapshot>();
        private static bool applied;

        internal static bool IsActive => applied;

        internal static void Init()
        {
            Refresh();
        }

        internal static void Shutdown()
        {
            if (WaterVolume.Instances != null)
            {
                List<WaterVolume> volumes = WaterVolume.Instances;
                for (int i = 0; i < volumes.Count; i++)
                {
                    WaterVolume volume = volumes[i];
                    if (volume)
                    {
                        CaptureAndApply(volume.m_waterSurface, false);
                    }
                }
            }

            if (Water.Instances != null)
            {
                List<Water> waters = Water.Instances;
                for (int i = 0; i < waters.Count; i++)
                {
                    Water water = waters[i];
                    if (water)
                    {
                        CaptureAndApply(water.m_meshRenderer, false);
                    }
                }
            }

            originals.Clear();
            applied = false;
        }

        internal static void Refresh()
        {
            bool on = Enabled;
            if (WaterVolume.Instances != null)
            {
                List<WaterVolume> volumes = WaterVolume.Instances;
                for (int i = 0; i < volumes.Count; i++)
                {
                    WaterVolume volume = volumes[i];
                    if (volume)
                    {
                        CaptureAndApply(volume.m_waterSurface, on);
                    }
                }
            }

            if (Water.Instances != null)
            {
                List<Water> waters = Water.Instances;
                for (int i = 0; i < waters.Count; i++)
                {
                    Water water = waters[i];
                    if (water)
                    {
                        CaptureAndApply(water.m_meshRenderer, on);
                    }
                }
            }

            applied = on;
        }

        internal static void NotifyToggle()
        {
            Refresh();
            string text = Enabled ? "Eau Qmod ON" : "Eau Qmod OFF";
            Jotunn.Logger.LogInfo(text);
            Util.NotifyCenter(text);
        }

        private static bool Enabled
        {
            get { return ModConfig.WaterShaderEnabled != null && ModConfig.WaterShaderEnabled.Value; }
        }

        private static void CaptureAndApply(MeshRenderer renderer, bool on)
        {
            if (!renderer)
            {
                return;
            }

            Material mat = renderer.material;
            if (!mat)
            {
                return;
            }

            int id = mat.GetInstanceID();
            Snapshot snap;
            if (!originals.TryGetValue(id, out snap))
            {
                snap = Snapshot.Capture(mat);
                originals[id] = snap;
            }

            if (on)
            {
                snap.ApplyLook(mat);
            }
            else
            {
                snap.Restore(mat);
            }
        }

        private struct Snapshot
        {
            public bool hasNormalPower;
            public bool hasNormalScale;
            public bool hasGlossiness;
            public bool hasWaveVel;
            public bool hasRefractionScale;
            public bool hasRefractionMax;
            public bool hasFoamDepth;
            public bool hasShoreFade;
            public bool hasDepthFade;
            public bool hasFoamColor;
            public bool hasSurfaceColor;
            public float normalPower;
            public float normalScale;
            public float glossiness;
            public float waveVel;
            public float refractionScale;
            public float refractionMax;
            public float foamDepth;
            public float shoreFade;
            public float depthFade;
            public Color foamColor;
            public Color surfaceColor;

            public static Snapshot Capture(Material mat)
            {
                Snapshot snap = default;
                snap.hasNormalPower = TryGetFloat(mat, IdNormalPower, out snap.normalPower);
                snap.hasNormalScale = TryGetFloat(mat, IdNormalScale, out snap.normalScale);
                snap.hasGlossiness = TryGetFloat(mat, IdGlossiness, out snap.glossiness);
                snap.hasWaveVel = TryGetFloat(mat, IdWaveVel, out snap.waveVel);
                snap.hasRefractionScale = TryGetFloat(mat, IdRefractionScale, out snap.refractionScale);
                snap.hasRefractionMax = TryGetFloat(mat, IdRefractionMax, out snap.refractionMax);
                snap.hasFoamDepth = TryGetFloat(mat, IdFoamDepth, out snap.foamDepth);
                snap.hasShoreFade = TryGetFloat(mat, IdShoreFade, out snap.shoreFade);
                snap.hasDepthFade = TryGetFloat(mat, IdDepthFade, out snap.depthFade);
                snap.hasFoamColor = TryGetColor(mat, IdFoamColor, out snap.foamColor);
                snap.hasSurfaceColor = TryGetColor(mat, IdSurfaceColor, out snap.surfaceColor);
                return snap;
            }

            public void ApplyLook(Material mat)
            {
                if (hasNormalPower)
                {
                    mat.SetFloat(IdNormalPower, Boost(normalPower, 1.45f, 1.2f));
                }

                if (hasNormalScale)
                {
                    mat.SetFloat(IdNormalScale, Boost(normalScale, 1.12f, 1f));
                }

                if (hasGlossiness)
                {
                    mat.SetFloat(IdGlossiness, Mathf.Clamp01(Mathf.Max(glossiness, 0.9f)));
                }

                if (hasWaveVel)
                {
                    mat.SetFloat(IdWaveVel, Boost(waveVel, 1.18f, 0.4f));
                }

                if (hasRefractionScale)
                {
                    mat.SetFloat(IdRefractionScale, Boost(refractionScale, 1.55f, 0.08f));
                }

                if (hasRefractionMax)
                {
                    mat.SetFloat(IdRefractionMax, Boost(refractionMax, 1.35f, 0.15f));
                }

                if (hasFoamDepth)
                {
                    mat.SetFloat(IdFoamDepth, Boost(foamDepth, 1.3f, 0.4f));
                }

                if (hasShoreFade)
                {
                    mat.SetFloat(IdShoreFade, Boost(shoreFade, 1.2f, 0.3f));
                }

                if (hasDepthFade)
                {
                    mat.SetFloat(IdDepthFade, Boost(depthFade, 1.15f, 0.5f));
                }

                if (hasFoamColor)
                {
                    mat.SetColor(IdFoamColor, Color.Lerp(foamColor, new Color(0.95f, 0.97f, 1f, foamColor.a), 0.28f));
                }

                if (hasSurfaceColor)
                {
                    mat.SetColor(IdSurfaceColor, Color.Lerp(surfaceColor, Color.white, 0.12f));
                }
            }

            public void Restore(Material mat)
            {
                if (hasNormalPower)
                {
                    mat.SetFloat(IdNormalPower, normalPower);
                }

                if (hasNormalScale)
                {
                    mat.SetFloat(IdNormalScale, normalScale);
                }

                if (hasGlossiness)
                {
                    mat.SetFloat(IdGlossiness, glossiness);
                }

                if (hasWaveVel)
                {
                    mat.SetFloat(IdWaveVel, waveVel);
                }

                if (hasRefractionScale)
                {
                    mat.SetFloat(IdRefractionScale, refractionScale);
                }

                if (hasRefractionMax)
                {
                    mat.SetFloat(IdRefractionMax, refractionMax);
                }

                if (hasFoamDepth)
                {
                    mat.SetFloat(IdFoamDepth, foamDepth);
                }

                if (hasShoreFade)
                {
                    mat.SetFloat(IdShoreFade, shoreFade);
                }

                if (hasDepthFade)
                {
                    mat.SetFloat(IdDepthFade, depthFade);
                }

                if (hasFoamColor)
                {
                    mat.SetColor(IdFoamColor, foamColor);
                }

                if (hasSurfaceColor)
                {
                    mat.SetColor(IdSurfaceColor, surfaceColor);
                }
            }
        }

        private static float Boost(float original, float mul, float fallback)
        {
            if (Mathf.Abs(original) < 0.0001f)
            {
                return fallback;
            }

            return original * mul;
        }

        private static bool TryGetFloat(Material mat, int id, out float value)
        {
            if (mat.HasProperty(id))
            {
                value = mat.GetFloat(id);
                return true;
            }

            value = 0f;
            return false;
        }

        private static bool TryGetColor(Material mat, int id, out Color value)
        {
            if (mat.HasProperty(id))
            {
                value = mat.GetColor(id);
                return true;
            }

            value = Color.white;
            return false;
        }

        [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.SetupMaterial))]
        private static class SetupMaterialPatch
        {
            private static void Postfix(WaterVolume __instance)
            {
                CaptureAndApply(__instance.m_waterSurface, Enabled);
            }
        }

        [HarmonyPatch(typeof(Water), nameof(Water.Awake))]
        private static class WaterAwakePatch
        {
            private static void Postfix(Water __instance)
            {
                CaptureAndApply(__instance.m_meshRenderer, Enabled);
            }
        }
    }
}
