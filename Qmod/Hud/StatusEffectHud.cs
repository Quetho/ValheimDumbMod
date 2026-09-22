using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Qmod
{
    internal static class StatusEffectHud
    {
        private const float RefreshInterval = 0.2f;
        private static GameObject root;
        private static Text label;
        private static float timer;
        private static readonly StringBuilder builder = new StringBuilder(256);

        internal static void EnsureCreated(Hud hud)
        {
            if (!ModConfig.StatusHudEnabled.Value)
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
                Jotunn.Logger.LogInfo("HUD des effets créé (bas droite)");
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogError("Impossible de créer le HUD des effets: " + e);
            }
        }

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

        private static void Create(Hud hud, Transform parent)
        {
            root = new GameObject("QmodStatusHud", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            root.transform.SetParent(parent, false);
            root.transform.SetAsLastSibling();

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-16f, 120f);
            rect.sizeDelta = new Vector2(340f, 400f);

            label = root.GetComponent<Text>();
            label.font = ResolveFont(hud);
            label.fontSize = 16;
            label.color = Color.white;
            label.alignment = TextAnchor.LowerRight;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.text = "";

            Outline outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private static Font ResolveFont(Hud hud)
        {
            foreach (Text existing in hud.GetComponentsInChildren<Text>(true))
            {
                if (existing && existing.font)
                {
                    return existing.font;
                }
            }

            if (GUIManager.Instance != null && GUIManager.Instance.AveriaSerifBold)
            {
                return GUIManager.Instance.AveriaSerifBold;
            }

            Font arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (arial)
            {
                return arial;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        internal static void Refresh()
        {
            if (!label || !root)
            {
                return;
            }

            if (!ModConfig.StatusHudEnabled.Value)
            {
                root.SetActive(false);
                return;
            }

            if (UnarmedHudHider.IsHiding)
            {
                root.SetActive(false);
                return;
            }

            Player player = Player.m_localPlayer;
            if (!player)
            {
                root.SetActive(false);
                return;
            }

            builder.Length = 0;
            AppendTotals(player);
            label.text = builder.ToString();
            root.SetActive(builder.Length > 0);
        }

        private static void AppendTotals(Player player)
        {
            SEMan seman = player.GetSEMan();
            float healthRegen = 1f;
            float staminaRegen = 1f;
            float eitrRegen = 1f;
            float carry = 0f;
            float stealth = 1f;
            float noise = 1f;
            float fall = 1f;
            float speed = 1f + player.GetEquipmentMovementModifier();
            float swim = 1f;
            float armorMul = 1f;
            float armorFlat = 0f;
            if (seman != null)
            {
                seman.ModifyHealthRegen(ref healthRegen);
                seman.ModifyStaminaRegen(ref staminaRegen);
                seman.ModifyEitrRegen(ref eitrRegen);
                seman.ModifyMaxCarryWeight(0f, ref carry);
                seman.ModifyStealth(1f, ref stealth);
                seman.ModifyNoise(1f, ref noise);
                seman.ModifyFallDamage(1f, ref fall);
            }

            List<StatusEffect> effects = seman != null ? seman.GetStatusEffects() : null;
            if (effects != null)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    SE_Stats stats = effects[i] as SE_Stats;
                    if (stats == null)
                    {
                        continue;
                    }

                    speed += stats.m_speedModifier;
                    swim += stats.m_swimSpeedModifier;
                    armorFlat += stats.m_addArmor;
                    if (!IsZero(stats.m_armorMultiplier))
                    {
                        armorMul *= 1f + stats.m_armorMultiplier;
                    }
                }
            }

            AppendMul("Dégâts", CombinedDamage(seman));
            AppendMul("Régén. vie", healthRegen);
            AppendMul("Régén. stamina", staminaRegen);
            AppendMul("Régén. eitr", eitrRegen);
            AppendMul("Vitesse", speed);
            AppendMul("Nage", swim);
            AppendMul("Armure", armorMul);
            AppendMul("Course (stamina)", 1f + player.GetEquipmentModifierPlusSE(9));
            AppendMul("Attaque (stamina)", 1f + player.GetEquipmentModifierPlusSE(4));
            AppendMul("Saut (stamina)", 1f + player.GetEquipmentModifierPlusSE(3));
            AppendMul("Furtivité", stealth);
            AppendMul("Bruit", noise);
            AppendMul("Chute", fall);
            AppendFlat("Poids", carry);
            AppendFlat("Armure", armorFlat);
        }

        private static float CombinedDamage(SEMan seman)
        {
            if (seman == null)
            {
                return 1f;
            }

            HitData hit = new HitData();
            hit.m_damage.m_slash = 100f;
            seman.ModifyAttack(Skills.SkillType.None, ref hit);
            return hit.m_damage.m_slash / 100f;
        }

        private static void AppendMul(string labelText, float multiplier)
        {
            if (IsOne(multiplier))
            {
                return;
            }

            AppendLine(labelText + " " + FormatMultiplier(multiplier), multiplier > 1f);
        }

        private static void AppendFlat(string labelText, float value)
        {
            if (IsZero(value))
            {
                return;
            }

            string sign = value > 0f ? "+" : "";
            AppendLine(labelText + " " + sign + value.ToString("0.##", CultureInfo.InvariantCulture), value > 0f);
        }

        private static void AppendLine(string text, bool bonus)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append("<color=").Append(bonus ? "#8CFF8C" : "#FF7A7A").Append('>')
                .Append(text).Append("</color>");
        }

        private static string FormatMultiplier(float value)
        {
            return "x" + value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static bool IsOne(float value)
        {
            return Mathf.Abs(value - 1f) < 0.001f;
        }

        private static bool IsZero(float value)
        {
            return Mathf.Abs(value) < 0.001f;
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        private static class HudAwakePatch
        {
            private static void Postfix(Hud __instance)
            {
                root = null;
                label = null;
                EnsureCreated(__instance);
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
        [HarmonyPriority(Priority.Last)]
        private static class HudUpdatePatch
        {
            private static void Postfix(Hud __instance)
            {
                UnarmedHudHider.Tick(__instance);
                Tick(__instance);
            }
        }
    }
}
