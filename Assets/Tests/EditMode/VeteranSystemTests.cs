using System;
using NUnit.Framework;

public class VeteranSystemTests
{
    [Test]
    public void GradeForFloors_FullClear_ReturnsSPlus()
    {
        Assert.AreEqual("S+", VeteranSystem.GradeForFloors(DungeonManager.TotalFloors));
    }

    [Test]
    public void GradeForFloors_ZeroFloors_ReturnsCMinus()
    {
        Assert.AreEqual("C-", VeteranSystem.GradeForFloors(0));
    }

    [Test]
    public void IsEligibleMentor_SameCharacterId_ReturnsFalse()
    {
        var veteran = new VeteranCharacter { characterId = "jennifer", floorsCleared = 3, uniquePassiveSkillName = "Полевой ремонт" };

        Assert.IsFalse(VeteranSystem.IsEligibleMentor(veteran, "jennifer"));
    }

    [Test]
    public void IsEligibleMentor_DifferentCharacterIdAndCleared_ReturnsTrue()
    {
        var veteran = new VeteranCharacter { characterId = "jennifer", floorsCleared = 3, uniquePassiveSkillName = "Полевой ремонт" };

        Assert.IsTrue(VeteranSystem.IsEligibleMentor(veteran, "violet"));
    }

    [Test]
    public void RollTransferredSkills_AlwaysIncludesUniquePassiveFirst()
    {
        var veteran = new VeteranCharacter { characterId = "jennifer", floorsCleared = 1, uniquePassiveSkillName = "Полевой ремонт" };

        var result = VeteranSystem.RollTransferredSkills(veteran, new Random(42));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Полевой ремонт", result[0]);
    }
}
