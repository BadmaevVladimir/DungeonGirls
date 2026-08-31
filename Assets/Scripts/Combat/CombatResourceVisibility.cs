// UI combat resources belong to the effects a character actually has, not only to their base class.
// This matters for mentor inheritance: for example, Jennifer can inherit Champion of the Tribe or
// a Barbarian class skill, and any character can enter Stealth through an inherited Rogue skill.
public static class CombatResourceVisibility
{
    public static bool ShouldShowRage(CharacterClass characterClass, CombatantRuntime combatant)
    {
        if (characterClass == CharacterClass.Barbarian)
        {
            return true;
        }

        return combatant != null &&
            (combatant.UniqueChampionOfTheTribeLevel > 0 ||
             combatant.SkillStubbornnessLevel > 0 ||
             combatant.SkillFrenzyLevel > 0 ||
             combatant.SkillIntimidationLevel > 0 ||
             combatant.SkillSuperstitionLevel > 0 ||
             combatant.RageFlatBonusPercent > 0f);
    }

    public static bool ShouldShowStealth(CombatantRuntime combatant) =>
        combatant != null && combatant.IsStealthed;
}
