using System.Collections.Generic;
using UnityEngine;

// Держит живое состояние персонажа в текущем забеге: прогресс уровня/навыков (3.5),
// боевой рантайм (HP/защита/оружие, см. Фаза 3) и временные модификаторы от ловушек/квестов.
public class CharacterManager : MonoBehaviour
{
    public CharacterData Character;
    public RunCharacterProgress Progress { get; private set; }
    public CombatantRuntime Combatant { get; private set; }
    public RunModifiers Modifiers { get; private set; } = new RunModifiers();
    public int RunCurrency { get; private set; } // 5.2/8.2: валюта забега, обнуляется в конце забега

    public int CurrentHP => Mathf.CeilToInt(Combatant != null ? Combatant.CurrentHP : 0f);
    public int Level => Progress != null ? Progress.Level : 1;

    public void BeginRun(CharacterData character)
    {
        Character = character;
        Progress = new RunCharacterProgress(character);
        Modifiers = new RunModifiers();
        RunCurrency = 0;
        Combatant = CombatantFactory.CreatePlayerCombatant(character, Progress.Level, Progress);
    }

    public void AddCurrency(int amount)
    {
        RunCurrency += amount;
    }

    // Пересобирает боевые статы персонажа (после левел-апа/нового навыка), сохраняя текущее
    // HP/физ. защиту относительно старого максимума, а не сбрасывая их к полному (3.1: левел-ап
    // "даёт" дополнительное здоровье, а не лечит целиком; аналогично трактуем прирост макс. защиты
    // от новых навыков — см. решение по этому вопросу в отчёте по Фазе 4).
    public void RefreshCombatStats()
    {
        float oldMaxHP = Combatant.MaxHP;
        float oldCurrentHP = Combatant.CurrentHP;
        float oldDefenseMax = Combatant.PhysicalDefenseMax;
        float oldDefenseCurrent = Combatant.PhysicalDefenseCurrent;

        var rebuilt = CombatantFactory.CreatePlayerCombatant(Character, Progress.Level, Progress);

        rebuilt.CurrentHP = Mathf.Clamp(oldCurrentHP + (rebuilt.MaxHP - oldMaxHP), 0f, rebuilt.MaxHP);
        rebuilt.PhysicalDefenseCurrent = Mathf.Clamp(oldDefenseCurrent + (rebuilt.PhysicalDefenseMax - oldDefenseMax), 0f, rebuilt.PhysicalDefenseMax);

        Combatant = rebuilt;
    }

    public bool IsAlive => Combatant != null && Combatant.IsAlive;

    public void ApplyDirectDamage(float amount)
    {
        Combatant.CurrentHP = Mathf.Max(0f, Combatant.CurrentHP - amount);
    }

    public void ApplyDirectArmorLoss(float amount)
    {
        Combatant.PhysicalDefenseCurrent = Mathf.Max(0f, Combatant.PhysicalDefenseCurrent - amount);
    }

    public List<int> GrantExperience(RewardManager rewardManager, ExperienceSource source)
    {
        return rewardManager.GrantExperience(Progress, source);
    }
}
