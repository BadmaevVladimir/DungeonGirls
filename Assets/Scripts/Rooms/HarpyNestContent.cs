using System;
using System.Collections.Generic;

public static class HarpyNestContent
{
    public const string ContentKey = "trap:harpy-nest";
    public const string MonsterName = "Гарпия";
    public const string Title = "Гнездо гарпий";
    public const string Description =
        "Из пролома под потолком тянет холодным воздухом. На каменном уступе свито огромное гнездо из веток, костей и обрывков ткани. " +
        "Среди перьев лежит кладка крупных пятнистых яиц. Гарпии нигде не видно, но свежие следы когтей говорят, что хозяйка скоро вернётся. " +
        "Можно попытаться забрать несколько яиц или тихо уйти.";
    public const string Failure =
        "Под ногой хрустит кость. Сверху раздаётся пронзительный крик — хозяйка гнезда возвращается и бросается на вас. Приготовьтесь к бою!";
    public const string Skip =
        "Вы решаете не трогать кладку и уходите прежде, чем хозяйка гнезда вернётся.";
    public const string AttemptButton = "Украсть яйца";
    public const string SkipButton = "Не тревожить гнездо";

    public static bool HasHarpy(IReadOnlyList<MonsterData> monsters)
    {
        if (monsters == null) return false;
        for (int i = 0; i < monsters.Count; i++)
            if (monsters[i] != null && string.Equals(monsters[i].monsterName, MonsterName, StringComparison.Ordinal))
                return true;
        return false;
    }

    public static void ConfigureNode(FloorMapNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        node.ContentKey = ContentKey;
        node.ResolvedMonsterIds ??= new List<string>();
        node.ResolvedMonsterIds.Clear();
        node.ResolvedMonsterIds.Add(MonsterName);
    }

    public static string Success(int amount) =>
        "Стараясь не задеть хрупкие ветки, вы добираетесь до кладки и успеваете спрятать яйца до того, как над коридором раздаётся далёкий крик. " +
        $"Получено: яйца монстров ×{amount}.";

    public static string Victory(int amount) =>
        "Гарпия повержена. В разворошённом гнезде уцелело одно яйцо. " +
        $"Получено: яйцо монстра ×{amount}.";
}
