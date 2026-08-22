using System.Collections.Generic;

// 5.4: тип взаимодействия с квестом. MultipleChoice — несколько вариантов ответа с
// гарантированным (не шансовым) исходом (Загадка сфинкса). TryOrSkip — "Попытаться"/
// "Пройти мимо" с шансом успеха по формуле 8.3 (Круг фей, Меч в камне).
public enum QuestInteractionType
{
    MultipleChoice,
    TryOrSkip
}

public class QuestChoiceOption
{
    public string ButtonText;
    public string OutcomeText;
    public bool IsCorrect;
}

public class QuestDefinition
{
    public string Name;
    public QuestInteractionType InteractionType;
    public string DescriptionText;

    public int Level; // используется только для TryOrSkip (шанс успеха по 8.3)
    public string SuccessText;
    public string FailText;
    public string SkipText;

    public List<QuestChoiceOption> Choices; // используется только для MultipleChoice
}
