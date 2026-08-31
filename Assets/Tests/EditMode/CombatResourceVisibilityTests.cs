using NUnit.Framework;

public class CombatResourceVisibilityTests
{
    [Test]
    public void ShouldShowRage_JenniferWithInheritedChampion_ReturnsTrue()
    {
        var combatant = new CombatantRuntime { UniqueChampionOfTheTribeLevel = 1 };

        Assert.IsTrue(CombatResourceVisibility.ShouldShowRage(CharacterClass.Warrior, combatant));
    }

    [Test]
    public void ShouldShowRage_JenniferWithInheritedRageClassSkill_ReturnsTrue()
    {
        var combatant = new CombatantRuntime { SkillFrenzyLevel = 1 };

        Assert.IsTrue(CombatResourceVisibility.ShouldShowRage(CharacterClass.Warrior, combatant));
    }

    [Test]
    public void ShouldShowRage_WarriorWithoutRageMechanic_ReturnsFalse()
    {
        Assert.IsFalse(CombatResourceVisibility.ShouldShowRage(CharacterClass.Warrior, new CombatantRuntime()));
    }

    [Test]
    public void ShouldShowStealth_NonRogueInStealth_ReturnsTrue()
    {
        var combatant = new CombatantRuntime { IsStealthed = true };

        Assert.IsTrue(CombatResourceVisibility.ShouldShowStealth(combatant));
    }

    [Test]
    public void ShouldShowStealth_WhenEffectIsInactive_ReturnsFalse()
    {
        Assert.IsFalse(CombatResourceVisibility.ShouldShowStealth(new CombatantRuntime()));
    }
}
