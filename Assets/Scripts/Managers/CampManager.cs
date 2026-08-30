using UnityEngine;

public class CampManager : MonoBehaviour
{
    public const int StartingRations = 5;
    public int RationsRemaining { get; private set; }

    public void BeginRun(int tavernLevel = 0) => RationsRemaining = StartingRations + BuildingCatalog.TavernRationsBonus(tavernLevel);
    public bool CanCamp => RationsRemaining > 0;

    public struct CampResult
    {
        public float HpRestored;
        public float ArmorRestored;
    }

    public bool TrySpendRation()
    {
        if (!CanCamp) return false;
        RationsRemaining--;
        return true;
    }

    public void AddRations(int amount)
    {
        if (amount > 0) RationsRemaining += amount;
    }

    // Трата рациона отделена от лечения: ВН-сцена привала запускается после траты, но до HP.
    public CampResult RestoreAtCamp(CharacterManager characterManager, float healMultiplier = 1f)
    {
        var combatant = characterManager.Combatant;
        float basePercent = 0.5f + BuildingCatalog.TavernCampHealBonusPercent(characterManager.TavernLevelThisRun) / 100f;
        float hpBefore = combatant.CurrentHP;
        combatant.CurrentHP = Mathf.Min(combatant.MaxHP, combatant.CurrentHP + combatant.MaxHP * basePercent * healMultiplier);
        return new CampResult
        {
            HpRestored = combatant.CurrentHP - hpBefore,
            ArmorRestored = RestoreCampArmor(characterManager)
        };
    }

    // Совместимый метод для существующих вызовов: тратит рацион, затем лечит.
    public CampResult RestAtCamp(CharacterManager characterManager, float healMultiplier = 1f) =>
        TrySpendRation() ? RestoreAtCamp(characterManager, healMultiplier) : default;

    // «Горячие источники»: бесплатно восстанавливают HP полностью, но не броню.
    public float RestoreFullHealth(CharacterManager characterManager)
    {
        var combatant = characterManager.Combatant;
        float hpBefore = combatant.CurrentHP;
        combatant.CurrentHP = combatant.MaxHP;
        return combatant.CurrentHP - hpBefore;
    }

    float RestoreCampArmor(CharacterManager characterManager)
    {
        var combatant = characterManager.Combatant;
        int fieldRepairLevel = characterManager.Progress.GetEffectiveUniquePassiveLevel(SkillId.FieldRepair);
        float totalRepairPercent = (fieldRepairLevel > 0 ? fieldRepairLevel * 10f : 0f) +
            ItemEffectBalance.RepairCampArmorPercent(combatant.ItemRepairLevel) + BuildingCatalog.ForgeCampArmorRestorePercent(characterManager.ForgeLevelThisRun);
        if (totalRepairPercent <= 0f) return 0f;

        float armorBefore = combatant.PhysicalDefenseCurrent;
        combatant.PhysicalDefenseCurrent = Mathf.Min(combatant.PhysicalDefenseMax,
            combatant.PhysicalDefenseCurrent + combatant.PhysicalDefenseMax * BalanceClamps.ClampArmorRestorePercent(totalRepairPercent) / 100f);
        return combatant.PhysicalDefenseCurrent - armorBefore;
    }
}
