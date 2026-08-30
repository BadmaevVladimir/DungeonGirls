using System.Collections.Generic;
using UnityEngine;

// 5.4: примеры квестов Дженифер из ГДД. Квесты персонаж-специфичны — общего пула нет.
public static class QuestCatalog
{
    public static readonly QuestDefinition Sphinx = new QuestDefinition
    {
        Name = "Загадка сфинкса",
        InteractionType = QuestInteractionType.MultipleChoice,
        DescriptionText = "При входе в комнату Дженифер натыкается на сфинкса, который загадывает классическую загадку: «Что ходит на четырёх ногах утром, на двух — днём и на трёх — вечером?»",
        Choices = new List<QuestChoiceOption>
        {
            new QuestChoiceOption { ButtonText = "Человек", OutcomeText = "Правильно! Сфинкс удовлетворённо кивает и исчезает, оставляя после себя лёгкое ощущение удачи — в следующем бою будет бонус к деньгам.", IsCorrect = true },
            new QuestChoiceOption { ButtonText = "Собака", OutcomeText = "Сфинкс недовольно щёлкает зубами — неверно. В следующем бою из сундука не будет валюты.", IsCorrect = false },
            new QuestChoiceOption { ButtonText = "Паук", OutcomeText = "Сфинкс недовольно щёлкает зубами — неверно. В следующем бою из сундука не будет валюты.", IsCorrect = false }
        }
    };

    public static readonly QuestDefinition FairyRing = new QuestDefinition
    {
        Name = "Круг фей",
        InteractionType = QuestInteractionType.TryOrSkip,
        Level = 5,
        DescriptionText = "В комнате обнаруживается круг, похожий на круг фей. Можно попытаться лечь там на отдых, или просто пройти мимо.",
        SuccessText = "Сон в кругу фей был на удивление спокойным и глубоким — этот привал восстановит больше здоровья.",
        FailText = "Всю ночь её мучали кошмары — этот привал восстановит только половину обычного объёма.",
        SkipText = "Дженифер решает не рисковать и проходит мимо круга."
    };

    public static readonly QuestDefinition SwordInStone = new QuestDefinition
    {
        Name = "Меч в камне",
        InteractionType = QuestInteractionType.TryOrSkip,
        Level = 10,
        DescriptionText = "В комнате стоит меч, воткнутый в камень. Можно попытаться его вытащить.",
        SuccessText = "Меч поддаётся! Получен предмет: Кровавый меч (Эпический).",
        FailText = "Дженифер теряет уверенность в себе — в следующем бою она наносит на 10% меньше урона.",
        SkipText = "Дженифер решает не искушать судьбу и проходит мимо.",
        SuccessRewardItemName = "Кровавый меч",
        SuccessRewardItemTier = ItemTier.Epic,
        SuccessRewardWeaponSubtype = WeaponSubtype.Sword
    };

    public static readonly QuestDefinition Hunt = new QuestDefinition
    {
        Name = "Добыча",
        InteractionType = QuestInteractionType.TryOrSkip,
        Level = 3,
        DescriptionText = "Во время исследования подземелья вам удалось заметить следы кабана, который каким-то образом забрался так глубоко. Если повезёт, его мясо можно будет использовать в качестве еды ещё некоторое время.",
        SuccessText = "Охота на кабана оказалась быстрой, а мясо, хоть и жёстким, но съедобным.",
        FailText = "Кабан оказался вам не по зубам. После короткого сражения он сбежал, оставив вам только шрамы на память.",
        SkipText = "Вы решаете не тратить на это время и продолжаете путь.",
        AttemptButtonText = "Пойти охотиться на кабана",
        SkipButtonText = "Не тратить на это время"
    };

    public static readonly QuestDefinition[] All = { Sphinx, FairyRing, SwordInStone, Hunt };

    // «Добыча» доступна со 2-го этажа, с шансом 20% среди квестов и максимум один раз за забег
    // (huntAlreadyTriggered управляется вызывающей стороной — этот метод только решает, не мутирует
    // состояние забега). Награда «Меча в камне» может быть успешно получена только один раз за
    // забег — после успеха возвращается другой полноценный квест вместо пустого исхода.
    public static QuestDefinition PickForFloor(int floor, bool huntAlreadyTriggered, bool swordAlreadySucceeded)
    {
        if (floor >= 2 && !huntAlreadyTriggered && Random.value < 0.20f)
        {
            return Hunt;
        }

        switch (floor)
        {
            case 1: return Sphinx;
            case 2: return FairyRing;
            default: return swordAlreadySucceeded ? FairyRing : SwordInStone;
        }
    }
}
