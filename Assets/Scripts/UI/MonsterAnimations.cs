using UnityEngine;

// (доп.): диспетчер idle/атака по MonsterData.monsterName (Russian) для 10 обычных монстров —
// аналог PlayableCharacterAnimations, но ключ берётся из CombatantRuntime.MonsterAnimationKey
// (немодифицированное monsterName, стабильное при префиксах-модификаторах вроде "Бронебойный",
// см. CombatantRuntime), а не DisplayName. Босс ("Страж"/The Warden) сюда не входит — у него нет
// PixelLab-анимации, он остаётся на существующей системе статичного/фазового спрайта
// (BossEncounterState — см. RunFlowController.Combat.cs), поэтому Idle/Attack/AttackFps
// возвращают null/0 для нераспознанных ключей.
public static class MonsterAnimations
{
    struct Entry
    {
        public string FolderKey;
        public int IdleCount;
        public int AttackCount;
        public float AttackFps;

        public Entry(string folderKey, int idleCount, int attackCount, float attackFps)
        {
            FolderKey = folderKey;
            IdleCount = idleCount;
            AttackCount = attackCount;
            AttackFps = attackFps;
        }
    }

    static Entry? Lookup(string monsterAnimationKey) => monsterAnimationKey switch
    {
        "Летучая мышь" => new Entry("Bat", 4, 5, 12f),
        "Рыцарь тьмы" => new Entry("DarkKnight", 4, 9, 10f),
        "Жрец тьмы" => new Entry("DarkPriest", 4, 9, 9f),
        "Гоблин-вор" => new Entry("GoblinThief", 4, 7, 9f),
        "Гарпия" => new Entry("Harpy", 4, 7, 10f),
        "Коррозийный паук" => new Entry("PoisonSpiderling", 4, 7, 10f),
        "Скелет" => new Entry("Skeleton", 4, 9, 10f),
        "Слизь" => new Entry("Slime", 4, 11, 8f),
        "Каменный страж" => new Entry("StoneGuardian", 4, 13, 8f),
        "Колдун" => new Entry("Warlock", 4, 9, 10f),
        _ => null
    };

    public static Sprite[] Idle(string monsterAnimationKey)
    {
        var entry = Lookup(monsterAnimationKey);
        if (entry == null) return null;
        return MonsterAnimationFrames.Idle(entry.Value.FolderKey, entry.Value.IdleCount);
    }

    public static Sprite[] Attack(string monsterAnimationKey)
    {
        var entry = Lookup(monsterAnimationKey);
        if (entry == null) return null;
        return MonsterAnimationFrames.Attack(entry.Value.FolderKey, entry.Value.AttackCount);
    }

    public static float AttackFps(string monsterAnimationKey)
    {
        var entry = Lookup(monsterAnimationKey);
        return entry?.AttackFps ?? 10f;
    }
}
