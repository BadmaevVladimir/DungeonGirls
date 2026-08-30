using NUnit.Framework;

public class CombatManagerTests
{
    [Test]
    public void ResolveActiveSkillHitCount_Rogue_ReturnsZero()
    {
        Assert.AreEqual(0, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Rogue));
    }

    [Test]
    public void ResolveActiveSkillHitCount_NonRogue_ReturnsThree()
    {
        Assert.AreEqual(3, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Warrior));
        Assert.AreEqual(3, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Barbarian));
    }
}
