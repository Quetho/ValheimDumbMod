using UnityEngine;

namespace Qmod
{
    internal static class Util
    {
        internal static string GetPrefabName(GameObject gameObject)
        {
            if (!gameObject)
            {
                return "";
            }

            string name = gameObject.name;
            int clone = name.IndexOf("(Clone)");
            return clone >= 0 ? name.Substring(0, clone).Trim() : name.Trim();
        }

        internal static bool IsMenuBlocking()
        {
            if (Menu.IsVisible() || Menu.IsActive() || InventoryGui.IsVisible())
            {
                return true;
            }

            Chat chat = Chat.instance;
            return chat && chat.HasFocus();
        }

        // Message centré via le HUD global. Mêmes paramètres que les appels directs.
        internal static void NotifyCenter(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || !MessageHud.instance)
            {
                return;
            }

            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message, 0, null, true, false);
        }

        // Message centré via le joueur. Mêmes paramètres que les appels directs.
        internal static void NotifyPlayer(Player player, string message)
        {
            if (!player || string.IsNullOrEmpty(message))
            {
                return;
            }

            player.Message(MessageHud.MessageType.Center, message, 0, null, false);
        }

        internal static bool TryGetPrefab(string name, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrWhiteSpace(name) || !ZNetScene.instance)
            {
                return false;
            }

            prefab = ZNetScene.instance.GetPrefab(name.Trim());
            return prefab;
        }

        internal static bool IsAlive(Player player)
        {
            return player && !player.IsDead();
        }

        internal static bool CanAct(Player player)
        {
            return player && !player.IsDead() && !player.IsTeleporting();
        }

        internal static Player AlivePlayer()
        {
            Player player = Player.m_localPlayer;
            return IsAlive(player) ? player : null;
        }

        internal static Player ActingPlayer()
        {
            Player player = Player.m_localPlayer;
            return CanAct(player) ? player : null;
        }

        internal static Vector3 Flatten(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude < 0.0001f)
            {
                return fallback;
            }

            return value.normalized;
        }

        internal static Vector3 FlatForward(float yaw)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }
    }
}
