using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    // Les selles natives (loup greffé, Asksvin, futures) survivent aux TP :
    // quand le joueur local se téléporte en pilotant une Sadle, la monture
    // est déplacée sur le joueur (transform + corps + ZDO) au lieu de rester
    // sur place, et le démontage auto pour cause de distance est suspendu
    // pendant le TP. Si la monture a été déchargée entre-temps, son ZDO est
    // déplacé et une remontée est tentée à sa réapparition. Un démontage
    // volontaire (E) annule le suivi : comportement vanilla.
    internal static class RideCarry
    {
        private const float FollowDistance = 5f;
        private const float GraceSeconds = 2.5f;
        private const float RemountRetrySeconds = 0.5f;

        private static ZDOID pendingId = ZDOID.None;
        private static Sadle pendingSaddle;
        private static bool saddleLost;
        private static float graceUntil;
        private static float nextRemount;

        internal static bool IsEnabled =>
            ModConfig.MountCarryEnabled != null && ModConfig.MountCarryEnabled.Value;

        internal static bool TryGetRiddenSaddle(Player player, out Sadle saddle)
        {
            saddle = null;
            if (!player)
            {
                return false;
            }

            saddle = player.GetDoodadController() as Sadle;
            if (!saddle)
            {
                saddle = null;
                return false;
            }

            return true;
        }

        // Saut direct sans TeleportTo (recalage ThorTp proche) : la monture
        // suit le joueur tout de suite, sans suivi prolongé.
        internal static void FollowNow(Player player)
        {
            if (!IsEnabled || player == null || player != Player.m_localPlayer)
            {
                return;
            }

            Sadle saddle;
            if (!TryGetRiddenSaddle(player, out saddle))
            {
                return;
            }

            Character mount = saddle.GetCharacter();
            if (!mount)
            {
                return;
            }

            if (Vector3.Distance(mount.transform.position, player.transform.position) > FollowDistance)
            {
                TeleportMountToPlayer(player, mount);
            }
        }

        internal static void Tick()
        {
            if (!pendingSaddle && pendingId.IsNone())
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (!IsEnabled || !player || player.IsDead())
            {
                Clear();
                return;
            }

            Sadle saddle = pendingSaddle;
            if (!saddle)
            {
                saddleLost = true;
                saddle = ResolveById(pendingId);
                pendingSaddle = saddle;
            }

            if (!saddle)
            {
                // Objet déchargé (cas distant) : le ZDO voyage quand même, la
                // remontée est tentée à la réapparition, après le TP.
                TeleportMountZdo(pendingId, player);
                if (!player.IsTeleporting())
                {
                    if (graceUntil <= 0f)
                    {
                        graceUntil = Time.time + GraceSeconds;
                    }
                    else if (Time.time > graceUntil)
                    {
                        Clear();
                    }
                }

                return;
            }

            if (!ReferenceEquals(player.GetDoodadController(), saddle))
            {
                if (!saddleLost)
                {
                    // Selle vivante mais plus pilotée : démontage volontaire.
                    Clear();
                    return;
                }

                // Monture réapparue après déchargement : remontée (limitée),
                // seulement si le joueur est libre et le TP terminé.
                if (!player.IsTeleporting() && player.GetDoodadController() == null &&
                    Time.time >= nextRemount &&
                    Vector3.Distance(saddle.transform.position, player.transform.position) <= FollowDistance)
                {
                    nextRemount = Time.time + RemountRetrySeconds;
                    TryRemount(player, saddle);
                }

                if (!player.IsTeleporting())
                {
                    if (graceUntil <= 0f)
                    {
                        graceUntil = Time.time + GraceSeconds;
                    }
                    else if (Time.time > graceUntil)
                    {
                        Clear();
                    }
                }

                return;
            }

            saddleLost = false;
            Character mount = saddle.GetCharacter();
            if (!mount)
            {
                Clear();
                return;
            }

            if (Vector3.Distance(mount.transform.position, player.transform.position) > FollowDistance)
            {
                TeleportMountToPlayer(player, mount);
            }

            if (player.IsTeleporting())
            {
                graceUntil = 0f;
                return;
            }

            if (graceUntil <= 0f)
            {
                graceUntil = Time.time + GraceSeconds;
            }
            else if (Time.time > graceUntil)
            {
                Clear();
            }
        }

        private static void Capture(Player player)
        {
            Clear();
            if (!IsEnabled || player == null || player != Player.m_localPlayer)
            {
                return;
            }

            Sadle saddle;
            if (!TryGetRiddenSaddle(player, out saddle))
            {
                return;
            }

            pendingSaddle = saddle;
            Character mount = saddle.GetCharacter();
            pendingId = mount ? mount.GetZDOID() : ZDOID.None;
            Jotunn.Logger.LogInfo("RideCarry: suivi monture au TP");
        }

        private static void Clear()
        {
            pendingId = ZDOID.None;
            pendingSaddle = null;
            saddleLost = false;
            graceUntil = 0f;
            nextRemount = 0f;
        }

        private static Sadle ResolveById(ZDOID id)
        {
            if (id.IsNone() || !ZNetScene.instance)
            {
                return null;
            }

            GameObject instance = ZNetScene.instance.FindInstance(id);
            if (!instance)
            {
                return null;
            }

            Sadle saddle = instance.GetComponentInChildren<Sadle>();
            return saddle ? saddle : null;
        }

        private static void TryRemount(Player player, Sadle saddle)
        {
            Tameable tame = saddle.GetTameable();
            if (tame && WolfRide.IsWolf(tame))
            {
                WolfRide.CopyAsksvinConfig(saddle);
            }

            saddle.Interact(player, false, false);
            Jotunn.Logger.LogInfo("RideCarry: remontée demandée");
        }

        private static void TeleportMountToPlayer(Player player, Character mount)
        {
            Vector3 dest = player.transform.position;
            Quaternion rot = player.transform.rotation;
            mount.transform.SetPositionAndRotation(dest, rot);
            Rigidbody body = mount.m_body;
            if (body)
            {
                body.position = dest;
                body.rotation = rot;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            ZNetView nview = mount.m_nview;
            if (nview && nview.IsValid())
            {
                if (!nview.IsOwner())
                {
                    nview.ClaimOwnership();
                }

                ZDO zdo = nview.GetZDO();
                if (zdo != null && zdo.IsOwner())
                {
                    zdo.SetPosition(dest);
                    zdo.SetRotation(rot);
                }
            }
        }

        private static void TeleportMountZdo(ZDOID id, Player player)
        {
            if (id.IsNone() || ZDOMan.instance == null)
            {
                return;
            }

            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null || !zdo.IsOwner())
            {
                return;
            }

            zdo.SetPosition(player.transform.position);
            zdo.SetRotation(player.transform.rotation);
        }

        // Portail vanilla, ThorTp, console : tout passe par TeleportTo.
        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        private static class TeleportToPatch
        {
            private static void Postfix(Player __instance, bool __result)
            {
                if (__result)
                {
                    Capture(__instance);
                }
            }
        }

        // Pendant le TP, la distance joueur/selle explose un instant : le
        // démontage auto vanilla casserait la chevauchée avant l'arrivée.
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateDoodadControls))]
        private static class UpdateDoodadControlsPatch
        {
            private static bool Prefix(Player __instance)
            {
                if (!IsEnabled || __instance == null || __instance != Player.m_localPlayer)
                {
                    return true;
                }

                if (!__instance.IsTeleporting())
                {
                    return true;
                }

                Sadle saddle;
                if (!TryGetRiddenSaddle(__instance, out saddle))
                {
                    return true;
                }

                return false;
            }
        }
    }
}
