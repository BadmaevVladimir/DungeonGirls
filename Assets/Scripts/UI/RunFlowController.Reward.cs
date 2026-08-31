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
        element.AddToClassList(tier switch
        {
            ItemTier.Common => "rarity-common",
            ItemTier.Rare => "rarity-rare",
            _ => "rarity-epic"
        });
    }

    IEnumerator ShowRewardChestFlow(int floorNumber, bool isBoss)
    {
        floorManager.SetFloorState(FloorState.RewardChest);

        int luckLevel = characterManager.Progress.GetSkillLevel(SkillId.Luck);
        int currencyBonus = characterManager.Modifiers.ConsumeChestCurrencyBonus();
        bool noCurrency = characterManager.Modifiers.ConsumeChestNoCurrency();

        int goldenTouchLevel = characterManager.Combatant.ItemGoldenTouchLevel;
        var reward = rewardManager.CalculateRewards(floorNumber, isBoss, characterManager.Level, luckLevel, currencyBonus, noCurrency, goldenTouchLevel, characterManager.Character.characterClass);

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
        var pool = rewardManager.itemCatalog != null
            ? rewardManager.itemCatalog.GetCompatibleItems(characterManager.Character.characterClass)
            : null;
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
        if (chestOpenAudioSource != null)
        {
            AudioClip openClip = ChestOpenClipFor(reward.ItemRarity);
            if (openClip != null)
            {
                chestOpenAudioSource.PlayOneShot(openClip);
            }
        }

        void BuildSlot(int index, bool isWinning)
        {
            Sprite iconSprite = isWinning ? winningIcon : pool[Random.Range(0, pool.Count)].icon;
            var icon = new Image { sprite = iconSprite };
            icon.AddToClassList("chest-reel-icon");
            icon.AddToClassList(isWinning ? ChestReelBgClassFor(reward.ItemRarity) : ChestReelBgClassFor(rewardManager.RollItemRarity(false)));
            chestReelStrip.Add(icon);
        }

        yield return ChestRevealAnimator.PlayReel(chestReelStrip, chestReelViewport, BuildSlot, chestSkipButton, winningIndex);

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
        _ => "chest-reel-icon-epic"
    };

    AudioClip ChestOpenClipFor(ItemTier tier) => tier switch
    {
        ItemTier.Common => chestOpenCommonClip,
        ItemTier.Rare => chestOpenRareClip,
        _ => chestOpenEpicClip
    };

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
                mainStats.Add("двуручное: урон +30%");
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
            mainStats.Add($"HP +{item.HpBonusEffective:F0}");
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
            lines.Add($"Пассивка: {item.passiveSkill.skillName}");
        }

        return string.Join("\n", lines);
    }

    // 3.4: если новый предмет подходит сразу в несколько слотов (2 слота оружия/рук, 2 слота
    // колец) — показываем ВСЕ подходящие слоты с их текущим содержимым и даём игроку самому
    // выбрать, какой занять (или отказаться от нового предмета вовсе). Никакого автовыбора слота.
    IEnumerator ItemCompareFlow(ItemData newItem)
    {
        var candidates = characterManager.GetComparisonCandidates(newItem); // null-элемент = свободный слот

        ShowOnly(itemComparePanel);
        newItemName.text = newItem.itemName;
        newItemStats.text = ItemComparisonSummary(newItem);
        newItemStats.tooltip = DisplayFormat.ItemStatsText(newItem);
        tutorialManager?.QueueOnce(TutorialContent.Equipment);

        slotChoicesContainer.Clear();
        var buttons = new List<Button>();
        foreach (var candidate in candidates)
        {
            var btn = new Button
            {
                text = candidate != null ? $"Заменить: {candidate.itemName}\n{ItemComparisonSummary(candidate)}" : "Занять свободный слот",
                tooltip = candidate != null ? DisplayFormat.ItemStatsText(candidate) : "Новый предмет займёт свободный слот."
            };
            btn.AddToClassList("choice-card");
            btn.AddToClassList("item-slot-choice");
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
