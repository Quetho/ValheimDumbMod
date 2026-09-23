using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Qmod
{
    // Raccourci et bouton (au-dessus de l'armure, à droite de l'inventaire) :
    // range l'inventaire dans les coffres proches qui ne contiennent qu'un
    // seul nom d'objet et qui ont encore de la place. Le plus proche d'abord.
    // Le scan est celui du pull : mêmes coffres, même filtre de poseur.
    // Le bouton scanne au survol (une fois) et passe au vert quand c'est prêt.
    // Le clic ne refait pas le scan : il envoie seulement l'écriture.
    internal static class ChestDump
    {
        private const float OwnershipTimeout = 2f;
        private const float ButtonSize = 30f;
        private const float ButtonGap = 4f;
        private static readonly Color IdleColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color ReadyColor = new Color(0.28f, 0.78f, 0.32f, 1f);
        private static readonly Color BlockedColor = new Color(0.82f, 0.18f, 0.16f, 1f);
        // Le message vanilla part à 30 s de la sauvegarde. Fenêtre rouge : 10 s avant, 10 s après.
        private const float SaveBlockStart = 20f;
        private const float SaveBlockEnd = 40f;

        private static bool running;
        private static bool scanReady;
        private static bool wasSaveBlocked;
        private static float saveWarningAt = -1f;
        private static int wanted;
        private static int moved;
        private static bool hitFull;
        private static Button dumpButton;
        private static RectTransform dumpRt;
        private static Image dumpIcon;
        private static UITooltip dumpTip;
        private static bool iconResolved;
        private static bool tooltipReady;
        private static bool createFailed;
        private static float nextTooltipRetry;
        private static readonly List<Container> chests = new List<Container>();
        private static readonly List<Container> targets = new List<Container>();
        private static readonly List<DumpMove> moves = new List<DumpMove>();
        private static readonly List<ItemDrop.ItemData> playerStacks = new List<ItemDrop.ItemData>();
        private static readonly Dictionary<ItemDrop.ItemData, int> remaining = new Dictionary<ItemDrop.ItemData, int>();
        private static readonly Dictionary<long, int> partialRoom = new Dictionary<long, int>();
        private static readonly HashSet<string> countedNames = new HashSet<string>();
        private static readonly HashSet<Container> touched = new HashSet<Container>();

        internal static bool IsRunning()
        {
            return running;
        }

        internal static void Tick()
        {
            RefreshTint();
        }

        internal static void TryDump()
        {
            if (ModConfig.ChestDumpEnabled == null || !ModConfig.ChestDumpEnabled.Value || running)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (BlockedNotice(player))
            {
                return;
            }

            if (ChestPull.IsPullRunning())
            {
                Util.NotifyPlayer(player, "Pull en cours");
                return;
            }

            if (!Util.IsAlive(player))
            {
                return;
            }

            Inventory playerInv = player.GetInventory();
            if (playerInv == null)
            {
                return;
            }

            Plan(player, playerInv);
            if (moves.Count == 0)
            {
                Util.NotifyPlayer(player, "Rien à ranger dans les coffres proches");
                Jotunn.Logger.LogInfo("ChestDump: rien à ranger");
                return;
            }

            running = true;
            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(Execute(player, playerInv));
            }
            else
            {
                ExecuteMoves(playerInv);
                Finish(player);
            }
        }

        // Une passe, au survol. Pas de ClaimOwnership ici : ça part au clic.
        private static void OnButtonHover()
        {
            if (running || scanReady || IsSaveBlocked())
            {
                return;
            }

            if (ModConfig.ChestDumpEnabled == null || !ModConfig.ChestDumpEnabled.Value)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (ChestPull.IsPullRunning() || !Util.IsAlive(player))
            {
                return;
            }

            Inventory playerInv = player.GetInventory();
            if (playerInv == null)
            {
                return;
            }

            Plan(player, playerInv);
            scanReady = true;
            RefreshTint();
        }

        private static void OnButtonExit()
        {
            if (running)
            {
                return;
            }

            InvalidateScan();
        }

        private static void OnButtonClick()
        {
            Player player = Player.m_localPlayer;
            if (BlockedNotice(player))
            {
                return;
            }

            if (!scanReady || running)
            {
                return;
            }

            if (ModConfig.ChestDumpEnabled == null || !ModConfig.ChestDumpEnabled.Value)
            {
                return;
            }

            if (ChestPull.IsPullRunning())
            {
                Util.NotifyPlayer(player, "Pull en cours");
                return;
            }

            if (!Util.IsAlive(player))
            {
                return;
            }

            Inventory playerInv = player.GetInventory();
            if (playerInv == null)
            {
                return;
            }

            if (moves.Count == 0)
            {
                Util.NotifyPlayer(player, "Rien à ranger dans les coffres proches");
                return;
            }

            running = true;
            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(Execute(player, playerInv));
            }
            else
            {
                ExecuteMoves(playerInv);
                Finish(player);
            }
        }

        private static void InvalidateScan()
        {
            scanReady = false;
            moves.Clear();
            targets.Clear();
            RefreshTint();
        }

        // $msg_worldsavewarning est envoyé par le serveur (ou l'hôte) au moment
        // où il reste 30 s. Le texte arrive tel quel via MessageHud.ShowMessage.
        private static void NoteSaveWarning()
        {
            float now = Time.unscaledTime;
            if (saveWarningAt >= 0f && now - saveWarningAt < 5f)
            {
                return;
            }

            saveWarningAt = now;
            Jotunn.Logger.LogInfo("ChestDump: sauvegarde serveur dans 30 s, blocage 10 s avant et 10 s après");
        }

        private static bool IsSaveBlocked()
        {
            if (saveWarningAt < 0f)
            {
                return false;
            }

            float since = Time.unscaledTime - saveWarningAt;
            if (since >= SaveBlockEnd)
            {
                saveWarningAt = -1f;
                return false;
            }

            return since >= SaveBlockStart;
        }

        private static bool BlockedNotice(Player player)
        {
            if (!IsSaveBlocked())
            {
                return false;
            }

            Util.NotifyPlayer(player, "Sauvegarde du serveur, transfert bloqué");
            return true;
        }

        private static void RefreshTint()
        {
            bool blocked = IsSaveBlocked();
            // Un transfert déjà lancé finit. On n'en démarre plus, et le scan survolé est oublié.
            if (blocked && !wasSaveBlocked && !running)
            {
                scanReady = false;
                moves.Clear();
                targets.Clear();
            }

            wasSaveBlocked = blocked;
            if (!dumpButton)
            {
                return;
            }

            if (blocked)
            {
                ApplyTint(BlockedColor, true);
            }
            else if (scanReady)
            {
                ApplyTint(ReadyColor, true);
            }
            else
            {
                ApplyTint(IdleColor, false);
            }
        }

        private static void ApplyTint(Color color, bool iconLit)
        {
            if (!dumpButton)
            {
                return;
            }

            ColorBlock block = dumpButton.colors;
            block.normalColor = color;
            block.highlightedColor = color;
            block.pressedColor = new Color(color.r * 0.82f, color.g * 0.82f, color.b * 0.82f, color.a);
            block.selectedColor = color;
            block.disabledColor = color;
            block.colorMultiplier = 1f;
            block.fadeDuration = 0.05f;
            dumpButton.colors = block;
            if (dumpIcon)
            {
                dumpIcon.color = iconLit ? Color.white : IdleColor;
            }
        }

        private static void Plan(Player player, Inventory playerInv)
        {
            moves.Clear();
            targets.Clear();
            remaining.Clear();
            countedNames.Clear();
            playerStacks.Clear();
            wanted = 0;
            moved = 0;
            hitFull = false;

            long playerId = ChestPull.LocalPlayerId();
            ChestPull.GatherChests(player.transform.position, ChestPull.PullScanRadius(), playerId, false, chests);
            int skippedOwner = ChestPull.LastOwnerRejects;
            for (int i = chests.Count - 1; i >= 0; i--)
            {
                if (!IsDumpChest(chests[i]))
                {
                    chests.RemoveAt(i);
                }
            }

            Vector3 origin = player.transform.position;
            chests.Sort((a, b) =>
            {
                float da = (a.transform.position - origin).sqrMagnitude;
                float db = (b.transform.position - origin).sqrMagnitude;
                return da.CompareTo(db);
            });

            List<ItemDrop.ItemData> live = playerInv.GetAllItems();
            if (live != null)
            {
                for (int i = 0; i < live.Count; i++)
                {
                    ItemDrop.ItemData item = live[i];
                    if (item == null || item.m_equipped || item.m_stack <= 0 || item.m_shared == null ||
                        string.IsNullOrEmpty(item.m_shared.m_name))
                    {
                        continue;
                    }

                    playerStacks.Add(item);
                    remaining[item] = item.m_stack;
                }
            }

            for (int c = 0; c < chests.Count; c++)
            {
                Container container = chests[c];
                Inventory chestInv = container.GetInventory();
                string resName;
                if (chestInv == null || chestInv == playerInv || !TryReadChest(chestInv, out resName))
                {
                    continue;
                }

                int empty = Mathf.Max(0, chestInv.GetEmptySlots());
                int chestTake = 0;
                for (int s = 0; s < playerStacks.Count; s++)
                {
                    ItemDrop.ItemData stack = playerStacks[s];
                    int leftAvailable;
                    if (!remaining.TryGetValue(stack, out leftAvailable) || leftAvailable <= 0 ||
                        stack.m_shared.m_name != resName)
                    {
                        continue;
                    }

                    long key = StackKey(stack.m_quality, stack.m_worldLevel);
                    int room;
                    partialRoom.TryGetValue(key, out room);
                    int intoPartial = Mathf.Min(leftAvailable, Mathf.Max(0, room));
                    if (intoPartial > 0)
                    {
                        partialRoom[key] = room - intoPartial;
                    }

                    int left = leftAvailable - intoPartial;
                    int intoEmpty = 0;
                    int itemMax = stack.m_shared.m_maxStackSize;
                    if (itemMax <= 0)
                    {
                        itemMax = 1;
                    }

                    if (left > 0 && empty > 0 && itemMax > 1)
                    {
                        int slotsNeeded = (left + itemMax - 1) / itemMax;
                        int slots = Mathf.Min(slotsNeeded, empty);
                        intoEmpty = Mathf.Min(left, slots * itemMax);
                        empty -= (intoEmpty + itemMax - 1) / itemMax;
                    }
                    else if (left > 0 && empty > 0)
                    {
                        intoEmpty = Mathf.Min(left, empty);
                        empty -= intoEmpty;
                    }

                    int take = intoPartial + intoEmpty;
                    if (take <= 0)
                    {
                        continue;
                    }

                    remaining[stack] = leftAvailable - take;
                    moves.Add(new DumpMove
                    {
                        Chest = container,
                        Stack = stack,
                        Amount = take
                    });
                    chestTake += take;
                }

                if (chestTake <= 0)
                {
                    continue;
                }

                targets.Add(container);
                if (countedNames.Add(resName))
                {
                    wanted += UnequippedCount(resName);
                }

                Jotunn.Logger.LogInfo("ChestDump: " + resName + " -> " + Util.GetPrefabName(container.gameObject) + " x" + chestTake);
            }

            Jotunn.Logger.LogInfo("ChestDump: " + chests.Count + " coffres, " + targets.Count + " cibles, " + moves.Count + " transferts, prévu " + SumMoves() + "/" + wanted + ", pas à toi=" + skippedOwner);
        }

        private static int SumMoves()
        {
            int total = 0;
            for (int i = 0; i < moves.Count; i++)
            {
                total += moves[i].Amount;
            }

            return total;
        }

        private static int UnequippedCount(string name)
        {
            int total = 0;
            for (int i = 0; i < playerStacks.Count; i++)
            {
                ItemDrop.ItemData item = playerStacks[i];
                if (item != null && item.m_shared != null && item.m_shared.m_name == name)
                {
                    total += item.m_stack;
                }
            }

            return total;
        }

        private static bool IsDumpChest(Container container)
        {
            if (!container || container.m_wagon)
            {
                return false;
            }

            string name = Util.GetPrefabName(container.gameObject);
            return name.IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Un seul m_shared.m_name, et la place restante par qualité + world level
        // (FindFreeStackItem n'empile que là-dessus). partialRoom est rempli pour l'appelant.
        private static bool TryReadChest(Inventory inv, out string name)
        {
            name = null;
            partialRoom.Clear();
            List<ItemDrop.ItemData> items = inv.GetAllItems();
            if (items == null || items.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null || string.IsNullOrEmpty(item.m_shared.m_name))
                {
                    return false;
                }

                if (name == null)
                {
                    name = item.m_shared.m_name;
                }
                else if (item.m_shared.m_name != name)
                {
                    return false;
                }

                int stackMax = item.m_shared.m_maxStackSize;
                if (stackMax > 1 && item.m_stack < stackMax)
                {
                    long key = StackKey(item.m_quality, item.m_worldLevel);
                    int room;
                    partialRoom.TryGetValue(key, out room);
                    partialRoom[key] = room + (stackMax - item.m_stack);
                }
            }

            return name != null;
        }

        private static long StackKey(int quality, int worldLevel)
        {
            return ((long)quality << 32) | (uint)worldLevel;
        }

        private static IEnumerator Execute(Player player, Inventory playerInv)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Container container = targets[i];
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
                running = false;
                InvalidateScan();
                Util.NotifyPlayer(player, "Coffres inaccessibles (pas d'ownership)");
                Jotunn.Logger.LogWarning("ChestDump: ownership non obtenue, abandon");
                yield break;
            }

            ExecuteMoves(playerInv);
            Finish(player);
        }

        private static bool AllOwned()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Container container = targets[i];
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

        // AddItem insère l'objet qu'on lui donne et peut n'en absorber qu'une partie
        // avant d'échouer : on clone les piles partielles, et on ne retire au joueur
        // que ce que le coffre a vraiment gagné.
        private static void ExecuteMoves(Inventory playerInv)
        {
            touched.Clear();
            moved = 0;
            hitFull = false;
            for (int i = 0; i < moves.Count; i++)
            {
                DumpMove move = moves[i];
                Container container = move.Chest;
                ItemDrop.ItemData stack = move.Stack;
                if (!container || stack == null || stack.m_shared == null || move.Amount <= 0)
                {
                    continue;
                }

                if (playerInv.GetItemAt(stack.m_gridPos.x, stack.m_gridPos.y) != stack || stack.m_equipped || stack.m_stack <= 0)
                {
                    continue;
                }

                Inventory chestInv = container.GetInventory();
                if (chestInv == null || chestInv == playerInv)
                {
                    continue;
                }

                int got = MoveToChest(playerInv, chestInv, stack, move.Amount);
                if (got > 0)
                {
                    moved += got;
                    touched.Add(container);
                }

                if (got < move.Amount)
                {
                    hitFull = true;
                }
            }

            foreach (Container container in touched)
            {
                SyncChest(container);
            }

            touched.Clear();
        }

        private static int MoveToChest(Inventory playerInv, Inventory chestInv, ItemDrop.ItemData stack, int amount)
        {
            amount = Mathf.Min(amount, stack.m_stack);
            if (amount <= 0)
            {
                return 0;
            }

            string name = stack.m_shared.m_name;
            int before = chestInv.CountItems(name, -1, false);
            if (amount >= stack.m_stack)
            {
                playerInv.RemoveItem(stack);
                if (!chestInv.AddItem(stack))
                {
                    playerInv.AddItem(stack);
                }
            }
            else
            {
                ItemDrop.ItemData clone = stack.Clone();
                clone.m_stack = amount;
                clone.m_equipped = false;
                if (chestInv.AddItem(clone))
                {
                    playerInv.RemoveItem(stack, amount);
                }
                else
                {
                    int added = chestInv.CountItems(name, -1, false) - before;
                    if (added > 0)
                    {
                        playerInv.RemoveItem(stack, Mathf.Min(added, stack.m_stack));
                    }

                    return Mathf.Max(0, added);
                }
            }

            return Mathf.Max(0, chestInv.CountItems(name, -1, false) - before);
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

        private static void Finish(Player player)
        {
            running = false;
            Jotunn.Logger.LogInfo("ChestDump: rangé " + moved + "/" + wanted + (hitFull ? " plein" : ""));
            if (moved <= 0)
            {
                Util.NotifyPlayer(player, "Rien à ranger dans les coffres proches");
            }
            else if (moved < wanted || hitFull)
            {
                Util.NotifyPlayer(player, "Rangé " + moved + " (coffres pleins)");
            }
            else
            {
                Util.NotifyPlayer(player, "Rangé dans les coffres : " + moved);
            }

            moves.Clear();
            scanReady = false;
            RefreshTint();
        }

        internal static void EnsureButton(InventoryGui gui)
        {
            if (!gui || !gui.m_armor)
            {
                return;
            }

            if (!dumpButton && !createFailed)
            {
                try
                {
                    dumpButton = CreateButton(gui);
                    RefreshTint();
                }
                catch (Exception e)
                {
                    createFailed = true;
                    Jotunn.Logger.LogError("ChestDump: bouton impossible à créer : " + e.Message);
                }
            }

            if (!dumpButton)
            {
                return;
            }

            if (!iconResolved && ZNetScene.instance)
            {
                iconResolved = true;
                Sprite sprite = ChestPull.FindChestIcon();
                if (sprite && dumpIcon)
                {
                    dumpIcon.sprite = sprite;
                    dumpIcon.enabled = true;
                }
                else if (dumpIcon)
                {
                    dumpIcon.enabled = false;
                }
            }

            if (!tooltipReady && dumpTip && Time.time >= nextTooltipRetry)
            {
                nextTooltipRetry = Time.time + 1f;
                tooltipReady = AttachTooltip();
            }

            PlaceAboveArmor(gui);
            bool show = ModConfig.ChestDumpEnabled != null && ModConfig.ChestDumpEnabled.Value;
            if (dumpButton.gameObject.activeSelf != show)
            {
                dumpButton.gameObject.SetActive(show);
            }
        }

        private static Button CreateButton(InventoryGui gui)
        {
            RectTransform parent = gui.m_player ? gui.m_player : gui.m_armor.rectTransform.parent as RectTransform;
            GameObject go = new GameObject("QmodChestDumpButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            dumpRt = go.GetComponent<RectTransform>();
            dumpRt.pivot = new Vector2(0.5f, 0.5f);
            dumpRt.sizeDelta = new Vector2(ButtonSize, ButtonSize);
            dumpRt.localScale = Vector3.one;

            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            Image bg = go.AddComponent<Image>();
            Button donor = gui.m_dropButton;
            if (donor)
            {
                Image donorImg = donor.GetComponent<Image>();
                if (donorImg)
                {
                    bg.sprite = donorImg.sprite;
                    bg.type = donorImg.type;
                    bg.color = donorImg.color;
                }
            }

            if (!bg.sprite)
            {
                bg.color = new Color(0.16f, 0.14f, 0.12f, 0.95f);
            }

            Button button = go.AddComponent<Button>();
            if (donor)
            {
                button.colors = donor.colors;
            }

            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            button.onClick.AddListener(OnButtonClick);
            go.AddComponent<HoverRelay>();

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            RectTransform iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(4f, 4f);
            iconRt.offsetMax = new Vector2(-4f, -4f);
            dumpIcon = iconGo.AddComponent<Image>();
            dumpIcon.raycastTarget = false;
            dumpIcon.preserveAspect = true;
            dumpIcon.enabled = false;

            dumpTip = go.AddComponent<UITooltip>();
            dumpTip.m_text = "";
            dumpTip.m_topic = "";
            go.transform.SetAsLastSibling();
            return button;
        }

        // Ancré sur le haut du texte d'armure, dans le panneau joueur : le layout
        // du prefab ne reprend pas la main (ignoreLayout) et le bouton suit le label.
        private static void PlaceAboveArmor(InventoryGui gui)
        {
            if (!dumpRt || !gui.m_armor)
            {
                return;
            }

            RectTransform parent = dumpRt.parent as RectTransform;
            RectTransform armorRt = gui.m_armor.rectTransform;
            if (!parent)
            {
                return;
            }

            Vector2 parentSize = parent.rect.size;
            if (parentSize.x <= 0f || parentSize.y <= 0f)
            {
                return;
            }

            Vector3[] corners = new Vector3[4];
            armorRt.GetWorldCorners(corners);
            Vector3 top = (corners[1] + corners[2]) * 0.5f;
            Vector2 topLocal = parent.worldToLocalMatrix.MultiplyPoint3x4(top);
            Vector2 center = topLocal + new Vector2(0f, ButtonGap + ButtonSize * 0.5f);
            Vector2 norm = new Vector2(
                (center.x + parent.pivot.x * parentSize.x) / parentSize.x,
                (center.y + parent.pivot.y * parentSize.y) / parentSize.y);
            dumpRt.anchorMin = norm;
            dumpRt.anchorMax = norm;
            dumpRt.anchoredPosition = Vector2.zero;
        }

        private static bool AttachTooltip()
        {
            try
            {
                UITooltip[] all = UnityEngine.Object.FindObjectsByType<UITooltip>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    UITooltip tip = all[i];
                    if (!tip || tip == dumpTip || !tip.m_tooltipPrefab)
                    {
                        continue;
                    }

                    dumpTip.m_tooltipPrefab = tip.m_tooltipPrefab;
                    dumpTip.m_topic = "Ranger";
                    dumpTip.m_text = "Survoler pour chercher les coffres. Vert = prêt, clic pour ranger. Rouge = sauvegarde du serveur. Seulement les coffres que tu as posés.";
                    return true;
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("ChestDump: infobulle impossible : " + e.Message);
            }

            return false;
        }

        private sealed class DumpMove
        {
            public Container Chest;
            public ItemDrop.ItemData Stack;
            public int Amount;
        }

        private sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public void OnPointerEnter(PointerEventData eventData)
            {
                OnButtonHover();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                OnButtonExit();
            }
        }

        [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
        private static class SaveWarningPatch
        {
            private static void Prefix(string text)
            {
                if (!string.IsNullOrEmpty(text) && text.IndexOf("$msg_worldsavewarning", StringComparison.Ordinal) >= 0)
                {
                    NoteSaveWarning();
                }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Show")]
        private static class ShowPatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                EnsureButton(__instance);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        private static class UpdatePatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                EnsureButton(__instance);
            }
        }
    }
}
