using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Qmod
{
    // Bouton "Pull" dans les onglets fabrication et amélioration (toutes
    // stations : forge, forge noire, établi, chaudron, chaudron à hydromel...).
    // Quand la recette sélectionnée manque de matériaux, le bouton apparaît
    // sous le bouton de craft et prend le manquant dans les coffres proches.
    // Fonctionnement en deux temps : scan + plan (quantités dispos, place
    // libre) sans rien toucher, puis transfert avec attente d'ownership
    // (indispensable côté client sur serveur dédié) et synchro ZDO.
    internal static class ChestPull
    {
        private const float OwnershipTimeout = 2f;
        private const float ChestScanInterval = 2f;

        private static readonly string[] ChestIconPrefabs =
        {
            "piece_chest",
            "piece_chestprivate",
            "piece_chestwood",
            "Chest"
        };

        private static Button pullButton;
        private static Image pullIcon;
        private static RectTransform pullLabelRt;
        private static bool iconResolved;
        private static RectTransform pullRt;
        private static UITooltip pullTip;
        private static bool tooltipReady;
        private static float nextTooltipRetry;
        private static readonly StringBuilder tooltipBuilder = new StringBuilder();
        private static readonly Dictionary<string, int> chestCounts = new Dictionary<string, int>();
        private static Recipe lastScanRecipe;
        private static int lastScanQuality;
        private static float nextChestScan;
        private static bool createFailed;
        private static bool pullRunning;
        private static Action pullFinishedCallback;
        private static bool wasShown;
        private static readonly List<ItemDrop.ItemData> foundStacks = new List<ItemDrop.ItemData>();
        private static readonly List<ItemDrop.ItemData> playerStacks = new List<ItemDrop.ItemData>();
        internal static readonly List<Container> nearbyChests = new List<Container>();
        private static readonly HashSet<Container> seenChests = new HashSet<Container>();
        private static readonly HashSet<Container> touchedChests = new HashSet<Container>();
        private static readonly List<PullPlan> plans = new List<PullPlan>();

        internal static void EnsureButton(InventoryGui gui)
        {
            if (pullButton || createFailed || !gui || !gui.m_craftButton)
            {
                return;
            }

            try
            {
                pullButton = CreateButton(gui);
            }
            catch (Exception e)
            {
                // Un bouton cosmétique ne doit jamais casser la logique du jeu
                // (UpdateCraftingPanel est appelé pendant Player.Load).
                createFailed = true;
                Jotunn.Logger.LogError("ChestPull: bouton impossible à créer : " + e.Message);
            }
        }

        internal static void Refresh(InventoryGui gui)
        {
            if (!pullButton)
            {
                return;
            }

            // ZNetScene peut manquer à la création (UpdateCraftingPanel tourne
            // pendant Player.Load) : résoudre l'icône dès qu'il est prêt.
            if (!iconResolved && ZNetScene.instance)
            {
                iconResolved = true;
                ResolveIcon();
            }

            bool show = ShouldShow(gui);
            UpdateTooltip(gui, show);
            if (show && !wasShown)
            {
                Recipe recipe = GetSelectedRecipe(gui);
                string name = recipe != null && recipe.m_item ? Util.GetPrefabName(recipe.m_item.gameObject) : "?";
                string mode = GetUpgradeItem(gui) != null ? "amélioration q" + GetSelectedQuality(gui) : "fabrication";
                Jotunn.Logger.LogInfo("ChestPull: bouton affiché (recette " + name + ", " + mode + ")");
            }

            // Le bouton se cache sous le curseur (clic Pull) sans OnPointerExit :
            // sans ça l'infobulle reste affichée (son LateUpdate est inactif).
            if (wasShown && !show)
            {
                HidePullTooltip();
            }

            wasShown = show;
            if (pullButton.gameObject.activeSelf != show)
            {
                pullButton.gameObject.SetActive(show);
            }
        }

        // Centre-bas du bouton craft en coordonnées d'ancre [0,1] du parent.
        // Robuste quel que soit le mode d'ancrage du bouton craft.
        private static Vector2 CraftBottomAnchor(RectTransform craftRt)
        {
            try
            {
                RectTransform parent = craftRt.parent as RectTransform;
                Vector2 parentSize = parent ? parent.rect.size : Vector2.zero;
                if (parentSize.x > 0f && parentSize.y > 0f)
                {
                    Vector3[] corners = new Vector3[4];
                    craftRt.GetWorldCorners(corners);
                    Matrix4x4 toLocal = parent.worldToLocalMatrix;
                    Vector2 bl = toLocal.MultiplyPoint3x4(corners[0]);
                    Vector2 br = toLocal.MultiplyPoint3x4(corners[3]);
                    Vector2 bottomCenter = (bl + br) * 0.5f + Vector2.Scale(parent.pivot, parentSize);
                    return new Vector2(bottomCenter.x / parentSize.x, bottomCenter.y / parentSize.y);
                }
            }
            catch (Exception)
            {
            }

            return new Vector2(0.5f, 0f);
        }

        private static Button CreateButton(InventoryGui gui)
        {
            Button craft = gui.m_craftButton;
            RectTransform craftRt = craft.GetComponent<RectTransform>();

            // Petit bouton compact (~3/4 de la hauteur du craft).
            float craftH = craftRt.rect.height;
            if (craftH <= 0f)
            {
                craftH = 40f;
            }

            float height = craftH * 0.72f;
            float width = height * 2.8f;
            float iconSize = height - 10f;
            float textLeft = iconSize + 10f;

            GameObject go = new GameObject("QmodChestPullButton", typeof(RectTransform));
            go.transform.SetParent(craft.transform.parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            // Ancres ponctuelles calculées depuis les coins réels : ne pas
            // copier les ancres du craft (étirées = taille qui explose).
            rt.anchorMin = CraftBottomAnchor(craftRt);
            rt.anchorMax = rt.anchorMin;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, -8f);
            rt.localScale = Vector3.one;

            Image bg = go.AddComponent<Image>();
            Image craftBg = craft.GetComponent<Image>();
            if (craftBg)
            {
                bg.sprite = craftBg.sprite;
                bg.color = craftBg.color;
                bg.type = craftBg.type;
            }

            Button button = go.AddComponent<Button>();
            button.colors = craft.colors;

            pullRt = rt;
            pullTip = go.AddComponent<UITooltip>();
            // Champs null = le hover croit qu'il y a du texte et instancie un
            // prefab encore absent : chaînes vides jusqu'au contenu réel.
            pullTip.m_text = "";
            pullTip.m_topic = "";

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            RectTransform iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(iconSize, iconSize);
            iconRt.anchoredPosition = new Vector2(6f + iconSize / 2f, 0f);
            pullIcon = iconGo.AddComponent<Image>();
            pullIcon.raycastTarget = false;
            pullIcon.preserveAspect = true;
            pullIcon.enabled = false;

            Text craftLabel = craft.GetComponentInChildren<Text>();
            TMP_Text craftTmp = craftLabel ? null : craft.GetComponentInChildren<TMP_Text>();
            GameObject labelGo = new GameObject("Text", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            RectTransform labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(textLeft, 0f);
            labelRt.offsetMax = Vector2.zero;
            pullLabelRt = labelRt;

            if (craftLabel)
            {
                Text label = labelGo.AddComponent<Text>();
                label.font = craftLabel.font;
                label.fontSize = craftLabel.fontSize;
                label.fontStyle = craftLabel.fontStyle;
                label.color = craftLabel.color;
                label.alignment = craftLabel.alignment;
                label.text = "Pull";
                label.raycastTarget = false;
            }
            else
            {
                // TMP_Text est abstrait : AddComponent exige la classe concrète.
                TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
                if (craftTmp)
                {
                    label.font = craftTmp.font;
                    label.fontSize = craftTmp.fontSize;
                    label.color = craftTmp.color;
                    label.alignment = craftTmp.alignment;
                }
                else
                {
                    label.alignment = TextAlignmentOptions.Center;
                }

                label.text = "Pull";
                label.raycastTarget = false;
            }

            button.onClick.AddListener(PullMissing);
            go.SetActive(false);
            return button;
        }

        private static void ResolveIcon()
        {
            if (!pullIcon)
            {
                return;
            }

            try
            {
                Sprite sprite = FindChestIcon();
                if (sprite)
                {
                    pullIcon.sprite = sprite;
                    pullIcon.enabled = true;
                    return;
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("ChestPull: icône illisible : " + e.Message);
            }

            // Pas d'icône : recentrer le texte sur tout le bouton.
            pullIcon.gameObject.SetActive(false);
            if (pullLabelRt)
            {
                pullLabelRt.offsetMin = Vector2.zero;
            }
        }

        private static Sprite FindChestIcon()
        {
            for (int i = 0; i < ChestIconPrefabs.Length; i++)
            {
                GameObject prefab;
                if (!Util.TryGetPrefab(ChestIconPrefabs[i], out prefab) || !prefab)
                {
                    continue;
                }

                Piece piece = prefab.GetComponent<Piece>();
                if (piece && piece.m_icon)
                {
                    Jotunn.Logger.LogInfo("ChestPull: icône coffre '" + ChestIconPrefabs[i] + "'");
                    return piece.m_icon;
                }

                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop && drop.m_itemData != null && drop.m_itemData.m_shared != null &&
                    drop.m_itemData.m_shared.m_icons != null && drop.m_itemData.m_shared.m_icons.Length > 0 &&
                    drop.m_itemData.m_shared.m_icons[0])
                {
                    Jotunn.Logger.LogInfo("ChestPull: icône objet '" + ChestIconPrefabs[i] + "'");
                    return drop.m_itemData.m_shared.m_icons[0];
                }
            }

            Jotunn.Logger.LogWarning("ChestPull: aucun prefab coffre trouvé, bouton sans icône");
            return null;
        }

        private static void UpdateTooltip(InventoryGui gui, bool show)
        {
            if (!pullTip)
            {
                return;
            }

            if (!tooltipReady)
            {
                if (Time.time < nextTooltipRetry)
                {
                    return;
                }

                nextTooltipRetry = Time.time + 1f;
                tooltipReady = AttachTooltipPrefab();
                if (!tooltipReady)
                {
                    return;
                }
            }

            if (!show)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            Recipe recipe = GetSelectedRecipe(gui);
            if (!player || !HasCraftingStation(recipe) || recipe.m_resources == null)
            {
                return;
            }

            Inventory playerInv = player.GetInventory();
            if (playerInv == null)
            {
                return;
            }

            int quality = GetSelectedQuality(gui);
            // Rescan immédiat si la sélection change, sinon throtté : le scan
            // coffres coûte trop cher pour tourner à chaque frame.
            if (recipe != lastScanRecipe || quality != lastScanQuality)
            {
                nextChestScan = 0f;
            }

            if (Time.time >= nextChestScan)
            {
                nextChestScan = Time.time + ChestScanInterval;
                RefreshChestCounts(player, recipe);
                lastScanRecipe = recipe;
                lastScanQuality = quality;
            }

            // Set() ignore les appels à contenu identique : coût nul en régime.
            pullTip.Set(BuildTooltipTopic(recipe),
                BuildTooltipText(playerInv, recipe, quality), pullRt, Vector2.zero);
        }

        private static void RefreshChestCounts(Player player, Recipe recipe)
        {
            chestCounts.Clear();
            GatherChests(player.transform.position, PullScanRadius(), LocalPlayerId(), false);
            if (recipe.m_resources == null)
            {
                return;
            }

            for (int i = 0; i < recipe.m_resources.Length; i++)
            {
                string resName = GetResourceName(recipe.m_resources[i]);
                if (string.IsNullOrEmpty(resName) || chestCounts.ContainsKey(resName))
                {
                    continue;
                }

                int available = 0;
                for (int c = 0; c < nearbyChests.Count; c++)
                {
                    Inventory chestInv = nearbyChests[c].GetInventory();
                    if (chestInv != null)
                    {
                        available += chestInv.CountItems(resName, -1, true);
                    }
                }

                chestCounts[resName] = available;
            }
        }

        internal static float PullScanRadius()
        {
            if (ModConfig.CraftPullRadius != null)
            {
                return Mathf.Clamp(ModConfig.CraftPullRadius.Value, 5f, 150f);
            }

            return 50f;
        }

        internal static long LocalPlayerId()
        {
            if (Game.instance)
            {
                return Game.instance.GetPlayerProfile().GetPlayerID();
            }

            return 0;
        }

        // Le prefab d'infobulle vient d'un UITooltip existant (slots
        // d'inventaire, etc.) : le jeu n'en expose aucun par API.
        private static bool AttachTooltipPrefab()
        {
            try
            {
                UITooltip[] all = UnityEngine.Object.FindObjectsByType<UITooltip>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                UITooltip fallback = null;
                for (int i = 0; i < all.Length; i++)
                {
                    UITooltip tip = all[i];
                    if (!tip || tip == pullTip || !tip.m_tooltipPrefab)
                    {
                        continue;
                    }

                    if (fallback == null)
                    {
                        fallback = tip;
                    }

                    if (TooltipPrefabHasText(tip.m_tooltipPrefab))
                    {
                        pullTip.m_tooltipPrefab = tip.m_tooltipPrefab;
                        Jotunn.Logger.LogInfo("ChestPull: prefab infobulle trouvé sur '" + tip.gameObject.name + "'");
                        return true;
                    }
                }

                if (fallback)
                {
                    pullTip.m_tooltipPrefab = fallback.m_tooltipPrefab;
                    Jotunn.Logger.LogInfo("ChestPull: prefab infobulle (repli) sur '" + fallback.gameObject.name + "'");
                    return true;
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("ChestPull: scan infobulle impossible : " + e.Message);
            }

            return false;
        }

        private static bool TooltipPrefabHasText(GameObject prefab)
        {
            TMP_Text[] texts = prefab.GetComponentsInChildren<TMP_Text>(true);
            bool text = false;
            bool topic = false;
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] && texts[i].name == "Text")
                {
                    text = true;
                }
                else if (texts[i] && texts[i].name == "Topic")
                {
                    topic = true;
                }
            }

            return text && topic;
        }

        private static void HidePullTooltip()
        {
            if (!pullTip || !pullRt)
            {
                return;
            }

            try
            {
                // Ne toucher au tooltip partagé que si le curseur est sur le
                // bouton : sinon on tuerait l'infobulle d'un autre élément.
                Vector3 pointer = ZInput.pointerPosition;
                if (RectTransformUtility.RectangleContainsScreenPoint(pullRt, new Vector2(pointer.x, pointer.y), null))
                {
                    UITooltip.HideTooltip();
                }
            }
            catch (Exception)
            {
            }
        }

        private static string BuildTooltipTopic(Recipe recipe)
        {
            string result = null;
            if (recipe.m_item && recipe.m_item.m_itemData != null && recipe.m_item.m_itemData.m_shared != null)
            {
                result = recipe.m_item.m_itemData.m_shared.m_name;
            }

            // Localize traduit les $clés mot à mot et laisse le reste intact.
            return string.IsNullOrEmpty(result) ? "Pull" : "Pull : " + result;
        }

        private static string BuildTooltipText(Inventory playerInv, Recipe recipe, int quality)
        {
            tooltipBuilder.Length = 0;
            for (int i = 0; i < recipe.m_resources.Length; i++)
            {
                Piece.Requirement req = recipe.m_resources[i];
                string resName = GetResourceName(req);
                if (string.IsNullOrEmpty(resName))
                {
                    continue;
                }

                int need = req.GetAmount(quality);
                int have = playerInv.CountItems(resName, -1, true);
                if (tooltipBuilder.Length > 0)
                {
                    tooltipBuilder.AppendLine();
                }

                tooltipBuilder.Append(resName).Append(" : ").Append(have).Append('/').Append(need);
                int missing = need - have;
                int chests = 0;
                chestCounts.TryGetValue(resName, out chests);
                tooltipBuilder.Append(" (coffres : ").Append(chests);
                if (missing > 0)
                {
                    tooltipBuilder.Append(", manque ").Append(missing);
                }

                tooltipBuilder.Append(')');
            }

            return tooltipBuilder.ToString();
        }

        private static bool ShouldShow(InventoryGui gui)
        {
            if (ModConfig.CraftPullEnabled == null || !ModConfig.CraftPullEnabled.Value)
            {
                return false;
            }

            if (!gui || (!gui.InCraftTab() && !gui.InUpradeTab()))
            {
                return false;
            }

            Player player = Player.m_localPlayer;
            if (!player)
            {
                return false;
            }

            Recipe recipe = GetSelectedRecipe(gui);
            if (!HasCraftingStation(recipe))
            {
                return false;
            }

            if (IsUpgradeMaxed(gui))
            {
                return false;
            }

            return MissingTotal(player, recipe, GetSelectedQuality(gui)) > 0;
        }

        private static Recipe GetSelectedRecipe(InventoryGui gui)
        {
            Recipe recipe = gui.m_selectedRecipe.Recipe;
            if (recipe != null)
            {
                return recipe;
            }

            return gui.m_craftRecipe;
        }

        // Toute recette liée à une station (forge, forge noire, établi,
        // chaudron, chaudron à hydromel, tailleur de pierre...) y compris
        // les stations moddées. Les pièces de construction n'ont pas de
        // recette et restent exclues, comme avant.
        private static bool HasCraftingStation(Recipe recipe)
        {
            return recipe != null && recipe.m_craftingStation;
        }

        private static ItemDrop.ItemData GetUpgradeItem(InventoryGui gui)
        {
            if (!gui)
            {
                return null;
            }

            return gui.m_selectedRecipe.ItemData;
        }

        private static bool IsUpgradeMaxed(InventoryGui gui)
        {
            ItemDrop.ItemData upgrade = GetUpgradeItem(gui);
            return upgrade != null && upgrade.m_shared != null && upgrade.m_quality >= upgrade.m_shared.m_maxQuality;
        }

        // Exactement comme UpdateRecipe : qualité 1 en fabrication, sinon
        // qualité visée = qualité de l'objet + 1 en amélioration.
        // Ne pas lire le label (il peut afficher la qualité d'un objet possédé).
        private static int GetSelectedQuality(InventoryGui gui)
        {
            ItemDrop.ItemData upgrade = GetUpgradeItem(gui);
            if (upgrade == null)
            {
                return 1;
            }

            return Mathf.Max(1, upgrade.m_quality + 1);
        }

        private static int CountStacks(List<ItemDrop.ItemData> stacks)
        {
            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                ItemDrop.ItemData item = stacks[i];
                if (item != null)
                {
                    total += item.m_stack;
                }
            }

            return total;
        }

        private static int MissingTotal(Player player, Recipe recipe, int quality)
        {
            Inventory playerInv = player.GetInventory();
            if (playerInv == null || recipe == null || recipe.m_resources == null)
            {
                return 0;
            }

            int missing = 0;
            for (int i = 0; i < recipe.m_resources.Length; i++)
            {
                Piece.Requirement req = recipe.m_resources[i];
                string resName = GetResourceName(req);
                if (string.IsNullOrEmpty(resName))
                {
                    continue;
                }

                missing += Mathf.Max(0, req.GetAmount(quality) - playerInv.CountItems(resName, -1, true));
            }

            playerStacks.Clear();
            return missing;
        }

        internal static string GetResourceName(Piece.Requirement req)
        {
            if (req == null || !req.m_resItem || req.m_resItem.m_itemData == null || req.m_resItem.m_itemData.m_shared == null)
            {
                return null;
            }

            return req.m_resItem.m_itemData.m_shared.m_name;
        }

        internal static void PullMissing()
        {
            if (ModConfig.CraftPullEnabled == null || !ModConfig.CraftPullEnabled.Value || pullRunning)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            InventoryGui gui = InventoryGui.instance;
            if (!player || !gui)
            {
                return;
            }

            Recipe recipe = GetSelectedRecipe(gui);
            if (!HasCraftingStation(recipe) || recipe.m_resources == null || IsUpgradeMaxed(gui))
            {
                return;
            }

            Inventory playerInv = player.GetInventory();
            if (playerInv == null)
            {
                return;
            }

            int chests = GatherChests(player.transform.position, PullScanRadius(), LocalPlayerId(), true);
            int quality = GetSelectedQuality(gui);
            plans.Clear();
            int totalTake = 0;
            for (int i = 0; i < recipe.m_resources.Length; i++)
            {
                PullPlan plan = PlanRequirement(playerInv, recipe.m_resources[i], quality);
                plans.Add(plan);
                totalTake += plan.Take;
            }

            Jotunn.Logger.LogInfo("ChestPull: " + chests + " coffres, " + totalTake + " à prendre");
            LogPlans();
            if (totalTake <= 0)
            {
                ReportShortage(player, plans);
                plans.Clear();
                return;
            }

            pullRunning = true;
            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(ExecutePull(player, playerInv));
            }
            else
            {
                ExecuteMoves(playerInv);
                FinishPull(player, playerInv);
            }
        }

        internal static bool IsPullRunning()
        {
            return pullRunning;
        }

        // Pull pour une pièce de construction (menu marteau), quantité count.
        // Les coffres doivent déjà être scannés (GatherChests par l'appelant).
        internal static void PullForPieces(Piece piece, int count, Action onDone)
        {
            if (pullRunning || piece == null || piece.m_resources == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            Inventory playerInv = player ? player.GetInventory() : null;
            if (playerInv == null)
            {
                return;
            }

            plans.Clear();
            int totalTake = 0;
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                PullPlan plan = PlanRequirement(playerInv, piece.m_resources[i], 1, count);
                plans.Add(plan);
                totalTake += plan.Take;
            }

            Jotunn.Logger.LogInfo("BuildPull: x" + count + " " + Util.GetPrefabName(piece.gameObject) +
                ", " + totalTake + " à prendre");
            LogPlans();
            if (totalTake <= 0)
            {
                ReportShortage(player, plans);
                plans.Clear();
                return;
            }

            pullFinishedCallback = onDone;
            pullRunning = true;
            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(ExecutePull(player, playerInv));
            }
            else
            {
                ExecuteMoves(playerInv);
                FinishPull(player, playerInv);
            }
        }

        internal static int GatherChests(Vector3 center, float radius, long playerId, bool verbose)
        {
            nearbyChests.Clear();
            seenChests.Clear();
            // Pas de OverlapSphere : le buffer sature dans les grosses bases et
            // des coffres sont ratés. Énumération directe (déclenchée au clic).
            Container[] all = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float maxDistSqr = radius * radius;
            int tooFar = 0;
            int invalid = 0;
            int guard = 0;
            int access = 0;
            int inUse = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Container container = all[i];
                if (!container || !seenChests.Add(container))
                {
                    continue;
                }

                if (!container.m_nview || !container.m_nview.IsValid())
                {
                    invalid++;
                    continue;
                }

                Vector3 delta = container.transform.position - center;
                if (delta.sqrMagnitude > maxDistSqr)
                {
                    tooFar++;
                    continue;
                }

                if (container.m_checkGuardStone && !PrivateArea.CheckAccess(container.transform.position, 0f, true, false))
                {
                    guard++;
                    continue;
                }

                if (!container.CheckAccess(playerId))
                {
                    access++;
                    continue;
                }

                if (container.IsInUse())
                {
                    inUse++;
                    continue;
                }

                Inventory chestInv = container.GetInventory();
                if (chestInv == null)
                {
                    invalid++;
                    continue;
                }

                // Relecture systématique : le contenu client d'un coffre fermé peut
                // être périmé (rempli après la dernière ouverture). Load() est sans
                // effet si la révision ZDO est inchangée, si le coffre est utilisé,
                // ou si le ZDO ne contient pas de données.
                int before = chestInv.NrOfItems();
                bool loaded = false;
                try
                {
                    loaded = container.Load();
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning("[ChestPull] Load coffre impossible : " + ex.GetType().Name);
                }

                if (verbose)
                {
                    Jotunn.Logger.LogInfo("ChestPull: coffre '" + Util.GetPrefabName(container.gameObject) +
                        "' items=" + before + " load=" + loaded + " items=" + chestInv.NrOfItems() +
                        " owner=" + container.m_nview.IsOwner());
                }

                nearbyChests.Add(container);
            }

            if (verbose)
            {
                Jotunn.Logger.LogInfo("ChestPull: scan r=" + radius + " scannés=" + all.Length +
                    " loin=" + tooFar + " invalides=" + invalid + " ward=" + guard +
                    " acces=" + access + " inUse=" + inUse + " gardés=" + nearbyChests.Count);
            }

            return nearbyChests.Count;
        }

        internal static PullPlan PlanRequirement(Inventory playerInv, Piece.Requirement req, int quality, int multiplier = 1)
        {
            PullPlan plan = new PullPlan();
            plan.ResName = GetResourceName(req);
            if (string.IsNullOrEmpty(plan.ResName))
            {
                return plan;
            }

            int maxStack = req.m_resItem.m_itemData.m_shared.m_maxStackSize;
            if (maxStack <= 0)
            {
                maxStack = 1;
            }

            plan.Need = req.GetAmount(quality) * Mathf.Max(1, multiplier);
            // Compter exactement comme le menu (CountItems -1/true) : seuls les
            // objets du niveau de monde courant servent au craft.
            plan.Have = playerInv.CountItems(plan.ResName, -1, true);
            plan.Missing = Mathf.Max(0, plan.Need - plan.Have);
            if (plan.Missing <= 0)
            {
                return plan;
            }

            // Comme le menu : seuls les objets du niveau de monde courant comptent.
            int available = 0;
            for (int i = 0; i < nearbyChests.Count; i++)
            {
                Inventory chestInv = nearbyChests[i].GetInventory();
                if (chestInv == null)
                {
                    continue;
                }

                available += chestInv.CountItems(plan.ResName, -1, true);
            }

            plan.Available = available;
            plan.Space = PlayerSpaceFor(playerInv, plan.ResName, maxStack);
            plan.Take = Mathf.Min(plan.Missing, Mathf.Min(plan.Available, plan.Space));
            return plan;
        }

        // Place estimée : remplissage des piles existantes + slots vides.
        // Surestimation possible si qualités différentes (le top-up exige la
        // même qualité) : sans danger, l'échec d'AddItem arrête le transfert.
        private static int PlayerSpaceFor(Inventory playerInv, string resName, int maxStack)
        {
            int space = 0;
            playerStacks.Clear();
            playerInv.GetAllItems(resName, playerStacks);
            for (int i = 0; i < playerStacks.Count; i++)
            {
                ItemDrop.ItemData stack = playerStacks[i];
                if (stack != null)
                {
                    space += Mathf.Max(0, maxStack - stack.m_stack);
                }
            }

            playerStacks.Clear();

            int empty = 0;
            for (int y = 0; y < playerInv.GetHeight(); y++)
            {
                for (int x = 0; x < playerInv.GetWidth(); x++)
                {
                    if (playerInv.GetItemAt(x, y) == null)
                    {
                        empty++;
                    }
                }
            }

            return space + empty * maxStack;
        }

        internal static IEnumerator ExecutePull(Player player, Inventory playerInv)
        {
            for (int i = 0; i < nearbyChests.Count; i++)
            {
                Container container = nearbyChests[i];
                if (container && container.m_nview && container.m_nview.IsValid() && !container.m_nview.IsOwner())
                {
                    container.m_nview.ClaimOwnership();
                }
            }

            float waited = 0f;
            while (waited < OwnershipTimeout && !AllOwned())
            {
                waited += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }

            if (!AllOwned())
            {
                pullRunning = false;
                pullFinishedCallback = null;
                Util.NotifyPlayer(player, "Coffres inaccessibles (pas d'ownership)");
                Jotunn.Logger.LogWarning("ChestPull: ownership non obtenue, abandon");
                plans.Clear();
                yield break;
            }

            ExecuteMoves(playerInv);
            FinishPull(player, playerInv);
        }

        private static bool AllOwned()
        {
            for (int i = 0; i < nearbyChests.Count; i++)
            {
                Container container = nearbyChests[i];
                if (!container || !container.m_nview || !container.m_nview.IsValid())
                {
                    continue;
                }

                if (!container.m_nview.IsOwner())
                {
                    return false;
                }
            }

            return true;
        }

        internal static void ExecuteMoves(Inventory playerInv)
        {
            touchedChests.Clear();
            for (int i = 0; i < plans.Count; i++)
            {
                PullPlan plan = plans[i];
                int remaining = plan.Take;
                if (remaining <= 0)
                {
                    continue;
                }

                for (int c = 0; c < nearbyChests.Count && remaining > 0; c++)
                {
                    Container container = nearbyChests[c];
                    if (!container || !container.m_nview || !container.m_nview.IsValid())
                    {
                        continue;
                    }

                    Inventory chestInv = container.GetInventory();
                    if (chestInv == null || chestInv == playerInv)
                    {
                        continue;
                    }

                    foundStacks.Clear();
                    chestInv.GetAllItems(plan.ResName, foundStacks);
                    for (int s = 0; s < foundStacks.Count && remaining > 0; s++)
                    {
                        ItemDrop.ItemData stack = foundStacks[s];
                        if (stack == null || stack.m_stack <= 0)
                        {
                            continue;
                        }

                        // Ne prendre que ce que le craft peut utiliser (même règle que le menu).
                        if (stack.m_worldLevel != Game.m_worldLevel)
                        {
                            continue;
                        }

                        int take = Mathf.Min(stack.m_stack, remaining);
                        if (take >= stack.m_stack)
                        {
                            chestInv.RemoveItem(stack);
                            if (playerInv.AddItem(stack))
                            {
                                remaining -= take;
                                plan.Moved += take;
                                touchedChests.Add(container);
                            }
                            else
                            {
                                chestInv.AddItem(stack);
                                plan.Full = true;
                                break;
                            }
                        }
                        else
                        {
                            ItemDrop.ItemData clone = stack.Clone();
                            clone.m_stack = take;
                            if (playerInv.AddItem(clone))
                            {
                                chestInv.RemoveItem(stack, take);
                                remaining -= take;
                                plan.Moved += take;
                                touchedChests.Add(container);
                            }
                            else
                            {
                                plan.Full = true;
                                break;
                            }
                        }
                    }

                    foundStacks.Clear();
                }
            }

            foreach (Container container in touchedChests)
            {
                SyncChest(container);
            }

            touchedChests.Clear();
        }

        private static void SyncChest(Container container)
        {
            if (!container || !container.m_nview || !container.m_nview.IsValid())
            {
                return;
            }

            container.Save();
            if (!container.m_nview.IsOwner())
            {
                container.m_nview.ClaimOwnership();
            }

            if (ZDOMan.instance != null)
            {
                ZDO zdo = container.m_nview.GetZDO();
                if (zdo != null)
                {
                    ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                }
            }
        }

        internal static void FinishPull(Player player, Inventory playerInv)
        {
            pullRunning = false;
            int moved = 0;
            int missing = 0;
            bool full = false;
            for (int i = 0; i < plans.Count; i++)
            {
                moved += plans[i].Moved;
                missing += plans[i].Missing;
                full |= plans[i].Full;
                Jotunn.Logger.LogInfo("ChestPull: " + plans[i].ResName + " faut=" + plans[i].Need + " ont=" + plans[i].Have +
                    " manque=" + plans[i].Missing + " dispos=" + plans[i].Available + " place=" + plans[i].Space + " pris=" + plans[i].Moved);
            }

            plans.Clear();
            // Le stock coffres a changé : forcer le rescan de l'infobulle.
            nextChestScan = 0f;
            // Comparer au manquant (pas au plan plafonné par le stock) : si les
            // coffres n'avaient pas tout, le message doit rester partiel.
            if (moved > 0 && moved >= missing && !full)
            {
                Util.NotifyPlayer(player, "Récupéré des coffres : " + moved);
            }
            else if (moved > 0)
            {
                Util.NotifyPlayer(player, "Récupéré " + moved + "/" + missing + (full ? " (inventaire plein)" : " (coffres vides)"));
            }
            else
            {
                Util.NotifyPlayer(player, full ? "Inventaire plein" : "Rien à récupérer dans les coffres proches");
            }

            Refresh(InventoryGui.instance);
            if (pullFinishedCallback != null)
            {
                Action done = pullFinishedCallback;
                pullFinishedCallback = null;
                done();
            }
        }

        internal static void LogPlans()
        {
            for (int i = 0; i < plans.Count; i++)
            {
                Jotunn.Logger.LogInfo("ChestPull: plan " + plans[i].ResName + " faut=" + plans[i].Need + " ont=" + plans[i].Have +
                    " manque=" + plans[i].Missing + " dispos=" + plans[i].Available + " place=" + plans[i].Space);
            }
        }

        internal static void ReportShortage(Player player, List<PullPlan> current)
        {
            bool noSpace = false;
            for (int i = 0; i < current.Count; i++)
            {
                if (current[i].Missing > 0 && current[i].Available > 0 && current[i].Space <= 0)
                {
                    noSpace = true;
                }
            }

            Util.NotifyPlayer(player, noSpace ? "Inventaire plein" : "Rien à récupérer dans les coffres proches");
        }

        internal class PullPlan
        {
            public string ResName;
            public int Need;
            public int Have;
            public int Missing;
            public int Available;
            public int Space;
            public int Take;
            public int Moved;
            public bool Full;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateCraftingPanel))]
        private static class UpdateCraftingPanelPatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                EnsureButton(__instance);
                Refresh(__instance);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
        private static class UpdateRecipePatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                EnsureButton(__instance);
                Refresh(__instance);
            }
        }
    }
}
