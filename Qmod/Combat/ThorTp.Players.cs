using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;

namespace Qmod
{
    internal static partial class ThorTp
    {
        private const float PullDistance = 1f;

        private const float PullCooldown = 2f;

        private const string RpcPull = "QmodThorPull";

        private static bool rpcRegistered;

        private static float nextPull;

        private static void RefreshPlayers()
        {
            if (!listContent)
            {
                return;
            }

            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(listContent.GetChild(i).gameObject);
            }

            if (!ZNet.instance)
            {
                AddHint("Pas de serveur.");
                return;
            }

            Player local = Player.m_localPlayer;
            ZDOID localId = local ? local.GetZDOID() : ZDOID.None;
            List<ZNet.PlayerInfo> players = ZNet.instance.GetPlayerList();
            int added = 0;
            for (int i = 0; i < players.Count; i++)
            {
                ZNet.PlayerInfo info = players[i];
                if (info.m_characterID.IsNone() || info.m_characterID == localId)
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(info.m_name) ? "Joueur" : info.m_name;
                AddPlayerRow(name, info.m_characterID);
                added++;
            }

            if (added == 0)
            {
                AddHint("Aucun autre joueur connecté.");
            }
        }

        private static void AddPlayerRow(string name, ZDOID id)
        {
            GameObject row = new GameObject("player", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(listContent, false);
            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            rowLayout.minHeight = 36f;
            rowLayout.preferredHeight = 36f;
            HorizontalLayoutGroup rowGroup = row.GetComponent<HorizontalLayoutGroup>();
            rowGroup.childAlignment = TextAnchor.MiddleCenter;
            rowGroup.childControlHeight = true;
            rowGroup.childControlWidth = true;
            rowGroup.childForceExpandHeight = true;
            rowGroup.childForceExpandWidth = false;
            rowGroup.spacing = 6f;

            GameObject nameGo = new GameObject("name", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            nameGo.transform.SetParent(row.transform, false);
            LayoutElement nameLayout = nameGo.GetComponent<LayoutElement>();
            nameLayout.minWidth = 150f;
            nameLayout.preferredWidth = 150f;
            nameLayout.flexibleWidth = 1f;
            nameLayout.minHeight = 36f;
            nameLayout.preferredHeight = 36f;
            nameGo.GetComponent<Image>().color = new Color(0.12f, 0.1f, 0.08f, 0.9f);

            GameObject textGo = new GameObject("text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(nameGo.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 0f);
            textRt.offsetMax = new Vector2(-8f, 0f);
            Text label = textGo.GetComponent<Text>();
            label.font = UiFont();
            label.fontSize = 16;
            label.color = new Color(0.95f, 0.88f, 0.7f, 1f);
            label.alignment = TextAnchor.MiddleLeft;
            label.text = name;
            label.raycastTarget = false;

            ZDOID captured = id;
            string capturedName = name;
            GameObject pull = MakeButton(row.transform, "TP", new Color(0.28f, 0.2f, 0.12f, 0.95f), 0f, 52f, 36f, () => PullPlayer(captured, capturedName));
            GameObject go = MakeButton(row.transform, "Aller", new Color(0.22f, 0.28f, 0.16f, 0.95f), 0f, 68f, 36f, () => GoToPlayer(captured));
            Text pullText = pull.GetComponentInChildren<Text>();
            if (pullText)
            {
                pullText.fontSize = 14;
            }

            Text goText = go.GetComponentInChildren<Text>();
            if (goText)
            {
                goText.fontSize = 14;
            }
        }

        private static void PullPlayer(ZDOID id, string name)
        {
            if (id.IsNone() || Time.unscaledTime < nextPull)
            {
                return;
            }

            Player caster = Player.m_localPlayer;
            if (!CanTravel(caster))
            {
                return;
            }

            long peer = ResolvePeerId(id, name);
            if (peer == 0 || Chat.instance == null)
            {
                Util.NotifyPlayer(caster, "Impossible de TP ce joueur");
                return;
            }

            Vector3 dest;
            float yaw;
            GetPullPose(caster, out dest, out yaw);
            dest.y += LandLift;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            Vector3 from;
            float fromYaw;
            bool hasFrom = TryGetPlayerWorldPos(id, out from, out fromYaw);
            bool distant = !hasFrom || Vector3.Distance(from, dest) > DistantRange;

            Chat.instance.TeleportPlayer(peer, dest, rot, distant);

            EnsureRpc();
            if (ZRoutedRpc.instance != null)
            {
                ZPackage pkg = new ZPackage();
                pkg.Write(id);
                pkg.Write(dest);
                pkg.Write(yaw);
                ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcPull, pkg);
            }

            nextPull = Time.unscaledTime + PullCooldown;
            Close();
            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(PlayBeats(DestStorm, dest, null));
                bool playSource = hasFrom && (!distant || from != Vector3.zero);
                if (playSource)
                {
                    Qmod.Instance.StartCoroutine(PlayBeats(SourceStorm, from, null));
                }
            }
        }

        private static long ResolvePeerId(ZDOID characterId, string name)
        {
            if (!ZNet.instance || characterId.IsNone())
            {
                return 0;
            }

            List<ZNetPeer> peers = ZNet.instance.GetPeers();
            if (peers != null)
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    ZNetPeer peer = peers[i];
                    if (peer == null || !peer.IsReady())
                    {
                        continue;
                    }

                    if (peer.m_characterID == characterId)
                    {
                        return peer.m_uid;
                    }

                    if (!string.IsNullOrEmpty(name) && string.Equals(peer.m_playerName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return peer.m_uid;
                    }
                }
            }

            if (!string.IsNullOrEmpty(name))
            {
                ZNetPeer named = ZNet.instance.GetPeerByPlayerName(name);
                if (named != null)
                {
                    return named.m_uid;
                }
            }

            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player other = players[i];
                if (!other || other.GetZDOID() != characterId || !other.m_nview || !other.m_nview.IsValid())
                {
                    continue;
                }

                long owner = other.m_nview.GetZDO().GetOwner();
                if (owner != 0)
                {
                    return owner;
                }
            }

            long user = characterId.UserID;
            if (user != 0 && user != ZNet.GetUID())
            {
                return user;
            }

            return 0;
        }

        private static void GoToPlayer(ZDOID id)
        {
            if (traveling || id.IsNone())
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (!CanTravel(player))
            {
                return;
            }

            Vector3 around;
            float theirYaw;
            if (!TryGetPlayerWorldPos(id, out around, out theirYaw))
            {
                Util.NotifyPlayer(player, "Position inconnue");
                return;
            }

            Vector3 dest;
            float yaw;
            if (!FindClearLanding(around, theirYaw, false, out dest, out yaw))
            {
                FallbackBeside(around, theirYaw, out dest, out yaw);
            }

            Close();
            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(TravelBeside(id, around, theirYaw, dest, yaw));
            }
        }

        private static bool TryGetPlayerWorldPos(ZDOID id, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero;
            yaw = 0f;
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player other = players[i];
                if (!other || other.GetZDOID() != id)
                {
                    continue;
                }

                pos = other.transform.position;
                yaw = other.transform.eulerAngles.y;
                return true;
            }

            if (ZNet.instance)
            {
                List<ZNet.PlayerInfo> infos = ZNet.instance.GetPlayerList();
                for (int i = 0; i < infos.Count; i++)
                {
                    ZNet.PlayerInfo info = infos[i];
                    if (info.m_characterID != id || !info.m_publicPosition)
                    {
                        continue;
                    }

                    pos = info.m_position;
                    return true;
                }
            }

            if (ZNetScene.instance)
            {
                GameObject instance = ZNetScene.instance.FindInstance(id);
                if (instance)
                {
                    pos = instance.transform.position;
                    yaw = instance.transform.eulerAngles.y;
                    return true;
                }
            }

            if (ZDOMan.instance != null)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo != null)
                {
                    pos = zdo.GetPosition();
                    yaw = zdo.GetRotation().eulerAngles.y;
                    return true;
                }
            }

            return false;
        }

        private static void GetPullPose(Player caster, out Vector3 dest, out float yaw)
        {
            Vector3 around = caster.transform.position;
            float aroundYaw = caster.transform.eulerAngles.y;
            if (FindClearLanding(around, aroundYaw, true, out dest, out yaw))
            {
                return;
            }

            Vector3 forward = Util.FlatForward(aroundYaw);
            dest = around + forward * PullDistance;
            yaw = Quaternion.LookRotation(-forward).eulerAngles.y;
            SnapToSupport(around.y, ref dest);
        }

        private static void EnsureRpc()
        {
            if (rpcRegistered || ZRoutedRpc.instance == null)
            {
                return;
            }

            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcPull, OnPull);
                rpcRegistered = true;
            }
            catch (ArgumentException)
            {
                rpcRegistered = true;
            }
        }

        private static void OnPull(long sender, ZPackage pkg)
        {
            if (pkg == null)
            {
                return;
            }

            pkg.SetPos(0);
            ZDOID id = pkg.ReadZDOID();
            Vector3 dest = pkg.ReadVector3();
            float yaw = pkg.ReadSingle();
            Player player = Util.AlivePlayer();
            if (!player || player.GetZDOID() != id)
            {
                return;
            }

            if (traveling || player.IsTeleporting())
            {
                return;
            }

            Close();
            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(TravelTo(dest, yaw));
            }
        }

        [HarmonyPatch(typeof(Game), "Start")]
        private static class GameStartPatch
        {
            private static void Postfix()
            {
                rpcRegistered = false;
                EnsureRpc();
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        private static class ZNetDestroyPatch
        {
            private static void Prefix()
            {
                rpcRegistered = false;
            }
        }
    }
}
