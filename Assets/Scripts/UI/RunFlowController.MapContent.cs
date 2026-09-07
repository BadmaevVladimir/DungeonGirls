using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class RunFlowController
{
    const string PersonalRestContentKey = "special:personal-rest";
    const string MushroomCaveContentKey = "special:mushroom-cave";
    const string AbandonedForgeContentKey = "special:abandoned-forge";
    const string HarpyNestContentKey = "trap:harpy-nest";

    void ResolveGeneratedFloorMapContent()
    {
        var rareState = new RareRoomFloorState();
        // R01: одноразовое содержимое резервируется на время прохода по узлам этажа — run-флаги
        // выставляются только при входе в комнату и на этапе генерации ещё все false.
        var oneShotState = new OneShotRoomFloorState();
        foreach (var node in floorManager.CurrentMap.Nodes)
        {
            var previousRandomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(node.ContentSeed);
            try
            {
                ResolveNodeContent(node, rareState, oneShotState);
                node.ContentResolved = true;
            }
            finally
            {
                UnityEngine.Random.state = previousRandomState;
            }
        }
        floorManager.FinalizeGeneratedContent();
    }

    void ResolveNodeContent(FloorMapNode node, RareRoomFloorState rareState, OneShotRoomFloorState oneShotState)
    {
        node.ResolvedMonsterIds ??= new List<string>();
        node.ResolvedMerchantOffers ??= new List<FloorMerchantOfferState>();
        node.ResolvedMonsterIds.Clear();
        node.ResolvedMerchantOffers.Clear();

        switch (node.RoomType)
        {
            case RoomType.Combat:
                ResolveCombatContent(node);
                break;
            case RoomType.Boss:
                node.ContentKey = $"boss:{bossData.monsterName}";
                break;
            case RoomType.Trap:
                // Harpy Nest has a fully resolved backend/content ID, but live selection remains
                // disabled until its missing success-check difficulty is approved.
                var trap = TrapCatalog.All[UnityEngine.Random.Range(0, TrapCatalog.All.Length)];
                node.ContentKey = $"trap:{trap.Name}";
                break;
            case RoomType.Special:
                ResolveSpecialContent(node, rareState, oneShotState);
                break;
            case RoomType.Merchant:
                ResolveMerchantContent(node);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node.RoomType), node.RoomType, "Unknown room type.");
        }
    }

    void ResolveCombatContent(FloorMapNode node)
    {
        int count = MonsterEncounterBudget.RollMonsterCount(characterManager.Level);
        int remainingThreatBudget = MonsterEncounterBudget.GetThreatBudget(dungeonManager.CurrentFloorNumber);
        var eligible = regularMonsterPool.FindAll(monster => monster != null && monster.minFloorTier <= dungeonManager.CurrentFloorNumber);
        if (eligible.Count == 0) eligible = regularMonsterPool.FindAll(monster => monster != null);

        for (int i = 0; i < count; i++)
        {
            var monster = MonsterEncounterBudget.RollAffordableMonster(eligible, remainingThreatBudget);
            if (monster == null) break;
            node.ResolvedMonsterIds.Add(monster.monsterName);
            remainingThreatBudget -= MonsterEncounterBudget.GetThreatCost(monster);
        }
        if (node.ResolvedMonsterIds.Count == 0)
            throw new InvalidOperationException($"Could not resolve combat encounter for node {node.Id}.");
        node.ContentKey = $"combat:{string.Join("|", node.ResolvedMonsterIds)}";
    }

    void ResolveSpecialContent(FloorMapNode node, RareRoomFloorState rareState, OneShotRoomFloorState oneShotState)
    {
        var rare = RareRoomContentResolver.Resolve(RoomType.Special, dungeonManager.CurrentFloorNumber,
            RareRoomConfig, rareState, new UnityRewardRandom());
        if (rare == RareRoomContentId.MushroomCave)
        {
            node.ContentKey = MushroomCaveContentKey;
            return;
        }
        if (rare == RareRoomContentId.AbandonedForge)
        {
            node.ContentKey = AbandonedForgeContentKey;
            return;
        }

        bool personalRoomAvailable = IsPersonalRestRoomAvailable() &&
            !oneShotState.IsReserved(OneShotRoomContentId.PersonalRest) &&
            (characterManager.RoomsClearedThisRun > 0 || node.Kind != FloorMapNodeKind.Start);
        if (personalRoomAvailable && UnityEngine.Random.value < 0.30f)
        {
            oneShotState.TryReserve(OneShotRoomContentId.PersonalRest);
            node.ContentKey = PersonalRestContentKey;
            return;
        }

        // Добыча и Меч в камне тоже одноразовые: run-флаг ещё не выставлен, поэтому уже
        // зарезервированный на этом этаже вариант исключается через те же аргументы PickForFloor.
        var quest = QuestCatalog.PickForFloor(dungeonManager.CurrentFloorNumber,
            huntQuestTriggeredThisRun || oneShotState.IsReserved(OneShotRoomContentId.HuntQuest),
            swordInStoneSucceededThisRun || oneShotState.IsReserved(OneShotRoomContentId.SwordInStone));
        if (quest == QuestCatalog.Hunt) oneShotState.TryReserve(OneShotRoomContentId.HuntQuest);
        else if (quest == QuestCatalog.SwordInStone) oneShotState.TryReserve(OneShotRoomContentId.SwordInStone);
        node.ContentKey = $"special:{quest.Name}";
    }

    bool IsPersonalRestRoomAvailable()
    {
        string characterId = characterManager?.Character?.characterId;
        if (string.Equals(characterId, "jennifer", StringComparison.OrdinalIgnoreCase)) return !hotSpringsTriggeredThisRun;
        if (string.Equals(characterId, "violet", StringComparison.OrdinalIgnoreCase)) return !violetTrapRoomTriggeredThisRun;
        if (string.Equals(characterId, "sasha", StringComparison.OrdinalIgnoreCase)) return !sashaBeerCellarTriggeredThisRun;
        return false;
    }

    void ResolveMerchantContent(FloorMapNode node)
    {
        var offers = rewardManager.GenerateMerchantOffers(characterManager.Level, characterManager.Character.characterClass);
        foreach (var offer in offers)
        {
            var item = offer.Item;
            node.ResolvedMerchantOffers.Add(new FloorMerchantOfferState
            {
                ItemName = item != null ? item.itemName : null,
                ItemTier = item != null ? item.tier : default,
                WeaponSubtype = item != null ? item.weaponSubtype : WeaponSubtype.None,
                ItemLevel = item != null ? item.itemLevel : 0,
                OriginalPrice = offer.OriginalPrice,
                Price = offer.Price,
                HasDiscount = offer.HasDiscount
            });
            if (item != null) UnityEngine.Object.Destroy(item);
        }
        node.ContentKey = $"merchant:{string.Join("|", node.ResolvedMerchantOffers.Select(offer => offer.ItemName ?? "empty"))}";
    }

    List<MonsterData> GetResolvedMonsters(FloorMapNode node)
    {
        var result = new List<MonsterData>();
        foreach (string monsterId in node.ResolvedMonsterIds)
        {
            var monster = regularMonsterPool.Find(candidate => candidate != null &&
                string.Equals(candidate.monsterName, monsterId, StringComparison.Ordinal));
            if (monster == null) throw new InvalidOperationException($"Resolved monster '{monsterId}' is missing for node {node.Id}.");
            result.Add(monster);
        }
        return result;
    }

    TrapDefinition GetResolvedTrap(FloorMapNode node)
    {
        const string prefix = "trap:";
        string id = node.ContentKey.StartsWith(prefix, StringComparison.Ordinal) ? node.ContentKey.Substring(prefix.Length) : null;
        var trap = Array.Find(TrapCatalog.All, candidate => string.Equals(candidate.Name, id, StringComparison.Ordinal));
        return trap ?? throw new InvalidOperationException($"Resolved trap '{node.ContentKey}' is missing for node {node.Id}.");
    }

    QuestDefinition GetResolvedQuest(FloorMapNode node)
    {
        const string prefix = "special:";
        string id = node.ContentKey.StartsWith(prefix, StringComparison.Ordinal) ? node.ContentKey.Substring(prefix.Length) : null;
        var quest = Array.Find(QuestCatalog.All, candidate => string.Equals(candidate.Name, id, StringComparison.Ordinal));
        return quest ?? throw new InvalidOperationException($"Resolved quest '{node.ContentKey}' is missing for node {node.Id}.");
    }

    List<MerchantOffer> GetResolvedMerchantOffers(FloorMapNode node)
    {
        var result = new List<MerchantOffer>();
        foreach (var state in node.ResolvedMerchantOffers)
        {
            ItemData item = null;
            if (!string.IsNullOrWhiteSpace(state.ItemName))
            {
                if (rewardManager.itemCatalog == null || !rewardManager.itemCatalog.TryGetItem(
                    state.ItemName, state.ItemTier, state.WeaponSubtype, characterManager.Character.characterClass, out var baseItem))
                    throw new InvalidOperationException($"Resolved merchant item '{state.ItemName}' is missing for node {node.Id}.");
                item = rewardManager.CreateItemAtExactLevel(baseItem, state.ItemLevel);
            }
            result.Add(new MerchantOffer
            {
                Item = item,
                OriginalPrice = state.OriginalPrice,
                Price = state.Price,
                HasDiscount = state.HasDiscount
            });
        }
        return result;
    }

    void ResetPendingRoomRewards()
    {
        pendingCombatReward = false;
        pendingCombatWasBoss = false;
        pendingStandaloneChestReward = false;
        pendingSuccessfulEventOrTrapXp = false;
        pendingRoomRewardGrant = null;
    }

    IEnumerator ResolvePendingRoomRewards()
    {
        // Один список на всю комнату: если ловушка провалилась и вызвала бой, опыт за бой и опыт за
        // событие не должны порождать два независимых прохода левел-апов.
        var levelsGained = new List<int>();

        if (pendingCombatReward)
        {
            floorManager.SetFloorState(FloorState.RoomRewardResolve);
            int luckLevel = characterManager.Progress.GetSkillLevel(SkillId.Luck);
            int currencyBonus = characterManager.Modifiers.ConsumeChestCurrencyBonus();
            bool noCurrency = characterManager.Modifiers.ConsumeChestNoCurrency();
            int goldenTouchLevel = characterManager.Combatant.ItemGoldenTouchLevel;
            var foodIngredients = new List<ResourceAmount>();
            if (characterManager.ConsumeExplorerIngredientRoll())
            {
                var bonus = rewardManager.RollCombatIngredient(new UnityRewardRandom());
                if (bonus.HasValue) foodIngredients.Add(bonus.Value);
            }
            pendingRoomRewardGrant ??= new RoomRewardGrant(rewardManager.CalculateRoomReward(
                dungeonManager.CurrentFloorNumber, pendingCombatWasBoss, characterManager.Level,
                luckLevel, currencyBonus, noCurrency, goldenTouchLevel,
                characterManager.Character.characterClass, extraIngredients: foodIngredients));

            var ingredientStacks = new List<ResourceAmount>();
            pendingRoomRewardGrant.TryApply(characterManager.AddCurrency, ingredientStacks.Add);
            saveManager.AddResources(ingredientStacks);

            levelsGained.AddRange(characterManager.GrantExperience(
                rewardManager,
                pendingCombatWasBoss ? ExperienceSource.Boss : ExperienceSource.CombatRoom,
                dungeonManager.CurrentFloorNumber));
        }

        // R02: ровно одна транзакция опыта за успешную ловушку/событие в комнате. Раньше
        // RewardManager.SuccessfulEventOrTrap считался, но никем не выдавался.
        if (pendingSuccessfulEventOrTrapXp)
        {
            floorManager.SetFloorState(FloorState.RoomRewardResolve);
            levelsGained.AddRange(characterManager.GrantExperience(
                rewardManager, ExperienceSource.SuccessfulEventOrTrap, dungeonManager.CurrentFloorNumber));
        }

        if (pendingCombatReward)
        {
            yield return ShowLootSummaryFlow(pendingRoomRewardGrant.Result);
            if (pendingRoomRewardGrant.Result.HasChest)
                yield return ShowResolvedRewardChestFlow(pendingRoomRewardGrant.Result.Chest);
        }

        // ГДД: повышение уровня открывается только после завершения выдачи награды.
        foreach (int reachedLevel in levelsGained)
        {
            bool activeUpgraded = characterManager.Progress.TryAutoUpgradeUniqueActiveAtLevel(reachedLevel);
            string activeUpgradeNotice = activeUpgraded
                ? $"Уникальный активный навык «{characterManager.Progress.Character.uniqueActiveSkill.skillName}» автоматически повышен до ур. {characterManager.Progress.UniqueActiveLevel}."
                : null;
            yield return LevelUpFlow(activeUpgradeNotice);
        }

        if (pendingStandaloneChestReward)
            yield return ShowRewardChestFlow(dungeonManager.CurrentFloorNumber, false);

        ResetPendingRoomRewards();
    }
}
