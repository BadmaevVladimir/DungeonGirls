using System.Collections.Generic;

// Выбор босса этажа из пула. Чистая статика без UnityEngine.Random внутри: источник случайности
// приходит параметром (как ICombatRandom в CombatManager), поэтому логика тестируется без сцены и
// без плеймода. Всегда возвращает не-null, если fallback не-null, — вызывающая сторона не обязана
// проверять результат.
public static class BossPoolSelector
{
    // pickIndex получает КОЛИЧЕСТВО подходящих кандидатов и обязан вернуть индекс в [0, count).
    // null = обычный UnityEngine.Random.Range(0, count).
    public static MonsterData Select(BossPoolData pool, int floorNumber, MonsterData fallback,
        System.Func<int, int> pickIndex = null)
    {
        if (pool == null || pool.entries == null || pool.entries.Count == 0)
        {
            return fallback;
        }

        var candidates = new List<MonsterData>();
        foreach (var entry in pool.entries)
        {
            // Пустая строка в списке (boss == null) — обычная ситуация при ручном редактировании
            // ассета в инспекторе, это не ошибка данных: просто пропускаем.
            if (entry == null || entry.boss == null) continue;
            if (floorNumber < entry.minFloor || floorNumber > entry.maxFloor) continue;
            candidates.Add(entry.boss);
        }

        if (candidates.Count == 0)
        {
            return fallback;
        }

        int index = pickIndex != null
            ? pickIndex(candidates.Count)
            : UnityEngine.Random.Range(0, candidates.Count);
        index = UnityEngine.Mathf.Clamp(index, 0, candidates.Count - 1);
        return candidates[index];
    }
}
