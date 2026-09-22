using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Qmod
{
    internal static partial class ThorTp
    {
        private const int MaxPoints = 40;

        private static readonly Color TabOn = new Color(0.42f, 0.28f, 0.12f, 0.95f);

        private static readonly Color TabOff = new Color(0.18f, 0.14f, 0.1f, 0.9f);

        private enum Page
        {
            Home,
            Teleport,
            Spawn
        }

        private static GameObject panel;
        private static Transform listContent;
        private static InputField nameField;
        private static GameObject inputGo;
        private static GameObject saveGo;
        private static Image destTabImage;
        private static Image playerTabImage;
        private static Text titleText;
        private static GameObject homePage;
        private static GameObject teleportPage;
        private static GameObject spawnPage;
        private static InputField spawnNameField;
        private static InputField spawnQtyField;
        private static Text spawnFeedback;
        private static Page currentPage = Page.Home;
        private static bool showingPlayers;
        private static bool inputBlocked;

        internal static bool IsOpen { get; private set; }

        internal static void OnGuiAvailable()
        {
            panel = null;
            listContent = null;
            nameField = null;
            inputGo = null;
            saveGo = null;
            destTabImage = null;
            playerTabImage = null;
            titleText = null;
            homePage = null;
            teleportPage = null;
            spawnPage = null;
            spawnNameField = null;
            spawnQtyField = null;
            spawnFeedback = null;
            currentPage = Page.Home;
            showingPlayers = false;
            if (IsOpen)
            {
                IsOpen = false;
                SetInputBlocked(false);
            }
        }

        internal static void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            Open();
        }

        internal static void Close()
        {
            IsOpen = false;
            if (panel)
            {
                panel.SetActive(false);
            }

            SetInputBlocked(false);
        }

        private static void Open()
        {
            if (ModConfig.LightningEnabled == null || !ModConfig.LightningEnabled.Value)
            {
                return;
            }

            Player player = Util.ActingPlayer();
            if (!player || MagicBush.IsActive)
            {
                return;
            }

            if (!ModConfig.IsOdin())
            {
                Util.NotifyPlayer(player, "Odin ne répond pas");
                return;
            }

            if (GUIManager.Instance == null || !GUIManager.CustomGUIFront)
            {
                return;
            }

            if (!EnsurePanel())
            {
                return;
            }

            EnsureRpc();
            IsOpen = true;
            panel.SetActive(true);
            ShowPage(Page.Home);
            SetInputBlocked(true);
        }

        private static bool EnsurePanel()
        {
            if (panel)
            {
                return true;
            }

            GUIManager gui = GUIManager.Instance;
            Transform parent = GUIManager.CustomGUIFront.transform;
            panel = gui.CreateWoodpanel(
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                460f,
                560f,
                true);
            if (!panel)
            {
                return false;
            }

            panel.name = "QmodMenu";
            GameObject title = gui.CreateText(
                "Qmod",
                panel.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -28f),
                gui.AveriaSerifBold,
                22,
                gui.ValheimOrange,
                true,
                Color.black,
                420f,
                32f,
                false);
            titleText = title.GetComponent<Text>();
            if (titleText)
            {
                titleText.alignment = TextAnchor.MiddleCenter;
            }

            homePage = MakePage("Home");
            teleportPage = MakePage("Teleport");
            spawnPage = MakePage("Spawn");

            BuildHome(gui);
            BuildTeleportPage(gui);
            BuildSpawnPage(gui);

            GameObject close = gui.CreateButton(
                "Fermer",
                panel.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 18f),
                160f,
                36f);
            Button closeBtn = close.GetComponent<Button>() ?? close.GetComponentInChildren<Button>();
            if (closeBtn)
            {
                closeBtn.onClick.AddListener(Close);
            }

            panel.SetActive(false);
            return true;
        }

        private static GameObject MakePage(string name)
        {
            GameObject page = new GameObject(name, typeof(RectTransform));
            page.transform.SetParent(panel.transform, false);
            RectTransform rt = page.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return page;
        }

        private static void ShowPage(Page page)
        {
            currentPage = page;
            if (homePage)
            {
                homePage.SetActive(page == Page.Home);
            }

            if (teleportPage)
            {
                teleportPage.SetActive(page == Page.Teleport);
            }

            if (spawnPage)
            {
                spawnPage.SetActive(page == Page.Spawn);
            }

            if (titleText)
            {
                titleText.text = page == Page.Teleport ? "Téléportation" : page == Page.Spawn ? "Spawn" : "Qmod";
            }

            if (page == Page.Teleport)
            {
                ShowTab(showingPlayers);
            }
        }

        private static void BuildHome(GUIManager gui)
        {
            GameObject tp = gui.CreateButton(
                "Téléportation",
                homePage.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -160f),
                260f,
                44f);
            Button tpBtn = tp.GetComponent<Button>() ?? tp.GetComponentInChildren<Button>();
            if (tpBtn)
            {
                tpBtn.onClick.AddListener(() => ShowPage(Page.Teleport));
            }

            GameObject spawn = gui.CreateButton(
                "Spawn",
                homePage.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -220f),
                260f,
                44f);
            Button spawnBtn = spawn.GetComponent<Button>() ?? spawn.GetComponentInChildren<Button>();
            if (spawnBtn)
            {
                spawnBtn.onClick.AddListener(() => ShowPage(Page.Spawn));
            }

            GameObject hint = gui.CreateText(
                "Choisis une catégorie",
                homePage.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -100f),
                gui.AveriaSerifBold,
                16,
                Color.white,
                true,
                Color.black,
                400f,
                28f,
                false);
            Text hintText = hint.GetComponent<Text>();
            if (hintText)
            {
                hintText.alignment = TextAnchor.MiddleCenter;
            }
        }

        private static void BuildTeleportPage(GUIManager gui)
        {
            GameObject back = gui.CreateButton(
                "Retour",
                teleportPage.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(70f, -26f),
                110f,
                30f);
            Button backBtn = back.GetComponent<Button>() ?? back.GetComponentInChildren<Button>();
            if (backBtn)
            {
                backBtn.onClick.AddListener(() => ShowPage(Page.Home));
            }

            CreateTabs(teleportPage.transform);
            listContent = CreateListArea(teleportPage.transform);

            GameObject input = gui.CreateInputField(
                teleportPage.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-70f, 58f),
                InputField.ContentType.Standard,
                "Nom de la destination",
                16,
                240f,
                36f);
            inputGo = input;
            nameField = input.GetComponent<InputField>() ?? input.GetComponentInChildren<InputField>();

            GameObject save = gui.CreateButton(
                "Sauver ici",
                teleportPage.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(130f, 58f),
                130f,
                36f);
            saveGo = save;
            Button saveBtn = save.GetComponent<Button>() ?? save.GetComponentInChildren<Button>();
            if (saveBtn)
            {
                saveBtn.onClick.AddListener(SaveHere);
            }
        }

        private static void BuildSpawnPage(GUIManager gui)
        {
            GameObject back = gui.CreateButton(
                "Retour",
                spawnPage.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(70f, -26f),
                110f,
                30f);
            Button backBtn = back.GetComponent<Button>() ?? back.GetComponentInChildren<Button>();
            if (backBtn)
            {
                backBtn.onClick.AddListener(() => ShowPage(Page.Home));
            }

            GameObject nameInput = gui.CreateInputField(
                spawnPage.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -130f),
                InputField.ContentType.Standard,
                "Nom de la ressource (ex: Wood, Stone, Coal)",
                16,
                380f,
                40f);
            spawnNameField = nameInput.GetComponent<InputField>() ?? nameInput.GetComponentInChildren<InputField>();

            GameObject qtyInput = gui.CreateInputField(
                spawnPage.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -185f),
                InputField.ContentType.IntegerNumber,
                "Quantité (ex: 10)",
                16,
                380f,
                40f);
            spawnQtyField = qtyInput.GetComponent<InputField>() ?? qtyInput.GetComponentInChildren<InputField>();

            GameObject spawn = gui.CreateButton(
                "Spawn",
                spawnPage.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -245f),
                200f,
                40f);
            Button spawnBtn = spawn.GetComponent<Button>() ?? spawn.GetComponentInChildren<Button>();
            if (spawnBtn)
            {
                spawnBtn.onClick.AddListener(SpawnResource);
            }

            GameObject feedback = gui.CreateText(
                "",
                spawnPage.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -295f),
                gui.AveriaSerifBold,
                15,
                Color.white,
                true,
                Color.black,
                400f,
                60f,
                false);
            spawnFeedback = feedback.GetComponent<Text>();
            if (spawnFeedback)
            {
                spawnFeedback.alignment = TextAnchor.MiddleCenter;
            }
        }

        private static void SetSpawnFeedback(string text)
        {
            if (spawnFeedback)
            {
                spawnFeedback.text = text ?? "";
            }
        }

        private static GameObject ResolveSpawnPrefab(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || ZNetScene.instance == null)
            {
                return null;
            }

            string trimmed = name.Trim();
            GameObject prefab;
            if (Util.TryGetPrefab(trimmed, out prefab))
            {
                return prefab;
            }

            if (ObjectDB.instance != null)
            {
                try
                {
                    prefab = ObjectDB.instance.GetItemPrefab(trimmed);
                }
                catch (Exception)
                {
                    prefab = null;
                }

                if (prefab)
                {
                    return prefab;
                }
            }

            return null;
        }

        private static void SpawnResource()
        {
            string typed = spawnNameField ? spawnNameField.text : "";
            if (string.IsNullOrWhiteSpace(typed))
            {
                SetSpawnFeedback("Saisis un nom de ressource.");
                return;
            }

            int qty = 1;
            if (spawnQtyField && !string.IsNullOrWhiteSpace(spawnQtyField.text))
            {
                if (!int.TryParse(spawnQtyField.text.Trim(), out qty))
                {
                    SetSpawnFeedback("Quantité invalide.");
                    return;
                }
            }

            qty = Mathf.Clamp(qty, 1, 999);

            Player player = Util.AlivePlayer();
            if (!player)
            {
                SetSpawnFeedback("Joueur indisponible.");
                return;
            }

            if (player.IsTeleporting())
            {
                SetSpawnFeedback("Impossible pendant une téléportation.");
                return;
            }

            GameObject prefab = ResolveSpawnPrefab(typed);
            if (!prefab)
            {
                SetSpawnFeedback("Ressource inconnue : " + typed.Trim());
                Jotunn.Logger.LogWarning("Spawn Qmod: prefab introuvable pour '" + typed.Trim() + "'");
                return;
            }

            ItemDrop item = prefab.GetComponent<ItemDrop>();
            if (!item)
            {
                SetSpawnFeedback("'" + typed.Trim() + "' n'est pas un objet d'inventaire.");
                return;
            }

            if (player.GetInventory() == null)
            {
                SetSpawnFeedback("Inventaire indisponible.");
                return;
            }

            player.GetInventory().AddItem(prefab, qty);
            SetSpawnFeedback("Spawn : " + prefab.name + " x" + qty);
            Util.NotifyPlayer(player, "Spawn : " + prefab.name + " x" + qty);
        }

        private static Transform CreateListArea(Transform parent)
        {
            GameObject root = new GameObject("TpList", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            root.transform.SetParent(parent, false);
            Image bg = root.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.28f);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -98f);
            rt.sizeDelta = new Vector2(420f, 328f);

            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(root.transform, false);
            RectTransform crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 4f;
            layout.padding = new RectOffset(6, 6, 6, 6);

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.viewport = rt;
            scroll.content = crt;
            return content.transform;
        }

        private static void CreateTabs(Transform parent)
        {
            GameObject row = new GameObject("tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -58f);
            rt.sizeDelta = new Vector2(420f, 32f);

            HorizontalLayoutGroup group = row.GetComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = true;
            group.childForceExpandWidth = true;
            group.spacing = 6f;

            GameObject destTab = MakeButton(row.transform, "Destinations", TabOn, 1f, 180f, 32f, () => ShowTab(false));
            GameObject playerTab = MakeButton(row.transform, "Teleport player", TabOff, 1f, 180f, 32f, () => ShowTab(true));
            destTabImage = destTab.GetComponent<Image>();
            playerTabImage = playerTab.GetComponent<Image>();
            Text playerTabText = playerTab.GetComponentInChildren<Text>();
            if (playerTabText)
            {
                playerTabText.fontSize = 14;
            }
        }

        private static void ShowTab(bool players)
        {
            showingPlayers = players;
            if (inputGo)
            {
                inputGo.SetActive(!players);
            }

            if (saveGo)
            {
                saveGo.SetActive(!players);
            }

            if (destTabImage)
            {
                destTabImage.color = players ? TabOff : TabOn;
            }

            if (playerTabImage)
            {
                playerTabImage.color = players ? TabOn : TabOff;
            }

            if (players)
            {
                RefreshPlayers();
            }
            else
            {
                RefreshList();
            }
        }

        private static void RefreshList()
        {
            if (!listContent)
            {
                return;
            }

            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(listContent.GetChild(i).gameObject);
            }

            List<TpPoint> points = LoadCurrentPoints();
            if (points.Count == 0)
            {
                AddHint("Aucune destination. Sauve ta position.");
                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                AddRow(points[i], i);
            }
        }

        private static void AddHint(string text)
        {
            GameObject hint = new GameObject("hint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
            hint.transform.SetParent(listContent, false);
            LayoutElement layout = hint.GetComponent<LayoutElement>();
            layout.minHeight = 28f;
            layout.preferredHeight = 28f;
            Text label = hint.GetComponent<Text>();
            label.font = UiFont();
            label.fontSize = 16;
            label.color = new Color(0.9f, 0.82f, 0.65f, 1f);
            label.alignment = TextAnchor.MiddleCenter;
            label.text = text;
            label.raycastTarget = false;
        }

        private static void AddRow(TpPoint point, int index)
        {
            GameObject row = new GameObject("row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
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

            int captured = index;
            MakeButton(row.transform, point.name, new Color(0.28f, 0.2f, 0.12f, 0.95f), 1f, 240f, 36f, () => Travel(captured));
            MakeButton(row.transform, "X", new Color(0.45f, 0.12f, 0.1f, 0.95f), 0f, 36f, 36f, () => DeleteAt(captured));
        }

        private static GameObject MakeButton(Transform parent, string label, Color color, float flex, float minWidth, float height, UnityAction click)
        {
            GameObject go = new GameObject("btn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.minWidth = minWidth;
            layout.preferredWidth = minWidth;
            layout.flexibleWidth = flex;
            layout.minHeight = height;
            layout.preferredHeight = height;
            Image image = go.GetComponent<Image>();
            image.color = color;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(click);

            GameObject textGo = new GameObject("text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6f, 0f);
            textRt.offsetMax = new Vector2(-6f, 0f);
            Text text = textGo.GetComponent<Text>();
            text.font = UiFont();
            text.fontSize = 16;
            text.color = new Color(0.95f, 0.88f, 0.7f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            text.raycastTarget = false;
            return go;
        }

        private static Font UiFont()
        {
            GUIManager gui = GUIManager.Instance;
            if (gui != null && gui.AveriaSerifBold)
            {
                return gui.AveriaSerifBold;
            }

            Font arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return arial ? arial : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static void SaveHere()
        {
            Player player = Util.AlivePlayer();
            if (!player)
            {
                return;
            }

            string name = nameField && !string.IsNullOrWhiteSpace(nameField.text)
                ? nameField.text.Trim()
                : "";
            List<TpPoint> points = LoadCurrentPoints();
            if (string.IsNullOrEmpty(name))
            {
                name = "Point " + (points.Count + 1);
            }

            if (points.Count >= MaxPoints && IndexOfName(points, name) < 0)
            {
                Util.NotifyPlayer(player, "Trop de destinations");
                return;
            }

            TpPoint point = new TpPoint
            {
                name = name,
                x = player.transform.position.x,
                y = player.transform.position.y,
                z = player.transform.position.z,
                yaw = player.transform.eulerAngles.y
            };

            int existing = IndexOfName(points, name);
            if (existing >= 0)
            {
                points[existing] = point;
            }
            else
            {
                points.Add(point);
            }

            SaveCurrentPoints(points);
            if (nameField)
            {
                nameField.text = "";
            }

            RefreshList();
            Util.NotifyPlayer(player, "Destination sauvée : " + name);
        }

        private static void DeleteAt(int index)
        {
            List<TpPoint> points = LoadCurrentPoints();
            if (index < 0 || index >= points.Count)
            {
                return;
            }

            points.RemoveAt(index);
            SaveCurrentPoints(points);
            RefreshList();
        }

        private static void SetInputBlocked(bool blocked)
        {
            if (inputBlocked == blocked || GUIManager.Instance == null)
            {
                return;
            }

            GUIManager.BlockInput(blocked);
            inputBlocked = blocked;
        }

        private static int IndexOfName(List<TpPoint> points, string name)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (string.Equals(points[i].name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
