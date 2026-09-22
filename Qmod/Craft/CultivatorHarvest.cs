using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    internal static class CultivatorHarvest
    {
        private const string CultivatePrefab = "cultivate";
        private const string CultivatePieceToken = "$piece_cultivate";
        private const float FallbackRadius = 2.5f;
        private const float WalkPickupRange = 0.8f;

        private static bool uprooting;
        private static readonly Collider[] overlapBuffer = new Collider[64];
        private static readonly HashSet<int> seenPickables = new HashSet<int>();

        internal static void TryHarvest(TerrainOp terrainOp)
        {
            if (!ModConfig.CultivateHarvestEnabled.Value)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (!player || !terrainOp)
            {
                return;
            }

            Piece piece = terrainOp.GetComponent<Piece>();
            if (!IsCultivatePiece(piece))
            {
                return;
            }

            float radius = terrainOp.GetRadius();
            if (radius <= 0f)
            {
                radius = FallbackRadius;
            }

            UprootCrops(terrainOp.transform.position, radius);
        }

        private static bool IsCultivatePiece(Piece piece)
        {
            if (!piece)
            {
                return false;
            }

            string prefab = Util.GetPrefabName(piece.gameObject);
            return prefab == CultivatePrefab || piece.m_name == CultivatePieceToken;
        }

        private static void UprootCrops(Vector3 position, float radius)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(position, radius, overlapBuffer);
            seenPickables.Clear();
            int uprooted = 0;

            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = overlapBuffer[i];
                if (!collider)
                {
                    continue;
                }

                Pickable pickable = collider.GetComponentInParent<Pickable>();
                if (!pickable || !seenPickables.Add(pickable.GetInstanceID()))
                {
                    continue;
                }

                if (!pickable.m_harvestable || !pickable.CanBePicked())
                {
                    continue;
                }

                if (!pickable.m_nview || !pickable.m_nview.IsValid())
                {
                    continue;
                }

                uprooting = true;
                try
                {
                    pickable.m_nview.InvokeRPC("RPC_Pick", 0);
                    uprooted++;
                }
                finally
                {
                    uprooting = false;
                }
            }

            if (uprooted > 0)
            {
                Jotunn.Logger.LogInfo($"Cultivate uproot: {uprooted} crops");
            }
        }

        [HarmonyPatch(typeof(TerrainOp), nameof(TerrainOp.OnPlaced))]
        private static class TerrainOpOnPlacedPatch
        {
            private static void Prefix(TerrainOp __instance)
            {
                TryHarvest(__instance);
            }
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), typeof(ItemDrop), typeof(bool))]
        private static class ItemDropOnCreateNewPatch
        {
            private static void Postfix(ItemDrop item)
            {
                if (!uprooting || !item)
                {
                    return;
                }

                item.m_autoPickup = false;
                if (!item.GetComponent<GroundWalkPickup>())
                {
                    item.gameObject.AddComponent<GroundWalkPickup>();
                }
            }
        }

        private sealed class GroundWalkPickup : MonoBehaviour
        {
            private ItemDrop itemDrop;

            private void Awake()
            {
                itemDrop = GetComponent<ItemDrop>();
                if (itemDrop)
                {
                    itemDrop.m_autoPickup = false;
                }
            }

            private void Update()
            {
                if (!itemDrop)
                {
                    Destroy(this);
                    return;
                }

                Player player = Player.m_localPlayer;
                if (!player)
                {
                    return;
                }

                if ((player.transform.position - transform.position).sqrMagnitude <= WalkPickupRange * WalkPickupRange)
                {
                    itemDrop.m_autoPickup = true;
                    Destroy(this);
                }
            }
        }
    }
}
