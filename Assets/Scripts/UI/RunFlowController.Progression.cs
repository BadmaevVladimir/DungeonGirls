using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Левел-ап (3.5) ====================

    IEnumerator LevelUpFlow(string activeUpgradeNotice = null)
    {
        floorManager.SetFloorState(FloorState.LevelUpChoice);
        ShowOnly(levelUpPanel);
        tutorialManager?.QueueOnce(TutorialContent.LevelUp);
        while (true)
        {
            var options = levelUpManager.GenerateLevelUpOptions(characterManager.Progress);
            string rerollText = characterManager.Progress.LevelUpRerollsRemaining > 0
                ? $"Перебросить варианты (осталось: {characterManager.Progress.LevelUpRerollsRemaining})"
                : string.Empty;
            levelUpTitle.text = string.IsNullOrWhiteSpace(activeUpgradeNotice)
                ? $"Выбери навык\nПеребросов: {characterManager.Progress.LevelUpRerollsRemaining}"
                : $"Новый уровень\n{activeUpgradeNotice}\nПеребросов: {characterManager.Progress.LevelUpRerollsRemaining}";
            levelUpCardsContainer.Clear();
            var buttons = new List<Button>();
            if (options.Count == 0)
            {
                var continueButton = new Button { text = "Продолжить" };
                continueButton.AddToClassList("button-primary");
                levelUpCardsContainer.Add(continueButton);
                buttons.Add(continueButton);
            }

            foreach (var option in options)
            {
                string description = option.Description;
                string cardText = string.IsNullOrWhiteSpace(description) ? option.ToString() : $"{option}\n{description}";
                var btn = new Button { text = cardText };
                btn.AddToClassList("choice-card");
                levelUpCardsContainer.Add(btn);
                buttons.Add(btn);
            }

            bool canReroll = options.Count > 0 && characterManager.Progress.LevelUpRerollsRemaining > 0;
            levelUpRerollButton.text = rerollText;
            levelUpRerollButton.EnableInClassList("hidden", !canReroll);
            if (canReroll)
            {
                buttons.Add(levelUpRerollButton);
            }

            yield return WaitForAnyClick(buttons.ToArray());
            if (canReroll && clickedIndex == buttons.Count - 1)
            {
                characterManager.Progress.TrySpendLevelUpReroll();
                continue;
            }

            levelUpRerollButton.AddToClassList("hidden");
            if (options.Count == 0)
            {
                yield break;
            }

            var chosen = options[clickedIndex];
            levelUpManager.ApplyChoice(characterManager.Progress, chosen);
            characterManager.RefreshCombatStats();
            LogEvent($"[Левел-ап] {chosen} (уровень {characterManager.Level}).");
            yield break;
        }
    }

    // ==================== Привал (раздел 6) ====================

    // 6.1: триггер привала — явное решение игрока. Игра предлагает встать на привал; если
    // игрок отказывается, рацион не тратится и автоматика 6.2 не запускается. Показывает текущее
    // HP, чтобы решение о трате рациона было осознанным.
    IEnumerator CampOfferAndPhaseCoroutine()
    {
        ShowOnly(campPanel);
        tutorialManager?.QueueOnce(TutorialContent.Camp);
        var combatant = characterManager.Combatant;
        campText.text = $"Можно встать на привал (потратит 1 рацион). Здоровье: {Mathf.Max(combatant.CurrentHP, 0f):F0}/{combatant.MaxHP:F0}. Осталось рационов: {campManager.RationsRemaining}.";
        SetCampOfferButtonsVisible(true);

        yield return WaitForAnyClick(campAcceptButton, campDeclineButton);
        SetCampOfferButtonsVisible(false);

        bool accepted = clickedIndex == 0;
        if (!accepted)
        {
            LogEvent("[Привал] Игрок отказался от привала.");
            yield break;
        }

        yield return CampPhaseCoroutine();
    }

    void SetCampOfferButtonsVisible(bool visible)
    {
        campAcceptButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        campDeclineButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        campContinueButton.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
    }

    IEnumerator CampPhaseCoroutine(float healMultiplierOverride = -1f)
    {
        ShowOnly(campPanel);
        if (!campManager.TrySpendRation()) yield break;
        yield return TryPlayCampSceneAfterRation();

        float multiplier = healMultiplierOverride > 0f ? healMultiplierOverride : characterManager.Modifiers.ConsumeCampHealMultiplier();
        var result = campManager.RestoreAtCamp(characterManager, multiplier);

        campText.text = $"{characterManager.Character.characterName} отдыхает у привала..." +
            $"\n+{result.HpRestored:F0} HP" +
            (result.ArmorRestored > 0f ? $", +{result.ArmorRestored:F0} физ. защиты (Полевой ремонт)" : string.Empty) +
            $"\nОсталось рационов: {campManager.RationsRemaining}";
        LogEvent($"[Привал] +{result.HpRestored:F0} HP{(result.ArmorRestored > 0f ? $", +{result.ArmorRestored:F0} физ. защиты" : string.Empty)}. Осталось рационов: {campManager.RationsRemaining}.");

        yield return WaitForClick(campContinueButton);
    }

    IEnumerator TryPlayCampSceneAfterRation()
    {
        if (campSceneTriggeredThisRun || characterManager?.Character == null || Random.value >= 0.10f) yield break;

        string characterId = characterManager.Character.characterId;
        string sceneId = null;
        bool highRelationship = saveManager.GetRelationshipLevel(characterId) >= SaveManager.MaxRelationshipLevel;
        if (string.Equals(characterId, "jennifer", System.StringComparison.OrdinalIgnoreCase))
        {
            sceneId = highRelationship ? "jennifer_camp_high" : "jennifer_camp_low";
        }
        else if (string.Equals(characterId, "violet", System.StringComparison.OrdinalIgnoreCase))
        {
            sceneId = highRelationship ? "violet_camp_high" : "violet_camp_low";
        }
        else if (string.Equals(characterId, "sasha", System.StringComparison.OrdinalIgnoreCase))
        {
            sceneId = highRelationship ? "sasha_camp_high" : "sasha_camp_low";
        }

        if (string.IsNullOrWhiteSpace(sceneId) || saveManager.HasSeenVNScene(characterId, sceneId)) yield break;
        campSceneTriggeredThisRun = true;
        if (!TryPlayRunVNScene(sceneId)) yield break;
        while (vnManager != null && vnManager.IsPlaying) yield return null;
    }
}
