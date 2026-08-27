using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 9.2/9.3: персистентность мета-прогрессии между сессиями — JSON в Application.persistentDataPath.
// Единственный источник правды для валют/уровней зданий/гача-копий/ветеранов (Фаза 5+).
// ФИКС (Codex P2 2026-08-27): SaveGame() теперь пишет во временный файл и атомарно заменяет
// основной (File.Replace/Move) вместо прямой перезаписи — обрыв процесса посреди записи больше не
// может повредить единственный save. Операции, меняющие несколько полей (апгрейд здания и т.п.),
// мутируют Data в памяти одной транзакцией и вызывают SaveGame() РОВНО ОДИН РАЗ — раньше апгрейд
// здания списывал валюту и сохранял, затем повышал уровень и сохранял снова, что при сбое между
// двумя записями могло списать валюту без апгрейда.
public class SaveManager : MonoBehaviour
{
    const string SaveFileName = "dungeongirls_save.json";

    public SaveData Data { get; private set; } = new SaveData();

    string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);
    string TempSavePath => SavePath + ".tmp";

    void Awake()
    {
        LoadGame();
    }

    public void LoadGame()
    {
        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                Data = JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
                MigrateIfNeeded(Data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] Не удалось прочитать сохранение ({SavePath}): {e.Message}. Начинаем с чистого прогресса.");
                Data = new SaveData();
            }
        }
        else
        {
            Data = new SaveData();
        }
    }

    // 9.4 (ФИКС, Codex P2 2026-08-27): минимальная миграция по saveVersion — а не полный
    // downgrade/reset прогресса на несовпадении версии. JsonUtility молча оставляет отсутствующие
    // в старом JSON поля в их C#-дефолте при десериализации (для List<T> это null, не пустой
    // список — в отличие от инициализатора поля в объявлении класса, который применяется только
    // при `new SaveData()`, не при FromJson) — поэтому здесь только НОРМАЛИЗУЮТСЯ потенциально-null
    // коллекции до пустых списков и проставляется текущая версия. Числовые поля (metaCurrency и
    // т.д.) при отсутствии в JSON уже корректно остаются 0 через JsonUtility без нашего участия.
    public static void MigrateIfNeeded(SaveData data)
    {
        if (data.veteranDeck == null) data.veteranDeck = new List<VeteranCharacter>();
        if (data.gachaOwnedCharacters == null) data.gachaOwnedCharacters = new List<KeyCountEntry>();
        if (data.characterRunCounts == null) data.characterRunCounts = new List<KeyCountEntry>();
        if (data.seenVNScenes == null) data.seenVNScenes = new List<CharacterSceneList>();

        data.saveVersion = SaveData.CurrentSaveVersion;
    }

    public void SaveGame()
    {
        string json = JsonUtility.ToJson(Data, true);

        // Атомарная запись: пишем во временный файл рядом, затем заменяем основной за одну
        // файловую операцию. File.Replace требует существующий целевой файл — на самом первом
        // сохранении (SavePath ещё не существует) используем File.Move как эквивалент.
        File.WriteAllText(TempSavePath, json);
        if (File.Exists(SavePath))
        {
            File.Replace(TempSavePath, SavePath, null);
        }
        else
        {
            File.Move(TempSavePath, SavePath);
        }
    }

    // ==================== Мета-валюта / гача-валюта (8.5) ====================

    public void AddMetaCurrency(int amount)
    {
        Data.metaCurrency += amount;
        SaveGame();
    }

    public void AddGachaCurrency(int amount)
    {
        Data.gachaCurrency += amount;
        SaveGame();
    }

    public bool TrySpendMetaCurrency(int amount)
    {
        if (Data.metaCurrency < amount) return false;
        Data.metaCurrency -= amount;
        SaveGame();
        return true;
    }

    public bool TrySpendGachaCurrency(int amount)
    {
        if (Data.gachaCurrency < amount) return false;
        Data.gachaCurrency -= amount;
        SaveGame();
        return true;
    }

    // ==================== Здания деревни (8.1) ====================

    public int GetBuildingLevel(BuildingType building)
    {
        switch (building)
        {
            case BuildingType.Forge: return Data.forgeLevel;
            case BuildingType.Temple: return Data.templeLevel;
            case BuildingType.Tavern: return Data.tavernLevel;
            default: return 0;
        }
    }

    void SetBuildingLevel(BuildingType building, int level)
    {
        switch (building)
        {
            case BuildingType.Forge: Data.forgeLevel = level; break;
            case BuildingType.Temple: Data.templeLevel = level; break;
            case BuildingType.Tavern: Data.tavernLevel = level; break;
        }
    }

    // ФИКС (Codex P2 2026-08-27): списание валюты и повышение уровня теперь одна транзакция в
    // памяти с ОДНИМ вызовом SaveGame() в конце — раньше это были два отдельных TrySpendMetaCurrency
    // (со своим SaveGame внутри) + SetBuildingLevel + SaveGame, т.е. 2 записи на диск с окном сбоя
    // между ними, где валюта уже списана, а уровень ещё не повышен.
    public bool TryUpgradeBuilding(BuildingType building)
    {
        int level = GetBuildingLevel(building);
        if (level >= BuildingCatalog.MaxLevel) return false;

        int cost = BuildingCatalog.UpgradeCost(level);
        if (Data.metaCurrency < cost) return false;

        Data.metaCurrency -= cost;
        SetBuildingLevel(building, level + 1);
        SaveGame();
        return true;
    }

    // ==================== Гача (8.5/11.1) ====================

    // ФИКС (Codex P2 2026-08-27): ключ — стабильный CharacterData.characterId, не отображаемое имя.
    public int GetCharacterCopies(string characterId) => FindEntry(Data.gachaOwnedCharacters, characterId)?.count ?? 0;

    public void AddCharacterCopy(string characterId)
    {
        FindOrCreateEntry(Data.gachaOwnedCharacters, characterId).count++;
        SaveGame();
    }

    // ==================== Ветераны / прохождения (9.2, раздел завершения забега) ====================

    // ФИКС (Codex P2 2026-08-27): добавление ветерана и инкремент счётчика прохождений — одна
    // транзакция в памяти, один SaveGame() — раньше этой пары не существовало вовсе (см. Task 8).
    public void AddVeteranAndIncrementRunCount(VeteranCharacter veteran)
    {
        Data.veteranDeck.Add(veteran);
        FindOrCreateEntry(Data.characterRunCounts, veteran.characterId).count++;
        SaveGame();
    }

    public int GetRunCount(string characterId) => FindEntry(Data.characterRunCounts, characterId)?.count ?? 0;

    // 7.1: кнопка «Сбросить прогресс» в хабе — полностью очищает SaveData (мета-валюта,
    // гача-валюта, уровни зданий, гача-данные, колода ветеранов, счётчики прохождений/отношений,
    // открытые ВН-сцены), возвращая игру в состояние первого запуска.
    public void ResetProgress()
    {
        Data = new SaveData();
        SaveGame();
    }

    static KeyCountEntry FindEntry(List<KeyCountEntry> list, string key) => list.Find(e => e.key == key);

    static KeyCountEntry FindOrCreateEntry(List<KeyCountEntry> list, string key)
    {
        var entry = FindEntry(list, key);
        if (entry == null)
        {
            entry = new KeyCountEntry { key = key, count = 0 };
            list.Add(entry);
        }
        return entry;
    }
}
