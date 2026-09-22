using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Qmod
{
    // Menu marteau (BuildUi) : après 1 s de survol maintenu, le panneau bas
    // affiche le stock coffres par ressource ; clic droit sur une pièce =
    // pull pour x10 (ou le max < 10 que les coffres couvrent), sans fermer.
    // Clic droit hors item : fermeture vanilla inchangée. Le clic droit est
    // sondé globalement par BuildUi.NavigationUpdate (bouton "BuildMenu"),
    // sans handler par bouton : on l'intercepte là quand un bouton est
    // survolé (m_currentHoveredPieceButton).
    internal static class BuildPull
    {
        private const float HoverDelay = 1f;
        private const int MaxPullCount = 10;

        private static Piece hoverPiece;
        private static float hoverStart;
        private static Piece prevPiece;
        private static int clearFrame = -1;
        private static Piece scannedPiece;
        private static readonly Dictionary<string, int> chestCounts = new Dictionary<string, int>();
        private static readonly List<TMP_Text> rowAmounts = new List<TMP_Text>();
        private static Piece cachedRowsPiece;

        internal static bool IsEnabled =>
            ModConfig.BuildPullEnabled != null && ModConfig.BuildPullEnabled.Value;

        internal static void TrackHover(Piece piece)
        {
            if (piece != null)
            {
                hoverPiece = piece;
                prevPiece = piece;
                hoverStart = Time.time;
                return;
            }

            // Le clic droit désélectionne le bouton (EventSystem avant
            // BuildUi.Update) : le survol est effacé la frame même du clic.
            // On garde le dernier survolé + la frame de l'effacement pour
            // l'accepter si le clic arrive sur cette même frame.
            hoverPiece = null;
            hoverStart = Time.time;
            clearFrame = Time.frameCount;
        }

        internal static void ClearHover()
        {
            hoverPiece = null;
            prevPiece = null;
            clearFrame = -1;
            scannedPiece = null;
            cachedRowsPiece = null;
            rowAmounts.Clear();
        }

        internal static void Annotate(Hud hud, Piece piece)
        {
            if (!IsEnabled || hud == null || piece == null || piece != hoverPiece)
            {
                return;
            }

            if (piece.m_resources == null || Time.time - hoverStart < HoverDelay)
            {
                return;
            }

            if (scannedPiece != piece)
            {
                ScanQuiet(piece);
                scannedPiece = piece;
            }

            if (cachedRowsPiece != piece)
            {
                CacheRows(hud, piece);
            }

            int rows = Math.Min(piece.m_resources.Length, rowAmounts.Count);
            for (int i = 0; i < rows; i++)
            {
                TMP_Text amount = rowAmounts[i];
                if (!amount)
                {
                    continue;
                }

                string resName = ChestPull.GetResourceName(piece.m_resources[i]);
                int chests = 0;
                if (!string.IsNullOrEmpty(resName))
                {
                    chestCounts.TryGetValue(resName, out chests);
                }

                amount.text += " (" + chests + ")";
            }
        }

        private static void ScanQuiet(Piece piece)
        {
            chestCounts.Clear();
            Player player = Player.m_localPlayer;
            if (!player || piece.m_resources == null)
            {
                return;
            }

            ChestPull.GatherChests(player.transform.position, ChestPull.PullScanRadius(), ChestPull.LocalPlayerId(), false);
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                string resName = ChestPull.GetResourceName(piece.m_resources[i]);
                if (string.IsNullOrEmpty(resName) || chestCounts.ContainsKey(resName))
                {
                    continue;
                }

                int available = 0;
                for (int c = 0; c < ChestPull.nearbyChests.Count; c++)
                {
                    Inventory chestInv = ChestPull.nearbyChests[c].GetInventory();
                    if (chestInv != null)
                    {
                        available += chestInv.CountItems(resName, -1, true);
                    }
                }

                chestCounts[resName] = available;
            }
        }

        private static void CacheRows(Hud hud, Piece piece)
        {
            rowAmounts.Clear();
            cachedRowsPiece = piece;
            if (hud.m_requirementItems == null || piece.m_resources == null)
            {
                return;
            }

            int rows = Math.Min(piece.m_resources.Length, hud.m_requirementItems.Length);
            for (int i = 0; i < rows; i++)
            {
                TMP_Text amount = null;
                GameObject row = hud.m_requirementItems[i];
                if (row)
                {
                    Transform node = row.transform.Find("res_amount");
                    if (node)
                    {
                        amount = node.GetComponent<TMP_Text>();
                    }
                }

                rowAmounts.Add(amount);
            }
        }

        // true = pull pris en charge (le natif ne doit pas tourner).
        // La pièce vient du jeu au moment du clic, pas du suivi (fraîcheur).
        internal static bool TryPull(Piece hovered, List<BuildUiPieceButton> buttons, BuildUiPieceButton special)
        {
            if (!IsEnabled)
            {
                return false;
            }

            Piece piece = hovered;
            if (piece == null || piece.m_resources == null || piece.m_resources.Length == 0)
            {
                return false;
            }

            Player player = Player.m_localPlayer;
            if (!Util.IsAlive(player) || ChestPull.IsPullRunning())
            {
                return false;
            }

            ChestPull.GatherChests(player.transform.position, ChestPull.PullScanRadius(), ChestPull.LocalPlayerId(), true);
            int count = BestCount(player, piece);
            if (count <= 0)
            {
                Util.NotifyPlayer(player, "Rien à récupérer dans les coffres proches");
                Jotunn.Logger.LogInfo("BuildPull: x1 non couvert pour " + Util.GetPrefabName(piece.gameObject));
                return true;
            }

            if (MissingFor(player, piece, count) <= 0)
            {
                Util.NotifyPlayer(player, "Matériaux déjà en poche (x" + count + ")");
                return true;
            }

            ChestPull.PullForPieces(piece, count, () => RefreshButtons(buttons, special));
            // Le stock a changé : rescan des lignes au prochain frame.
            // Le pointeur est toujours sur le bouton (désélection parasite
            // du clic) : on restaure le survol pour réafficher les (N).
            scannedPiece = null;
            hoverPiece = piece;
            hoverStart = Time.time - HoverDelay;
            return true;
        }

        // Fin de pull (coroutine) : les ressources sont arrivées, les
        // boutons grisés par manque redeviennent colorés. Le menu peut
        // avoir été fermé entre-temps : gardes null/détruit partout.
        private static void RefreshButtons(List<BuildUiPieceButton> buttons, BuildUiPieceButton special)
        {
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    BuildUiPieceButton button = buttons[i];
                    if (button)
                    {
                        button.UpdateRequirements();
                    }
                }
            }

            if (special)
            {
                special.UpdateRequirements();
            }
        }

        private static int BestCount(Player player, Piece piece)
        {
            Inventory playerInv = player.GetInventory();
            if (playerInv == null)
            {
                return 0;
            }

            for (int count = MaxPullCount; count >= 1; count--)
            {
                if (ChestsCover(playerInv, piece, count))
                {
                    return count;
                }
            }

            return 0;
        }

        private static bool ChestsCover(Inventory playerInv, Piece piece, int count)
        {
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                Piece.Requirement req = piece.m_resources[i];
                string resName = ChestPull.GetResourceName(req);
                if (string.IsNullOrEmpty(resName))
                {
                    continue;
                }

                int missing = Mathf.Max(0, req.GetAmount(1) * count - playerInv.CountItems(resName, -1, true));
                if (missing <= 0)
                {
                    continue;
                }

                int available = 0;
                for (int c = 0; c < ChestPull.nearbyChests.Count; c++)
                {
                    Inventory chestInv = ChestPull.nearbyChests[c].GetInventory();
                    if (chestInv != null)
                    {
                        available += chestInv.CountItems(resName, -1, true);
                    }
                }

                if (available < missing)
                {
                    return false;
                }
            }

            return true;
        }

        private static int MissingFor(Player player, Piece piece, int count)
        {
            Inventory playerInv = player.GetInventory();
            if (playerInv == null)
            {
                return 0;
            }

            int missing = 0;
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                Piece.Requirement req = piece.m_resources[i];
                string resName = ChestPull.GetResourceName(req);
                if (string.IsNullOrEmpty(resName))
                {
                    continue;
                }

                missing += Mathf.Max(0, req.GetAmount(1) * count - playerInv.CountItems(resName, -1, true));
            }

            return missing;
        }

        // BuildUi.OnHoverPiece -> Hud.OnHoverPiece(piece), avec null en sortie.
        [HarmonyPatch(typeof(Hud), nameof(Hud.OnHoverPiece), new Type[] { typeof(Piece) })]
        private static class HoverDirectPatch
        {
            private static void Postfix(Piece piece)
            {
                if (IsEnabled)
                {
                    TrackHover(piece);
                }
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.SetupPieceInfo))]
        private static class InfoPatch
        {
            private static void Postfix(Hud __instance, Piece piece)
            {
                Annotate(__instance, piece);
            }
        }

        // Clic droit (bouton "BuildMenu") sondé par frame : si un bouton de
        // pièce est survolé et le dropdown favoris fermé, on pull et on
        // bloque la fermeture vanilla. Sinon on laisse passer (fermeture,
        // dropdown, Escape, manette : inchangés). Le survol live est déjà
        // effacé au moment du sondage (désélection du clic traitée avant)
        // donc on accepte aussi le dernier survolé effacé sur cette frame.
        [HarmonyPatch(typeof(BuildUi), "NavigationUpdate")]
        private static class NavPatch
        {
            private static bool Prefix(BuildUiPieceButton ___m_currentHoveredPieceButton, BuildUiFavoritesDropdown ___m_favoritesDropdown, List<BuildUiPieceButton> ___m_pieceButtons, BuildUiPieceButton ___m_specialPieceButton)
            {
                if (!IsEnabled || UnifiedPopup.IsVisible() || Console.IsVisible())
                {
                    return true;
                }

                if (ZInput.IsGamepadActive())
                {
                    return true;
                }

                if (!ZInput.GetButtonDown("BuildMenu"))
                {
                    return true;
                }

                if (___m_favoritesDropdown != null && ___m_favoritesDropdown.IsOpen())
                {
                    return true;
                }

                Piece piece = ___m_currentHoveredPieceButton ? ___m_currentHoveredPieceButton.Piece : null;
                if (piece == null && clearFrame == Time.frameCount)
                {
                    piece = prevPiece;
                }

                if (piece == null)
                {
                    return true;
                }

                Jotunn.Logger.LogInfo("BuildPull: clic droit, survol=" + Util.GetPrefabName(piece.gameObject));
                return !TryPull(piece, ___m_pieceButtons, ___m_specialPieceButton);
            }
        }

        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.Close))]
        private static class HidePatch
        {
            private static void Postfix()
            {
                ClearHover();
            }
        }
    }
}
