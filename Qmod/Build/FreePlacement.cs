using HarmonyLib;

namespace Qmod
{
    // Lève les 2 verrous de chevauchement de UpdatePlacementGhost :
    // - TestGhostClipping : fantôme rouge dès qu'une pièce croise une
    //   autre pièce (ou le terrain) au-delà de 0.2 m de pénétration.
    // - IsOverlappingOtherPiece : point d'ancrage ignoré si la même
    //   pièce est déjà posée à cet endroit.
    // Le reste (stabilité, ressources, établi, zones) est inchangé.
    internal static class FreePlacement
    {
        internal static bool IsEnabled =>
            ModConfig.FreePlacementEnabled != null && ModConfig.FreePlacementEnabled.Value;

        [HarmonyPatch(typeof(Player), nameof(Player.TestGhostClipping))]
        private static class TestGhostClippingPatch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!IsEnabled)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.IsOverlappingOtherPiece))]
        private static class IsOverlappingOtherPiecePatch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!IsEnabled)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }
    }
}
