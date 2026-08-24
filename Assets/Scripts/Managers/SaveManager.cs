using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 9.2/9.3: персистентность мета-прогрессии между сессиями — JSON в Application.persistentDataPath.
// Единственный источник правды для валют/уровней зданий/гача-копий (Фаза 5).
public class SaveManager : MonoBehaviour
{
    const string SaveFileName = "dungeongirls_save.json";

    public SaveData Data { get; private set; } = new SaveData();

    string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

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

    public void SaveGame()
    {
        string json = JsonUtility.ToJson(Data, true);
        File.WriteAllText(SavePath, json);
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

    public bool TryUpgradeBuilding(BuildingType building)
    {
        int level = GetBuildingLevel(building);
        if (level >= BuildingCatalog.MaxLevel) return false;

        int cost = BuildingCatalog.UpgradeCost(level);
        if (!TrySpendMetaCurrency(cost)) return false;

        SetBuildingLevel(building, level + 1);
        SaveGame();
        return true;
    }

    // ==================== Гача (8.5/9.2, раздел 0) ====================

    public int GetCharacterCopies(string characterName) => FindEntry(Data.characterCopies, characterName)?.count ?? 0;

    public void AddCharacterCopy(string characterName)
    {
        FindOrCreateEntry(Data.characterCopies, characterName).count++;
        SaveGame();
    }

    public int GetItemCount(string itemName) => FindEntry(Data.gachaItemCounts, itemName)?.count ?? 0;

    public void AddItemCopy(string itemName)
    {
        FindOrCreateEntry(Data.gachaItemCounts, itemName).count++;
        SaveGame();
    }

    // 7.1: кнопка «Сбросить прогресс» в хабе — полностью очищает SaveData (мета-валюта,
    // гача-валюта, уровни зданий, гача-копии), возвращая игру в состояние первого запуска.
    // Нужна для удобства баланс-тестирования (см. ГДД 7.1).
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
