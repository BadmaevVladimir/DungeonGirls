using System.Collections.Generic;

// План 9 (Зеркальный Двойник): выбор набора способностей босса по классу игрока. Вынесено в чистую
// функцию по образцу BossPoolSelector — так правило проверяется тестом без сцены, без фабрики и без
// живого CombatManager, а фабрика остаётся тупым исполнителем.
//
// BossEncounterState намеренно НИЧЕГО не знает о классах игрока: его задача — фазы и кулдауны.
// Поэтому ветвление живёт здесь, до создания энкаунтера, а не внутри кита.
public static class MirrorKitSelector
{
    // Возвращает кит, с которым босс выйдет на сцену. Без вариантов, с пустым списком или когда для
    // класса игрока ветки нет — это bossKit, то есть набор по умолчанию. Никогда не возвращает
    // вариант с пустым kit: такая запись в ассете означает недозаполненные данные, а не «без кита».
    public static BossKitData Select(MonsterData boss, CharacterClass playerClass)
    {
        if (boss == null)
        {
            return null;
        }

        var variants = boss.classVariantKits;
        if (variants == null)
        {
            return boss.bossKit;
        }

        for (int i = 0; i < variants.Count; i++)
        {
            var variant = variants[i];
            if (variant == null || variant.kit == null) continue;
            if (variant.playerClass != playerClass) continue;

            return variant.kit;
        }

        return boss.bossKit;
    }

    // Есть ли у босса ветки по классам вообще. Нужно вызывающей стороне, чтобы не тянуть класс
    // игрока туда, где он ни на что не влияет.
    public static bool HasClassVariants(MonsterData boss)
    {
        if (boss == null || boss.classVariantKits == null) return false;

        for (int i = 0; i < boss.classVariantKits.Count; i++)
        {
            if (boss.classVariantKits[i] != null && boss.classVariantKits[i].kit != null) return true;
        }

        return false;
    }
}
