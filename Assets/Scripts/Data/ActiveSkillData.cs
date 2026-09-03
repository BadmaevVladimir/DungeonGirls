using UnityEngine;

public enum ActiveSkillType
{
    Cooldown,
    Toggle
}

[CreateAssetMenu(fileName = "NewActiveSkill", menuName = "DungeonGirls/Active Skill")]
public class ActiveSkillData : ScriptableObject
{
    public string skillName;
    public SkillId skillId;

    [TextArea(3, 10)]
    public string effectDescription;

    public int maxLevel;
    public float cooldownSeconds; // Toggle-скиллы (см. skillType) это поле игнорируют.
    public ActiveSkillTargetType targetType;

    // Активные-скилы-панель (2026-09-03): Cooldown — уходит в кулдаун и авто-кастуется, если
    // включён авто-режим; Toggle — ручной вкл/выкл без кулдауна (например "Берсерк").
    public ActiveSkillType skillType;
    public Sprite icon;
}
