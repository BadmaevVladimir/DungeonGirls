using NUnit.Framework;

// ГДД «Механика адаптивного боевого микса», раздел «Расчёт напряжения боя».
// Датчик отдаёт цель 0–1 по наблюдаемым признакам текущего боя; сглаживание — забота MusicMixer.
public class CombatMusicIntensityTests
{
    [Test]
    public void EncounterKinds_GetDifferentStartingBases()
    {
        Assert.Less(CombatMusicIntensity.BaseForEncounter(isBoss: false, isChallenge: false),
                    CombatMusicIntensity.BaseForEncounter(isBoss: false, isChallenge: true));
        Assert.Less(CombatMusicIntensity.BaseForEncounter(isBoss: false, isChallenge: true),
                    CombatMusicIntensity.BaseForEncounter(isBoss: true, isChallenge: false));
    }

    [Test]
    public void StartingBase_DoesNotByItselfOpenTheNextLayer()
    {
        var sensor = new CombatMusicIntensity();
        sensor.Begin(CombatMusicIntensity.BossEncounterBase);
        Assert.Less(sensor.Tick(0.2f, aliveEnemies: 1, playerHpFraction: 1f), MusicLayerMix.LeadFadeIn);
    }

    [Test]
    public void DamageExchange_RaisesTensionAndDecaysWhenItStops()
    {
        var sensor = new CombatMusicIntensity();
        sensor.Begin(CombatMusicIntensity.NormalEncounterBase);
        float calm = sensor.Tick(0.2f, aliveEnemies: 2, playerHpFraction: 1f);

        for (int i = 0; i < 5; i++)
        {
            sensor.ReportDamage(20f, referenceHp: 100f);
            sensor.Tick(0.2f, aliveEnemies: 2, playerHpFraction: 1f);
        }
        float hot = sensor.Target;
        Assert.Greater(hot, calm);

        // Обмен прекратился — цель ползёт вниз сама, без внешней команды.
        for (int i = 0; i < 40; i++) sensor.Tick(0.2f, aliveEnemies: 1, playerHpFraction: 1f);
        Assert.Less(sensor.Target, hot);
    }

    [Test]
    public void LowHealthAlone_CannotReachThePeak()
    {
        var sensor = new CombatMusicIntensity();
        sensor.Begin(CombatMusicIntensity.BossEncounterBase);

        // Бой затих: враг один, урона нет, здоровье на нуле.
        for (int i = 0; i < 40; i++) sensor.Tick(0.2f, aliveEnemies: 1, playerHpFraction: 0.01f);

        Assert.LessOrEqual(sensor.Target, CombatMusicIntensity.QuietCombatCeiling);
        Assert.Less(sensor.Target, MusicLayerMix.LeadFull, "Пик не должен включаться только из-за низкого здоровья.");
    }

    [Test]
    public void LowHealthUnderFire_DoesReachThePeak()
    {
        var sensor = new CombatMusicIntensity();
        sensor.Begin(CombatMusicIntensity.BossEncounterBase);

        for (int i = 0; i < 10; i++)
        {
            sensor.ReportDamage(30f, referenceHp: 100f);
            sensor.ReportSpike();
            sensor.Tick(0.2f, aliveEnemies: 3, playerHpFraction: 0.15f);
        }

        Assert.Greater(sensor.Target, MusicLayerMix.LeadFull);
    }

    [Test]
    public void MoreEnemies_MeanMorePressure()
    {
        var lonely = new CombatMusicIntensity();
        lonely.Begin(CombatMusicIntensity.NormalEncounterBase);
        float one = lonely.Tick(0.2f, aliveEnemies: 1, playerHpFraction: 1f);

        var crowd = new CombatMusicIntensity();
        crowd.Begin(CombatMusicIntensity.NormalEncounterBase);
        float many = crowd.Tick(0.2f, aliveEnemies: 4, playerHpFraction: 1f);

        Assert.Greater(many, one);
    }

    [Test]
    public void Value_StaysWithinZeroToOne()
    {
        var sensor = new CombatMusicIntensity();
        sensor.Begin(1f);
        for (int i = 0; i < 20; i++)
        {
            sensor.ReportDamage(1000f, referenceHp: 100f);
            sensor.ReportSpike(1f);
            float value = sensor.Tick(0.2f, aliveEnemies: 12, playerHpFraction: 0f);
            Assert.GreaterOrEqual(value, 0f);
            Assert.LessOrEqual(value, 1f);
        }
    }

    [Test]
    public void Begin_ResetsStateBetweenFights()
    {
        var sensor = new CombatMusicIntensity();
        sensor.Begin(CombatMusicIntensity.BossEncounterBase);
        sensor.ReportDamage(80f, referenceHp: 100f);
        sensor.ReportSpike(0.5f);
        sensor.Tick(0.2f, aliveEnemies: 5, playerHpFraction: 0.2f);

        sensor.Begin(CombatMusicIntensity.NormalEncounterBase);
        Assert.AreEqual(CombatMusicIntensity.NormalEncounterBase, sensor.Target, 1e-4f);
    }
}
