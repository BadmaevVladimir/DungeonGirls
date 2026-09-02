using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

// Фаза 5: хаб деревни (7.1) — навигация между экраном зданий (8.1) и экраном гачи (8.5).
// Плейсхолдер-стиль (3.8): только текст/кнопки, без арта.
public partial class HubManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] UIDocument uiDocument;

    [Header("Менеджеры")]
    [SerializeField] SaveManager saveManager;
    TutorialManager tutorialManager;
    VNManager vnManager;

    [Header("Гача-контент (11.1: Дженифер / Вайолет / Саша)")]
    [SerializeField] CharacterData[] gachaCharacters;

    [Header("Гача-анимация (11.1, общая механика сундука 8.2)")]
    [SerializeField] Texture2D gachaChestClosedTexture;
    [SerializeField] Texture2D gachaChestOpenTexture;

    // Тот же джингл открытия сундука (Audio/SFX/ChestOpen_Jingle.rpp), что и в RunFlowController.
    // Персонаж в гаче считается эпиком (роллы персонажа — самый редкий/ценный исход, 15% суммарно,
    // ItemTier для валютных призов сюда не подходит); для валюты — по result.CurrencyTier как есть.
    [SerializeField] AudioSource gachaOpenAudioSource;
    [SerializeField] AudioClip gachaOpenCommonClip;
    [SerializeField] AudioClip gachaOpenRareClip;
    [SerializeField] AudioClip gachaOpenEpicClip;
    [SerializeField] Sprite characterSilhouetteRayOverlay;
    [SerializeField] ItemCatalogData currencyIconCatalog;

    const int GachaPullCost = 50; // 8.5
    const string OpeningSceneId = "jennifer_intro_tavern";
    const string VioletFirstGachaSceneId = "violet_intro_gacha";
    const string SashaFirstGachaSceneId = "sasha_intro_gacha";

    static readonly BuildingType[] BuildingOrder = { BuildingType.Forge, BuildingType.Temple, BuildingType.Tavern };
    static readonly string[] BuildingIds = { "Forge", "Temple", "Tavern" };

    VisualElement mainMenuScreen;
    VisualElement buildingsScreen;
    VisualElement gachaScreen;
    VisualElement veteranDeckScreen;
    VisualElement charactersScreen;

    // Кнопки "Здания" больше нет: экран зданий открывается кликом по домику на карте деревни
    // (ForgeSpotButton/TempleSpotButton/TavernSpotButton, см. HubManager.Village.cs).
    Button gachaButton;
    Button veteranDeckButton;
    Button charactersButton;
    Button buildingsBackButton;
    Button gachaBackButton;
    Button veteranDeckBackButton;
    Button charactersBackButton;
    ScrollView veteranDeckScrollView;
    ScrollView charactersScrollView;

    Label metaCurrencyLabel;
    Label gachaCurrencyLabel;

    readonly Label[] buildingLevelLabels = new Label[3];
    readonly Label[] buildingBonusLabels = new Label[3];
    readonly Label[] buildingCostLabels = new Label[3];
    readonly Button[] buildingUpgradeButtons = new Button[3];

    Button gachaPullButton;
    VisualElement gachaResultPopup;
    Label gachaResultLabel;
    Button gachaResultCloseButton;
    VisualElement gachaRevealContainer;
    Image gachaChestSpriteImage;
    VisualElement gachaReelViewport;
    VisualElement gachaReelStrip;
    Button gachaSkipButton;
    bool gachaPullInProgress;

    Button resetProgressButton;
    VisualElement resetProgressConfirmPopup;
    Button resetProgressConfirmButton;
    Button resetProgressCancelButton;

    Button cheatMenuButton;
    Button quitGameButton;
    VisualElement cheatMenuPopup;
    TextField cheatCommandField;
    Label cheatResultLabel;
    Button cheatSubmitButton;
    Button cheatCloseButton;

    const string GreedIsGoodCheat = "greedisgood";
    const int GreedIsGoodReward = 10000;

    // Даёт по 1 копии каждого персонажа из gachaCharacters (см. SubmitCheatCommand в
    // HubManager.Navigation.cs) — не через гачу, напрямую в saveManager.
    const string IWantBitchesCheat = "iwantbitches";

    // Start() вместо OnEnable(): HubManager сидит на GameObject "GameManager", а UIDocument — на
    // отдельном GameObject "UI". Unity не гарантирует порядок OnEnable МЕЖДУ разными GameObject
    // (в отличие от порядка компонентов ВНУТРИ одного GameObject, где UIDocument идёт раньше
    // RunFlowController и поэтому его OnEnable успевает построить rootVisualElement). Если
    // HubManager.OnEnable отрабатывает раньше UIDocument.OnEnable, root.Q<>() возвращают null и
    // весь метод падает с NullReferenceException — кнопки "Здания"/"Гача" в итоге ничего не
    // делают (или не видны, если вылет случается до отрисовки). Start() гарантированно выполняется
    // после Awake/OnEnable всех объектов сцены, так что к этому моменту UIDocument уже построил дерево.
    void Start()
    {
        var root = uiDocument.rootVisualElement;
        CacheElements(root);
        tutorialManager = TutorialManager.GetOrCreate(uiDocument, saveManager);
        vnManager = uiDocument.GetComponent<VNManager>();
        if (vnManager == null) vnManager = uiDocument.gameObject.AddComponent<VNManager>();
        vnManager.SceneCompleted += OnVNSceneCompleted;
        BindTutorialTooltips();

        SetUpVillageMap();
        gachaButton.clicked += OpenGacha;
        buildingsBackButton.clicked += OpenVillage;
        gachaBackButton.clicked += OpenVillage;
        veteranDeckButton.clicked += OpenVeteranDeck;
        charactersButton.clicked += OpenCharacters;
        veteranDeckBackButton.clicked += OpenVillage;
        charactersBackButton.clicked += OpenVillage;
        gachaPullButton.clicked += TryPullGacha;
        gachaResultCloseButton.clicked += () => gachaResultPopup.style.display = DisplayStyle.None;

        resetProgressButton.clicked += () => resetProgressConfirmPopup.style.display = DisplayStyle.Flex;
        resetProgressCancelButton.clicked += () => resetProgressConfirmPopup.style.display = DisplayStyle.None;
        resetProgressConfirmButton.clicked += ConfirmResetProgress;
        cheatMenuButton.clicked += OpenCheatMenu;
        cheatCloseButton.clicked += CloseCheatMenu;
        cheatSubmitButton.clicked += SubmitCheatCommand;
        cheatCommandField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                SubmitCheatCommand();
                evt.StopPropagation();
            }
        });
        quitGameButton.clicked += QuitGame;

        for (int i = 0; i < BuildingOrder.Length; i++)
        {
            int index = i; // захват для замыкания
            buildingUpgradeButtons[index].clicked += () => TryUpgradeBuilding(BuildingOrder[index]);
        }

        RefreshBuildingsScreen();
        RefreshGachaScreen();
        StartOpeningSequence();
        if (!HasValidGachaCharacterPool())
        {
            Debug.LogError("[Hub] GDD 11.1: gachaCharacters должен содержать ровно Дженифер, Вайолет и Сашу с непустыми characterId.");
        }
    }

    void OnDestroy()
    {
        if (vnManager != null) vnManager.SceneCompleted -= OnVNSceneCompleted;
    }

    void StartOpeningSequence()
    {
        if (saveManager != null && !saveManager.HasSeenVNScene("jennifer", OpeningSceneId) &&
            vnManager != null && vnManager.TryPlayScene(OpeningSceneId))
        {
            return;
        }

        tutorialManager?.QueueOnce(TutorialContent.Intro);
    }

    void OnVNSceneCompleted(NarrativeSceneData scene, bool skipped)
    {
        if (scene == null) return;
        saveManager?.MarkVNSceneSeen(scene.characterId, scene.id);
        if (string.Equals(scene.id, OpeningSceneId, System.StringComparison.OrdinalIgnoreCase))
        {
            tutorialManager?.QueueOnce(TutorialContent.Intro);
        }
    }

    void CacheElements(VisualElement root)
    {
        mainMenuScreen = root.Q<VisualElement>("MainMenuScreen");
        buildingsScreen = root.Q<VisualElement>("BuildingsScreen");
        gachaScreen = root.Q<VisualElement>("GachaScreen");
        veteranDeckScreen = root.Q<VisualElement>("VeteranDeckScreen");
        charactersScreen = root.Q<VisualElement>("CharactersScreen");

        gachaButton = root.Q<Button>("GachaButton");
        veteranDeckButton = root.Q<Button>("VeteranDeckButton");
        charactersButton = root.Q<Button>("CharactersButton");
        buildingsBackButton = root.Q<Button>("BuildingsBackButton");
        gachaBackButton = root.Q<Button>("GachaBackButton");
        veteranDeckBackButton = root.Q<Button>("VeteranDeckBackButton");
        charactersBackButton = root.Q<Button>("CharactersBackButton");
        veteranDeckScrollView = root.Q<ScrollView>("VeteranDeckScrollView");
        charactersScrollView = root.Q<ScrollView>("CharactersScrollView");

        metaCurrencyLabel = root.Q<Label>("MetaCurrencyLabel");
        gachaCurrencyLabel = root.Q<Label>("GachaCurrencyLabel");

        for (int i = 0; i < BuildingIds.Length; i++)
        {
            buildingLevelLabels[i] = root.Q<Label>(BuildingIds[i] + "LevelLabel");
            buildingBonusLabels[i] = root.Q<Label>(BuildingIds[i] + "BonusLabel");
            buildingCostLabels[i] = root.Q<Label>(BuildingIds[i] + "CostLabel");
            buildingUpgradeButtons[i] = root.Q<Button>(BuildingIds[i] + "UpgradeButton");
        }

        gachaPullButton = root.Q<Button>("GachaPullButton");
        gachaResultPopup = root.Q<VisualElement>("GachaResultPopup");
        gachaResultLabel = root.Q<Label>("GachaResultLabel");
        gachaResultCloseButton = root.Q<Button>("GachaResultCloseButton");
        gachaRevealContainer = root.Q<VisualElement>("GachaRevealContainer");
        gachaChestSpriteImage = root.Q<Image>("GachaChestSpriteImage");
        gachaReelViewport = root.Q<VisualElement>("GachaReelViewport");
        gachaReelStrip = root.Q<VisualElement>("GachaReelStrip");
        gachaSkipButton = root.Q<Button>("GachaSkipButton");

        resetProgressButton = root.Q<Button>("ResetProgressButton");
        resetProgressConfirmPopup = root.Q<VisualElement>("ResetProgressConfirmPopup");
        resetProgressConfirmButton = root.Q<Button>("ResetProgressConfirmButton");
        resetProgressCancelButton = root.Q<Button>("ResetProgressCancelButton");

        cheatMenuButton = root.Q<Button>("CheatMenuButton");
        quitGameButton = root.Q<Button>("QuitGameButton");
        cheatMenuPopup = root.Q<VisualElement>("CheatMenuPopup");
        cheatCommandField = root.Q<TextField>("CheatCommandField");
        cheatResultLabel = root.Q<Label>("CheatResultLabel");
        cheatSubmitButton = root.Q<Button>("CheatSubmitButton");
        cheatCloseButton = root.Q<Button>("CheatCloseButton");

        CacheVillageElements(root);
    }

}
