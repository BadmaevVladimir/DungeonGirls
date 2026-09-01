using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class RunFlowController
{
    const string PersonalRestContentKey = "special:personal-rest";

    void ResolveGeneratedFloorMapContent()
    {
        foreach (var node in floorManager.CurrentMap.Nodes)
        {
            var previousRandomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(node.ContentSeed);
            try
            {
                ResolveNodeContent(node);
                node.ContentResolved = true;
            }
            finally
            {
                UnityEngine.Random.state = previousRandomState;
            }
        }
        floorManager.FinalizeGeneratedContent();
    }

    void ResolveNodeContent(FloorMapNode node)
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
                var trap = TrapCatalog.All[UnityEngine.Random.Range(0, TrapCatalog.All.Length)];
                node.ContentKey = $"trap:{trap.Name}";
                break;
            case RoomType.Special:
                ResolveSpecialContent(node);
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

    void ResolveSpecialContent(FloorMapNode node)
    {
        bool personalRoomAvailable = IsPersonalRestRoomAvailable() &&
            (characterManager.RoomsClearedThisRun > 0 || node.Kind != FloorMapNodeKind.Start);
        if (personalRoomAvailable && UnityEngine.Random.value < 0.30f)
        {
            node.ContentKey = PersonalRestContentKey;
            return;
        }

        var quest = QuestCatalog.PickForFloor(dungeonManager.CurrentFloorNumber, huntQuestTriggeredThisRun, swordInStoneSucceededThisRun);
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
    }

    IEnumerator ResolvePendingRoomRewards()
    {
        if (pendingCombatReward)
        {
            var levelsGained = characterManager.GrantExperience(
                rewardManager,
                pendingCombatWasBoss ? ExperienceSource.Boss : ExperienceSource.CombatRoom,
                dungeonManager.CurrentFloorNumber);
            yield return ShowRewardChestFlow(dungeonManager.CurrentFloorNumber, pendingCombatWasBoss);

            // ГДД: повышение уровня открывается только после завершения выдачи награды.
            foreach (int reachedLevel in levelsGained)
            {
                bool activeUpgraded = characterManager.Progress.TryAutoUpgradeUniqueActiveAtLevel(reachedLevel);
                string activeUpgradeNotice = activeUpgraded
                    ? $"Уникальный активный навык «{characterManager.Progress.Character.uniqueActiveSkill.skillName}» автоматически повышен до ур. {characterManager.Progress.UniqueActiveLevel}."
                    : null;
                yield return LevelUpFlow(activeUpgradeNotice);
            }
        }

        if (pendingStandaloneChestReward)
            yield return ShowRewardChestFlow(dungeonManager.CurrentFloorNumber, false);

        ResetPendingRoomRewards();
    }
}
