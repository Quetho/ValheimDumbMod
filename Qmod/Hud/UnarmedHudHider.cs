using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    internal static class UnarmedHudHider
    {
        private static float unarmedTime;
        private static Transform keepCrosshair;
        private static Vector3 keepCrosshairOrig;
        private static Transform keepStamina;
        private static Vector3 keepStaminaOrig;
        private static Transform keepMap;
        private static Vector3 keepMapOrig;

        internal static bool IsHiding { get; private set; }

        internal static void Tick(Hud hud)
        {
            if (!hud)
            {
                return;
            }

            UpdateState(hud);
            ApplyKeepVisible(hud);
        }

        private static void UpdateState(Hud hud)
        {
            if (!ModConfig.UnarmedHudHideEnabled.Value)
            {
                Reset();
                return;
            }

            Player player = Util.AlivePlayer();
            if (!player)
            {
                Reset();
                return;
            }

            if (HasWeaponOrToolInHands(player))
            {
                Reset();
                return;
            }

            if (Util.IsMenuBlocking())
            {
                if (IsHiding)
                {
                    hud.SetVisible(true);
                    IsHiding = false;
                }

                return;
            }

            if (CinematicIdleCamera.IsActive)
            {
                IsHiding = true;
                return;
            }

            unarmedTime += Time.deltaTime;
            float delay = ModConfig.UnarmedHudHideDelay.Value;
            if (unarmedTime < delay)
            {
                IsHiding = false;
                return;
            }

            IsHiding = true;
        }

        // Le masquage déplace m_rootObject hors écran (Hud.SetVisible) au
        // lieu de le désactiver : chaque élément "gardé" est re-compensé
        // par frame (position d'origine moins le décalage live du root).
        // Hors masquage, la position d'origine est restaurée. La cinématique
        // garde le masquage total (pas de compensation). Le crosshair d'arc
        // est ignoré : arc en main = pas de masquage de toute façon.
        // Minimap petite : UnarmedHudKeepMap, si enfant d'un root décalé
        // (HUD / Menu). Grande map : Minimap.IsOpen() débloque le HUD.
        private static void ApplyKeepVisible(Hud hud)
        {
            bool hiding = IsHiding && !CinematicIdleCamera.IsActive;
            Vector3 shift = Vector3.zero;
            if (hud.m_rootObject)
            {
                shift = hud.m_rootObject.transform.localPosition;
            }

            Transform cross = hud.m_crosshair ? hud.m_crosshair.transform : null;
            ApplyKeptElement(ref keepCrosshair, ref keepCrosshairOrig, cross, shift,
                hiding && ModConfig.UnarmedHudKeepCrosshair.Value);

            Transform stamina = hud.m_staminaBar2Root ? hud.m_staminaBar2Root.transform : null;
            ApplyKeptElement(ref keepStamina, ref keepStaminaOrig, stamina, shift,
                hiding && ModConfig.UnarmedHudKeepStamina.Value);

            Transform map = SmallMapTransform();
            ApplyKeptElement(ref keepMap, ref keepMapOrig, map, shift,
                hiding && ModConfig.UnarmedHudKeepMap.Value && IsUnderShiftedRoot(map, hud));
        }

        private static Transform SmallMapTransform()
        {
            Minimap map = Minimap.instance;
            if (!map || !map.m_smallRoot)
            {
                return null;
            }

            return map.m_smallRoot.transform;
        }

        private static bool IsUnderShiftedRoot(Transform t, Hud hud)
        {
            if (!t)
            {
                return false;
            }

            if (hud && hud.m_rootObject && t.IsChildOf(hud.m_rootObject.transform))
            {
                return true;
            }

            Menu menu = Menu.instance;
            return menu && menu.m_root && t.IsChildOf(menu.m_root);
        }

        private static void ApplyKeptElement(ref Transform cached, ref Vector3 orig, Transform current, Vector3 shift, bool keep)
        {
            if (keep && current)
            {
                if (cached != current)
                {
                    cached = current;
                    orig = current.localPosition;
                }

                current.localPosition = orig - shift;
                return;
            }

            if (cached)
            {
                cached.localPosition = orig;
            }

            cached = null;
        }

        [HarmonyPatch(typeof(Hud), "SetVisible")]
        private static class HudSetVisiblePatch
        {
            private static void Prefix(ref bool visible)
            {
                if (!visible || !IsHiding)
                {
                    return;
                }

                if (Util.IsMenuBlocking())
                {
                    return;
                }

                visible = false;
            }
        }

        private static void Reset()
        {
            unarmedTime = 0f;
            IsHiding = false;
        }

        private static bool HasWeaponOrToolInHands(Player player)
        {
            return IsWeaponOrTool(player.GetRightItem()) || IsWeaponOrTool(player.GetLeftItem());
        }

        private static bool IsWeaponOrTool(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
            {
                return false;
            }

            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Fish:
                    return true;
                default:
                    return false;
            }
        }
    }
}
