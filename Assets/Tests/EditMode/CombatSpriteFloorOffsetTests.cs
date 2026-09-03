using NUnit.Framework;
using UnityEngine;

public class CombatSpriteFloorOffsetTests
{
    static float FakeLookup(string key) => key switch
    {
        "Jennifer" => 0.08f,
        "Monster_Bat" => 0.14f,
        _ => 0f
    };

    [Test]
    public void Player_UsesFolderKeyFromDisplayName()
    {
        var player = new CombatantRuntime { IsPlayer = true, DisplayName = "Дженифер" };
        Assert.AreEqual(0.08f, CombatSpriteFloorOffset.GetOffsetFraction(player, FakeLookup), 0.0001f);
    }

    [Test]
    public void Player_UnknownDisplayName_ReturnsZero()
    {
        var player = new CombatantRuntime { IsPlayer = true, DisplayName = "Кто-то новый" };
        Assert.AreEqual(0f, CombatSpriteFloorOffset.GetOffsetFraction(player, FakeLookup), 0.0001f);
    }

    [Test]
    public void Monster_UsesMonsterPrefixedFolderKey()
    {
        var monster = new CombatantRuntime { IsPlayer = false, MonsterAnimationKey = "Летучая мышь" };
        Assert.AreEqual(0.14f, CombatSpriteFloorOffset.GetOffsetFraction(monster, FakeLookup), 0.0001f);
    }

    [Test]
    public void Monster_UnknownAnimationKey_ReturnsZero()
    {
        var monster = new CombatantRuntime { IsPlayer = false, MonsterAnimationKey = "Неизвестный монстр" };
        Assert.AreEqual(0f, CombatSpriteFloorOffset.GetOffsetFraction(monster, FakeLookup), 0.0001f);
    }

    [Test]
    public void Boss_UsesCurrentPhaseFloorPaddingDirectly_NotTheLookupTable()
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.phases.Add(new BossPhaseData { hpThresholdPercent = 100f, floorPaddingFraction = 0.22f });
        var boss = new CombatantRuntime { IsPlayer = false, MonsterAnimationKey = "Не должно использоваться" };
        boss.BossEncounter = new BossEncounterState(kit);

        // FakeLookup не знает "Не должно использоваться" (вернул бы 0) — если результат НЕ 0.22,
        // значит боссовский путь ошибочно ушёл через таблицу вместо floorPaddingFraction фазы.
        Assert.AreEqual(0.22f, CombatSpriteFloorOffset.GetOffsetFraction(boss, FakeLookup), 0.0001f);

        Object.DestroyImmediate(kit);
    }

    [Test]
    public void NullCombatant_ReturnsZero()
    {
        Assert.AreEqual(0f, CombatSpriteFloorOffset.GetOffsetFraction(null, FakeLookup));
    }

    [Test]
    public void SingleArgumentOverload_UsesRealSpriteFloorOffsetsTable()
    {
        // Не проверяем конкретное значение (реальная таблица ещё не сгенерирована на этом этапе
        // плана) — только то, что однопараметрический вызов не бросает и возвращает валидное число
        // (SpriteFloorOffsets.GetOffsetFraction безопасно возвращает 0f для отсутствующей таблицы).
        var player = new CombatantRuntime { IsPlayer = true, DisplayName = "Дженифер" };
        Assert.DoesNotThrow(() => CombatSpriteFloorOffset.GetOffsetFraction(player));
    }
}
