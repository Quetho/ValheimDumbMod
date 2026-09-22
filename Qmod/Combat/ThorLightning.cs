using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    internal static class ThorLightning
    {
        private const string LightningPrefab = "lightningAOE";
        private const string IncineratorPrefab = "incinerator";
        private const string SuccessToken = "$piece_incinerator_success";
        private const float Cooldown = 1.5f;

        private static GameObject cachedPrefab;
        private static float nextStrike;

        internal static void Strike()
        {
            if (ModConfig.LightningEnabled == null || !ModConfig.LightningEnabled.Value)
            {
                return;
            }

            Player player = Util.ActingPlayer();
            if (!player)
            {
                return;
            }

            if (!ModConfig.IsOdin())
            {
                Util.NotifyPlayer(player, "Odin ne répond pas");
                return;
            }

            if (Time.unscaledTime < nextStrike)
            {
                return;
            }

            nextStrike = Time.unscaledTime + Cooldown;

            if (ModConfig.LightningMessage != null && ModConfig.LightningMessage.Value)
            {
                player.Message(MessageHud.MessageType.Center, SuccessToken, 0, null, false);
            }

            Play(player.transform.position);
        }

        internal static bool Play(Vector3 position)
        {
            return Play(position, Quaternion.identity, 1f);
        }

        internal static bool PlayDirected(Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            float dist = delta.magnitude;
            if (dist < 1.2f)
            {
                return Play(end);
            }

            Vector3 dir = delta / dist;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
            const float nativeHeight = 22f;
            float scaleY = Mathf.Clamp(dist / nativeHeight, 0.2f, 5f);
            return Play(start, rot, scaleY);
        }

        internal static bool Play(Vector3 position, Quaternion rotation, float scaleY)
        {
            GameObject prefab = ResolvePrefab();
            if (!prefab)
            {
                Jotunn.Logger.LogWarning("Prefab éclair introuvable");
                return false;
            }

            GameObject fx = Object.Instantiate(prefab, position, rotation);
            if (!fx)
            {
                return false;
            }

            if (!Mathf.Approximately(scaleY, 1f))
            {
                fx.transform.localScale = new Vector3(1f, scaleY, 1f);
                ZNetView view = fx.GetComponent<ZNetView>();
                if (view && view.IsValid())
                {
                    view.GetZDO().Set(ZDOVars.s_scaleHash, fx.transform.localScale);
                    view.SyncScale();
                }
            }

            DisableDamage(fx);
            return true;
        }

        private static GameObject ResolvePrefab()
        {
            if (cachedPrefab)
            {
                return cachedPrefab;
            }

            GameObject found;
            if (Util.TryGetPrefab(LightningPrefab, out found))
            {
                cachedPrefab = found;
                return cachedPrefab;
            }

            GameObject incinerator;
            if (!Util.TryGetPrefab(IncineratorPrefab, out incinerator))
            {
                return cachedPrefab;
            }

            Incinerator component = incinerator.GetComponent<Incinerator>();
            if (component && component.m_lightingAOEs)
            {
                cachedPrefab = component.m_lightingAOEs;
            }

            return cachedPrefab;
        }

        private static void DisableDamage(GameObject fx)
        {
            foreach (Aoe aoe in fx.GetComponentsInChildren<Aoe>(true))
            {
                aoe.enabled = false;
                aoe.m_hitCharacters = false;
                aoe.m_hitOwner = false;
                aoe.m_hitFriendly = false;
                aoe.m_hitEnemy = false;
                aoe.m_hitProps = false;
                aoe.m_hitTerrain = false;
                Object.Destroy(aoe);
            }
        }

        [HarmonyPatch(typeof(ZNetScene), "Shutdown")]
        private static class ZNetSceneShutdownPatch
        {
            private static void Prefix()
            {
                cachedPrefab = null;
            }
        }
    }
}
