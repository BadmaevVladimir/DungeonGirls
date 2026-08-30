using NUnit.Framework;

public class SkillEffectMapTests
{
    [Test]
    public void ResolveId_KnownCharacterSkillName_ReturnsMatchingId()
    {
        Assert.AreEqual(SkillId.Berserk, SkillEffectMap.ResolveId(SkillEffectMap.Berserk));
        Assert.AreEqual(SkillId.Sturdy, SkillEffectMap.ResolveId(SkillEffectMap.Sturdy));
    }

    [Test]
    public void ResolveId_UnknownName_ReturnsNone()
    {
        Assert.AreEqual(SkillId.None, SkillEffectMap.ResolveId("не существует"));
        Assert.AreEqual(SkillId.None, SkillEffectMap.ResolveId(null));
    }

    [Test]
    public void MonsterResolveId_KnownMonsterSkillName_ReturnsMatchingId()
    {
        Assert.AreEqual(SkillId.MonsterCorrosion, MonsterSkillEffectMap.ResolveId(MonsterSkillEffectMap.Corrosion));
    }

    [Test]
    public void MonsterResolveId_UnknownName_ReturnsNone()
    {
        Assert.AreEqual(SkillId.None, MonsterSkillEffectMap.ResolveId("не существует"));
    }
}
