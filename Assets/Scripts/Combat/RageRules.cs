// Чистые правила Ярости, общие для всех боевых эффектов. Не зависят от сцены или MonoBehaviour.
public static class RageRules
{
    public static float SkillMultiplier(int level) => level switch
    {
        1 => 0.7f, 2 => 0.75f, 3 => 0.8f, 4 => 0.9f, 5 => 1.0f, _ => 0f
    };

    public static float StubbornnessThreshold(int level) => level switch
    {
        1 => 90f, 2 => 80f, 3 => 70f, 4 => 60f, 5 => 50f, _ => 101f
    };
}
