using System;

// Компенсация "зависания" боевых спрайтов (2026-09-03) — один вызов на комбатанта (игрок/монстр/
// босс), определяет константный отступ снизу для ВСЕХ его кадров анимации сразу (не пересчитывается
// по кадрам — см. SpriteFloorOffsets/SpriteFloorScan, почему это важно для прыжковых анимаций).
// Диспетчеризация: босс — читает floorPaddingFraction ТЕКУЩЕЙ фазы напрямую с BossPhaseData (не
// через таблицу — боссовские спрайты не в Resources/); игрок/обычный монстр — через таблицу
// SpriteFloorOffsets по ключу папки анимации (Jennifer/Sasha/Violet или Monster_<Key>).
public static class CombatSpriteFloorOffset
{
    public static float GetOffsetFraction(CombatantRuntime combatant) =>
        GetOffsetFraction(combatant, SpriteFloorOffsets.GetOffsetFraction);

    public static float GetOffsetFraction(CombatantRuntime combatant, Func<string, float> lookup)
    {
        if (combatant == null)
        {
            return 0f;
        }

        if (combatant.BossEncounter != null)
        {
            return combatant.BossEncounter.CurrentPhase.floorPaddingFraction;
        }

        if (combatant.IsPlayer)
        {
            var key = PlayableCharacterAnimations.FolderKey(combatant.DisplayName);
            return key != null ? lookup(key) : 0f;
        }

        var monsterKey = MonsterAnimations.FolderKey(combatant.MonsterAnimationKey);
        return monsterKey != null ? lookup($"Monster_{monsterKey}") : 0f;
    }
}
