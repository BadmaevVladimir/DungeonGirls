using System.Collections.Generic;
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

    [Header("Гача-контент (раздел 0: 2 персонажа + 10 предметов-заглушек)")]
    [SerializeField] CharacterData[] gachaCharacters;
    [SerializeField]
    List<string> gachaItemNames = new List<string>
    {
        "Предмет-заглушка 1", "Предмет-заглушка 2", "Предмет-заглушка 3", "Предмет-заглушка 4",
        "Предмет-заглушка 5", "Предмет-заглушка 6", "Предмет-заглушка 7", "Предмет-заглушка 8",
        "Предмет-заглушка 9", "Предмет-заглушка 10"
    };

    const int GachaPullCost = 50; // 8.5

    static readonly BuildingType[] BuildingOrder = { BuildingType.Forge, BuildingType.Temple, BuildingType.Tavern };
    static readonly string[] BuildingIds = { "Forge", "Temple", "Tavern" };

    VisualElement mainMenuScreen;
    VisualElement buildingsScreen;
    VisualElement gachaScreen;

    Button buildingsButton;
    Button gachaButton;
    Button buildingsBackButton;
    Button gachaBackButton;

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

        buildingsButton.clicked += OpenBuildings;
        gachaButton.clicked += OpenGacha;
        buildingsBackButton.clicked += OpenVillage;
        gachaBackButton.clicked += OpenVillage;
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
    }

    void CacheElements(VisualElement root)
    {
        mainMenuScreen = root.Q<VisualElement>("MainMenuScreen");
        buildingsScreen = root.Q<VisualElement>("BuildingsScreen");
        gachaScreen = root.Q<VisualElement>("GachaScreen");

        buildingsButton = root.Q<Button>("BuildingsButton");
        gachaButton = root.Q<Button>("GachaButton");
        buildingsBackButton = root.Q<Button>("BuildingsBackButton");
        gachaBackButton = root.Q<Button>("GachaBackButton");

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
        mainMenuScreen.style.display = DisplayStyle.Flex;
    }

    public void OpenBuildings()
    {
        RefreshBuildingsScreen();
        mainMenuScreen.style.display = DisplayStyle.None;
        buildingsScreen.style.display = DisplayStyle.Flex;
    }

    public void OpenGacha()
    {
        RefreshGachaScreen();
        mainMenuScreen.style.display = DisplayStyle.None;
        gachaScreen.style.display = DisplayStyle.Flex;
    }

    public void OpenVeteranDeck()
    {
        // Экран колоды ветеранов (7.1) — отдельная фаза, вне скоупа Фазы 5.
    }

    public void OpenCharacters()
    {
        // Экран персонажей/био (7.1) — отдельная фаза, вне скоупа Фазы 5.
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

    // ==================== Гача (8.5, раздел 0) ====================

    void RefreshGachaScreen()
    {
        gachaCurrencyLabel.text = $"Гача-валюта: {saveManager.Data.gachaCurrency}";
        gachaPullButton.SetEnabled(saveManager.Data.gachaCurrency >= GachaPullCost);
    }

    void TryPullGacha()
    {
        if (!saveManager.TrySpendGachaCurrency(GachaPullCost))
        {
            return;
        }

        // [DRAFT, 8.5/раздел 0]: равномерное распределение между 2 персонажами и 10 предметами —
        // точные шансы в ГДД не заданы, требует баланс-решения позже.
        int characterCount = gachaCharacters != null ? gachaCharacters.Length : 0;
        int totalOptions = characterCount + gachaItemNames.Count;
        int roll = Random.Range(0, totalOptions);

        string resultText;
        if (roll < characterCount)
        {
            var character = gachaCharacters[roll];
            saveManager.AddCharacterCopy(character.characterName);
            int copies = saveManager.GetCharacterCopies(character.characterName);
            resultText = $"Персонаж: {character.characterName} (копия №{copies})";
        }
        else
        {
            string itemName = gachaItemNames[roll - characterCount];
            saveManager.AddItemCopy(itemName);
            int copies = saveManager.GetItemCount(itemName);
            resultText = $"Предмет: {itemName} (x{copies})";
        }

        gachaResultLabel.text = resultText;
        gachaResultPopup.style.display = DisplayStyle.Flex;

        RefreshGachaScreen();
    }

    // ==================== Сброс прогресса (7.1) ====================

    void ConfirmResetProgress()
    {
        saveManager.ResetProgress();
        resetProgressConfirmPopup.style.display = DisplayStyle.None;
        RefreshBuildingsScreen();
        RefreshGachaScreen();
    }
}
