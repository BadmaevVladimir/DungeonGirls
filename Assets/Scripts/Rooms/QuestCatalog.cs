using System.Collections.Generic;

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
        SkipText = "Дженифер решает не искушать судьбу и проходит мимо."
    };

    public static readonly QuestDefinition[] All = { Sphinx, FairyRing, SwordInStone };
}
