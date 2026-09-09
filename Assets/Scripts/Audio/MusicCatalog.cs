using System;
using System.Collections.Generic;

// ГДД 10 (G10), W11: подбор музыкального трека под игровой контекст.
//
// Вся логика здесь чистая и не трогает Unity: контекст превращается в список имён-кандидатов от
// частного к общему, а решение «какой файл существует» принимает вызывающая сторона (MusicPlayer
// грузит из Resources). Это позволяет проверять правила подбора EditMode-тестами без единого
// AudioSource и без реальных треков в проекте.
//
// Треки привязаны ИМЕНЕМ ФАЙЛА, а не ссылкой в инспекторе (решение от 08.09.2026): достаточно
// положить wav в Assets/Resources/Music/ с именем по соглашению, править код и открывать Unity не
// нужно. Обратная сторона — опечатка в имени даёт тишину, а не ошибку сборки, поэтому MusicPlayer
// один раз пишет в лог, что ничего не нашёл.
public enum MusicContextKind
{
    Silent,
    Hub,
    Combat,
    // Отдельная музыка особой комнаты и события: включается временным override поверх основной
    // темы забега, см. MusicMixer.BeginOverride.
    SpecialRoom,
    Event
}

public readonly struct MusicRequest
{
    public readonly MusicContextKind Kind;

    // Уточняющий идентификатор контекста: героиня для боя, ключ комнаты/события — для остальных.
    public readonly string Id;

    public readonly bool IsBoss;

    // Прежнее имя поля: боевые вызовы читаются привычно и не переписывались при обобщении Id.
    public string CharacterId => Id;

    MusicRequest(MusicContextKind kind, string id, bool isBoss)
    {
        Kind = kind;
        Id = id;
        IsBoss = isBoss;
    }

    public static MusicRequest Silent() => new MusicRequest(MusicContextKind.Silent, null, false);
    public static MusicRequest Hub() => new MusicRequest(MusicContextKind.Hub, null, false);
    public static MusicRequest Combat(string characterId, bool isBoss) =>
        new MusicRequest(MusicContextKind.Combat, characterId, isBoss);

    // Музыка забега — это боевая тема героини, играющая непрерывно: на карте она звучит одним
    // минимальным слоем, в бою подмешиваются остальные. Отдельного трека и отдельного контекста
    // ей не нужно ровно поэтому — совпадение имени с Combat(hero, false) и есть механизм
    // «бой не перезапускает музыку» (см. MusicMixer.PlayContext).
    public static MusicRequest Run(string characterId) => Combat(characterId, false);

    public static MusicRequest SpecialRoom(string roomId) =>
        new MusicRequest(MusicContextKind.SpecialRoom, roomId, false);
    public static MusicRequest Event(string eventId) =>
        new MusicRequest(MusicContextKind.Event, eventId, false);
}

// Результат подбора: базовое имя трека (оно же ключ контекста) и список файлов, которые нужно
// запустить одновременно. Один элемент означает откат на контрольный микс, три — набор слоёв.
public readonly struct MusicTrackSet
{
    public readonly string ContextKey;
    public readonly IReadOnlyList<string> Layers;

    // Роль каждого слоя (harmony/drums/lead) — по ней MusicLayerMix считает громкость.
    // Для отката на полный микс роль одна: MusicLayerMix.FullMixLayer.
    public readonly IReadOnlyList<string> Roles;

    public bool IsSilent => Layers == null || Layers.Count == 0;
    public bool IsLayered => Layers != null && Layers.Count > 1;

    public MusicTrackSet(string contextKey, IReadOnlyList<string> layers, IReadOnlyList<string> roles)
    {
        ContextKey = contextKey;
        Layers = layers;
        Roles = roles;
    }

    public static readonly MusicTrackSet Silence = new MusicTrackSet(null, null, null);
}

public static class MusicCatalog
{
    public const string ResourceFolder = "Music";

    public static string ResourcePath(string trackName) => $"{ResourceFolder}/{trackName}";

    // От частного к общему. Порядок и есть правило: положив позже Combat_Boss.wav, дизайнер
    // получает отдельную музыку боссов во всех боях сразу, ничего не программируя.
    public static IReadOnlyList<string> CandidateNames(MusicRequest request)
    {
        var names = new List<string>();
        string id = Normalize(request.Id);
        bool hasId = !string.IsNullOrEmpty(id);

        switch (request.Kind)
        {
            case MusicContextKind.Hub:
                names.Add("Hub");
                break;

            case MusicContextKind.Combat:
                if (request.IsBoss)
                {
                    if (hasId) names.Add($"Combat_Boss_{id}");
                    names.Add("Combat_Boss");
                }
                if (hasId) names.Add($"Combat_{id}");
                names.Add("Combat");
                break;

            case MusicContextKind.SpecialRoom:
                if (hasId) names.Add($"Room_{id}");
                names.Add("Room");
                break;

            case MusicContextKind.Event:
                if (hasId) names.Add($"Event_{id}");
                names.Add("Event");
                break;
        }
        return names;
    }

    // Возвращает имя первого существующего кандидата или null, если нет ни одного. Null — штатный
    // результат: трека может ещё не быть, и это означает тишину, а не ошибку.
    public static string ResolveTrackName(MusicRequest request, Func<string, bool> exists)
    {
        if (exists == null) return null;
        var candidates = CandidateNames(request);
        for (int i = 0; i < candidates.Count; i++)
            if (exists(candidates[i])) return candidates[i];
        return null;
    }

    public static string LayerTrackName(string baseName, string role) => $"{baseName}_{role}";

    // Полный набор слоёв либо null. Неполный набор играть нельзя: два стема из трёх — это молча
    // другая аранжировка, а не тихая версия темы. Откат идёт на контрольный микс целиком
    // (ГДД «Инструмент — MCP-сервер openDAW и слои музыки», раздел «Отказоустойчивость»).
    public static IReadOnlyList<string> ResolveLayerSet(string baseName, Func<string, bool> exists)
    {
        if (string.IsNullOrEmpty(baseName) || exists == null) return null;
        var roles = MusicLayerMix.LayerRoles;
        var layers = new List<string>(roles.Count);
        for (int i = 0; i < roles.Count; i++)
        {
            string layerName = LayerTrackName(baseName, roles[i]);
            if (!exists(layerName)) return null;
            layers.Add(layerName);
        }
        return layers;
    }

    // Полный подбор: сначала базовое имя по цепочке уточнения, затем набор слоёв рядом с ним,
    // затем — откат на сам контрольный микс. Ключ контекста в обоих случаях один и тот же
    // (базовое имя), поэтому появление стемов в Resources не считается сменой контекста.
    public static MusicTrackSet Resolve(MusicRequest request, Func<string, bool> exists)
    {
        string baseName = ResolveTrackName(request, exists);
        if (baseName == null) return MusicTrackSet.Silence;

        var layers = ResolveLayerSet(baseName, exists);
        if (layers != null) return new MusicTrackSet(baseName, layers, MusicLayerMix.LayerRoles);

        return new MusicTrackSet(baseName, new[] { baseName }, new[] { MusicLayerMix.FullMixLayer });
    }

    // Повторный запрос уже играющего трека не должен его перезапускать: экраны обновляются часто,
    // а перезапуск слышен как рывок и наложение.
    public static bool ShouldSwitch(string currentTrackName, string nextTrackName) =>
        !string.Equals(currentTrackName, nextTrackName, StringComparison.Ordinal);

    static string Normalize(string id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Trim().ToLowerInvariant();
}
