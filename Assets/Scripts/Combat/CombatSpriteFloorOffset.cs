using System;

// Компенсация "зависания" боевых спрайтов (2026-09-03) — один вызов на комбатанта (игрок/монстр/
// босс), определяет константный отступ снизу для ВСЕХ его кадров анимации сразу (не пересчитывается
// по кадрам — см. SpriteFloorOffsets/SpriteFloorScan, почему это важно для прыжковых анимаций).
// Диспетчеризация: анимированный босс — через таблицу по ключу Boss_<animationFolderKey> (его
// кадры лежат в Resources/, как у монстров), босс без анимации — floorPaddingFraction ТЕКУЩЕЙ фазы
// напрямую с BossPhaseData (статичный phaseSprite не в Resources/); игрок/обычный монстр — через
// таблицу SpriteFloorOffsets по ключу папки анимации (Jennifer/Sasha/Violet или Monster_<Key>).
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
            var phase = combatant.BossEncounter.CurrentPhase;
            // Анимированный босс (2026-09-05) показывает на сцене НЕ phaseSprite, а кадры из
            // Resources/CharacterAnimations/Boss_<animationFolderKey>/ — отступ надо брать по ним,
            // иначе компенсация считается по другой картинке (у Стража расхождение ~13px на рамке
            // 518px). Таблица для этих папок генерируется тем же анализатором, что и для монстров;
            // если записи нет (босс без анимации) — старый путь через floorPaddingFraction фазы.
            if (!string.IsNullOrEmpty(phase.animationFolderKey))
            {
                float animated = lookup($"Boss_{phase.animationFolderKey}");
                if (animated > 0f)
                {
                    return animated;
                }
            }

            return phase.floorPaddingFraction;
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
