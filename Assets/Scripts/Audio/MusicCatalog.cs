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
    Combat
}

public readonly struct MusicRequest
{
    public readonly MusicContextKind Kind;
    public readonly string CharacterId;
    public readonly bool IsBoss;

    MusicRequest(MusicContextKind kind, string characterId, bool isBoss)
    {
        Kind = kind;
        CharacterId = characterId;
        IsBoss = isBoss;
    }

    public static MusicRequest Silent() => new MusicRequest(MusicContextKind.Silent, null, false);
    public static MusicRequest Hub() => new MusicRequest(MusicContextKind.Hub, null, false);
    public static MusicRequest Combat(string characterId, bool isBoss) =>
        new MusicRequest(MusicContextKind.Combat, characterId, isBoss);
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
        switch (request.Kind)
        {
            case MusicContextKind.Hub:
                names.Add("Hub");
                break;

            case MusicContextKind.Combat:
                string id = Normalize(request.CharacterId);
                bool hasId = !string.IsNullOrEmpty(id);
                if (request.IsBoss)
                {
                    if (hasId) names.Add($"Combat_Boss_{id}");
                    names.Add("Combat_Boss");
                }
                if (hasId) names.Add($"Combat_{id}");
                names.Add("Combat");
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

    // Повторный запрос уже играющего трека не должен его перезапускать: экраны обновляются часто,
    // а перезапуск слышен как рывок и наложение.
    public static bool ShouldSwitch(string currentTrackName, string nextTrackName) =>
        !string.Equals(currentTrackName, nextTrackName, StringComparison.Ordinal);

    static string Normalize(string characterId) =>
        string.IsNullOrWhiteSpace(characterId) ? null : characterId.Trim().ToLowerInvariant();
}
