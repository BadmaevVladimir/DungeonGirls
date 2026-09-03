using NUnit.Framework;

public class SpriteFloorOffsetsTests
{
    [Test]
    public void ParseTable_KnownKey_ReturnsValue()
    {
        string json = "{\"entries\":[{\"key\":\"Jennifer\",\"value\":0.08},{\"key\":\"Monster_Bat\",\"value\":0.14}]}";
        var table = SpriteFloorOffsets.ParseTable(json);

        Assert.AreEqual(0.08f, table["Jennifer"], 0.0001f);
        Assert.AreEqual(0.14f, table["Monster_Bat"], 0.0001f);
    }

    [Test]
    public void ParseTable_EmptyEntries_ReturnsEmptyDictionary()
    {
        var table = SpriteFloorOffsets.ParseTable("{\"entries\":[]}");
        Assert.AreEqual(0, table.Count);
    }

    [Test]
    public void GetOffsetFraction_UnknownKey_ReturnsZero()
    {
        // Реальной Resources-таблицы ещё нет (Task 4 её ещё не сгенерировал) — GetOffsetFraction
        // должен безопасно вернуть 0f, а не бросить исключение, независимо от наличия файла.
        Assert.AreEqual(0f, SpriteFloorOffsets.GetOffsetFraction("НесуществующийКлюч"));
    }

    [Test]
    public void GetOffsetFraction_NullKey_ReturnsZero()
    {
        Assert.AreEqual(0f, SpriteFloorOffsets.GetOffsetFraction(null));
    }
}
