using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class SolarisUIManager : MonoBehaviour
{
    public static SolarisUIManager Instance { get; private set; }

    // References
    private SandGridWorld _world;
    private EcoReclaimerPlayer _player;
    private SweeperDroneManager _drones;
    private OutpostAndCombatManager _combat;
    private SolarisGameManager _gm;

    // Canvas & Sprite
    private Canvas _canvas;
    private CanvasScaler _scaler;
    private GraphicRaycaster _raycaster;
    private Sprite _whiteSprite;
    private Font _uiFont;

    // Palette Colors (Matte-Black Antigravity Aesthetic)
    private readonly Color COLOR_BG_DARK = new Color(0.051f, 0.059f, 0.071f, 0.94f); // #0D0F12
    private readonly Color COLOR_PANEL = new Color(0.086f, 0.098f, 0.118f, 0.95f);
    private readonly Color COLOR_BORDER = new Color(0.224f, 0.816f, 0.753f, 0.60f); // #39D0C0 border
    private readonly Color COLOR_CYAN = new Color(0.224f, 0.816f, 0.753f, 1f);     // #39D0C0 turquoise
    private readonly Color COLOR_AMBER = new Color(0.961f, 0.620f, 0.043f, 1f);    // #F59E0B amber
    private readonly Color COLOR_EMERALD = new Color(0.063f, 0.725f, 0.506f, 1f);  // #10B981 green
    private readonly Color COLOR_CRIMSON = new Color(0.937f, 0.267f, 0.267f, 1f);  // #EF4444 red
    private readonly Color COLOR_TEXT_MUTED = new Color(0.58f, 0.64f, 0.72f, 1f);

    // Bottom-Left Elements
    private RawImage _minimapRawImage;
    private Texture2D _minimapTex;
    private Color32[] _minimapPixels;
    private float _minimapTimer = 0f;
    private const float MINIMAP_INTERVAL = 0.12f;

    // 4-Pill Horizontal Strip
    private Text _pillSilicaText;
    private Text _pillRestorationText;
    private Text _pillOutpostText;
    private Text _pillStormText;
    private Image _pillStormBg;

    // Bottom-Right 3 Sleek Status Bars
    private Image _barHealthFill;
    private Text _barHealthText;
    private Image _barDustFill;
    private Text _barDustText;
    private Image _barHeatFill;
    private Text _barHeatText;

    // Floating Workshop Badge
    private GameObject _workshopBadge;
    private Text _workshopBadgeText;

    // In-World Workshop Modal
    private GameObject _workshopModal;
    private Text _modalSilicaText;
    private Text _modalDustText;
    private Button _btnRefineDust;
    private Text _btnRefineDustText;
    private Button _btnPlantTree;
    private Text _btnPlantTreeText;

    // Workshop 5 Upgrade Cards UI references
    private struct UpgradeCardUI
    {
        public Text LevelText;
        public Text CostText;
        public Button BuyButton;
    }
    private UpgradeCardUI[] _upgradeCards = new UpgradeCardUI[5];

    // Main Menu, Pause Menu, Controls, Victory & Toast
    private GameObject _mainMenu;
    private Button _btnSoundToggle;
    private Text _txtSoundToggle;
    private GameObject _pauseMenu;
    private GameObject _controlsModal;
    private GameObject _victoryModal;

    // Toast
    private GameObject _toastObj;
    private Text _toastText;
    private CanvasGroup _toastCanvasGroup;
    private Coroutine _toastCoroutine;

    // Properties for external checking
    public bool IsWorkshopOpen => _workshopModal != null && _workshopModal.activeSelf;
    public bool IsPauseOpen => _pauseMenu != null && _pauseMenu.activeSelf;
    public bool IsControlsOpen => _controlsModal != null && _controlsModal.activeSelf;
    public bool IsMainMenuOpen => _mainMenu != null && _mainMenu.activeSelf;
    public bool IsVictoryOpen => _victoryModal != null && _victoryModal.activeSelf;

    public static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { SolarisConfig.SafeDestroy(gameObject); return; }

        EnsureAssetsLoaded();
        EnsureEventSystem();

        // Self-heal: ensure all child Text components have a valid font
        if (_uiFont != null)
        {
            foreach (var t in GetComponentsInChildren<Text>(true))
            {
                if (t.font == null) t.font = _uiFont;
            }
        }
    }

    public void EnsureAssetsLoaded()
    {
        CreateWhiteSprite();
        LoadFont();
    }

    private void CreateWhiteSprite()
    {
        if (_whiteSprite != null) return;
        Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Color[] cols = new Color[] { Color.white, Color.white, Color.white, Color.white };
        t.SetPixels(cols);
        t.Apply();
        _whiteSprite = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
    }

    private void LoadFont()
    {
        if (_uiFont != null) return;
        _uiFont = Resources.Load<Font>("Fonts/Inter-SemiBold")
               ?? Resources.Load<Font>("Fonts/Inter-Regular")
               ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_uiFont == null)
        {
            _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 14);
        }
        if (_uiFont == null)
        {
            _uiFont = Font.CreateDynamicFontFromOSFont("Segoe UI", 14);
        }
        if (_uiFont == null)
        {
            Font[] allFonts = Resources.FindObjectsOfTypeAll<Font>();
            if (allFonts != null && allFonts.Length > 0)
            {
                _uiFont = allFonts[0];
            }
        }
    }

    public void InitializeUI(SandGridWorld world, EcoReclaimerPlayer player, SweeperDroneManager drones, OutpostAndCombatManager combat, SolarisGameManager gm)
    {
        _world = world;
        _player = player;
        _drones = drones;
        _combat = combat;
        _gm = gm;

        EnsureAssetsLoaded();
        EnsureEventSystem();
        BuildCanvasAndUI();
    }

    private void BuildCanvasAndUI()
    {
        EnsureAssetsLoaded();
        EnsureEventSystem();

        // 1. Setup Canvas
        _canvas = GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;

        _scaler = GetComponent<CanvasScaler>();
        if (_scaler == null) _scaler = gameObject.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1920, 1080);
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        _scaler.matchWidthOrHeight = 0.5f;

        _raycaster = GetComponent<GraphicRaycaster>();
        if (_raycaster == null) _raycaster = gameObject.AddComponent<GraphicRaycaster>();

        // Clear previous runtime children if any
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            SolarisConfig.SafeDestroy(transform.GetChild(i).gameObject);
        }

        // 2. Build Bottom-Left (Minimap & 4-Pill strip)
        BuildBottomLeftHUD();

        // 3. Build Bottom-Right (3 Sleek Status Bars)
        BuildBottomRightHUD();

        // 4. Build Floating Workshop Badge
        BuildWorkshopBadge();

        // 5. Build Workshop Modal
        BuildWorkshopModal();

        // 6. Build Main Menu Overlay
        BuildMainMenuOverlay();

        // 7. Build Pause Menu Overlay
        BuildPauseMenuOverlay();

        // 8. Build Controls Modal
        BuildControlsModal();

        // 9. Build Victory Celebration Modal
        BuildVictoryModal();

        // 10. Build Toast Notification
        BuildToastNotification();

        // Initial visibility
        SetWorkshopVisible(false);
        SetPauseMenuVisible(false);
        SetControlsVisible(false);
        if (_victoryModal != null) _victoryModal.SetActive(false);
        if (_workshopBadge != null) _workshopBadge.SetActive(false);
    }

    // ==========================================
    // BOTTOM-LEFT HUD: MINIMAP & 4-PILL STRIP
    // ==========================================
    private void BuildBottomLeftHUD()
    {
        // Root Anchor: (0, 0)
        GameObject root = new GameObject("BottomLeftHUD", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        RectTransform rtRoot = root.GetComponent<RectTransform>();
        rtRoot.anchorMin = new Vector2(0f, 0f);
        rtRoot.anchorMax = new Vector2(0f, 0f);
        rtRoot.pivot = new Vector2(0f, 0f);
        rtRoot.anchoredPosition = new Vector2(28f, 28f);
        rtRoot.sizeDelta = new Vector2(360f, 260f);

        // 180x180 Radar Minimap Frame
        GameObject minimapFrame = CreatePanel(root.transform, "MinimapFrame", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(188f, 188f), new Vector2(0f, 0f), COLOR_BORDER);
        GameObject minimapBg = CreatePanel(minimapFrame.transform, "MinimapBg", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(180f, 180f), Vector2.zero, COLOR_BG_DARK);

        GameObject rawGo = new GameObject("MinimapRawImage", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(minimapBg.transform, false);
        RectTransform rawRt = rawGo.GetComponent<RectTransform>();
        rawRt.anchorMin = Vector2.zero;
        rawRt.anchorMax = Vector2.one;
        rawRt.sizeDelta = Vector2.zero;
        _minimapRawImage = rawGo.GetComponent<RawImage>();

        // Init 128x128 Minimap Texture
        _minimapTex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        _minimapTex.filterMode = FilterMode.Bilinear;
        _minimapTex.wrapMode = TextureWrapMode.Clamp;
        _minimapPixels = new Color32[128 * 128];
        _minimapRawImage.texture = _minimapTex;

        // Directly above the minimap: 4-Pill Horizontal Strip (anchored at y = 196f)
        GameObject pillStrip = new GameObject("PillStrip", typeof(RectTransform));
        pillStrip.transform.SetParent(root.transform, false);
        RectTransform pillRt = pillStrip.GetComponent<RectTransform>();
        pillRt.anchorMin = new Vector2(0f, 0f);
        pillRt.anchorMax = new Vector2(0f, 0f);
        pillRt.pivot = new Vector2(0f, 0f);
        pillRt.anchoredPosition = new Vector2(0f, 196f);
        pillRt.sizeDelta = new Vector2(480f, 32f);

        float pillW = 108f;
        float pillH = 28f;
        float spacing = 8f;

        // Pill 1: Silica
        var p1 = CreatePill(pillStrip.transform, "PillSilica", new Vector2(0f * (pillW + spacing), 0f), new Vector2(pillW, pillH), COLOR_PANEL, COLOR_BORDER);
        _pillSilicaText = CreateText(p1.transform, "Text", "💎 Silika: 0", 12, FontStyle.Bold, TextAnchor.MiddleCenter, COLOR_CYAN, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // Pill 2: Restoration
        var p2 = CreatePill(pillStrip.transform, "PillRestoration", new Vector2(1f * (pillW + spacing), 0f), new Vector2(pillW, pillH), COLOR_PANEL, COLOR_BORDER);
        _pillRestorationText = CreateText(p2.transform, "Text", "🌿 Restorasyon: 0%", 12, FontStyle.Bold, TextAnchor.MiddleCenter, COLOR_EMERALD, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // Pill 3: Outpost
        var p3 = CreatePill(pillStrip.transform, "PillOutposts", new Vector2(2f * (pillW + spacing), 0f), new Vector2(pillW, pillH), COLOR_PANEL, COLOR_BORDER);
        _pillOutpostText = CreateText(p3.transform, "Text", "🏭 Karakol: 0/12", 12, FontStyle.Bold, TextAnchor.MiddleCenter, COLOR_AMBER, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // Pill 4: Storm
        var p4 = CreatePill(pillStrip.transform, "PillStorm", new Vector2(3f * (pillW + spacing), 0f), new Vector2(pillW + 18f, pillH), COLOR_PANEL, COLOR_BORDER);
        _pillStormBg = p4.GetComponent<Image>();
        _pillStormText = CreateText(p4.transform, "Text", "🌪️ Fırtına: 120s", 12, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    // ==========================================
    // BOTTOM-RIGHT HUD: 3 SLEEK STATUS BARS
    // ==========================================
    private void BuildBottomRightHUD()
    {
        // Root Anchor: (1, 0)
        GameObject root = new GameObject("BottomRightHUD", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        RectTransform rtRoot = root.GetComponent<RectTransform>();
        rtRoot.anchorMin = new Vector2(1f, 0f);
        rtRoot.anchorMax = new Vector2(1f, 0f);
        rtRoot.pivot = new Vector2(1f, 0f);
        rtRoot.anchoredPosition = new Vector2(-28f, 28f);
        rtRoot.sizeDelta = new Vector2(260f, 130f);

        float barW = 240f;
        float barH = 22f;

        // Bar 1: CAN (HP)
        var hpBar = CreateBar(root.transform, "HealthBar", new Vector2(0f, 76f), new Vector2(barW, barH), "❤️ CAN", COLOR_EMERALD, out _barHealthFill, out _barHealthText);

        // Bar 2: TOZ TANKI
        var dustBar = CreateBar(root.transform, "DustBar", new Vector2(0f, 40f), new Vector2(barW, barH), "🛢️ TOZ TANKI", COLOR_AMBER, out _barDustFill, out _barDustText);

        // Bar 3: TERMAL
        var heatBar = CreateBar(root.transform, "HeatBar", new Vector2(0f, 4f), new Vector2(barW, barH), "🔥 TERMAL", COLOR_CYAN, out _barHeatFill, out _barHeatText);
    }

    // ==========================================
    // FLOATING WORKSHOP BADGE
    // ==========================================
    private void BuildWorkshopBadge()
    {
        _workshopBadge = CreateBorderedPanel(transform, "WorkshopBadge", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(440f, 44f), new Vector2(0f, 42f), COLOR_PANEL, COLOR_AMBER, 2f);

        _workshopBadgeText = CreateText(_workshopBadge.transform, "BadgeText", "⚡ [F] SİLİKA ATÖLYESİ & ARITMA MERKEZİ", 15, FontStyle.Bold, TextAnchor.MiddleCenter, COLOR_AMBER, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    // ==========================================
    // IN-WORLD WORKSHOP MODAL
    // ==========================================
    private void BuildWorkshopModal()
    {
        _workshopModal = new GameObject("WorkshopModal", typeof(RectTransform));
        _workshopModal.transform.SetParent(transform, false);
        RectTransform rt = _workshopModal.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(680f, 560f);

        // Backdrop panel
        CreateBorderedPanel(_workshopModal.transform, "ModalFrame", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, COLOR_PANEL, COLOR_CYAN, 2.5f);

        // Header Title
        CreateText(_workshopModal.transform, "Title", "⚙️ SİLİKA ATÖLYESİ & ARITMA MERKEZİ", 18, FontStyle.Bold, TextAnchor.MiddleLeft, COLOR_CYAN,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(440f, 36f), new Vector2(24f, -16f));

        // Sub Status (Silica & Dust Tank)
        _modalSilicaText = CreateText(_workshopModal.transform, "ModalSilica", "💎 Silika: 0", 14, FontStyle.Bold, TextAnchor.MiddleRight, COLOR_CYAN,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(160f, 24f), new Vector2(-68f, -14f));
        _modalDustText = CreateText(_workshopModal.transform, "ModalDust", "🛢️ Toz Tankı: 0%", 12, FontStyle.Normal, TextAnchor.MiddleRight, COLOR_AMBER,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(160f, 20f), new Vector2(-68f, -36f));

        // Close Button [X]
        CreateButton(_workshopModal.transform, "CloseBtn", "✕", new Color(0.2f, 0.2f, 0.2f, 0.9f), Color.white, () => SetWorkshopVisible(false),
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(34f, 34f), new Vector2(-16f, -16f));

        // Separator line
        CreatePanel(_workshopModal.transform, "Divider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(-32f, 2f), new Vector2(0f, -60f), COLOR_BORDER);

        // 5 Upgrade Cards
        string[] cardTitles = new string[] {
            "Siklon Vakum Başlığı",
            "Yüksek Basınçlı Toz Tankı",
            "Silika Kristal Tüfeği [Sağ Tık]",
            "Güneş Plazma Mızrağı [Q] & Şok",
            "Otonom Muhafız Dronu"
        };
        string[] cardSub = new string[] {
            "Vakum menzili ve çekiş hızını artırır",
            "Maksimum toz kapasitesini ve arıtma verimini katlar",
            "Atış hasarını ve mermi hızını yükseltir",
            "Delici lazer ışını ve sismik darbe hasarını artırır",
            "Otomatik devriye gezen ve kum temizleyen drone üretir"
        };

        float startY = -74f;
        float cardH = 68f;
        float cardGap = 8f;

        for (int i = 0; i < 5; i++)
        {
            int index = i;
            float cy = startY - i * (cardH + cardGap);
            GameObject card = CreateBorderedPanel(_workshopModal.transform, "Card_" + i, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(-48f, cardH), new Vector2(24f, cy), new Color(0.06f, 0.07f, 0.09f, 0.95f), new Color(0.2f, 0.25f, 0.3f, 0.6f), 1.5f);

            // Title
            CreateText(card.transform, "Title", cardTitles[i], 14, FontStyle.Bold, TextAnchor.UpperLeft, Color.white,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(360f, 22f), new Vector2(14f, -8f));

            // Subtitle
            CreateText(card.transform, "Desc", cardSub[i], 11, FontStyle.Normal, TextAnchor.UpperLeft, COLOR_TEXT_MUTED,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(380f, 18f), new Vector2(14f, -32f));

            // Level indicator
            _upgradeCards[i].LevelText = CreateText(card.transform, "Level", "Lv 0/3", 12, FontStyle.Bold, TextAnchor.MiddleLeft, COLOR_CYAN,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, 18f), new Vector2(14f, -48f));

            // Buy Button
            _upgradeCards[i].BuyButton = CreateButton(card.transform, "BuyBtn", "YÜKSELT (💎 15)", COLOR_CYAN, COLOR_BG_DARK, () => OnBuyUpgradeClicked(index),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(160f, 38f), new Vector2(-14f, 0f));
            _upgradeCards[i].CostText = _upgradeCards[i].BuyButton.GetComponentInChildren<Text>();
        }

        // Bottom 2 Action Buttons
        // Button 1: Refine Dust
        _btnRefineDust = CreateButton(_workshopModal.transform, "BtnRefine", "♻️ Toz Tankını Arıt [R]", COLOR_AMBER, COLOR_BG_DARK, OnRefineDustClicked,
            new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-36f, 44f), new Vector2(150f, 22f));
        _btnRefineDustText = _btnRefineDust.GetComponentInChildren<Text>();

        // Button 2: Plant Oasis Tree
        _btnPlantTree = CreateButton(_workshopModal.transform, "BtnPlantTree", "🌳 Yeşil Kuşak Ağacı Dik [T] (💎 8)", COLOR_EMERALD, COLOR_BG_DARK, OnPlantTreeClicked,
            new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(-36f, 44f), new Vector2(160f, 22f));
        _btnPlantTreeText = _btnPlantTree.GetComponentInChildren<Text>();
    }

    // ==========================================
    // MAIN MENU OVERLAY
    // ==========================================
    private void BuildMainMenuOverlay()
    {
        _mainMenu = new GameObject("MainMenuOverlay", typeof(RectTransform));
        _mainMenu.transform.SetParent(transform, false);
        RectTransform rt = _mainMenu.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        // Dark gradient overlay
        CreatePanel(_mainMenu.transform, "OverlayBg", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.04f, 0.05f, 0.06f, 0.82f), true);

        // Center card
        GameObject card = CreateBorderedPanel(_mainMenu.transform, "MenuCard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(560f, 440f), Vector2.zero, COLOR_BG_DARK, COLOR_CYAN, 2.5f);

        // Title
        CreateText(card.transform, "Title", "SOLARIS RECLAIM", 34, FontStyle.Bold, TextAnchor.MiddleCenter, COLOR_CYAN,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(500f, 50f), new Vector2(0f, -40f));
        CreateText(card.transform, "SubTitle", "Küllerin Altındaki Şehir v2.0", 15, FontStyle.Normal, TextAnchor.MiddleCenter, COLOR_AMBER,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(500f, 26f), new Vector2(0f, -90f));

        // Buttons
        float btnW = 340f;
        float btnH = 44f;
        float btnSpacing = 14f;
        float topY = -150f;

        CreateButton(card.transform, "BtnStart", "OYUNA BAŞLA / DEVAM ET", COLOR_CYAN, COLOR_BG_DARK, () => {
            SetMainMenuVisible(false);
            if (_gm != null) _gm.StartGame();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(btnW, btnH), new Vector2(0f, topY));

        CreateButton(card.transform, "BtnReset", "YENİ OYUN (SIFIRLA)", new Color(0.18f, 0.22f, 0.28f, 0.95f), Color.white, () => {
            SetMainMenuVisible(false);
            if (_gm != null) _gm.RestartGame();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(btnW, btnH), new Vector2(0f, topY - (btnH + btnSpacing)));

        CreateButton(card.transform, "BtnControls", "KONTROLLER & REHBER", new Color(0.18f, 0.22f, 0.28f, 0.95f), Color.white, () => {
            SetControlsVisible(true);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(btnW, btnH), new Vector2(0f, topY - 2f * (btnH + btnSpacing)));

        _btnSoundToggle = CreateButton(card.transform, "BtnSound", "SES: AÇIK", new Color(0.18f, 0.22f, 0.28f, 0.95f), COLOR_EMERALD, () => {
            if (_gm != null)
            {
                bool muted = _gm.ToggleMute();
                _txtSoundToggle.text = muted ? "SES: KAPALI" : "SES: AÇIK";
                _txtSoundToggle.color = muted ? COLOR_CRIMSON : COLOR_EMERALD;
            }
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(btnW, btnH), new Vector2(0f, topY - 3f * (btnH + btnSpacing)));
        _txtSoundToggle = _btnSoundToggle.GetComponentInChildren<Text>();
    }

    // ==========================================
    // ESC PAUSE MENU OVERLAY
    // ==========================================
    private void BuildPauseMenuOverlay()
    {
        _pauseMenu = new GameObject("PauseMenuOverlay", typeof(RectTransform));
        _pauseMenu.transform.SetParent(transform, false);
        RectTransform rt = _pauseMenu.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        CreatePanel(_pauseMenu.transform, "OverlayBg", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.04f, 0.05f, 0.06f, 0.75f), true);

        GameObject card = CreateBorderedPanel(_pauseMenu.transform, "PauseCard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(440f, 360f), Vector2.zero, COLOR_BG_DARK, COLOR_AMBER, 2.5f);

        CreateText(card.transform, "Title", "OYUN DURAKLATILDI", 24, FontStyle.Bold, TextAnchor.MiddleCenter, COLOR_AMBER,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(400f, 44f), new Vector2(0f, -30f));

        float btnW = 300f;
        float btnH = 40f;
        float btnSpacing = 12f;
        float topY = -95f;

        CreateButton(card.transform, "BtnResume", "OYUNA DEVAM ET", COLOR_CYAN, COLOR_BG_DARK, () => {
            if (_gm != null) _gm.TogglePause();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(btnW, btnH), new Vector2(0f, topY));

        CreateButton(card.transform, "BtnControls", "KONTROLLER & REHBER", new Color(0.18f, 0.22f, 0.28f, 0.95f), Color.white, () => {
            SetControlsVisible(true);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(btnW, btnH), new Vector2(0f, topY - (btnH + btnSpacing)));

        CreateButton(card.transform, "BtnRestart", "YENİDEN BAŞLAT", new Color(0.18f, 0.22f, 0.28f, 0.95f), Color.white, () => {
            SetPauseMenuVisible(false);
            if (_gm != null) _gm.RestartGame();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(btnW, btnH), new Vector2(0f, topY - 2f * (btnH + btnSpacing)));

        CreateButton(card.transform, "BtnMainMenu", "ANA MENÜYE DÖN", new Color(0.18f, 0.22f, 0.28f, 0.95f), COLOR_CRIMSON, () => {
            SetPauseMenuVisible(false);
            SetMainMenuVisible(true);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(btnW, btnH), new Vector2(0f, topY - 3f * (btnH + btnSpacing)));
    }

    // ==========================================
    // CONTROLS & GUIDE MODAL
    // ==========================================
    private void BuildControlsModal()
    {
        _controlsModal = new GameObject("ControlsModal", typeof(RectTransform));
        _controlsModal.transform.SetParent(transform, false);
        RectTransform rt = _controlsModal.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(620f, 480f);

        CreateBorderedPanel(_controlsModal.transform, "ModalBg", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, COLOR_BG_DARK, COLOR_CYAN, 2.5f);

        CreateText(_controlsModal.transform, "Title", "🎮 KONTROLLER VE OYUN REHBERİ", 20, FontStyle.Bold, TextAnchor.MiddleCenter, COLOR_CYAN,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(560f, 40f), new Vector2(0f, -25f));

        string guide =
            "• WASD / Ok Tuşları: Karakter Hareketi & Koşma\n" +
            "• Fare İmleci: Yönelme ve Nişan Alma\n" +
            "• Sol Tık / [E]: Siklon Vakum Başlığı (Kum Toplama)\n" +
            "• Sağ Tık: Silika Kristal Tüfeği (Düşman & Vent Saldırısı)\n" +
            "• Space: Sismik Şok Dalgası (AoE Kum Temizleme & İtme)\n" +
            "• [Q]: Güneş Plazma Mızrağı (Delici Işın Saldırısı)\n" +
            "• [R]: Toz Tankını Arıt (Atölye/Merkezde bonuslu Silika)\n" +
            "• [F]: Atölye Aç / Kapat (Eco-Hub [18, 14] Yanında)\n" +
            "• [T]: Yeşil Kuşak Vaha Ağacı Dik (💎 8 Silika)\n" +
            "• ESC: Duraklat / Pencereleri Kapat";

        CreateText(_controlsModal.transform, "Content", guide, 14, FontStyle.Normal, TextAnchor.UpperLeft, Color.white,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(-60f, 300f), new Vector2(0f, -80f));

        CreateButton(_controlsModal.transform, "CloseBtn", "KAPAT", COLOR_CYAN, COLOR_BG_DARK, () => SetControlsVisible(false),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(180f, 38f), new Vector2(0f, 35f));
    }

    // ==========================================
    // VICTORY CELEBRATION MODAL
    // ==========================================
    private void BuildVictoryModal()
    {
        _victoryModal = new GameObject("VictoryModal", typeof(RectTransform));
        _victoryModal.transform.SetParent(transform, false);
        RectTransform rt = _victoryModal.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(620f, 400f);

        CreateBorderedPanel(_victoryModal.transform, "VictoryFrame", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, COLOR_BG_DARK, COLOR_EMERALD, 2.5f);

        CreateText(_victoryModal.transform, "Title", "🌿 ŞEHİR KURTARILDI! İLK YAĞMUR BAŞLADI!", 22, FontStyle.Bold, TextAnchor.MiddleCenter, COLOR_EMERALD,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(560f, 45f), new Vector2(0f, -30f));

        string msg =
            "Solaris-7 ekolojik restorasyonu %80 sınırını aştı!\n\n" +
            "Güneş panelleri yeniden şebekeye bağlandı, atmosferik su kuleleri devreye girdi " +
            "ve yüzyıllar sonra çölün üzerine ilk yaşam yağmuru yağmaya başladı.\n\n" +
            "Düşman karakollarını temizlemeye, şehri tamamen yeşillendirmeye ve " +
            "ekipmanlarını geliştirmeye kesintisiz devam edebilirsin!";

        CreateText(_victoryModal.transform, "Body", msg, 14, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(520f, 180f), new Vector2(0f, 15f));

        CreateButton(_victoryModal.transform, "BtnFreePlay", "OYNAMAYA DEVAM ET (SERBEST MOD)", COLOR_EMERALD, COLOR_BG_DARK, () => {
            HideVictoryModal();
            if (_gm != null) _gm.EnableFreePlay();
        }, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(360f, 44f), new Vector2(0f, 42f));
    }

    // ==========================================
    // CENTRAL TOAST NOTIFICATION
    // ==========================================
    private void BuildToastNotification()
    {
        _toastObj = new GameObject("ToastNotification", typeof(RectTransform), typeof(CanvasGroup));
        _toastObj.transform.SetParent(transform, false);
        RectTransform rt = _toastObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.85f);
        rt.anchorMax = new Vector2(0.5f, 0.85f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(520f, 44f);

        _toastCanvasGroup = _toastObj.GetComponent<CanvasGroup>();
        CreateBorderedPanel(_toastObj.transform, "ToastFrame", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.08f, 0.10f, 0.12f, 0.95f), COLOR_AMBER, 2f);

        _toastText = CreateText(_toastObj.transform, "Text", "", 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        _toastObj.SetActive(false);
    }

    public void ShowToast(string msg)
    {
        if (_toastObj == null) return;
        if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
        _toastCoroutine = StartCoroutine(ToastRoutine(msg));
    }

    private IEnumerator ToastRoutine(string msg)
    {
        _toastText.text = msg;
        _toastObj.SetActive(true);
        _toastCanvasGroup.alpha = 1f;

        yield return new WaitForSeconds(1.8f);

        float elapsed = 0f;
        while (elapsed < 0.6f)
        {
            elapsed += Time.unscaledDeltaTime;
            _toastCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / 0.6f);
            yield return null;
        }

        _toastObj.SetActive(false);
        _toastCoroutine = null;
    }

    // ==========================================
    // PUBLIC VISIBILITY CONTROLS
    // ==========================================
    public void SetMainMenuVisible(bool visible)
    {
        if (_mainMenu != null) _mainMenu.SetActive(visible);
    }

    public void SetPauseMenuVisible(bool visible)
    {
        if (_pauseMenu != null) _pauseMenu.SetActive(visible);
    }

    public void SetWorkshopVisible(bool visible)
    {
        if (_workshopModal != null)
        {
            _workshopModal.SetActive(visible);
            if (visible) RefreshWorkshopUI();
        }
    }

    public void SetControlsVisible(bool visible)
    {
        if (_controlsModal != null) _controlsModal.SetActive(visible);
    }

    public void ShowVictoryModal()
    {
        if (_victoryModal != null) _victoryModal.SetActive(true);
    }

    public void HideVictoryModal()
    {
        if (_victoryModal != null) _victoryModal.SetActive(false);
    }

    // ==========================================
    // WORKSHOP ACTIONS & UPGRADE LOGIC
    // ==========================================
    private void OnBuyUpgradeClicked(int index)
    {
        if (_player == null) return;

        int[] costs;
        int currentLevel;
        int maxLevel;
        string upgradeKey;

        switch (index)
        {
            case 0:
                costs = SolarisConfig.COST_NOZZLE;
                currentLevel = _player.NozzleLevel;
                maxLevel = costs.Length;
                upgradeKey = "nozzle";
                break;
            case 1:
                costs = SolarisConfig.COST_TANK;
                currentLevel = _player.TankLevel;
                maxLevel = costs.Length;
                upgradeKey = "tank";
                break;
            case 2:
                costs = SolarisConfig.COST_BLASTER;
                currentLevel = _player.BlasterLevel;
                maxLevel = costs.Length;
                upgradeKey = "blaster";
                break;
            case 3:
                costs = SolarisConfig.COST_LANCE;
                currentLevel = _player.LanceLevel;
                maxLevel = costs.Length;
                upgradeKey = "lance";
                break;
            case 4:
                costs = SolarisConfig.COST_DRONE;
                currentLevel = _drones != null ? _drones.ActiveDroneCount : _player.DroneLevel;
                maxLevel = costs.Length;
                upgradeKey = "drone";
                break;
            default:
                return;
        }

        if (currentLevel >= maxLevel)
        {
            ShowToast("Bu yükseltme zaten MAKSİMUM seviyede!");
            return;
        }

        int cost = costs[currentLevel];
        if (_player.Silica < cost)
        {
            ShowToast("Yetersiz Silika! Gerekli: 💎 " + cost);
            return;
        }

        // Deduct and Apply
        _player.Silica -= cost;
        if (index == 4 && _drones != null)
        {
            _drones.BuyDrone();
            _player.DroneLevel++;
        }
        else
        {
            _player.ApplyUpgrade(upgradeKey);
        }

        if (_gm != null) _gm.PlaySfx("upgrade_buy");
        ShowToast("Yükseltme tamamlandı: " + upgradeKey.ToUpper() + " Lv " + (currentLevel + 1));
        RefreshWorkshopUI();
    }

    private void OnRefineDustClicked()
    {
        if (_player == null) return;
        if (_player.DustTank < 20f)
        {
            ShowToast("Arıtma için en az %20 Toz Tankı gereklidir!");
            return;
        }

        int gained = _player.SmeltDust(true);
        if (_gm != null) _gm.PlaySfx("dust_collect");
        ShowToast("Toz tankı arıtıldı! +" + gained + " 💎 Silika elde edildi.");
        RefreshWorkshopUI();
    }

    private void OnPlantTreeClicked()
    {
        if (_player == null || _world == null) return;
        if (_player.Silica < SolarisConfig.TREE_COST)
        {
            ShowToast("Ağaç dikmek için 💎 " + SolarisConfig.TREE_COST + " Silika gerekli!");
            return;
        }

        bool planted = _player.TryPlantOasisTree();
        if (planted)
        {
            if (_gm != null) _gm.PlaySfx("structure_restore");
            ShowToast("🌳 Yeşil Kuşak Vaha Ağacı Dikildi! Fırtına Kalkanı Güçlendi.");
            RefreshWorkshopUI();
        }
        else
        {
            ShowToast("Buraya ağaç dikilemez!");
        }
    }

    public void RefreshWorkshopUI()
    {
        if (_player == null) return;

        if (_modalSilicaText != null) _modalSilicaText.text = "💎 Silika: " + _player.Silica;
        if (_modalDustText != null)
        {
            float pct = Mathf.Clamp01(_player.DustTank / Mathf.Max(1f, _player.DustTankMax)) * 100f;
            _modalDustText.text = "🛢️ Toz Tankı: " + pct.ToString("F0") + "%";
        }

        // Update each card
        int[][] allCosts = new int[][] {
            SolarisConfig.COST_NOZZLE,
            SolarisConfig.COST_TANK,
            SolarisConfig.COST_BLASTER,
            SolarisConfig.COST_LANCE,
            SolarisConfig.COST_DRONE
        };

        int[] curLevels = new int[] {
            _player.NozzleLevel,
            _player.TankLevel,
            _player.BlasterLevel,
            _player.LanceLevel,
            _drones != null ? _drones.ActiveDroneCount : _player.DroneLevel
        };

        for (int i = 0; i < 5; i++)
        {
            int lvl = curLevels[i];
            int maxLvl = allCosts[i].Length;

            if (lvl >= maxLvl)
            {
                _upgradeCards[i].LevelText.text = i == 4 ? "Aktif " + lvl + "/4 (MAKS)" : "Lv " + lvl + "/" + maxLvl + " (MAKS)";
                _upgradeCards[i].LevelText.color = COLOR_EMERALD;
                _upgradeCards[i].CostText.text = "MAKSİMUM";
                _upgradeCards[i].BuyButton.interactable = false;
            }
            else
            {
                int cost = allCosts[i][lvl];
                _upgradeCards[i].LevelText.text = i == 4 ? "Aktif " + lvl + "/4" : "Lv " + lvl + "/" + maxLvl;
                _upgradeCards[i].LevelText.color = COLOR_CYAN;
                _upgradeCards[i].CostText.text = "YÜKSELT (💎 " + cost + ")";
                _upgradeCards[i].BuyButton.interactable = _player.Silica >= cost;
            }
        }

        // Refine & Plant buttons
        if (_btnRefineDust != null)
        {
            _btnRefineDust.interactable = _player.DustTank >= 20f;
        }
        if (_btnPlantTree != null)
        {
            _btnPlantTree.interactable = _player.Silica >= SolarisConfig.TREE_COST;
        }
    }

    // ==========================================
    // UPDATE CYCLE: MINIMAP & LIVE HUD
    // ==========================================
    private void Update()
    {
        if (_gm == null || !_gm.GameStarted) return;

        UpdateMinimap();
        UpdateHUD();
        UpdateFloatingBadge();
    }

    private void UpdateFloatingBadge()
    {
        if (_workshopBadge == null) return;

        bool near = false;
        if (_world != null && _player != null)
        {
            near = _world.IsNearWorkshop(_player.transform.position);
        }

        bool showBadge = near && !IsWorkshopOpen && !IsPauseOpen && !IsMainMenuOpen;
        if (_workshopBadge.activeSelf != showBadge)
        {
            _workshopBadge.SetActive(showBadge);
        }
    }

    public void UpdateHUD()
    {
        if (_player == null) return;

        // 4-Pill strip
        if (_pillSilicaText != null)
            _pillSilicaText.text = "💎 Silika: " + _player.Silica;

        if (_pillRestorationText != null && _world != null)
            _pillRestorationText.text = "🌿 Restorasyon: " + _world.RestorationPercent.ToString("F0") + "%";

        if (_pillOutpostText != null && _combat != null)
            _pillOutpostText.text = "🏭 Karakol: " + _combat.DestroyedOutpostsCount + "/" + _combat.TotalOutposts;

        if (_pillStormText != null && _gm != null)
        {
            if (_gm.StormActive)
            {
                _pillStormText.text = "KANLI FIRTINA!";
                _pillStormText.color = Color.white;
                if (_pillStormBg != null) _pillStormBg.color = COLOR_CRIMSON;
            }
            else
            {
                int sec = Mathf.Max(0, Mathf.RoundToInt(_gm.StormTimer));
                _pillStormText.text = "🌪️ Fırtına: " + sec + "s";
                _pillStormText.color = Color.white;
                if (_pillStormBg != null) _pillStormBg.color = COLOR_PANEL;
            }
        }

        // Bottom-Right 3 Sleek Status Bars
        // 1. Health
        if (_barHealthFill != null)
        {
            float hpPct = Mathf.Clamp01(_player.HP / Mathf.Max(1f, _player.MaxHP));
            _barHealthFill.fillAmount = hpPct;
            if (_barHealthText != null)
                _barHealthText.text = "❤️ CAN: " + Mathf.RoundToInt(_player.HP) + " / " + Mathf.RoundToInt(_player.MaxHP);
        }

        // 2. Dust Tank
        if (_barDustFill != null)
        {
            float dustPct = Mathf.Clamp01(_player.DustTank / Mathf.Max(1f, _player.DustTankMax));
            _barDustFill.fillAmount = dustPct;
            if (_barDustText != null)
                _barDustText.text = "🛢️ TOZ TANKI: " + Mathf.RoundToInt(dustPct * 100f) + "%";
        }

        // 3. Thermal
        if (_barHeatFill != null)
        {
            float heatPct = Mathf.Clamp01(_player.Heat / Mathf.Max(1f, _player.HeatMax));
            _barHeatFill.fillAmount = heatPct;
            _barHeatFill.color = Color.Lerp(COLOR_CYAN, COLOR_CRIMSON, heatPct);
            if (_barHeatText != null)
            {
                string tag = _player.Overheated ? " (AŞIRI ISINDI!)" : "";
                _barHeatText.text = "🔥 TERMAL: " + Mathf.RoundToInt(heatPct * 100f) + "%" + tag;
            }
        }
    }

    // ==========================================
    // LIVE RADAR MINIMAP RENDERING (128x128)
    // ==========================================
    private void UpdateMinimap()
    {
        if (_minimapTex == null) return;
        _minimapTimer += Time.unscaledDeltaTime;
        if (_minimapTimer < MINIMAP_INTERVAL) return;
        _minimapTimer = 0f;

        // World coordinates range from -210 to +210 (420 world size)
        float halfWorld = SolarisConfig.HALF_WORLD;
        float worldSize = SolarisConfig.WORLD_SIZE;

        Color32 colSand = new Color32(215, 135, 60, 255);
        Color32 colGreen = new Color32(35, 195, 95, 255);
        Color32 colRoad = new Color32(35, 40, 45, 255);
        Color32 colBuilding = new Color32(50, 210, 235, 255);
        Color32 colEnemy = new Color32(235, 45, 45, 255);
        Color32 colPlayer = new Color32(255, 255, 255, 255);

        // Base terrain & roads
        for (int py = 0; py < 128; py++)
        {
            float wz = (py / 127f) * worldSize - halfWorld;
            int rowIdx = py * 128;

            for (int px = 0; px < 128; px++)
            {
                float wx = (px / 127f) * worldSize - halfWorld;

                // Roads check (|wx| < 7 or |wz| < 7)
                if (Mathf.Abs(wx) < 7f || Mathf.Abs(wz) < 7f)
                {
                    _minimapPixels[rowIdx + px] = colRoad;
                    continue;
                }

                // Sample World Grass/Reclaimed
                if (_world != null && _world.Grass != null)
                {
                    int gx = SolarisConfig.WorldToGridX(wx);
                    int gz = SolarisConfig.WorldToGridZ(wz);
                    int gIdx = gz * SolarisConfig.GRID_SIZE + gx;
                    if (gIdx >= 0 && gIdx < _world.Grass.Length && _world.Grass[gIdx] > 0.35f)
                    {
                        _minimapPixels[rowIdx + px] = colGreen;
                        continue;
                    }
                }

                _minimapPixels[rowIdx + px] = colSand;
            }
        }

        // Draw Buildings (cyan dots)
        if (_world != null && _world.BuildingColliders != null)
        {
            for (int b = 0; b < _world.BuildingColliders.Count; b++)
            {
                Bounds bounds = _world.BuildingColliders[b];
                int mx = Mathf.Clamp(Mathf.RoundToInt(((bounds.center.x + halfWorld) / worldSize) * 127f), 1, 126);
                int my = Mathf.Clamp(Mathf.RoundToInt(((bounds.center.z + halfWorld) / worldSize) * 127f), 1, 126);

                _minimapPixels[my * 128 + mx] = colBuilding;
                _minimapPixels[(my + 1) * 128 + mx] = colBuilding;
                _minimapPixels[my * 128 + (mx + 1)] = colBuilding;
            }
        }

        // Draw Enemy Outposts (red 3x3 dots)
        if (_combat != null && _combat.Outposts != null)
        {
            for (int o = 0; o < _combat.Outposts.Count; o++)
            {
                var op = _combat.Outposts[o];
                if (op != null && !op.destroyed)
                {
                    int mx = Mathf.Clamp(Mathf.RoundToInt(((op.position.x + halfWorld) / worldSize) * 127f), 2, 125);
                    int my = Mathf.Clamp(Mathf.RoundToInt(((op.position.z + halfWorld) / worldSize) * 127f), 2, 125);

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            _minimapPixels[(my + dy) * 128 + (mx + dx)] = colEnemy;
                        }
                    }
                }
            }
        }

        // Draw Player Marker (white 3x3 circle + forward pointer)
        if (_player != null)
        {
            Vector3 pPos = _player.transform.position;
            int px = Mathf.Clamp(Mathf.RoundToInt(((pPos.x + halfWorld) / worldSize) * 127f), 2, 125);
            int py = Mathf.Clamp(Mathf.RoundToInt(((pPos.z + halfWorld) / worldSize) * 127f), 2, 125);

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    _minimapPixels[(py + dy) * 128 + (px + dx)] = colPlayer;
                }
            }

            Vector3 fwd = _player.transform.forward;
            int fwdX = Mathf.Clamp(px + Mathf.RoundToInt(fwd.x * 3f), 0, 127);
            int fwdY = Mathf.Clamp(py + Mathf.RoundToInt(fwd.z * 3f), 0, 127);
            _minimapPixels[fwdY * 128 + fwdX] = colPlayer;
        }

        _minimapTex.SetPixels32(_minimapPixels);
        _minimapTex.Apply(false);
    }

    // ==========================================
    // UI CREATION HELPERS (Pure code, Zero prefabs)
    // ==========================================
    private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 pos, Color bg, bool raycast = false)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.sprite = _whiteSprite;
        img.color = bg;
        img.raycastTarget = raycast;
        return go;
    }

    private GameObject CreateBorderedPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 pos, Color bgColor, Color borderColor, float borderWidth = 2f)
    {
        // 1. Outer frame is the border
        GameObject frame = new GameObject(name, typeof(RectTransform), typeof(Image));
        frame.transform.SetParent(parent, false);
        RectTransform rtFrame = frame.GetComponent<RectTransform>();
        rtFrame.anchorMin = anchorMin;
        rtFrame.anchorMax = anchorMax;
        rtFrame.pivot = pivot;
        rtFrame.anchoredPosition = pos;
        rtFrame.sizeDelta = size;

        Image imgFrame = frame.GetComponent<Image>();
        imgFrame.sprite = _whiteSprite;
        imgFrame.color = borderColor;
        imgFrame.raycastTarget = false;

        // 2. Inner child is the dark background fill, inset by borderWidth
        GameObject inner = new GameObject(name + "_Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(frame.transform, false);
        RectTransform rtInner = inner.GetComponent<RectTransform>();
        rtInner.anchorMin = Vector2.zero;
        rtInner.anchorMax = Vector2.one;
        rtInner.pivot = new Vector2(0.5f, 0.5f);
        rtInner.anchoredPosition = Vector2.zero;
        rtInner.sizeDelta = new Vector2(-borderWidth * 2f, -borderWidth * 2f);

        Image imgInner = inner.GetComponent<Image>();
        imgInner.sprite = _whiteSprite;
        imgInner.color = bgColor;
        imgInner.raycastTarget = false;

        return frame;
    }

    private GameObject CreatePill(Transform parent, string name, Vector2 pos, Vector2 size, Color bgColor, Color borderColor)
    {
        return CreateBorderedPanel(parent, name, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), size, pos, bgColor, borderColor, 1.5f);
    }

    private GameObject CreateBar(Transform parent, string name, Vector2 pos, Vector2 size, string label, Color fillColor, out Image fillImg, out Text textComp)
    {
        GameObject root = CreateBorderedPanel(parent, name, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), size, pos, COLOR_BG_DARK, new Color(0.25f, 0.30f, 0.35f, 0.8f), 1.5f);

        // Fill Image (Horizontal)
        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(root.transform, false);
        RectTransform fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.anchoredPosition = Vector2.zero;
        fillRt.sizeDelta = new Vector2(-4f, -4f);

        fillImg = fillGo.GetComponent<Image>();
        fillImg.sprite = _whiteSprite;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;
        fillImg.color = fillColor;
        fillImg.raycastTarget = false;

        // Text label
        textComp = CreateText(root.transform, "Label", label, 11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        textComp.raycastTarget = false;

        return root;
    }

    private Text CreateText(Transform parent, string name, string text, int fontSize, FontStyle style, TextAnchor align, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 pos)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Text t = go.GetComponent<Text>();
        t.font = _uiFont;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private Button CreateButton(Transform parent, string name, string label, Color bgColor, Color textColor, UnityAction onClick,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 pos)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.sprite = _whiteSprite;
        img.color = bgColor;
        img.raycastTarget = true;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor = bgColor;
        cb.highlightedColor = Color.Lerp(bgColor, Color.white, 0.25f);
        cb.pressedColor = Color.Lerp(bgColor, Color.black, 0.35f);
        cb.selectedColor = bgColor;
        cb.disabledColor = new Color(0.15f, 0.17f, 0.20f, 0.8f);
        cb.colorMultiplier = 1f;
        btn.colors = cb;

        if (onClick != null) btn.onClick.AddListener(onClick);

        Text t = CreateText(go.transform, "BtnText", label, 13, FontStyle.Bold, TextAnchor.MiddleCenter, textColor,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        t.raycastTarget = false;

        return btn;
    }
}
