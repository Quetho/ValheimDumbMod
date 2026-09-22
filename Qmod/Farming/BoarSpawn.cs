using UnityEngine;

namespace Qmod
{
    internal static class BoarSpawn
    {
        private const string PrefabName = "Boar";
        private const int TwoStarLevel = 3;
        private const float Distance = 2.4f;
        private const float Cooldown = 1.2f;

        private static float nextSpawn;

        internal static void Spawn()
        {
            if (Time.unscaledTime < nextSpawn)
            {
                return;
            }

            Player player = Util.ActingPlayer();
            if (!player)
            {
                return;
            }

            if (!ZNetScene.instance)
            {
                return;
            }

            GameObject prefab;
            if (!Util.TryGetPrefab(PrefabName, out prefab))
            {
                Jotunn.Logger.LogWarning("Prefab Boar introuvable");
                return;
            }

            Vector3 pos;
            Quaternion rot;
            GetSpawnPose(player, out pos, out rot);

            nextSpawn = Time.unscaledTime + Cooldown;

            bool prev = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = false;
            GameObject go;
            try
            {
                go = Object.Instantiate(prefab, pos, rot);
            }
            finally
            {
                ZNetView.m_forceDisableInit = prev;
            }

            if (!go)
            {
                return;
            }

            Character character = go.GetComponent<Character>();
            if (character && character.m_nview && character.m_nview.IsValid())
            {
                character.SetLevel(TwoStarLevel);
            }

            Tameable tameable = go.GetComponent<Tameable>();
            if (tameable)
            {
                tameable.Tame();
            }
            else if (character)
            {
                character.SetTamed(true);
            }
        }

        private static void GetSpawnPose(Player player, out Vector3 pos, out Quaternion rot)
        {
            Vector3 forward = Util.Flatten(player.transform.forward, Vector3.forward);

            pos = player.transform.position + forward * Distance;
            rot = Quaternion.LookRotation(-forward);

            if (!ZoneSystem.instance)
            {
                return;
            }

            float height = ZoneSystem.instance.GetSolidHeight(pos);
            float playerY = player.transform.position.y;
            if (height > playerY - 4f && height < playerY + 6f)
            {
                pos.y = height;
            }
            else
            {
                pos.y = playerY;
            }
        }
    }
}
