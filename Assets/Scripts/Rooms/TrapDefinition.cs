// 5.5: содержимое одной ловушки. Конкретные эффекты успеха/провала у каждой ловушки свои
// (общего правила нет, см. ГДД) — обрабатываются в RunFlowController по ссылке на конкретный
// экземпляр из TrapCatalog, а не через универсальные поля.
public class TrapDefinition
{
    public string Name;
    public int Level;
    public string DescriptionText;
    public string SuccessText;
    public string FailText;
}
