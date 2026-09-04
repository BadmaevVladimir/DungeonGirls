using System.Collections.Generic;

public static class CombatAutoSkillPolicy
{
    // Product decision for hidden attestation: every toggle is active for the complete fight.
    // Keeping the rule generic makes future toggle skills behave consistently with Berserk.
    public static void EnableAlwaysOnToggles(CombatantRuntime player, IList<ActiveSkillRuntimeState> skills)
    {
        if (player == null || skills == null) return;

        for (int i = 0; i < skills.Count; i++)
        {
            var slot = skills[i];
            if (slot?.Data == null || slot.Data.skillType != ActiveSkillType.Toggle) continue;
            slot.IsToggleActive = true;
            if (slot.Data.skillId == SkillId.Berserk && player.UniqueBerserkLevel > 0)
                player.IsBerserkActive = true;
        }
    }

    public static void EnableAlwaysOnToggle(CombatantRuntime player, VeteranActiveSkillSnapshot skill)
    {
        if (player == null || skill == null || skill.skillType != ActiveSkillType.Toggle) return;
        if (skill.skillId == SkillId.Berserk && player.UniqueBerserkLevel > 0)
            player.IsBerserkActive = true;
    }
}
