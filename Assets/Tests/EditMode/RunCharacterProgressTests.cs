using NUnit.Framework;
using UnityEngine;

public class RunCharacterProgressTests
{
    CharacterData character;
    PassiveSkillData sturdySkill;

    [SetUp]
    public void SetUp()
    {
        character = ScriptableObject.CreateInstance<CharacterData>();
        sturdySkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        sturdySkill.skillName = SkillEffectMap.Sturdy;
        sturdySkill.skillId = SkillId.Sturdy;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(character);
        Object.DestroyImmediate(sturdySkill);
    }

    [Test]
    public void GetSkillLevel_KnownSkillId_ReturnsStoredLevel()
    {
        var progress = new RunCharacterProgress(character);
        progress.KnownSkillLevels[sturdySkill] = 3;

        Assert.AreEqual(3, progress.GetSkillLevel(SkillId.Sturdy));
    }

    [Test]
    public void GetSkillLevel_UnknownSkillId_ReturnsZero()
    {
        var progress = new RunCharacterProgress(character);
        Assert.AreEqual(0, progress.GetSkillLevel(SkillId.Berserk));
    }

    [Test]
    public void GetEffectiveUniquePassiveLevel_MentorSkillMatchesByLegacyName_ReturnsMentorLevel()
    {
        var progress = new RunCharacterProgress(character)
        {
            MentorUniquePassiveSkillName = SkillEffectMap.Shadow,
            MentorUniquePassiveLevel = 2
        };

        Assert.AreEqual(2, progress.GetEffectiveUniquePassiveLevel(SkillId.Shadow));
    }
}
