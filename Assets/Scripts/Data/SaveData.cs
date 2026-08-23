using System;
using System.Collections.Generic;

// 9.2/9.3: всё, что сохраняется между сессиями (для Фазы 5 — валюты, здания, гача-данные).
// Колода ветеранов/открытые ВН-сцены/счётчик прохождений — вне скоупа Фазы 5, не хранятся здесь.
[Serializable]
public class KeyCountEntry
{
    public string key;
    public int count;
}

[Serializable]
public class SaveData
{
    public int metaCurrency;
    public int gachaCurrency;

    public int forgeLevel;
    public int templeLevel;
    public int tavernLevel;

    public List<KeyCountEntry> characterCopies = new List<KeyCountEntry>();
    public List<KeyCountEntry> gachaItemCounts = new List<KeyCountEntry>();
}
