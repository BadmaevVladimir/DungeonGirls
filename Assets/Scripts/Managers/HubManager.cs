using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

// Фаза 5: хаб деревни (7.1) — навигация между экраном зданий (8.1) и экраном гачи (8.5).
// Плейсхолдер-стиль (3.8): только текст/кнопки, без арта.
public class HubManager : MonoBehaviour
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
    [SerializeField] Sprite characterSilhouetteRayOverlay;
    [SerializeField] ItemCatalogData currencyIconCatalog;

    const int GachaPullCost = 50; // 8.5
    const string OpeningSceneId = "jennifer_intro_tavern";
    const string VioletFirstGachaSceneId = "violet_intro_gacha";

    static readonly BuildingType[] BuildingOrder = { BuildingType.Forge, BuildingType.Temple, BuildingType.Tavern };
    static readonly string[] BuildingIds = { "Forge", "Temple", "Tavern" };

    VisualElement mainMenuScreen;
    VisualElement buildingsScreen;
    VisualElement gachaScreen;
    VisualElement veteranDeckScreen;
    VisualElement charactersScreen;

    Button buildingsButton;
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

        buildingsButton.clicked += OpenBuildings;
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

        buildingsButton = root.Q<Button>("BuildingsButton");
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
    }

    // ==================== Навигация (7.1) ====================

    public void OpenVillage()
    {
        buildingsScreen.style.display = DisplayStyle.None;
        gachaScreen.style.display = DisplayStyle.None;
        veteranDeckScreen.style.display = DisplayStyle.None;
        charactersScreen.style.display = DisplayStyle.None;
        mainMenuScreen.style.display = DisplayStyle.Flex;
    }

    public void OpenBuildings()
    {
        RefreshBuildingsScreen();
        mainMenuScreen.style.display = DisplayStyle.None;
        buildingsScreen.style.display = DisplayStyle.Flex;
        tutorialManager?.QueueOnce(TutorialContent.Buildings);
    }

    public void OpenGacha()
    {
        RefreshGachaScreen();
        mainMenuScreen.style.display = DisplayStyle.None;
        gachaScreen.style.display = DisplayStyle.Flex;
        tutorialManager?.QueueOnce(TutorialContent.Gacha);
    }

    public void OpenVeteranDeck()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        veteranDeckScreen.style.display = DisplayStyle.Flex;
        veteranDeckScrollView.Clear();

        foreach (var veteran in saveManager.Data.veteranDeck)
        {
            if (veteran == null) continue;
            string displayName = CharacterDisplayName(veteran.characterId);
            int skillCount = veteran.finalSkills != null ? veteran.finalSkills.Count : 0;
            int equipmentCount = veteran.finalEquipmentSnapshot != null && veteran.finalEquipmentSnapshot.Count > 0
                ? veteran.finalEquipmentSnapshot.Count
                : (veteran.finalEquipment != null ? veteran.finalEquipment.Count : 0);
            var row = new Label($"{displayName} — {veteran.grade}, этажей {veteran.floorsCleared}, HP {veteran.finalHP:F0}, неуникальных навыков {skillCount}, снаряжения {equipmentCount}");
            row.AddToClassList("body-label");
            tutorialManager?.BindTooltip(row, "Оценка ветерана", TutorialContent.TooltipGrade);
            veteranDeckScrollView.Add(row);
        }

        if (veteranDeckScrollView.childCount == 0)
        {
            var empty = new Label("Пока нет завершённых забегов.");
            empty.AddToClassList("body-label");
            veteranDeckScrollView.Add(empty);
        }
        tutorialManager?.QueueOnce(TutorialContent.Veterans);
    }

    public void OpenCharacters()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        charactersScreen.style.display = DisplayStyle.Flex;
        charactersScrollView.Clear();

        foreach (var character in gachaCharacters)
        {
            if (character == null) continue;
            int copies = saveManager.GetCharacterCopies(character.characterId);
            if (copies <= 0) continue; // 7.1: экран содержит полученных в гаче персонажей.

            int runs = saveManager.GetRunCount(character.characterId);
            int relationshipPoints = saveManager.GetRelationshipPoints(character.characterId);
            int relationshipLevel = saveManager.GetRelationshipLevel(character.characterId);
            int nextThreshold = saveManager.GetRelationshipNextThreshold(character.characterId);
            var sceneEntry = saveManager.Data.seenVNScenes.Find(entry => entry != null && entry.characterId == character.characterId);
            int seenScenes = sceneEntry != null && sceneEntry.sceneIds != null ? sceneEntry.sceneIds.Count : 0;
            string relationship = relationshipLevel >= SaveManager.MaxRelationshipLevel
                ? $"отношения: ур. {relationshipLevel}/3 (макс.)"
                : $"отношения: ур. {relationshipLevel}/3, {relationshipPoints}/{nextThreshold}";
            var row = new Label($"{character.characterName} ({character.characterClass}) — копий: {copies}, прохождений: {runs}, {relationship}, открытых сцен: {seenScenes}");
            row.AddToClassList("body-label");
            tutorialManager?.BindTooltip(row, "Отношения", TutorialContent.TooltipRelationships);
            charactersScrollView.Add(row);
        }

        if (charactersScrollView.childCount == 0)
        {
            var empty = new Label("Полученных в гаче персонажей пока нет.");
            empty.AddToClassList("body-label");
            charactersScrollView.Add(empty);
        }
        tutorialManager?.QueueOnce(TutorialContent.Characters);
    }

    void BindTutorialTooltips()
    {
        if (tutorialManager == null) return;
        tutorialManager.BindTooltip(metaCurrencyLabel, "Мета-валюта", TutorialContent.TooltipMetaCurrency);
        tutorialManager.BindTooltip(gachaCurrencyLabel, "Гача-валюта", TutorialContent.TooltipGachaCurrency);
        tutorialManager.BindTooltip(gachaPullButton, "Призыв", "Стоит 50 гача-валюты. Шанс персонажа — 15%; pity-системы в демо нет.");
        tutorialManager.BindTooltip(buildingBonusLabels[0], "Бонус Кузницы", "Усиливает стартовое снаряжение и восстановление физической брони в будущих забегах.");
        tutorialManager.BindTooltip(buildingBonusLabels[1], "Бонус Храма", "Даёт магический щит и общий запас перебросов навыков на весь забег.");
        tutorialManager.BindTooltip(buildingBonusLabels[2], "Бонус Таверны", "Увеличивает запас рационов, урон и эффективность лечения на привале.");
    }

    string CharacterDisplayName(string characterId)
    {
        if (gachaCharacters != null)
        {
            foreach (var character in gachaCharacters)
            {
                if (character != null && System.String.Equals(character.characterId, characterId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return character.characterName;
                }
            }
        }
        return characterId;
    }

    // ==================== Здания (8.1) ====================

    void RefreshBuildingsScreen()
    {
        metaCurrencyLabel.text = $"Мета-валюта: {saveManager.Data.metaCurrency}";

        for (int i = 0; i < BuildingOrder.Length; i++)
        {
            var building = BuildingOrder[i];
            int level = saveManager.GetBuildingLevel(building);

            buildingLevelLabels[i].text = $"{BuildingCatalog.DisplayName(building)} — уровень {level}/{BuildingCatalog.MaxLevel}";
            buildingBonusLabels[i].text = string.Join("\n", BuildingCatalog.GetLevelBonuses(building));

            if (level >= BuildingCatalog.MaxLevel)
            {
                buildingCostLabels[i].text = "Максимальный уровень";
                buildingUpgradeButtons[i].SetEnabled(false);
            }
            else
            {
                int cost = BuildingCatalog.UpgradeCost(level);
                buildingCostLabels[i].text = $"Следующий уровень: {cost} мета-валюты";
                buildingUpgradeButtons[i].SetEnabled(saveManager.Data.metaCurrency >= cost);
            }
        }
    }

    void TryUpgradeBuilding(BuildingType building)
    {
        if (saveManager.TryUpgradeBuilding(building))
        {
            RefreshBuildingsScreen();
        }
    }

    // ==================== Гача (8.5/11.1) ====================

    void RefreshGachaScreen()
    {
        gachaCurrencyLabel.text = $"Гача-валюта: {saveManager.Data.gachaCurrency}";
        gachaPullButton.SetEnabled(!gachaPullInProgress && HasValidGachaCharacterPool() && saveManager.Data.gachaCurrency >= GachaPullCost);
    }

    void TryPullGacha()
    {
        if (gachaPullInProgress || !HasValidGachaCharacterPool()) return;
        if (!GachaPool.RollResult(Random.value, Random.value, out var result)) return;

        CharacterData character = result.IsCharacter ? gachaCharacters[result.CharacterIndex] : null;
        int metaCurrencyAmount = result.IsCharacter ? 0 : result.CurrencyAmount;
        if (!saveManager.TryApplyGachaPull(GachaPullCost, character != null ? character.characterId : null, metaCurrencyAmount, out int copies))
        {
            RefreshGachaScreen();
            return;
        }

        // Результат уже атомарно сохранён вместе со списанием стоимости. Анимация ниже — только
        // презентация и может быть безопасно пропущена/прервана без потери награды.
        gachaPullInProgress = true;
        gachaResultPopup.style.display = DisplayStyle.None;
        RefreshGachaScreen();
        StartCoroutine(GachaPullFlow(result, character, copies));
    }

    IEnumerator GachaPullFlow(GachaPool.Result result, CharacterData character, int copies)
    {
        gachaRevealContainer.style.display = DisplayStyle.Flex;
        gachaChestSpriteImage.image = gachaChestClosedTexture;
        gachaChestSpriteImage.style.translate = new Translate(0, 0, 0);
        gachaReelStrip.Clear();
        gachaBackButton.SetEnabled(false);

        yield return ChestRevealAnimator.ShakeChest(gachaChestSpriteImage);
        gachaChestSpriteImage.image = gachaChestOpenTexture;

        VisualElement winningSlot = null;
        Image winningPortrait = null;
        int winningIndex = ChestRevealAnimator.ReelPadding + ChestRevealAnimator.WinningLogicalIndex;

        void BuildSlot(int index, bool isWinning)
        {
            var slot = new VisualElement();
            slot.AddToClassList("chest-reel-icon");

            if (isWinning && result.IsCharacter)
            {
                if (characterSilhouetteRayOverlay != null)
                {
                    var ray = new Image { sprite = characterSilhouetteRayOverlay, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    ray.style.position = Position.Absolute;
                    ray.style.left = 0;
                    ray.style.right = 0;
                    ray.style.top = 0;
                    ray.style.bottom = 0;
                    slot.Add(ray);
                }

                winningPortrait = new Image { sprite = character.portrait, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                winningPortrait.style.width = Length.Percent(100);
                winningPortrait.style.height = Length.Percent(100);
                winningPortrait.style.unityBackgroundImageTintColor = Color.black;
                slot.Add(winningPortrait);
            }
            else if (isWinning)
            {
                slot.AddToClassList(ReelBackgroundClass(result.CurrencyTier));
                var amount = new Label($"+{result.CurrencyAmount}");
                amount.style.unityTextAlign = TextAnchor.MiddleCenter;
                amount.style.flexGrow = 1;
                slot.Add(amount);
            }
            else if (currencyIconCatalog != null && currencyIconCatalog.items != null && currencyIconCatalog.items.Length > 0)
            {
                var noiseItem = currencyIconCatalog.items[Random.Range(0, currencyIconCatalog.items.Length)];
                if (noiseItem != null)
                {
                    var noise = new Image { sprite = noiseItem.icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    noise.style.width = Length.Percent(100);
                    noise.style.height = Length.Percent(100);
                    slot.Add(noise);
                }
            }

            if (isWinning) winningSlot = slot;
            gachaReelStrip.Add(slot);
        }

        yield return ChestRevealAnimator.PlayReel(gachaReelStrip, gachaReelViewport, BuildSlot, gachaSkipButton, winningIndex);
        if (winningSlot != null) ChestRevealAnimator.SpawnBurst(winningSlot, gachaRevealContainer);

        if (winningPortrait != null)
        {
            Color tint = Color.black;
            bool revealComplete = false;
            DG.Tweening.DOTween.To(() => tint, value =>
            {
                tint = value;
                winningPortrait.style.unityBackgroundImageTintColor = value;
            }, Color.white, 0.18f).OnComplete(() => revealComplete = true);
            while (!revealComplete) yield return null;
        }

        yield return new WaitForSeconds(0.3f);
        gachaRevealContainer.style.display = DisplayStyle.None;

        // Первая встреча Вайолет должна начаться сразу после её первого выпадения, ещё до
        // текстового результата гачи. Это хаб-сцена: сохраняется как просмотренная, но не
        // начисляет отношения (отношения дают только вызовы RunFlowController внутри забега).
        bool firstViolet = result.IsCharacter && character != null &&
            string.Equals(character.characterId, "violet", System.StringComparison.OrdinalIgnoreCase) && copies == 1 &&
            !saveManager.HasSeenVNScene("violet", VioletFirstGachaSceneId);
        if (firstViolet && vnManager != null && vnManager.TryPlayScene(VioletFirstGachaSceneId))
        {
            while (vnManager.IsPlaying) yield return null;
        }

        gachaResultPopup.style.display = DisplayStyle.Flex;

        if (result.IsCharacter)
        {
            gachaResultLabel.text = $"Персонаж: {character.characterName} (копия №{copies})";
        }
        else
        {
            int shownAmount = 0;
            gachaResultLabel.text = $"Мета-валюта: +0 ({RarityLabel(result.CurrencyTier)})";
            bool countComplete = false;
            DG.Tweening.DOTween.To(() => shownAmount, value =>
            {
                shownAmount = value;
                gachaResultLabel.text = $"Мета-валюта: +{shownAmount} ({RarityLabel(result.CurrencyTier)})";
            }, result.CurrencyAmount, 0.5f).OnComplete(() => countComplete = true);
            while (!countComplete) yield return null;
        }

        gachaBackButton.SetEnabled(true);
        gachaPullInProgress = false;
        RefreshGachaScreen();
    }

    bool HasValidGachaCharacterPool()
    {
        if (gachaCharacters == null || gachaCharacters.Length != GachaPool.CharacterCount) return false;
        var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var character in gachaCharacters)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.characterId) || !ids.Add(character.characterId)) return false;
        }
        return true;
    }

    static string RarityLabel(ItemTier tier) => tier switch
    {
        ItemTier.Common => "Обычный",
        ItemTier.Rare => "Редкий",
        _ => "Эпический"
    };

    static string ReelBackgroundClass(ItemTier tier) => tier switch
    {
        ItemTier.Common => "chest-reel-icon-common",
        ItemTier.Rare => "chest-reel-icon-rare",
        _ => "chest-reel-icon-epic"
    };

    // ==================== Сброс прогресса (7.1) ====================

    void ConfirmResetProgress()
    {
        saveManager.ResetProgress();
        resetProgressConfirmPopup.style.display = DisplayStyle.None;
        RefreshBuildingsScreen();
        RefreshGachaScreen();
        StartOpeningSequence();
    }
}
