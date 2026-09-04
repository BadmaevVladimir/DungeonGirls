using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Результаты забега (1 п.7-8, 7.2 п.6) ====================

    IEnumerator ShowResultsFlow(bool victory)
    {
        LogEvent($"[Забег] Завершён: {(victory ? "победа" : "поражение")}.");

        runScreen.style.display = DisplayStyle.None;
        resultsScreen.style.display = DisplayStyle.Flex;
        tutorialManager?.QueueOnce(TutorialContent.Results);
        resultsContinueButton.SetEnabled(false);
        resultsSkipRequested = false;

        if (runCompletionCommitted)
        {
            resultsContinueButton.SetEnabled(true);
            yield return WaitForClick(resultsContinueButton);
            yield break;
        }

        var completion = rewardManager.CalculateRunCompletionReward(
            victory,
            characterManager.RoomsClearedThisRun,
            dungeonManager.CurrentFloorNumber,
            characterManager.RoomsClearedOnCurrentFloor);
        string clearBonus = victory
            ? $"Бонус зачистки: +{completion.ClearBonusMetaCurrency} мета-валюты, +{completion.ClearBonusGachaCurrency} гача-валюты\n"
            : string.Empty;
        VeteranCharacter veteran = null;
        VeteranAttestationResult attestation = null;
        if (ShouldCreateVeteran(victory))
        {
            veteran = BuildVeteranSnapshot(DungeonManager.TotalFloors);
            var service = new VeteranAttestationService(new CombatSimulationEngine());
            attestation = service.Evaluate(veteran.buildSnapshot, VeteranAttestationConfig, AttestationRunMode.Release);
            ApplyAttestation(veteran, attestation);
            yield return ShowAttestationCeremony(attestation);
        }
        else
        {
            resultsAttestationPanel.style.display = DisplayStyle.None;
        }

        if (saveManager != null)
        {
            int floorsCleared = victory ? DungeonManager.TotalFloors : Mathf.Max(0, dungeonManager.CurrentFloorNumber - 1);
            int relationshipPoints = floorsCleared * 10;
            int relationshipAdded = 0;
            int relationshipBefore = saveManager.GetRelationshipPoints(characterManager.Character.characterId);
            if (saveManager.CompleteRun(completion.MetaCurrency, completion.GachaCurrency, characterManager.Character.characterId,
                veteran, relationshipPoints, currentRunCompletionId))
            {
                runCompletionCommitted = true;
                relationshipAdded = saveManager.GetRelationshipPoints(characterManager.Character.characterId) - relationshipBefore;
                if (relationshipAdded > 0) tutorialManager?.QueueOnce(TutorialContent.Relationships);
            }
            if (veteran != null && runCompletionCommitted) tutorialManager?.QueueOnce(TutorialContent.VeteranCreated);

            string relationshipReward = relationshipAdded > 0
                ? $"+{relationshipAdded} отношений с {characterManager.Character.characterName} ({saveManager.GetRelationshipPoints(characterManager.Character.characterId)}/{SaveManager.RelationshipLevelThreeThreshold})\n"
                : string.Empty;
            string veteranReward = veteran != null && runCompletionCommitted
                ? $"Ветеран добавлен в колоду. Ранг: {veteran.veteranRank}\n"
                : string.Empty;
            resultsBodyLabel.text = BuildResultsText(victory, completion, clearBonus, relationshipReward, veteranReward);
        }

        resultsTitleLabel.text = victory ? "Победа" : "Поражение";
        resultsTitleLabel.RemoveFromClassList(victory ? "results-defeat" : "results-victory");
        resultsTitleLabel.AddToClassList(victory ? "results-victory" : "results-defeat");

        if (saveManager == null) resultsBodyLabel.text = BuildResultsText(victory, completion, clearBonus, string.Empty,
            veteran != null ? $"Ранг: {veteran.veteranRank}\n" : string.Empty);

        resultsContinueButton.SetEnabled(true);
        yield return WaitForClick(resultsContinueButton);
    }

    public static bool ShouldCreateVeteran(bool victory) => victory;

    string BuildResultsText(bool victory, RunCompletionReward completion, string clearBonus, string relationshipReward, string veteranReward)
    {
        return $"{characterManager.Character.characterName} достигла {characterManager.Level} уровня.\n" +
            $"Валюта забега (сгорает): {characterManager.RunCurrency}\n\n" +
            "Награды за забег:\n" +
            $"+{completion.MetaCurrency} мета-валюты\n" +
            $"+{completion.GachaCurrency} гача-валюты\n" +
            clearBonus + relationshipReward + veteranReward;
    }

    void ApplyAttestation(VeteranCharacter veteran, VeteranAttestationResult attestation)
    {
        VeteranRank rank = attestation?.FinalRank ?? VeteranRank.C;
        veteran.veteranRank = VeteranRankFormat.ToPersistentString(rank);
        veteran.grade = veteran.veteranRank;
        veteran.ratingVersion = !string.IsNullOrWhiteSpace(attestation?.RatingVersion)
            ? attestation.RatingVersion
            : (VeteranAttestationConfig != null ? VeteranAttestationConfig.ratingVersion : "fallback-c");
        veteran.qualifyingTrialId = attestation?.QualifyingTrialId ?? string.Empty;
        veteran.isLegacy = false;
        veteran.schemaVersion = VeteranCharacter.CurrentVeteranSchemaVersion;
        if (attestation == null || attestation.CompletionStatus == AttestationCompletionStatus.Fallback)
            Debug.LogWarning($"[VeteranAttestation] Fallback C: character={veteran.characterId}, version={veteran.ratingVersion}, error={attestation?.ErrorCode ?? "no_result"}.");
    }

    IEnumerator ShowAttestationCeremony(VeteranAttestationResult result)
    {
        resultsAttestationPanel.style.display = DisplayStyle.Flex;
        resultsSkipButton.style.display = DisplayStyle.Flex;
        resultsPortraitImage.sprite = characterManager.Character.portrait;
        resultsFinalRankLabel.text = string.Empty;
        resultsSkipButton.SetEnabled(false);
        string[] stages =
        {
            "Фиксация боевого опыта…",
            "Анализ снаряжения…",
            "Моделирование предельной угрозы…",
            "Ранг присваивается…"
        };
        float duration = VeteranAttestationConfig != null ? VeteranAttestationConfig.ceremonyMinimumSeconds : 4.5f;
        float skipDelay = VeteranAttestationConfig != null ? VeteranAttestationConfig.ceremonySkipDelaySeconds : 1.5f;
        float elapsed = 0f;
        int stage = -1;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            int nextStage = Mathf.Min(stages.Length - 1, Mathf.FloorToInt(elapsed / Mathf.Max(0.01f, duration / stages.Length)));
            if (nextStage != stage)
            {
                stage = nextStage;
                resultsAttestationStageLabel.text = stages[stage];
            }
            if (elapsed >= skipDelay) resultsSkipButton.SetEnabled(true);
            if (elapsed >= skipDelay && resultsSkipRequested) break;
            yield return null;
        }
        resultsAttestationStageLabel.text = "Ранг присвоен";
        resultsFinalRankLabel.text = VeteranRankFormat.ToPersistentString(result?.FinalRank ?? VeteranRank.C);
        resultsSkipButton.style.display = DisplayStyle.None;
    }

    VeteranCharacter BuildVeteranSnapshot(int floorsCleared)
    {
        var veteran = new VeteranCharacter
        {
            characterId = characterManager.Character.characterId,
            // finalHP в схеме трактуется как финальный максимальный HP-стат персонажа, а не
            // оставшееся после последнего удара здоровье (при поражении оно всегда было бы 0).
            finalHP = characterManager.Combatant.MaxHP,
            uniquePassiveSkillName = characterManager.Character.uniquePassiveSkill != null ? characterManager.Character.uniquePassiveSkill.skillName : string.Empty,
            uniquePassiveLevel = characterManager.Progress.UniquePassiveLevel,
            uniqueActiveSkillName = characterManager.Character.uniqueActiveSkill != null ? characterManager.Character.uniqueActiveSkill.skillName : string.Empty,
            uniqueActiveLevel = characterManager.Progress.UniqueActiveLevel,
            inheritedUniquePassiveSkillName = characterManager.Progress.MentorUniquePassiveSkillName,
            inheritedUniquePassiveLevel = characterManager.Progress.MentorUniquePassiveLevel,
            floorsCleared = floorsCleared,
            grade = "C",
            // Формула PowerLevel остаётся открытым вопросом ГДД. Не подменяем решение дизайнера.
            powerLevel = 0
        };

        veteran.buildSnapshot = VeteranBuildSnapshot.Capture(
            characterManager.Character.characterId,
            characterManager.Combatant,
            characterManager.Character.uniqueActiveSkill,
            characterManager.Progress.UniqueActiveLevel);

        foreach (var pair in characterManager.Progress.KnownSkillLevels)
        {
            if (pair.Key != null)
            {
                veteran.finalSkills.Add(new VeteranSkillEntry { skillName = pair.Key.skillName, level = pair.Value });
            }
        }

        foreach (var item in characterManager.EquippedItems)
        {
            if (item == null) continue;
            veteran.finalEquipment.Add(item.itemName);
            veteran.finalEquipmentSnapshot.Add(new VeteranEquipmentEntry { itemName = item.itemName, itemLevel = item.itemLevel, itemRank = item.itemRank });
        }

        return veteran;
    }

    void ApplySelectedMentorInheritance()
    {
        levelUpManager.MentorSkillPool = new List<PassiveSkillData>();
        if (selectedMentor == null || selectedTransferredSkills == null || selectedTransferredSkills.Count == 0)
        {
            LogEvent("[Наставник] Забег начат без наставника.");
            return;
        }

        characterManager.Progress.MentorUniquePassiveSkillName = selectedTransferredSkills[0];
        characterManager.Progress.MentorUniquePassiveLevel = 1;
        for (int i = 1; i < selectedTransferredSkills.Count; i++)
        {
            var skill = FindPassiveSkill(selectedTransferredSkills[i]);
            if (skill != null) levelUpManager.MentorSkillPool.Add(skill);
            else Debug.LogWarning($"[Наставник] Не найден PassiveSkillData для «{selectedTransferredSkills[i]}»; навык пропущен.");
        }

        characterManager.RefreshCombatStats();
        string extras = levelUpManager.MentorSkillPool.Count > 0
            ? string.Join(", ", levelUpManager.MentorSkillPool.Select(skill => skill.skillName))
            : "нет";
        LogEvent($"[Наставник] {CharacterDisplayName(selectedMentor.characterId)} передаёт «{selectedTransferredSkills[0]}»; в пул левел-апа добавлено: {extras}.");
    }

    PassiveSkillData FindPassiveSkill(string skillName)
    {
        return generalSkillPool.Concat(warriorSkillPool).Concat(rogueSkillPool).Concat(barbarianSkillPool)
            .FirstOrDefault(skill => skill != null && string.Equals(skill.skillName, skillName, System.StringComparison.OrdinalIgnoreCase));
    }
}
