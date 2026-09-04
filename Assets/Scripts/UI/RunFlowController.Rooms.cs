using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Ловушка (5.5) и квесты TryOrSkip (5.4) — общий попап ====================

    IEnumerator TrapRoomFlow(FloorMapNode roomNode)
    {
        var trap = GetResolvedTrap(roomNode);
        trapPopupTitle.text = "Ловушка";
        yield return ShowChancePopupAndWait(trap.DescriptionText, trap.Level, trap.SuccessText, trap.FailText, "Попытаться пройти ловушку", "Пойти дальше");

        if (!chanceAttempted)
        {
            yield break; // 5.5: "Пойти дальше" — риска и награды нет
        }

        if (chanceSucceeded)
        {
            var trapResources = new List<ResourceAmount>();
            var ingredient = rewardManager.RollIngredientReward(RewardRoomContext.Trap, new UnityRewardRandom());
            var material = rewardManager.RollForgeMaterial(RewardRoomContext.Trap, new UnityRewardRandom());
            if (ingredient.HasValue) trapResources.Add(ingredient.Value);
            if (material.HasValue) trapResources.Add(material.Value);
            if (trapResources.Count > 0)
            {
                saveManager.AddResources(trapResources);
                LogEvent("[Ловушка] Дополнительные ресурсы: " + string.Join(", ",
                    trapResources.ConvertAll(value => $"{PersistentResourceDisplay.Name(value.resourceId)} +{value.amount}")));
            }

            if (trap == TrapCatalog.Idol)
            {
                characterManager.AddCurrency(500);
            }
            else
            {
                pendingStandaloneChestReward = true;
            }
        }
        else
        {
            if (trap == TrapCatalog.MinedChest)
            {
                // «Крепкая подошва»: 10/15/20/25/30% снижения урона от сработавших ловушек.
                float toughSoleReduction = ItemEffectBalance.ToughSoleTrapReductionPercent(characterManager.Combatant.ItemToughSoleLevel) / 100f;
                characterManager.ApplyDirectDamage(15 * (1f - toughSoleReduction));
                characterManager.ApplyDirectArmorLoss(20);
            }
            else if (trap == TrapCatalog.Alarm)
            {
                characterManager.Modifiers.NextCombatMonsterDamageBuff10Percent = true;
                if (characterManager.IsAlive)
                {
                    yield return CombatRoomFlow(false);
                }
            }
            else if (trap == TrapCatalog.Idol)
            {
                characterManager.Modifiers.NextCombatDamageMultiplier = (characterManager.Modifiers.NextCombatDamageMultiplier ?? 1f) * 0.9f;
                characterManager.Modifiers.NextCombatAttackSpeedMultiplier = (characterManager.Modifiers.NextCombatAttackSpeedMultiplier ?? 1f) * 0.9f;
            }
        }
    }

    // hintId: тот же попап обслуживает и ловушки, и события с проверкой шанса — подсказка должна
    // соответствовать тому, что игрок видит в заголовке, а не всегда говорить про ловушку.
    IEnumerator ShowChancePopupAndWait(string description, int level, string successText, string failText, string attemptLabel, string skipLabel, string skipOutcome = null,
        string hintId = TutorialContent.RiskRoom)
    {
        ShowOnly(trapPopup);
        tutorialManager?.QueueOnce(hintId);
        trapDescriptionLabel.text = description;

        int luckLevel = characterManager.Progress.GetSkillLevel(SkillId.Luck);
        float chance = SuccessChanceCalculator.CalculateSuccessChancePercent(characterManager.Level, level, SuccessChanceCalculator.GetLuckBonusPercent(luckLevel));
        trapChanceLabel.text = $"Шанс успеха: {chance:F0}%";

        trapAttemptButton.text = attemptLabel;
        trapSkipButton.text = skipLabel;
        trapChoiceRow.style.display = DisplayStyle.Flex;
        trapOutcomeLabel.AddToClassList("hidden");
        trapContinueButton.AddToClassList("hidden");

        yield return WaitForAnyClick(trapAttemptButton, trapSkipButton);
        bool attempted = clickedIndex == 0;
        trapChoiceRow.style.display = DisplayStyle.None;

        chanceAttempted = attempted;
        chanceSucceeded = false;
        string outcome;

        if (!attempted)
        {
            outcome = string.IsNullOrWhiteSpace(skipOutcome) ? "Вы решаете не рисковать и идёте дальше." : skipOutcome;
        }
        else
        {
            chanceSucceeded = Random.value * 100f < chance;
            outcome = chanceSucceeded ? successText : failText;
        }

        LogEvent($"[{trapPopupTitle.text}] {outcome}");

        trapOutcomeLabel.text = outcome;
        trapOutcomeLabel.RemoveFromClassList("hidden");
        trapContinueButton.RemoveFromClassList("hidden");
        yield return WaitForClick(trapContinueButton);
    }

    // ==================== Особая комната / квест (5.3-5.4) ====================

    IEnumerator EventRoomFlow(FloorMapNode roomNode)
    {
        if (string.Equals(roomNode.ContentKey, MushroomCaveContentKey, System.StringComparison.Ordinal))
        {
            yield return MushroomCaveRoomFlow();
            yield break;
        }
        if (string.Equals(roomNode.ContentKey, AbandonedForgeContentKey, System.StringComparison.Ordinal))
        {
            yield return AbandonedForgeRoomFlow();
            yield break;
        }

        if (string.Equals(roomNode.ContentKey, PersonalRestContentKey, System.StringComparison.Ordinal))
        {
            if (!TryReservePersonalRestRoom())
                throw new System.InvalidOperationException($"Personal rest content for node {roomNode.Id} was already consumed.");
            yield return PersonalRestRoomFlow();
            yield break;
        }

        var quest = GetResolvedQuest(roomNode);
        if (quest == QuestCatalog.Hunt) huntQuestTriggeredThisRun = true;

        if (quest.InteractionType == QuestInteractionType.MultipleChoice)
        {
            ShowOnly(eventPopup);
            tutorialManager?.QueueOnce(TutorialContent.EventRoom);
            eventDescriptionLabel.text = quest.DescriptionText;
            eventChoicesContainer.Clear();

            var buttons = new List<Button>();
            foreach (var choice in quest.Choices)
            {
                var btn = new Button { text = choice.ButtonText };
                btn.AddToClassList("choice-card");
                eventChoicesContainer.Add(btn);
                buttons.Add(btn);
            }

            yield return WaitForAnyClick(buttons.ToArray());
            var picked = quest.Choices[clickedIndex];

            LogEvent($"[Событие] {picked.OutcomeText}");

            eventChoicesContainer.Clear();
            eventDescriptionLabel.text = picked.OutcomeText;
            var continueButton = new Button { text = "Продолжить" };
            continueButton.AddToClassList("button-primary");
            eventChoicesContainer.Add(continueButton);
            yield return WaitForClick(continueButton);

            if (picked.IsCorrect)
            {
                // ГДД 5.4: верный ответ на загадку сфинкса — +200 валюты забега в следующем бою.
                characterManager.Modifiers.NextChestCurrencyBonus = (characterManager.Modifiers.NextChestCurrencyBonus ?? 0) + 200;
            }
            else
            {
                characterManager.Modifiers.NextChestNoCurrency = true;
            }
        }
        else
        {
            trapPopupTitle.text = "Событие";
            yield return ShowChancePopupAndWait(quest.DescriptionText, quest.Level, quest.SuccessText, quest.FailText,
                quest.AttemptButtonText, quest.SkipButtonText, quest.SkipText, TutorialContent.EventRoom);
            trapPopupTitle.text = "Ловушка";

            if (quest == QuestCatalog.FairyRing)
            {
                if (chanceAttempted && campManager.CanCamp)
                {
                    // ГДД 5.4: успех — на 20% больше здоровья, чем базовый отдых (70% вместо
                    // базовых 50%, т.е. x1.4); провал — половина обычного объёма привала.
                    float healMultiplier = chanceSucceeded ? 1.4f : 0.5f;
                    floorManager.SetFloorState(FloorState.CampPhase);
                    yield return CampPhaseCoroutine(healMultiplier);
                    skipNextAutoCamp = true;
                }
            }
            else if (quest == QuestCatalog.SwordInStone)
            {
                if (chanceAttempted && chanceSucceeded)
                {
                    ItemData questReward = null;
                    ItemData baseReward = null;
                    bool rewardFound = rewardManager.itemCatalog != null && rewardManager.itemCatalog.TryGetItem(
                        quest.SuccessRewardItemName,
                        quest.SuccessRewardItemTier,
                        quest.SuccessRewardWeaponSubtype,
                        characterManager.Character.characterClass,
                        out baseReward);

                    if (rewardFound)
                    {
                        questReward = rewardManager.CreateItemAtExactLevel(baseReward, characterManager.Level);
                    }

                    if (questReward != null)
                    {
                        swordInStoneSucceededThisRun = true;
                        LogEvent($"[Событие] Меч в камне: получен {questReward.itemName}, уровень {questReward.itemLevel}.");
                        yield return ItemCompareFlow(questReward);
                    }
                    else
                    {
                        Debug.LogError("[Quest] Не удалось найти совместимый Кровавый меч для награды квеста «Меч в камне».");
                        LogEvent("[Событие] Ошибка: награда «Меча в камне» не найдена в каталоге.");
                    }
                }
                else if (chanceAttempted)
                {
                    characterManager.Modifiers.NextCombatDamageMultiplier = (characterManager.Modifiers.NextCombatDamageMultiplier ?? 1f) * 0.9f;
                }
            }
            else if (quest == QuestCatalog.Hunt && chanceAttempted)
            {
                if (chanceSucceeded)
                {
                    campManager.AddRations(5);
                    LogEvent("[Событие] Добыча: +5 рационов.");
                }
                else
                {
                    characterManager.ApplyDirectDamage(20f);
                    characterManager.ApplyDirectArmorLoss(15f);
                    LogEvent("[Событие] Добыча: −20 здоровья, −15 физической защиты.");
                }
            }
        }
    }

    IEnumerator MushroomCaveRoomFlow()
    {
        ShowOnly(eventPopup);
        tutorialManager?.QueueOnce(TutorialContent.EventRoom);
        eventDescriptionLabel.text = "В пещере растут редкие съедобные грибы. Можно собрать немного безопасно или рискнуть ради большей добычи.";
        eventChoicesContainer.Clear();
        var safe = new Button { text = "Безопасно собрать 2 гриба" };
        var risky = new Button { text = "Рискнуть и собрать 4 гриба" };
        safe.AddToClassList("choice-card");
        risky.AddToClassList("choice-card");
        eventChoicesContainer.Add(safe);
        eventChoicesContainer.Add(risky);
        yield return WaitForAnyClick(safe, risky);

        bool riskChosen = clickedIndex == 1;
        var outcome = RareRoomRewardHooks.ResolveMushroomCave(riskChosen, RareRoomConfig,
            new UnityRewardRandom());
        saveManager.AddResources(new[] { outcome.Mushrooms });
        if (outcome.ApplyNegativeConsequence) characterManager.ApplyMushroomPoison(RareRoomConfig);

        eventChoicesContainer.Clear();
        eventDescriptionLabel.text = $"Получено: {outcome.Mushrooms.amount} пещерных грибов." +
            (outcome.ApplyNegativeConsequence
                ? $"\nОтравление: −{RareRoomConfig.mushroomPoisonHealingPenaltyPercent:F0}% получаемого лечения на следующие {RareRoomConfig.mushroomPoisonDurationRooms} комнаты."
                : string.Empty);
        var continueButton = new Button { text = "Продолжить" };
        continueButton.AddToClassList("button-primary");
        eventChoicesContainer.Add(continueButton);
        yield return WaitForClick(continueButton);
    }

    IEnumerator AbandonedForgeRoomFlow()
    {
        var materials = rewardManager.RollAbandonedForgeMaterials(RareRoomConfig, new UnityRewardRandom());
        saveManager.AddResources(materials);
        ShowOnly(eventPopup);
        tutorialManager?.QueueOnce(TutorialContent.EventRoom);
        eventChoicesContainer.Clear();
        eventDescriptionLabel.text = "В заброшенной кузнице ещё осталось немного пригодных материалов.\n" +
            string.Join("\n", materials.ConvertAll(value =>
                $"{PersistentResourceDisplay.Name(value.resourceId)}: +{value.amount}"));
        var continueButton = new Button { text = "Забрать материалы" };
        continueButton.AddToClassList("button-primary");
        eventChoicesContainer.Add(continueButton);
        yield return WaitForClick(continueButton);
    }

    bool TryReservePersonalRestRoom()
    {
        string characterId = characterManager?.Character?.characterId;
        if (string.Equals(characterId, "jennifer", System.StringComparison.OrdinalIgnoreCase) && !hotSpringsTriggeredThisRun)
        {
            hotSpringsTriggeredThisRun = true;
            return true;
        }
        if (string.Equals(characterId, "violet", System.StringComparison.OrdinalIgnoreCase) && !violetTrapRoomTriggeredThisRun)
        {
            violetTrapRoomTriggeredThisRun = true;
            return true;
        }
        if (string.Equals(characterId, "sasha", System.StringComparison.OrdinalIgnoreCase) && !sashaBeerCellarTriggeredThisRun)
        {
            sashaBeerCellarTriggeredThisRun = true;
            return true;
        }
        return false;
    }

    IEnumerator PersonalRestRoomFlow()
    {
        string characterId = characterManager.Character.characterId;
        bool highRelationship = saveManager.GetRelationshipLevel(characterId) >= SaveManager.MaxRelationshipLevel;
        string sceneId = characterId.ToLowerInvariant() switch
        {
            "jennifer" => highRelationship ? "jennifer_hot_springs_high" : "jennifer_hot_springs_low",
            "violet" => highRelationship ? "violet_trap_room_high" : "violet_trap_room_low",
            "sasha" => highRelationship ? "sasha_beer_cellar_high" : "sasha_beer_cellar_low",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(sceneId) && !saveManager.HasSeenVNScene(characterManager.Character.characterId, sceneId) && TryPlayRunVNScene(sceneId))
        {
            while (vnManager != null && vnManager.IsPlaying) yield return null;
        }

        ShowOnly(campPanel);
        tutorialManager?.QueueOnce(TutorialContent.HotSprings);
        float hpRestored = campManager.RestoreFullHealth(characterManager);
        string roomName = characterId.ToLowerInvariant() switch
        {
            "violet" => "Комната ловушек",
            "sasha" => "Пивной погреб",
            _ => "Горячие источники"
        };
        campText.text = $"{roomName} восстанавливает силы...\n+{hpRestored:F0} здоровья\nРационы не потрачены: {campManager.RationsRemaining}";
        LogEvent($"[{roomName}] +{hpRestored:F0} здоровья, рацион не потрачен.");
        yield return WaitForClick(campContinueButton);
    }

    // ==================== Торговец (5.2) ====================

    IEnumerator MerchantRoomFlow(FloorMapNode roomNode)
    {
        var offers = GetResolvedMerchantOffers(roomNode);

        bool leave = false;
        while (!leave)
        {
            ShowOnly(merchantPanel);
            tutorialManager?.QueueOnce(TutorialContent.Merchant);
            merchantCurrencyLabel.text = $"Валюта забега: {characterManager.RunCurrency}";
            merchantOffersContainer.Clear();

            var buttons = new List<Button>();
            foreach (var offer in offers)
            {
                var card = new VisualElement();
                card.AddToClassList("merchant-offer-card");

                if (offer.Item == null)
                {
                    card.Add(new Label("Пусто") { });
                    merchantOffersContainer.Add(card);
                    continue;
                }

                var nameLabel = new Label(offer.Item.itemName);
                nameLabel.AddToClassList("item-card-name");
                SetRarityClass(nameLabel, offer.Item.tier);
                card.Add(nameLabel);

                var statsLabel = new Label(DisplayFormat.ItemStatsText(offer.Item));
                statsLabel.AddToClassList("body-label");
                card.Add(statsLabel);

                if (offer.HasDiscount)
                {
                    var originalPriceLabel = new Label($"{offer.OriginalPrice} монет");
                    originalPriceLabel.AddToClassList("merchant-offer-price-original");
                    card.Add(originalPriceLabel);
                    var discountTag = new Label("СКИДКА!");
                    discountTag.AddToClassList("merchant-offer-discount-tag");
                    card.Add(discountTag);
                }

                var priceLabel = new Label($"{offer.Price} монет");
                priceLabel.AddToClassList("merchant-offer-price");
                card.Add(priceLabel);

                var buyButton = new Button { text = "Купить" };
                buyButton.AddToClassList("button-primary");
                buyButton.AddToClassList("merchant-offer-buy-button");
                buyButton.SetEnabled(characterManager.RunCurrency >= offer.Price);
                card.Add(buyButton);

                merchantOffersContainer.Add(card);
                buttons.Add(buyButton);
            }

            buttons.Add(merchantContinueButton);

            yield return WaitForAnyClick(buttons.ToArray());

            if (clickedIndex == buttons.Count - 1)
            {
                leave = true; // "Уйти от торговца"
                continue;
            }

            // clickedIndex maps 1:1 into `offers` because empty-item offers still add a card but never
            // a button — so `buttons` only ever contains as many entries as offers WITH an item, plus
            // the leave button. Map back by re-walking offers with a running non-null index.
            int runningIndex = -1;
            MerchantOffer purchased = null;
            foreach (var offer in offers)
            {
                if (offer.Item == null) continue;
                runningIndex++;
                if (runningIndex == clickedIndex)
                {
                    purchased = offer;
                    break;
                }
            }

            if (purchased != null && characterManager.TrySpendCurrency(purchased.Price))
            {
                LogEvent($"[Торговец] Куплено: {purchased.Item.itemName} за {purchased.Price} валюты забега.");
                offers.Remove(purchased);
                yield return ItemCompareFlow(purchased.Item);
            }
        }
    }
}
