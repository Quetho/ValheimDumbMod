using System;
using HarmonyLib;

namespace Qmod
{
    internal static class BoarButcher
    {
        private const string ButcherKnifePrefab = "KnifeButcher";
        private static Character lastPouikVictim;

        internal static void TryPouik(Character victim, HitData hit)
        {
            if (hit == null || victim == lastPouikVictim)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (!player || hit.GetAttacker() != player)
            {
                return;
            }

            if (!IsBoar(victim) || !IsButcherKnife(player))
            {
                return;
            }

            lastPouikVictim = victim;
            ShowPouik();
        }

        private static void ShowPouik()
        {
            Util.NotifyCenter(ModConfig.KillMessage.Value);
        }

        private static bool IsBoar(Character character)
        {
            if (!character)
            {
                return false;
            }

            string prefab = Util.GetPrefabName(character.gameObject);
            return prefab == "Boar" || prefab == "Boar_piggy";
        }

        private static bool IsButcherKnife(Player player)
        {
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon == null)
            {
                return false;
            }

            if (weapon.m_dropPrefab && Util.GetPrefabName(weapon.m_dropPrefab) == ButcherKnifePrefab)
            {
                return true;
            }

            string sharedName = weapon.m_shared != null ? weapon.m_shared.m_name : null;
            return !string.IsNullOrEmpty(sharedName) &&
                   sharedName.IndexOf("butcher", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class CharacterOnDeathPatch
        {
            private static void Postfix(Character __instance)
            {
                TryPouik(__instance, __instance.m_lastHit);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class CharacterDamagePatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (__instance.m_nview && __instance.m_nview.IsOwner())
                {
                    return;
                }

                if (__instance.IsDead() || !__instance.IsTamed())
                {
                    return;
                }

                TryPouik(__instance, hit);
            }
        }
    }
}
