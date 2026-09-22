using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    internal static class UnarmedHudHider
    {
        private static float unarmedTime;

        internal static bool IsHiding { get; private set; }

        internal static void Tick(Hud hud)
        {
            if (!hud)
            {
                return;
            }

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
