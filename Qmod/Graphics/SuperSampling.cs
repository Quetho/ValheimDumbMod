using UnityEngine;

namespace Qmod
{
    // SSAA : la caméra rend dans un RenderTexture agrandi d'un multiplicateur,
    // puis blit vers l'écran. Camera.onPreRender / onPostRender, pas de Harmony.
    internal static class SuperSampling
    {
        private const int MaxHeight = 4320;
        private const int MaxWidth = 7680;

        private static RenderTexture rt;
        private static int appliedWidth;
        private static int appliedHeight;
        private static bool subscribed;
        private static bool diagLogged;
        private static bool creationFailed;
        private static int failedWidth;
        private static int failedHeight;

        internal static bool IsActive { get; private set; }
        internal static string LastState { get; private set; } = "inactif";
        internal static string LastReason { get; private set; } = "non évalué";

        internal static void Init()
        {
            if (subscribed)
            {
                return;
            }

            Camera.onPreRender += OnPreRender;
            Camera.onPostRender += OnPostRender;
            subscribed = true;
        }

        internal static void Shutdown()
        {
            if (subscribed)
            {
                Camera.onPreRender -= OnPreRender;
                Camera.onPostRender -= OnPostRender;
                subscribed = false;
            }

            Release();
        }

        internal static void NotifyToggle()
        {
            bool on = ModConfig.SupersamplingEnabled != null && ModConfig.SupersamplingEnabled.Value;
            string text;
            if (!on)
            {
                text = "Supersampling OFF";
            }
            else if (IsActive)
            {
                text = "Supersampling ON : " + LastState;
            }
            else
            {
                text = "Supersampling ON mais inactif : " + LastReason;
            }

            Jotunn.Logger.LogInfo(text);
            Util.NotifyCenter(text);
        }

        private static void OnPreRender(Camera cam)
        {
            if (!cam)
            {
                return;
            }

            GameCamera gameCamera = GameCamera.instance;
            if (!gameCamera || !gameCamera.m_camera)
            {
                return;
            }

            if (cam == gameCamera.m_skyCamera)
            {
                if (rt)
                {
                    if (cam.targetTexture != rt)
                    {
                        cam.targetTexture = rt;
                    }
                }
                else if (cam.targetTexture)
                {
                    cam.targetTexture = null;
                }

                return;
            }

            if (cam != gameCamera.m_camera)
            {
                return;
            }

            LogDiagOnce();

            if (!GetTargetSpec(out int width, out int height, out string reason))
            {
                Release();
                IsActive = false;
                LastState = "inactif";
                LastReason = reason;
                return;
            }

            if (rt == null || rt.width != width || rt.height != height || !rt.IsCreated())
            {
                Recreate(width, height);
            }

            if (rt == null)
            {
                IsActive = false;
                LastState = "inactif";
                LastReason = "création du RenderTexture impossible (GPU insuffisant ?)";
                return;
            }

            cam.targetTexture = rt;
            IsActive = true;
            LastState = $"actif {appliedWidth}x{appliedHeight} -> {Screen.width}x{Screen.height}";
            LastReason = "ok";
        }

        private static void OnPostRender(Camera cam)
        {
            if (rt == null || !rt.IsCreated() || !cam)
            {
                return;
            }

            GameCamera gameCamera = GameCamera.instance;
            if (!gameCamera || cam != gameCamera.m_camera || cam.targetTexture != rt)
            {
                return;
            }

            cam.targetTexture = null;
            Graphics.Blit(rt, (RenderTexture)null);
        }

        private static void LogDiagOnce()
        {
            if (diagLogged)
            {
                return;
            }

            diagLogged = true;
            bool enabled = ModConfig.SupersamplingEnabled != null && ModConfig.SupersamplingEnabled.Value;
            float scale = ModConfig.SupersamplingScale != null ? ModConfig.SupersamplingScale.Value : 0f;
            Jotunn.Logger.LogInfo($"SSAA diag : écran {Screen.width}x{Screen.height}, enabled={enabled}, scale={scale}");
        }

        private static bool GetTargetSpec(out int width, out int height, out string reason)
        {
            width = 0;
            height = 0;
            reason = "";

            if (ModConfig.SupersamplingEnabled == null || !ModConfig.SupersamplingEnabled.Value)
            {
                reason = "désactivé dans la config";
                return false;
            }

            int screenW = Screen.width;
            int screenH = Screen.height;
            if (screenW <= 0 || screenH <= 0)
            {
                reason = "résolution écran invalide";
                return false;
            }

            float scale = ModConfig.SupersamplingScale != null ? ModConfig.SupersamplingScale.Value : 1f;
            if (scale <= 1.001f)
            {
                reason = "multiplicateur ≤ 1 (aucun gain)";
                return false;
            }

            int targetW = Mathf.Clamp(Mathf.RoundToInt(screenW * scale), screenW, MaxWidth);
            int targetH = Mathf.Clamp(Mathf.RoundToInt(screenH * scale), screenH, MaxHeight);
            if (targetW <= screenW && targetH <= screenH)
            {
                reason = "cible = résolution native (aucun gain)";
                return false;
            }

            if (creationFailed && targetW == failedWidth && targetH == failedHeight)
            {
                reason = "création RT déjà échouée (change le multiplicateur pour réessayer)";
                return false;
            }

            creationFailed = false;
            width = targetW;
            height = targetH;
            return true;
        }

        private static void Recreate(int width, int height)
        {
            ReleaseTexture();
            try
            {
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = "QmodSSAA",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!rt.Create())
                {
                    throw new System.Exception("RenderTexture.Create a retourné false");
                }
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning("Supersampling désactivé (GPU insuffisant) : " + e.Message);
                ReleaseTexture();
                creationFailed = true;
                failedWidth = width;
                failedHeight = height;
                return;
            }

            appliedWidth = width;
            appliedHeight = height;
            Jotunn.Logger.LogInfo($"Supersampling {appliedWidth}x{appliedHeight} -> {Screen.width}x{Screen.height}");
        }

        private static void Release()
        {
            IsActive = false;
            if (rt)
            {
                GameCamera gameCamera = GameCamera.instance;
                if (gameCamera)
                {
                    if (gameCamera.m_camera && gameCamera.m_camera.targetTexture == rt)
                    {
                        gameCamera.m_camera.targetTexture = null;
                    }

                    if (gameCamera.m_skyCamera && gameCamera.m_skyCamera.targetTexture == rt)
                    {
                        gameCamera.m_skyCamera.targetTexture = null;
                    }
                }
            }

            ReleaseTexture();
        }

        private static void ReleaseTexture()
        {
            if (!rt)
            {
                return;
            }

            rt.Release();
            Object.Destroy(rt);
            rt = null;
        }
    }
}
