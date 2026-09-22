using System;
using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    // Monte des loups apprivoisés avec le système de selle natif (Asksvin) :
    // une Sadle est greffée sur chaque loup au spawn, puis configurée depuis
    // le prefab Asksvin au moment de monter (repli sur des valeurs par défaut).
    // Ctrl+E monte/descend ; E reste le câlin car le hover Tameable gagne
    // (la Sadle est ajoutée après, GetComponentInParent prend le premier).
    // Direction, stamina, HUD et multi : 100 % natif (MonsterAI + doodad).
    internal static class WolfRide
    {
        private const string WolfPrefab = "Wolf";
        private const string AsksvinPrefab = "Asksvin";
        private const string SaddleObjectName = "QmodWolfSaddle";
        private const string DefaultAttachAnimation = "attach_chair";
        private const float DefaultSaddleHeight = 0.85f;

        internal static bool IsEnabled =>
            ModConfig.WolfRideEnabled != null && ModConfig.WolfRideEnabled.Value;

        internal static float SaddleHeight()
        {
            if (ModConfig.WolfRideSaddleHeight == null)
            {
                return DefaultSaddleHeight;
            }

            return Mathf.Clamp(ModConfig.WolfRideSaddleHeight.Value, 0.2f, 1.5f);
        }

        internal static bool IsWolf(Tameable tame)
        {
            if (tame == null)
            {
                return false;
            }

            return WolfPrefab.Equals(Util.GetPrefabName(tame.gameObject), StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsRideableWolf(Tameable tame)
        {
            return IsEnabled && IsWolf(tame) && tame.IsTamed();
        }

        internal static void EnsureSaddle(Tameable tame)
        {
            if (tame == null || tame.m_saddle != null || tame.GetComponent<Sadle>() != null)
            {
                return;
            }

            if (!IsWolf(tame))
            {
                return;
            }

            Sadle sadle = tame.gameObject.AddComponent<Sadle>();
            sadle.m_attachPoint = CreateAttachPoint(tame.transform);
            sadle.m_attachAnimation = DefaultAttachAnimation;
            sadle.m_maxUseRange = 4f;
            sadle.m_detachOffset = new Vector3(1.5f, 0.3f, 0f);
            sadle.m_hoverOffset = 0f;
            sadle.m_maxStamina = 100f;
            sadle.m_runStaminaDrain = 6f;
            sadle.m_swimStaminaDrain = 10f;
            sadle.m_staminaRegen = 12f;
            sadle.m_staminaRegenHungry = 4f;
            sadle.m_hoverText = "";
            tame.m_saddle = sadle;
            // Pas de visuel ni d'objet selle : rien à faire tomber à la mort.
            tame.m_dropSaddleOnDeath = false;
        }

        private static Transform CreateAttachPoint(Transform parent)
        {
            GameObject go = new GameObject(SaddleObjectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, SaddleHeight(), 0.05f);
            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }

        // Le slider s'applique en direct, y compris en pleine chevauchée.
        internal static void SyncSaddleHeight(Tameable tame)
        {
            if (tame == null || tame.m_saddle == null)
            {
                return;
            }

            Transform attach = tame.m_saddle.m_attachPoint;
            if (attach == null || attach.name != SaddleObjectName)
            {
                return;
            }

            float wanted = SaddleHeight();
            Vector3 pos = attach.localPosition;
            if (Mathf.Abs(pos.y - wanted) > 0.001f)
            {
                pos.y = wanted;
                attach.localPosition = pos;
            }
        }

        // Idempotent, appelé à chaque montée (ZNetScene est prêt à ce stade,
        // contrairement au Awake pendant le chargement du monde).
        internal static void CopyAsksvinConfig(Sadle sadle)
        {
            if (sadle == null)
            {
                return;
            }

            try
            {
                GameObject prefab;
                if (!Util.TryGetPrefab(AsksvinPrefab, out prefab) || !prefab)
                {
                    return;
                }

                Sadle template = prefab.GetComponentInChildren<Sadle>();
                if (!template)
                {
                    return;
                }

                sadle.m_attachAnimation = template.m_attachAnimation;
                sadle.m_maxUseRange = template.m_maxUseRange;
                sadle.m_detachOffset = template.m_detachOffset;
                sadle.m_hoverOffset = template.m_hoverOffset;
                sadle.m_maxStamina = template.m_maxStamina;
                sadle.m_runStaminaDrain = template.m_runStaminaDrain;
                sadle.m_swimStaminaDrain = template.m_swimStaminaDrain;
                sadle.m_staminaRegen = template.m_staminaRegen;
                sadle.m_staminaRegenHungry = template.m_staminaRegenHungry;
                sadle.m_drownEffects = template.m_drownEffects;
                sadle.m_mountIcon = template.m_mountIcon;
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("WolfRide: config Asksvin illisible : " + e.Message);
            }
        }

        internal static bool IsRidingWolf(Player player, Tameable tame)
        {
            return player && tame && tame.m_saddle &&
                ReferenceEquals(player.GetDoodadController(), tame.m_saddle);
        }

        private static bool IsCtrlDown()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.Awake))]
        private static class TameableAwakePatch
        {
            private static void Postfix(Tameable __instance)
            {
                if (IsEnabled)
                {
                    EnsureSaddle(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.Update))]
        private static class TameableUpdatePatch
        {
            private static void Postfix(Tameable __instance)
            {
                if (IsEnabled)
                {
                    SyncSaddleHeight(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Interact))]
        private static class InteractPatch
        {
            private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt)
            {
                if (!IsEnabled || __instance == null || __instance != Player.m_localPlayer)
                {
                    return true;
                }

                // E maintenu (répétition) et Shift+E (renommer) : vanilla.
                if (hold || alt || !IsCtrlDown() || MagicBush.IsActive)
                {
                    return true;
                }

                if (go == null)
                {
                    return true;
                }

                Tameable tame = go.GetComponentInParent<Tameable>();
                if (tame == null || !IsRideableWolf(tame))
                {
                    return true;
                }

                Sadle sadle = tame.m_saddle ? tame.m_saddle : tame.GetComponent<Sadle>();
                if (sadle == null)
                {
                    EnsureSaddle(tame);
                    sadle = tame.m_saddle;
                }

                if (sadle == null)
                {
                    return true;
                }

                IDoodadController current = __instance.GetDoodadController();
                if (current != null && !ReferenceEquals(current, sadle))
                {
                    return true;
                }

                if (current != null)
                {
                    __instance.StopDoodadControl();
                    Jotunn.Logger.LogInfo("WolfRide: descente");
                }
                else
                {
                    CopyAsksvinConfig(sadle);
                    sadle.Interact(__instance, false, false);
                    Jotunn.Logger.LogInfo("WolfRide: montée demandée");
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
        private static class HoverPatch
        {
            private static void Postfix(Tameable __instance, ref string __result)
            {
                if (!IsRideableWolf(__instance))
                {
                    return;
                }

                string line = IsRidingWolf(Player.m_localPlayer, __instance) ? "Descendre" : "Monter";
                string raw = "\n[<color=yellow><b>Ctrl + $KEY_Use</b></color>] " + line;
                __result += Localization.instance != null ? Localization.instance.Localize(raw) : raw;
            }
        }
    }
}
