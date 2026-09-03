using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Главное меню = карта деревни (утверждённый макет main_menu_village_v5.html).
// Клик по домику открывает экран зданий, клик по спуску — StartRunButton (его вешает
// RunFlowController, здесь мы его не трогаем).
//
// Всё «оживление» карты сделано корутинами: в USS нет @keyframes, radial-gradient и aspect-ratio,
// поэтому пульсация/дрейф/дымка анимируются кодом — тем же приёмом, что PulseBerserkAura
// в RunFlowController.Combat.cs.
public partial class HubManager
{
    // ==================== Карта деревни (главное меню) ====================

    const float VillageMapAspect = 632f / 400f;

    VisualElement villageStage;
    VisualElement villageMapRoot;
    Image villageMapImage;
    Image villageWaterShimmer;

    readonly VisualElement[] villageClouds = new VisualElement[3];
    readonly VisualElement[] villageMistPuffs = new VisualElement[3];
    VisualElement villageDungeonPlate;

    Button forgeSpotButton;
    Button templeSpotButton;
    Button tavernSpotButton;

    readonly Label[] villagePlateLevelLabels = new Label[3];

    // Свечения: горн, окна таверны, фонтан, вход в подземелье — период пульса у каждого свой,
    // чтобы карта не «дышала» синхронно.
    readonly VisualElement[] villageGlows = new VisualElement[4];
    static readonly float[] VillageGlowPeriods = { 1.7f, 3.6f, 2.9f, 2.6f };
    static readonly float[] VillageGlowPhaseOffsets = { 0f, 1.1f, 0.6f, 0f };

    // Ширина в % карты и длительность полного прохода — из макета (.cs-1/.cs-2/.cs-3).
    static readonly float[] VillageCloudWidthsPercent = { 38f, 26f, 32f };
    static readonly float[] VillageCloudCycles = { 46f, 63f, 54f };
    static readonly float[] VillageCloudPhaseOffsets = { 0f, 22f, 38f };

    const float VillageMistCycle = 5f;
    static readonly float[] VillageMistDelays = { 0f, 1.7f, 3.4f };

    Sprite[] waterShimmerFrames;
    bool villageFxRunning;
    readonly List<Coroutine> villageFxCoroutines = new List<Coroutine>();
    Coroutine villageVisibilityWatcher;

    void CacheVillageElements(VisualElement root)
    {
        villageStage = root.Q<VisualElement>("VillageStage");
        villageMapRoot = root.Q<VisualElement>("VillageMapRoot");
        villageMapImage = root.Q<Image>("VillageMapImage");
        villageWaterShimmer = root.Q<Image>("VillageWaterShimmer");

        for (int i = 0; i < villageClouds.Length; i++)
        {
            villageClouds[i] = root.Q<VisualElement>("VillageCloud" + i);
        }

        villageGlows[0] = root.Q<VisualElement>("ForgeFireGlow");
        villageGlows[1] = root.Q<VisualElement>("TavernWindowsGlow");
        villageGlows[2] = root.Q<VisualElement>("FountainGlow");
        villageGlows[3] = root.Q<VisualElement>("DungeonGlow");

        for (int i = 0; i < villageMistPuffs.Length; i++)
        {
            villageMistPuffs[i] = root.Q<VisualElement>("MistPuff" + i);
        }

        villageDungeonPlate = root.Q<VisualElement>("DungeonPlate");

        forgeSpotButton = root.Q<Button>("ForgeSpotButton");
        templeSpotButton = root.Q<Button>("TempleSpotButton");
        tavernSpotButton = root.Q<Button>("TavernSpotButton");

        villagePlateLevelLabels[0] = root.Q<Label>("ForgePlateLevel");
        villagePlateLevelLabels[1] = root.Q<Label>("TemplePlateLevel");
        villagePlateLevelLabels[2] = root.Q<Label>("TavernPlateLevel");
    }

    void SetUpVillageMap()
    {
        LoadVillageSprites();

        // Кнопка "Здания" убрана из меню: три домика на карте открывают каждый свой экран.
        // Кузница/Таверна теперь ведут на новые функциональные экраны (крафт/готовка) — уровень
        // и апгрейд здания встроены в них же (см. HubManager.Forge.cs/Tavern.cs), а не только
        // в общий BuildingsScreen. Храм своего экрана пока не получил — ведёт на BuildingsScreen.
        if (forgeSpotButton != null) forgeSpotButton.clicked += OpenForge;
        if (templeSpotButton != null) templeSpotButton.clicked += OpenBuildings;
        if (tavernSpotButton != null) tavernSpotButton.clicked += OpenTavern;

        // В UI Toolkit нет aspect-ratio — пропорции 632:400 держим вручную по размеру сцены.
        if (villageStage != null)
        {
            villageStage.RegisterCallback<GeometryChangedEvent>(_ => FitVillageMapToStage());
            FitVillageMapToStage();
        }

        RefreshVillagePlates();

        if (villageVisibilityWatcher == null)
        {
            villageVisibilityWatcher = StartCoroutine(WatchVillageVisibility());
        }
    }

    void LoadVillageSprites()
    {
        var map = Resources.Load<Sprite>("UI/VillageMap");
        if (villageMapImage != null)
        {
            villageMapImage.sprite = map;
            villageMapImage.scaleMode = ScaleMode.StretchToFill;
        }

        waterShimmerFrames = new Sprite[8];
        for (int i = 0; i < waterShimmerFrames.Length; i++)
        {
            waterShimmerFrames[i] = Resources.Load<Sprite>($"UI/VillageFX/WaterShimmer_{i}");
        }

        if (villageWaterShimmer != null)
        {
            villageWaterShimmer.scaleMode = ScaleMode.StretchToFill;
            if (waterShimmerFrames.Length > 0) villageWaterShimmer.sprite = waterShimmerFrames[0];
        }

        var cloudSprite = Resources.Load<Sprite>("UI/VillageFX/CloudShadow");
        foreach (var cloud in villageClouds)
        {
            if (cloud != null && cloudSprite != null) cloud.style.backgroundImage = new StyleBackground(cloudSprite);
        }

        // Одна и та же мягкая клякса используется и для свечений, и для дымки, и для подсветки
        // при наведении — цвет задаётся через -unity-background-image-tint-color в USS.
        var glowSprite = Resources.Load<Sprite>("UI/VillageFX/SoftGlow");
        if (glowSprite != null)
        {
            foreach (var glow in villageGlows)
            {
                if (glow != null) glow.style.backgroundImage = new StyleBackground(glowSprite);
            }

            foreach (var puff in villageMistPuffs)
            {
                if (puff != null) puff.style.backgroundImage = new StyleBackground(glowSprite);
            }

            foreach (var spotGlow in new[] { "ForgeSpotGlow", "TempleSpotGlow", "TavernSpotGlow", "DungeonSpotGlow" })
            {
                var element = uiDocument.rootVisualElement.Q<VisualElement>(spotGlow);
                if (element != null) element.style.backgroundImage = new StyleBackground(glowSprite);
            }
        }
    }

    void FitVillageMapToStage()
    {
        if (villageStage == null || villageMapRoot == null) return;

        float availableWidth = villageStage.contentRect.width;
        float availableHeight = villageStage.contentRect.height;
        if (float.IsNaN(availableWidth) || availableWidth <= 1f || float.IsNaN(availableHeight) || availableHeight <= 1f) return;

        // Карта занимает всё доступное место сцены, упираясь либо в ширину, либо в высоту (contain-фит).
        // Потолка по ширине намеренно нет: карта — пиксель-арт с Point-фильтрацией, её штатно тянет
        // на любой масштаб, а фиксированный максимум оставлял на широком окне маленькую картинку
        // посреди большой тёмной панели.
        float width = availableWidth;
        float height = width / VillageMapAspect;
        if (height > availableHeight)
        {
            height = availableHeight;
            width = height * VillageMapAspect;
        }

        villageMapRoot.style.width = width;
        villageMapRoot.style.height = height;
    }

    public void RefreshVillagePlates()
    {
        if (saveManager == null) return;

        for (int i = 0; i < BuildingOrder.Length && i < villagePlateLevelLabels.Length; i++)
        {
            var label = villagePlateLevelLabels[i];
            if (label == null) continue;
            int level = saveManager.GetBuildingLevel(BuildingOrder[i]);
            label.text = $"Ур. {level} / {BuildingCatalog.MaxLevel}";
        }
    }

    // ---------- жизненный цикл анимаций ----------

    // Экран меню прячут сразу несколько мест (HubManager.Navigation, RunFlowController,
    // RunFlowController.CharacterSelect), а события «элемент скрыли» в UI Toolkit нет — поэтому
    // просто следим за display и включаем/выключаем анимации. Во время забега они не крутятся.
    IEnumerator WatchVillageVisibility()
    {
        while (true)
        {
            bool visible = mainMenuScreen != null && mainMenuScreen.resolvedStyle.display == DisplayStyle.Flex;
            if (visible && !villageFxRunning) StartVillageFx();
            else if (!visible && villageFxRunning) StopVillageFx();
            yield return null;
        }
    }

    void StartVillageFx()
    {
        if (villageFxRunning) return;
        villageFxRunning = true;

        if (villageWaterShimmer != null && waterShimmerFrames != null)
        {
            villageFxCoroutines.Add(StartCoroutine(SpriteFlipbook.Play(villageWaterShimmer, waterShimmerFrames, 5.5f, loop: true)));
        }

        for (int i = 0; i < villageClouds.Length; i++)
        {
            if (villageClouds[i] == null) continue;
            villageFxCoroutines.Add(StartCoroutine(DriftCloud(villageClouds[i], VillageCloudWidthsPercent[i], VillageCloudCycles[i], VillageCloudPhaseOffsets[i])));
        }

        for (int i = 0; i < villageGlows.Length; i++)
        {
            if (villageGlows[i] == null) continue;
            villageFxCoroutines.Add(StartCoroutine(PulseVillageGlow(villageGlows[i], VillageGlowPeriods[i], VillageGlowPhaseOffsets[i])));
        }

        for (int i = 0; i < villageMistPuffs.Length; i++)
        {
            if (villageMistPuffs[i] == null) continue;
            villageFxCoroutines.Add(StartCoroutine(RiseMistPuff(villageMistPuffs[i], VillageMistDelays[i])));
        }

        if (villageDungeonPlate != null)
        {
            villageFxCoroutines.Add(StartCoroutine(PulseDungeonPlate(villageDungeonPlate)));
        }
    }

    void StopVillageFx()
    {
        villageFxRunning = false;
        foreach (var routine in villageFxCoroutines)
        {
            if (routine != null) StopCoroutine(routine);
        }
        villageFxCoroutines.Clear();

        foreach (var puff in villageMistPuffs)
        {
            if (puff != null) puff.style.opacity = 0f;
        }

        if (villageDungeonPlate != null) villageDungeonPlate.style.scale = new Scale(Vector3.one);
    }

    // ---------- сами анимации ----------

    // Тени облаков ползут слева направо и заворачиваются; неба в кадре нет, читаются именно тени.
    IEnumerator DriftCloud(VisualElement cloud, float widthPercent, float cycleSeconds, float phaseOffsetSeconds)
    {
        float travel = 100f + widthPercent;
        while (true)
        {
            float t = ((Time.unscaledTime + phaseOffsetSeconds) % cycleSeconds) / cycleSeconds;
            cloud.style.left = Length.Percent(-widthPercent + travel * t);
            yield return null;
        }
    }

    IEnumerator PulseVillageGlow(VisualElement glow, float period, float phaseOffsetSeconds)
    {
        while (true)
        {
            float phase = ((Time.unscaledTime + phaseOffsetSeconds) % period) / period;
            // 0 → 1 → 0 за период (как emberpulse в макете).
            float wave = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
            glow.style.opacity = Mathf.Lerp(0.62f, 1f, wave);
            float scale = Mathf.Lerp(1f, 1.14f, wave);
            glow.style.scale = new Scale(new Vector3(scale, scale, 1f));
            yield return null;
        }
    }

    // Дымка из прохода: клякса поднимается на 150% своей высоты, разгораясь и затухая.
    IEnumerator RiseMistPuff(VisualElement puff, float delaySeconds)
    {
        while (true)
        {
            float phase = ((Time.unscaledTime + delaySeconds) % VillageMistCycle) / VillageMistCycle;
            float opacity = phase < 0.18f
                ? Mathf.Lerp(0f, 0.85f, phase / 0.18f)
                : Mathf.Lerp(0.85f, 0f, (phase - 0.18f) / 0.82f);
            puff.style.opacity = opacity;
            puff.style.translate = new Translate(Length.Percent(0f), Length.Percent(-150f * phase), 0);
            float scale = Mathf.Lerp(0.55f, 1.2f, phase);
            puff.style.scale = new Scale(new Vector3(scale, scale, 1f));
            yield return null;
        }
    }

    // Плашка подземелья — главное действие сцены, поэтому мягко «дышит».
    // box-shadow в USS нет, поэтому вместо свечения из макета — едва заметный масштаб.
    IEnumerator PulseDungeonPlate(VisualElement plate)
    {
        const float period = 2.8f;
        while (true)
        {
            float phase = (Time.unscaledTime % period) / period;
            float wave = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
            float scale = Mathf.Lerp(1f, 1.035f, wave);
            plate.style.scale = new Scale(new Vector3(scale, scale, 1f));
            yield return null;
        }
    }
}
