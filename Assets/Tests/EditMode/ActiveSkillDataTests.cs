using NUnit.Framework;
using UnityEditor;

public class ActiveSkillDataTests
{
    [Test]
    public void ThreeQuickStrikes_DefaultsToCooldownType()
    {
        var data = AssetDatabase.LoadAssetAtPath<ActiveSkillData>(
            "Assets/ScriptableObjects/Skills/Unique/Skill_ThreeQuickStrikes.asset");
        Assert.AreEqual(ActiveSkillType.Cooldown, data.skillType);
    }

    [Test]
    public void SmokeBomb_DefaultsToCooldownType()
    {
        var data = AssetDatabase.LoadAssetAtPath<ActiveSkillData>(
            "Assets/ScriptableObjects/Skills/Unique/Skill_SmokeBomb.asset");
        Assert.AreEqual(ActiveSkillType.Cooldown, data.skillType);
    }

    [Test]
    public void Berserk_IsToggleType()
    {
        var data = AssetDatabase.LoadAssetAtPath<ActiveSkillData>(
            "Assets/ScriptableObjects/Skills/Unique/Skill_Berserk.asset");
        Assert.AreEqual(ActiveSkillType.Toggle, data.skillType);
    }
}
