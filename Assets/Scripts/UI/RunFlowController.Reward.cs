using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Награда / сундук (8.2, только текстовый результат) ====================


    static void SetRarityClass(VisualElement element, ItemTier tier)
    {
        element.RemoveFromClassList("rarity-common");
        element.RemoveFromClassList("rarity-rare");
        element.RemoveFromClassList("rarity-epic");
        element.RemoveFromClassList("rarity-cursed");
        element.AddToClassList(tier switch
        {
            ItemTier.Common => "rarity-common",
            ItemTier.Rare => "rarity-rare",
            ItemTier.Epic => "rarity-epic",
            _ => "rarity-cursed"
        });
    }

    static void SetRarityBorderClass(VisualElement element, ItemTier tier)
    {
        element.RemoveFromClassList("item-card-border-common");
        element.RemoveFromClassList("item-card-border-rare");
        element.RemoveFromClassList("item-card-border-epic");
        element.RemoveFromClassList("item-card-border-cursed");
        element.AddToClassList(tier switch
        {
            ItemTier.Common => "item-card-border-common",
            ItemTier.Rare => "item-card-border-rare",
            ItemTier.Epic => "item-card-border-epic",
            _ => "item-card-border-cursed"
        });
    }

    IEnumerator ShowLootSummaryFlow(RoomRewardResult reward)
    {
        floorManager.SetFloorState(FloorState.LootSummary);
        LootSummaryPresenter.Populate(lootSummaryRows, reward);
        lootSummaryContainer.style.display = DisplayStyle.Flex;
        chestRevealContainer.style.display = DisplayStyle.None;
        rewardText.style.display = DisplayStyle.None;
        rewardContinueButton.style.display = DisplayStyle.None;
        lootSummaryContinueButton.SetEnabled(true);

        yield return ShowRewardOverlay();
        bool confirmed = false;
        void Confirm()
        {
            if (confirmed) return;
            confirmed = true;
            lootSummaryContinueButton.SetEnabled(false);
        }
        lootSummaryConfirmHandler = Confirm;
        lootSummaryContinueButton.clicked += lootSummaryConfirmHandler;
        yield return new WaitUntil(() => confirmed);
        lootSummaryContinueButton.clicked -= lootSummaryConfirmHandler;
        lootSummaryConfirmHandler = null;
        yield return HideRewardOverlay();
        lootSummaryContainer.style.display = DisplayStyle.None;
    }

    IEnumerator ShowResolvedRewardChestFlow(ChestReward reward)
    {
        floorManager.SetFloorState(FloorState.RewardChest);
        lootSummaryContainer.style.display = DisplayStyle.None;
        rewardText.style.display = DisplayStyle.Flex;
        rewardContinueButton.style.display = DisplayStyle.Flex;
        yield return ShowRewardOverlay();
        tutorialManager?.QueueOnce(TutorialContent.Reward);
        rewardText.text = string.Empty;
        yield return ChestRevealFlow(reward);
        rewardText.text = $"Получено: {DisplayFormat.RarityLabel(reward.ItemRarity)} предмет" +
            (reward.BonusReward ? "\n+ дополнительная награда (Удача)" : string.Empty);
        SetRarityClass(rewardText, reward.ItemRarity);
        yield return WaitForClick(rewardContinueButton);
        yield return HideRewardOverlay();
        if (reward.Item != null) yield return ItemCompareFlow(reward.Item);
    }

    IEnumerator ShowRewardChestFlow(int floorNumber, bool isBoss)
    {
        floorManager.SetFloorState(FloorState.RewardChest);

        int luckLevel = characterManager.Progress.GetSkillLevel(SkillId.Luck);
        int currencyBonus = characterManager.Modifiers.ConsumeChestCurrencyBonus();
        bool noCurrency = characterManager.Modifiers.ConsumeChestNoCurrency();

        int goldenTouchLevel = characterManager.Combatant.ItemGoldenTouchLevel;
        var reward = rewardManager.CalculateRewards(floorNumber, isBoss, characterManager.Level, luckLevel, currencyBonus, noCurrency, goldenTouchLevel, characterManager.Character.characterClass);

        lootSummaryContainer.style.display = DisplayStyle.None;
        rewardText.style.display = DisplayStyle.Flex;
        rewardContinueButton.style.display = DisplayStyle.Flex;

        // 7.2/8.2 (НОВОЕ): модальное окно поверх текущей сцены — не ShowOnly, сцена позади (обычно
        // бой) остаётся видна затемнённой, а не скрывается целиком.
        yield return ShowRewardOverlay();
        tutorialManager?.QueueOnce(TutorialContent.Reward);
        // Баг (2026-08-26): описание награды из прошлой комнаты оставалось видимым поверх новой
        // анимации сундука (текст очищался только после ChestRevealFlow) — очищаем сразу, до тряски.
        rewardText.text = string.Empty;
        yield return ChestRevealFlow(reward);

        characterManager.AddCurrency(reward.Currency); // счётчик валюты — начисление происходит здесь,
            // ПОСЛЕ ленты (не до), чтобы RunCurrency в rewardText ниже уже отражал начисленную сумму —
            // порядок сознательно переставлен относительно исходного кода (было до ShowOnly).
        rewardText.text = $"Получено: {reward.Currency} монет забега, {DisplayFormat.RarityLabel(reward.ItemRarity)} предмет" +
            (reward.BonusReward ? "\n+ дополнительная награда (Удача)" : string.Empty) +
            $"\nВсего валюты забега: {characterManager.RunCurrency}";
        SetRarityClass(rewardText, reward.ItemRarity);
        LogEvent($"[Награда] +{reward.Currency} валюты забега, {DisplayFormat.RarityLabel(reward.ItemRarity)} предмет{(reward.Item != null ? $" ({reward.Item.itemName})" : string.Empty)}{(reward.BonusReward ? ", + доп. награда (Удача)" : string.Empty)}.");

        yield return WaitForClick(rewardContinueButton);
        yield return HideRewardOverlay();

        if (reward.Item != null)
        {
            yield return ItemCompareFlow(reward.Item);
        }
    }

    // 7.2/8.2 (НОВОЕ): скрим темнеет + модальная карточка появляется scale(0.9→1)+fade за ~0.3с —
    // мягкое появление вместо резкого хлопка. RewardPanel сознательно не участвует в ShowOnly() —
    // сцена позади (обычно бой) должна остаться видна затемнённой, а не исчезнуть.
    IEnumerator ShowRewardOverlay()
    {
        const float duration = 0.3f;

        rewardPanel.RemoveFromClassList("hidden");
        rewardScrim.style.opacity = 0f;
        rewardModalCard.style.opacity = 0f;
        rewardModalCard.style.scale = new Scale(new Vector3(0.9f, 0.9f, 1f));

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            rewardScrim.style.opacity = progress;
            rewardModalCard.style.opacity = progress;
            float scale = Mathf.Lerp(0.9f, 1f, progress);
            rewardModalCard.style.scale = new Scale(new Vector3(scale, scale, 1f));
            yield return null;
        }

        rewardScrim.style.opacity = 1f;
        rewardModalCard.style.opacity = 1f;
        rewardModalCard.style.scale = new Scale(Vector3.one);
    }

    IEnumerator HideRewardOverlay()
    {
        const float duration = 0.25f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = 1f - Mathf.Clamp01(elapsed / duration);
            rewardScrim.style.opacity = progress;
            rewardModalCard.style.opacity = progress;
            yield return null;
        }

        rewardScrim.style.opacity = 0f;
        rewardModalCard.style.opacity = 0f;
        rewardPanel.AddToClassList("hidden");
    }

    // 8.2/10.6: тряска закрытого сундука, открытие, рулетка иконок предметов, скип, вспышка на приземлении.
    IEnumerator ChestRevealFlow(ChestReward reward)
    {
        chestRevealContainer.style.display = DisplayStyle.Flex;
        chestSpriteImage.image = chestClosedTexture;
        chestReelStrip.Clear();
        chestSpriteImage.style.translate = new Translate(0, 0, 0);

        // 8.2 (уточнено): сундук трясётся закрытым ~1с, затем переключается на открытый — и только
        // ПОСЛЕ этого начинается формирование ленты (не одновременно с открытием, как раньше).
        yield return ChestRevealAnimator.ShakeChest(chestSpriteImage);
        chestSpriteImage.image = chestOpenTexture;

        // 8.2: лента из ~20 иконок предметов, взятых из пула каталога (те же иконки, что уже
        // назначены в Task 2) — случайный подбор с повторами, если в каталоге меньше 20 предметов.
        var pool = rewardManager.GetCompatibleLootItems(characterManager.Character.characterClass);
        if (pool == null || pool.Count == 0)
        {
            // Пустой каталог — деградируем на мгновенный переход к итогу без ленты, не зависаем.
            chestRevealContainer.style.display = DisplayStyle.None;
            yield break;
        }

        // 8.2 (уточнено): паддинг-иконки с обеих сторон — та же "шумовая" логика, что и остальные
        // ~19 слотов (случайный предмет + случайная фальшивая редкость), просто вне видимого при
        // покое диапазона. Итоговый индекс победного слота в массиве смещён на chestReelPadding.
        int winningIndex = ChestRevealAnimator.ReelPadding + ChestRevealAnimator.WinningLogicalIndex;
        Sprite winningIcon = reward.Item != null ? reward.Item.icon : pool[0].icon;

        // Джингл длиной ровно под PlayReel (4с) — стартует одновременно с началом прокрутки, не с
        // тряской: тайминг рассчитан на сам спин, финал совпадает с остановкой на выигрышном слоте.
        AudioClip openClip = ChestOpenClipFor(reward.ItemRarity);
        TaggedAudio.Play(chestOpenAudioSource, openClip, AudioCategory.SFX);

        void BuildSlot(int index, bool isWinning)
        {
            Sprite iconSprite = isWinning ? winningIcon : pool[Random.Range(0, pool.Count)].icon;
            var icon = new Image { sprite = iconSprite };
            icon.AddToClassList("chest-reel-icon");
            icon.AddToClassList(isWinning ? ChestReelBgClassFor(reward.ItemRarity) : ChestReelBgClassFor(rewardManager.RollItemRarity(false)));
            chestReelStrip.Add(icon);
        }

        yield return ChestRevealAnimator.PlayReel(chestReelStrip, chestReelViewport, BuildSlot, chestSkipButton, winningIndex, JumpChestAudioToEnding);

        // Вспышка/burst на приземлении (финальный ревью, замена world-space ParticleSystem — см.
        // SpawnChestBurst): UI Toolkit-нативные "искры" внутри chestRevealContainer.
        ChestRevealAnimator.SpawnBurst(chestSpriteImage, chestRevealContainer);

        yield return new WaitForSeconds(0.3f); // короткая пауза на "приземление" перед итоговым текстом

        chestRevealContainer.style.display = DisplayStyle.None;
    }

    // 8.2 (уточнено): фон слота ленты по редкости — переиспользует ту же палитру серый/синий/
    // фиолетовый, что и .rarity-common/.rarity-rare/.rarity-epic (там — цвет текста, здесь —
    // фон, поэтому отдельные CSS-классы, а не переиспользование тех же имён).
    static string ChestReelBgClassFor(ItemTier tier) => tier switch
    {
        ItemTier.Common => "chest-reel-icon-common",
        ItemTier.Rare => "chest-reel-icon-rare",
        ItemTier.Epic => "chest-reel-icon-epic",
        _ => "chest-reel-icon-cursed"
    };

    AudioClip ChestOpenClipFor(ItemTier tier) => tier switch
    {
        ItemTier.Common => chestOpenCommonClip,
        ItemTier.Rare => chestOpenRareClip,
        _ => chestOpenEpicClip
    };

    // При пропуске рулетки (Skip) джингл доматывается сразу на финальный аккорд, а не обрывается —
    // игрок должен успеть услышать акцент, идентифицирующий редкость награды.
    void JumpChestAudioToEnding()
    {
        if (chestOpenAudioSource == null) return;
        if (ChestRevealAnimator.ShouldJumpToEnding(chestOpenAudioSource.isPlaying, chestOpenAudioSource.time))
        {
            chestOpenAudioSource.time = ChestRevealAnimator.JingleBuildupDuration;
        }
    }

    // ==================== Сравнение предмета (3.4, "Без инвентаря") ====================


    // Карточка выбора должна оставаться короткой: иначе описание пассивки эпического предмета
    // вытесняет второй физический слот оружия/кольца за нижнюю границу экрана. Полный текст
    // по-прежнему доступен по стандартной подсказке элемента.
    static string ItemComparisonSummary(ItemData item)
    {
        if (item == null)
        {
            return "Свободный слот";
        }

        var lines = new List<string> { $"{DisplayFormat.SlotLabel(item)}, {DisplayFormat.RarityLabel(item.tier)}, ур. {item.itemLevel}" };
        if (item.tier == ItemTier.Cursed) lines.Add($"ранг эффекта {DisplayFormat.RankLabel(item.EffectRank)} из V");

        var mainStats = new List<string>();
        if (item.slot == EquipmentSlot.Weapon && item.weaponSubtype != WeaponSubtype.None && item.weaponSubtype != WeaponSubtype.Shield)
        {
            DamageCalculator.ComputeDamageRange(item.EffectiveDamage, out float dmgMin, out float dmgMax);
            mainStats.Add($"урон {dmgMin:F0}–{dmgMax:F0}");
            // Базовая скорость — характеристика самого оружия, а не его дополнительный процентный
            // бонус. Показываем обе строки независимо: иначе «+10% скорости» создаёт впечатление,
            // что предмет быстрее, но игрок не может сравнить его реальную частоту ударов.
            mainStats.Add($"скорость атаки {item.attackSpeed:F2}/с");
            if (item.isTwoHanded)
            {
                if (item.tier != ItemTier.Cursed) mainStats.Add("двуручное: урон +30%");
            }
        }

        if (item.physicalDefense > 0f)
        {
            mainStats.Add($"физ. защита {item.EffectiveDefense:F0}");
        }
        if (item.maxPhysicalDefenseBonus > 0f)
        {
            mainStats.Add($"макс. физ. защита +{item.EffectiveMaxDefenseBonus:F0}");
        }
        if (item.MagicShieldEffective > 0f)
        {
            mainStats.Add($"маг. щит +{item.MagicShieldEffective:F0}");
        }
        if (item.HpBonusEffective > 0f)
        {
            mainStats.Add($"Здоровье +{item.HpBonusEffective:F0}");
        }
        if (item.rageBonusFlatPercent > 0f)
        {
            mainStats.Add($"Ярость +{StatScaling.ScaleItemEffect(item.rageBonusFlatPercent, item.itemLevel):F1}%");
        }
        if (mainStats.Count > 0)
        {
            lines.Add(string.Join(" · ", mainStats));
        }

        string bonusText = DisplayFormat.BonusStatText(item);
        if (!string.IsNullOrWhiteSpace(bonusText))
        {
            lines.Add(bonusText);
        }

        if (item.passiveSkill != null)
        {
            lines.Add($"Пассивный навык: {item.passiveSkill.skillName}");
        }

        if (!string.IsNullOrWhiteSpace(item.handUsageDescription)) lines.Add(item.handUsageDescription);
        if (!string.IsNullOrWhiteSpace(item.positiveEffectDescription)) lines.Add($"Эффект: {item.positiveEffectDescription}");
        if (!string.IsNullOrWhiteSpace(item.curseDescription)) lines.Add($"Проклятие: {item.curseDescription}");

        return string.Join("\n", lines);
    }

    // Комплексная переработка выбора слота (вариант A): карточка = шапка (иконка + "Заменить: X")
    // и ПОЛНАЯ таблица статов предмета — у каждой строки своё "было → стало", а не единственная
    // выбранная "главная" дельта. Иначе для колец/амулетов с непересекающимися бонусами часть
    // информации (например старое значение стата, которого нет у нового предмета) терялась.
    static VisualElement BuildItemCompareCard(ItemData candidate, ItemData replacement)
    {
        var card = new VisualElement();

        var header = new VisualElement();
        header.AddToClassList("item-compare-card-header");
        if (candidate != null && candidate.icon != null)
        {
            var icon = new Image { sprite = candidate.icon, scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("item-compare-card-icon");
            header.Add(icon);
        }
        var title = new Label(candidate != null ? $"Заменить: {candidate.itemName}" : "Занять свободный слот");
        title.AddToClassList("choice-card-title");
        title.AddToClassList("item-compare-card-title");
        header.Add(title);
        if (candidate != null)
        {
            var levelBadge = new Label($"ур. {candidate.itemLevel}");
            levelBadge.AddToClassList("choice-card-level-badge");
            header.Add(levelBadge);
        }
        card.Add(header);

        foreach (var row in GetComparableStatRows(candidate, replacement))
        {
            card.Add(BuildStatRow(row.Label, row.Old, row.New));
        }

        return card;
    }

    static VisualElement BuildStatRow(string label, float? oldValue, float? newValue)
    {
        var row = new VisualElement();
        row.AddToClassList("item-compare-stat-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("item-compare-stat-label");
        row.Add(labelElement);

        var valueRow = new VisualElement();
        valueRow.AddToClassList("item-compare-stat-value-row");

        // Всегда показываем обе стороны "было → стало" (с "—"-заглушкой, если стата не было),
        // иначе форма строк "прыгает" — где-то есть стрелка, где-то голое число.
        string direction;
        if (oldValue.HasValue && newValue.HasValue)
        {
            float delta = newValue.Value - oldValue.Value;
            direction = delta > 0.05f ? "item-compare-stat-up" : delta < -0.05f ? "item-compare-stat-down" : "item-compare-stat-neutral";
        }
        else if (newValue.HasValue)
        {
            direction = "item-compare-stat-up"; // приобретаем стат, которого не было
        }
        else
        {
            direction = "item-compare-stat-down"; // теряем стат, которого нет у нового предмета
        }

        var oldLabel = new Label(oldValue.HasValue ? FormatStatValue(oldValue.Value) : "—");
        oldLabel.AddToClassList("item-compare-stat-value");
        oldLabel.AddToClassList("item-compare-stat-value-old");
        valueRow.Add(oldLabel);

        var arrow = new Label(" → ");
        arrow.AddToClassList("item-compare-stat-arrow");
        valueRow.Add(arrow);

        var valueLabel = new Label(newValue.HasValue ? FormatStatValue(newValue.Value) : "—");
        valueLabel.AddToClassList("item-compare-stat-value");
        valueLabel.AddToClassList(direction);
        valueRow.Add(valueLabel);

        row.Add(valueRow);
        return row;
    }

    static string FormatStatValue(float value) =>
        Mathf.Abs(value - Mathf.Round(value)) < 0.05f ? value.ToString("F0") : value.ToString("F2");

    static List<(string Label, float? Old, float? New)> GetComparableStatRows(ItemData current, ItemData replacement)
    {
        var rows = new List<(string, float?, float?)>();
        void AddRow(string label, float? oldVal, float? newVal)
        {
            if (oldVal.HasValue || newVal.HasValue) rows.Add((label, oldVal, newVal));
        }

        AddRow("Урон", WeaponDamageMid(current), WeaponDamageMid(replacement));
        AddRow("Скорость атаки", WeaponSpeedValue(current), WeaponSpeedValue(replacement));
        AddRow("Физ. защита", PhysicalDefenseValue(current), PhysicalDefenseValue(replacement));
        AddRow("Макс. физ. защита", MaxDefenseBonusValue(current), MaxDefenseBonusValue(replacement));
        AddRow("Маг. щит", MagicShieldValue(current), MagicShieldValue(replacement));
        AddRow("Здоровье", HpBonusValue(current), HpBonusValue(replacement));
        AddRow("Ярость", RageBonusValue(current), RageBonusValue(replacement));

        var currentBonus = BonusStatValue(current);
        var replacementBonus = BonusStatValue(replacement);
        if (currentBonus.Label != null && currentBonus.Label == replacementBonus.Label)
        {
            AddRow(currentBonus.Label, currentBonus.Value, replacementBonus.Value);
        }
        else
        {
            if (currentBonus.Label != null) AddRow(currentBonus.Label, currentBonus.Value, null);
            if (replacementBonus.Label != null) AddRow(replacementBonus.Label, null, replacementBonus.Value);
        }

        return rows;
    }

    static float? WeaponDamageMid(ItemData item)
    {
        if (item == null || item.slot != EquipmentSlot.Weapon || item.weaponSubtype == WeaponSubtype.None || item.weaponSubtype == WeaponSubtype.Shield) return null;
        DamageCalculator.ComputeDamageRange(item.EffectiveDamage, out float min, out float max);
        return (min + max) / 2f;
    }

    static float? WeaponSpeedValue(ItemData item) =>
        item != null && item.slot == EquipmentSlot.Weapon && item.weaponSubtype != WeaponSubtype.None && item.weaponSubtype != WeaponSubtype.Shield
            ? item.attackSpeed
            : (float?)null;

    static float? PhysicalDefenseValue(ItemData item) => item != null && item.physicalDefense > 0f ? item.EffectiveDefense : (float?)null;

    static float? MaxDefenseBonusValue(ItemData item) => item != null && item.maxPhysicalDefenseBonus > 0f ? item.EffectiveMaxDefenseBonus : (float?)null;

    static float? MagicShieldValue(ItemData item) => item != null && item.MagicShieldEffective > 0f ? item.MagicShieldEffective : (float?)null;

    static float? HpBonusValue(ItemData item) => item != null && item.HpBonusEffective > 0f ? item.HpBonusEffective : (float?)null;

    static float? RageBonusValue(ItemData item) =>
        item != null && item.rageBonusFlatPercent > 0f ? StatScaling.ScaleItemEffect(item.rageBonusFlatPercent, item.itemLevel) : (float?)null;

    static (string Label, float? Value) BonusStatValue(ItemData item)
    {
        if (item?.bonusStat == null || item.bonusStat.type == BonusStatType.None || Mathf.Approximately(item.bonusStat.baseValue, 0f))
            return (null, null);
        float value = item.bonusStat.type == BonusStatType.MaxPhysicalDefenseFlat
            ? ItemEffectBalance.ArmorAccessoryMaxDefense(item.bonusStat.baseValue, item.itemLevel)
            : StatScaling.ScaleItemEffect(item.bonusStat.baseValue, item.itemLevel);
        string label = BonusStatLabel(item.bonusStat.type);
        return string.IsNullOrEmpty(label) ? (null, null) : (label, value);
    }

    static string BonusStatLabel(BonusStatType type) => type switch
    {
        BonusStatType.CritChancePercent => "Шанс критического удара",
        BonusStatType.ArmorPenetrationFlat => "Пробивание брони",
        BonusStatType.AttackSpeedPercent => "Скорость атаки",
        BonusStatType.DamagePercent => "Урон",
        BonusStatType.FlatHP => "Здоровье",
        BonusStatType.MaxPhysicalDefenseFlat => "Макс. физ. защита",
        BonusStatType.MagicShieldFlat => "Маг. щит",
        BonusStatType.WeaponDamageFlat => "Урон оружия",
        BonusStatType.EvasionPercent => "Уклонение",
        BonusStatType.ArmorIgnorePercent => "Игнорирование брони",
        _ => string.Empty
    };

    // 3.4: если новый предмет подходит сразу в несколько слотов (2 слота оружия/рук, 2 слота
    // колец) — показываем ВСЕ подходящие слоты с их текущим содержимым и даём игроку самому
    // выбрать, какой занять (или отказаться от нового предмета вовсе). Никакого автовыбора слота.
    IEnumerator ItemCompareFlow(ItemData newItem)
    {
        var candidates = characterManager.GetComparisonCandidates(newItem); // null-элемент = свободный слот

        ShowOnly(itemComparePanel);
        newItemIcon.sprite = newItem.icon;
        newItemRarityLabel.text = DisplayFormat.RarityLabel(newItem.tier).ToUpperInvariant();
        SetRarityClass(newItemRarityLabel, newItem.tier);
        SetRarityBorderClass(newItemCard, newItem.tier);
        newItemName.text = newItem.itemName;
        newItemStats.text = ItemComparisonSummary(newItem);
        newItemStats.tooltip = SkillDescriptionFormatter.Plain(DisplayFormat.ItemStatsText(newItem));
        tutorialManager?.QueueOnce(TutorialContent.Equipment);

        slotChoicesContainer.Clear();
        var buttons = new List<Button>();
        foreach (var candidate in candidates)
        {
            var btn = new Button { tooltip = candidate != null ? SkillDescriptionFormatter.Plain(DisplayFormat.ItemStatsText(candidate)) : "Новый предмет займёт свободный слот." };
            btn.AddToClassList("choice-card");
            btn.AddToClassList("item-slot-choice");
            btn.Add(BuildItemCompareCard(candidate, newItem));
            slotChoicesContainer.Add(btn);
            buttons.Add(btn);
        }
        buttons.Add(itemDiscardButton);

        yield return WaitForAnyClick(buttons.ToArray());

        if (clickedIndex < candidates.Count)
        {
            var replacing = candidates[clickedIndex];
            characterManager.EquipItem(newItem, replacing);
            LogEvent($"[Снаряжение] Надето: {newItem.itemName}{(replacing != null ? $" (заменён {replacing.itemName})" : string.Empty)}.");
        }
        else
        {
            LogEvent($"[Снаряжение] Выброшено: {newItem.itemName}.");
        }
    }
}
