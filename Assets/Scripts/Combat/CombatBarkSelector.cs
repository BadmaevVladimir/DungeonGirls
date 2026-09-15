using System;

// Реплики в бою: выбор фразы под повод и героиню. Чистая функция без Unity-состояния — кит
// приходит снаружи, наружу уходит запись или null, поэтому вся таблица решений покрывается
// EditMode-тестами без сцены (см. CombatBarkSelectorTests).
public static class CombatBarkSelector
{
    // Точное совпадение по characterId перекрывает общую запись (characterId пуст). Записи без
    // единой строки пропускаются: это недозаполненный ассет, и перекрывать собой общую фразу он
    // не должен — иначе опечатка в редакторе молча превращает бой в немой.
    public static EncounterBark Select(
        BossKitData kit,
        string characterId,
        EncounterBarkTrigger trigger,
        int phaseIndex)
    {
        if (kit == null) return null;

        EncounterBark generic = null;
        foreach (var bark in kit.encounterBarks)
        {
            if (bark == null || bark.trigger != trigger) continue;

            // phaseIndex осмыслен только для смены фазы: у входа в бой фаза всегда стартовая.
            if (trigger == EncounterBarkTrigger.PhaseChanged && bark.phaseIndex != phaseIndex) continue;

            if (string.IsNullOrWhiteSpace(bark.heroLine) && string.IsNullOrWhiteSpace(bark.bossLine)) continue;

            if (!string.IsNullOrWhiteSpace(bark.characterId))
            {
                if (string.Equals(bark.characterId, characterId, StringComparison.OrdinalIgnoreCase))
                {
                    return bark;
                }

                continue;
            }

            if (generic == null) generic = bark;
        }

        return generic;
    }

    // Единственная точка, решающая «звучит ли реплика сейчас и чьим голосом». Держит вместе три
    // условия — есть ли что сказать, не говорили ли уже в этом забеге, кто говорит, — чтобы они
    // не разъехались по вызывающим сторонам. Отметка в памяти забега ставится только вместе с
    // готовой заявкой: бой без фраз не должен «съедать» реплику, которую допишут позже.
    public static bool TryBuildRequest(
        BossKitData kit,
        string characterId,
        MonsterData boss,
        RunCharacterProgress progress,
        EncounterBarkTrigger trigger,
        int phaseIndex,
        out BarkRequest request)
    {
        request = default;
        if (progress == null || boss == null) return false;

        var bark = Select(kit, characterId, trigger, phaseIndex);
        if (bark == null) return false;

        // Голос на сцене один: если заполнены обе строки, говорит героиня — бой её.
        var speaker = !string.IsNullOrWhiteSpace(bark.heroLine) ? BarkSpeaker.Hero : BarkSpeaker.Boss;
        var text = speaker == BarkSpeaker.Hero ? bark.heroLine : bark.bossLine;

        if (!progress.TryMarkBarkShown(boss, trigger, phaseIndex)) return false;

        request = new BarkRequest(speaker, text, trigger, phaseIndex);
        return true;
    }
}
