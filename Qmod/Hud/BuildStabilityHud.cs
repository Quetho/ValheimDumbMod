using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Qmod
{
    // Mode build (Player.InPlaceMode) : à droite du crosshair, la stabilité
    // réelle de la pièce visée (WearNTear.GetSupport, en % du max).
    internal static class BuildStabilityHud
    {
        private const float RefreshInterval = 0.25f;
        private static GameObject root;
        private static Text label;
        private static float timer;
        private static readonly StringBuilder builder = new StringBuilder(128);

        internal static void Tick(Hud hud)
        {
            EnsureCreated(hud);
            if (!root || !label)
            {
                return;
            }

            timer += Time.unscaledDeltaTime;
            if (timer < RefreshInterval)
            {
                return;
            }

            timer = 0f;
            Refresh();
        }

        internal static void EnsureCreated(Hud hud)
        {
            if (ModConfig.BuildStabilityHudEnabled == null || !ModConfig.BuildStabilityHudEnabled.Value)
            {
                if (root)
                {
                    root.SetActive(false);
                }

                return;
            }

            if (!hud)
            {
                return;
            }

            Transform parent = hud.m_rootObject ? hud.m_rootObject.transform : hud.transform;
            if (root && root.transform.parent != parent)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
                label = null;
            }

            if (root)
            {
                return;
            }

            try
            {
                Create(hud, parent);
                Jotunn.Logger.LogInfo("HUD stabilité build créé (droite crosshair)");
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogError("Impossible de créer le HUD stabilité: " + e);
            }
        }

        private static void Create(Hud hud, Transform parent)
        {
            root = new GameObject("QmodBuildStabilityHud", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            root.transform.SetParent(parent, false);
            root.transform.SetAsLastSibling();

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(48f, 0f);
            rect.sizeDelta = new Vector2(220f, 64f);

            label = root.GetComponent<Text>();
            label.font = StatusEffectHud.ResolveFont(hud);
            label.fontSize = 15;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.text = "";

            Outline outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private static void Refresh()
        {
            builder.Length = 0;
            Player player = Player.m_localPlayer;
            if (player && player.InPlaceMode())
            {
                Piece hovered = player.GetHoveringPiece();
                if (hovered)
                {
                    WearNTear worn = hovered.GetComponent<WearNTear>();
                    if (worn && worn.GetMaxSupport() > 0f)
                    {
                        int pct = Mathf.RoundToInt(Mathf.Clamp01(worn.GetSupport() / worn.GetMaxSupport()) * 100f);
                        builder.Append("<color=#").Append(SupportColor(worn.GetSupport(), worn.GetMaxSupport(), worn.GetMinSupport())).Append('>')
                            .Append("Visée ").Append(pct).Append("%</color>");
                    }
                }
            }

            label.text = builder.ToString();
            root.SetActive(builder.Length > 0);
        }

        // Couleurs vanilla : bleu au max (ancré), sinon dégradé rouge->vert
        // via la même normalisation que GetSupportColorValue.
        private static string SupportColor(float support, float max, float min)
        {
            Color color;
            if (support >= max * 0.999f)
            {
                color = new Color(0.25f, 0.6f, 1f);
            }
            else
            {
                float denom = 0.5f * max - min;
                float v = denom > 0f ? Mathf.Clamp01((support - min) / denom) : 1f;
                color = Color.HSVToRGB(v / 3f, 0.85f, 1f);
            }

            return ColorUtility.ToHtmlStringRGB(color);
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
        [HarmonyPriority(Priority.Last)]
        private static class HudUpdatePatch
        {
            private static void Postfix(Hud __instance)
            {
                Tick(__instance);
            }
        }
    }
}
