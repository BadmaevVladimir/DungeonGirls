using UnityEngine;

public class CampManager : MonoBehaviour
{
    // 1, п.1: стартовое подземелье даёт +5 рационов на забег (единственное подземелье прототипа).
    public const int StartingRations = 5;

    public int RationsRemaining { get; private set; }

    public void BeginRun()
    {
        RationsRemaining = StartingRations;
    }

    public bool CanCamp => RationsRemaining > 0;

    public struct CampResult
    {
        public float HpRestored;
        public float ArmorRestored;
    }

    // 6.2: восстанавливает 50% от максимума здоровья (черновое значение) + часть физ. защиты от
    // "Полевого ремонта" (3.1), клампится потолком 8.6. Тратит 1 рацион (6.1).
    public CampResult RestAtCamp(CharacterManager characterManager, float healMultiplier = 1f)
    {
        RationsRemaining = Mathf.Max(0, RationsRemaining - 1);

        var combatant = characterManager.Combatant;
        float healPercent = 0.5f * healMultiplier;
        float hpBefore = combatant.CurrentHP;
        combatant.CurrentHP = Mathf.Min(combatant.MaxHP, combatant.CurrentHP + combatant.MaxHP * healPercent);

        float armorRestored = 0f;
        int fieldRepairLevel = characterManager.Progress.UniquePassiveLevel;
        // "Ремонт" (3.10, Молот кузнеца): +1% за уровень предмета, складывается с "Полевым ремонтом".
        float itemRepairPercent = combatant.ItemRepairLevel * 1f;
        float fieldRepairPercent = fieldRepairLevel > 0 ? fieldRepairLevel * 10f : 0f; // "Полевой ремонт": 10/20/30/40/50%
        float totalRepairPercent = fieldRepairPercent + itemRepairPercent;
        if (totalRepairPercent > 0f)
        {
            float clampedPercent = BalanceClamps.ClampArmorRestorePercent(totalRepairPercent);
            float armorBefore = combatant.PhysicalDefenseCurrent;
            combatant.PhysicalDefenseCurrent = Mathf.Min(combatant.PhysicalDefenseMax, combatant.PhysicalDefenseCurrent + combatant.PhysicalDefenseMax * clampedPercent / 100f);
            armorRestored = combatant.PhysicalDefenseCurrent - armorBefore;
        }

        return new CampResult
        {
            HpRestored = combatant.CurrentHP - hpBefore,
            ArmorRestored = armorRestored
        };
    }
}
