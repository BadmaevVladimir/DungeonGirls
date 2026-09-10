using System.Collections.Generic;
using NUnit.Framework;

// W11 / ГДД «Механика адаптивного боевого микса»: жизненный цикл музыки забега.
//
// Всё, что здесь проверяется, — утверждения о вызовах к аудиовыходу: «после боя нет Stop»,
// «слои планируются на один момент», «повторный контекст ничего не трогает». Поэтому тесты
// работают на подставном IMusicOutput и не требуют ни AudioSource, ни реальных wav.
public class MusicMixerTests
{
    sealed class FakeMusicOutput : IMusicOutput
    {
        public double DspTime { get; set; }

        public readonly List<string> Prepared = new List<string>();
        public readonly Dictionary<int, double> Scheduled = new Dictionary<int, double>();
        public readonly Dictionary<int, float> Volumes = new Dictionary<int, float>();

        public int PrepareCalls;
        public int StopAllCalls;
        public bool PrepareSucceeds = true;

        public string OverrideTrack;
        public int OverridePlayCalls;
        public int OverrideStopCalls;
        public float OverrideVolume;

        public bool PrepareLayers(IReadOnlyList<string> trackNames)
        {
            PrepareCalls++;
            Prepared.Clear();
            Scheduled.Clear();
            if (!PrepareSucceeds) return false;
            Prepared.AddRange(trackNames);
            return true;
        }

        public void ScheduleLayer(int index, double dspStartTime) => Scheduled[index] = dspStartTime;
        public void SetLayerVolume(int index, float volume) => Volumes[index] = volume;
        public void StopAllLayers() => StopAllCalls++;

        public void PlayOverrideTrack(string trackName)
        {
            OverrideTrack = trackName;
            OverridePlayCalls++;
        }

        public void StopOverrideTrack()
        {
            OverrideTrack = null;
            OverrideStopCalls++;
        }

        public void SetOverrideVolume(float volume) => OverrideVolume = volume;
    }

    static HashSet<string> LayeredSet(string baseName) => new HashSet<string>
    {
        baseName,
        MusicCatalog.LayerTrackName(baseName, MusicLayerMix.Harmony),
        MusicCatalog.LayerTrackName(baseName, MusicLayerMix.Drums),
        MusicCatalog.LayerTrackName(baseName, MusicLayerMix.Lead)
    };

    static (MusicMixer mixer, FakeMusicOutput output) Rig(HashSet<string> existing)
    {
        var output = new FakeMusicOutput { DspTime = 100.0 };
        var mixer = new MusicMixer();
        mixer.Attach(output, existing.Contains);
        return (mixer, output);
    }

    // Прогоняет время большими шагами: чем именно сглажено значение, тесты не проверяют — им
    // важно, куда оно приходит.
    static void Settle(MusicMixer mixer, float seconds = 10f)
    {
        for (float t = 0f; t < seconds; t += 0.1f) mixer.Tick(0.1f);
    }

    static int LayerIndex(string role)
    {
        for (int i = 0; i < MusicLayerMix.LayerRoles.Count; i++)
            if (MusicLayerMix.LayerRoles[i] == role) return i;
        throw new KeyNotFoundException(role);
    }

    [Test]
    public void CombatEnd_DoesNotStopOrRestartTheRunTheme()
    {
        var (mixer, output) = Rig(LayeredSet("Combat_jennifer"));

        mixer.PlayContext(MusicRequest.Run("jennifer"));
        var scheduledAtStart = new Dictionary<int, double>(output.Scheduled);

        mixer.PlayContext(MusicRequest.Combat("jennifer", isBoss: false));
        mixer.EnterCombat(CombatMusicIntensity.NormalEncounterBase);
        Settle(mixer);

        mixer.ExitCombat();
        Settle(mixer);

        Assert.AreEqual(0, output.StopAllCalls, "Выход из боя не должен останавливать музыку.");
        Assert.AreEqual(1, output.PrepareCalls, "Бой не должен перезагружать набор слоёв.");
        CollectionAssert.AreEquivalent(scheduledAtStart, output.Scheduled,
            "Позиция слоёв не должна пересчитываться после боя.");
    }

    [Test]
    public void ZeroIntensity_LeavesOnlyTheBaseLayerAudible()
    {
        var (mixer, _) = Rig(LayeredSet("Combat_violet"));

        mixer.PlayContext(MusicRequest.Run("violet"));
        mixer.EnterCombat(1f);
        Settle(mixer);
        mixer.ExitCombat();
        Settle(mixer);

        Assert.Greater(mixer.LayerVolume(LayerIndex(MusicLayerMix.Harmony)), 0f);
        Assert.AreEqual(0f, mixer.LayerVolume(LayerIndex(MusicLayerMix.Drums)), 1e-4f);
        Assert.AreEqual(0f, mixer.LayerVolume(LayerIndex(MusicLayerMix.Lead)), 1e-4f);
    }

    [Test]
    public void RisingIntensity_MixesInTheRemainingLayers()
    {
        var (mixer, _) = Rig(LayeredSet("Combat_sasha"));

        mixer.PlayContext(MusicRequest.Run("sasha"));
        mixer.SetIntensity(1f);
        Settle(mixer);

        Assert.AreEqual(1f, mixer.LayerVolume(LayerIndex(MusicLayerMix.Drums)), 1e-4f);
        Assert.AreEqual(1f, mixer.LayerVolume(LayerIndex(MusicLayerMix.Lead)), 1e-4f);
    }

    [Test]
    public void AllLayers_AreScheduledOnTheSameDspMoment()
    {
        var (mixer, output) = Rig(LayeredSet("Combat_jennifer"));

        mixer.PlayContext(MusicRequest.Run("jennifer"));

        Assert.AreEqual(MusicLayerMix.LayerRoles.Count, output.Scheduled.Count);
        double expected = 100.0 + MusicMixer.ScheduleLeadSeconds;
        foreach (var pair in output.Scheduled) Assert.AreEqual(expected, pair.Value, 1e-9);
    }

    [Test]
    public void SameContextRequestedAgain_DoesNotResetPosition()
    {
        var (mixer, output) = Rig(LayeredSet("Combat_jennifer"));

        mixer.PlayContext(MusicRequest.Run("jennifer"));
        var scheduled = new Dictionary<int, double>(output.Scheduled);

        output.DspTime = 250.0; // время ушло вперёд — новый Schedule был бы виден
        mixer.PlayContext(MusicRequest.Run("jennifer"));
        mixer.PlayContext(MusicRequest.Combat("jennifer", isBoss: false));

        Assert.AreEqual(1, output.PrepareCalls);
        Assert.AreEqual(0, output.StopAllCalls);
        CollectionAssert.AreEquivalent(scheduled, output.Scheduled);
    }

    [Test]
    public void Override_MutesTheBaseThemeAndRestoresIt()
    {
        var (mixer, output) = Rig(LayeredSet("Combat_jennifer"));
        mixer.PlayContext(MusicRequest.Run("jennifer"));

        int token = mixer.BeginOverride();
        Settle(mixer, 2f);
        Assert.AreEqual(0f, mixer.BaseGain, 1e-4f, "Под джинглом основная тема должна быть заглушена.");
        Assert.AreEqual(0, output.StopAllCalls, "Заглушение — не остановка: таймлайн темы продолжает идти.");

        mixer.EndOverride(token);
        Settle(mixer, 2f);
        Assert.AreEqual(1f, mixer.BaseGain, 1e-4f);
        Assert.Greater(mixer.LayerVolume(LayerIndex(MusicLayerMix.Harmony)), 0f);
    }

    [Test]
    public void SkippedJingle_RestoresMusicEvenMidFade()
    {
        var (mixer, _) = Rig(LayeredSet("Combat_jennifer"));
        mixer.PlayContext(MusicRequest.Run("jennifer"));

        int token = mixer.BeginOverride();
        mixer.Tick(0.05f); // скип пришёл раньше, чем заглушение успело договорить
        Assert.Greater(mixer.BaseGain, 0f);

        mixer.EndOverride(token);
        Settle(mixer, 2f);
        Assert.AreEqual(1f, mixer.BaseGain, 1e-4f);
    }

    [Test]
    public void NestedOverrides_DoNotRestoreMusicEarly()
    {
        var (mixer, _) = Rig(LayeredSet("Combat_jennifer"));
        mixer.PlayContext(MusicRequest.Run("jennifer"));

        int outer = mixer.BeginOverride();
        int inner = mixer.BeginOverride();
        Settle(mixer, 2f);

        mixer.EndOverride(inner);
        Settle(mixer, 2f);
        Assert.AreEqual(0f, mixer.BaseGain, 1e-4f, "Внешний override ещё открыт — музыка не возвращается.");

        mixer.EndOverride(outer);
        Settle(mixer, 2f);
        Assert.AreEqual(1f, mixer.BaseGain, 1e-4f);
    }

    [Test]
    public void EndOverride_WithStaleToken_IsIgnored()
    {
        var (mixer, _) = Rig(LayeredSet("Combat_jennifer"));
        mixer.PlayContext(MusicRequest.Run("jennifer"));

        int token = mixer.BeginOverride();
        mixer.EndOverride(token);
        mixer.EndOverride(token); // повторное снятие того же токена
        mixer.EndOverride(9999);  // чужой токен

        Settle(mixer, 2f);
        Assert.AreEqual(0, mixer.OverrideDepth);
        Assert.AreEqual(1f, mixer.BaseGain, 1e-4f);
    }

    [Test]
    public void EventOverride_PlaysItsOwnTrackAndStopsItOnEnd()
    {
        var existing = LayeredSet("Combat_jennifer");
        existing.Add("Event_mushroom");
        var (mixer, output) = Rig(existing);
        mixer.PlayContext(MusicRequest.Run("jennifer"));

        int token = mixer.BeginOverride(MusicRequest.Event("mushroom"));
        Assert.AreEqual("Event_mushroom", output.OverrideTrack);
        Settle(mixer, 2f);
        Assert.Greater(output.OverrideVolume, 0f, "Трек события поднимается ровно на спад основы.");

        mixer.EndOverride(token);
        Assert.IsNull(output.OverrideTrack);
        Assert.AreEqual(1, output.OverrideStopCalls);

        Settle(mixer, 2f);
        Assert.AreEqual(1f, mixer.BaseGain, 1e-4f);
        Assert.AreEqual(0, output.StopAllCalls, "Тема забега не останавливалась на время события.");
    }

    [Test]
    public void EventWithoutItsOwnTrack_StillOnlyMutesTheBase()
    {
        var (mixer, output) = Rig(LayeredSet("Combat_jennifer"));
        mixer.PlayContext(MusicRequest.Run("jennifer"));

        mixer.BeginOverride(MusicRequest.Event("mushroom"));
        Settle(mixer, 2f);

        Assert.IsNull(output.OverrideTrack);
        Assert.AreEqual(0, output.OverridePlayCalls);
        Assert.AreEqual(0f, mixer.BaseGain, 1e-4f);
    }

    [Test]
    public void UserVolume_MultipliesLayerGainsAndAppliesWithoutRestart()
    {
        var (mixer, output) = Rig(LayeredSet("Combat_jennifer"));
        mixer.PlayContext(MusicRequest.Run("jennifer"));
        mixer.SetIntensity(1f);
        Settle(mixer);

        int drums = LayerIndex(MusicLayerMix.Drums);
        Assert.AreEqual(1f, mixer.LayerVolume(drums), 1e-4f);

        mixer.SetCategoryVolume(0.25f);

        Assert.AreEqual(0.25f, mixer.LayerVolume(drums), 1e-4f,
            "Настройка Music множится на коэффициент слоя, а не подменяет его.");
        Assert.AreEqual(0.25f, output.Volumes[drums], 1e-4f, "Новая громкость должна дойти до источника сразу.");
        Assert.AreEqual(1, output.PrepareCalls, "Смена громкости не перезапускает музыку.");
    }

    [Test]
    public void MissingLayer_FallsBackToTheFullMixInsteadOfPlayingAPartialSet()
    {
        // Есть контрольный микс и два слоя из трёх — набор неполный.
        var existing = new HashSet<string>
        {
            "Combat_jennifer",
            MusicCatalog.LayerTrackName("Combat_jennifer", MusicLayerMix.Harmony),
            MusicCatalog.LayerTrackName("Combat_jennifer", MusicLayerMix.Drums)
        };
        var (mixer, output) = Rig(existing);

        mixer.PlayContext(MusicRequest.Run("jennifer"));

        Assert.AreEqual(1, mixer.LayerCount);
        Assert.IsFalse(mixer.IsLayered);
        CollectionAssert.AreEqual(new[] { "Combat_jennifer" }, output.Prepared);

        mixer.ExitCombat();
        Settle(mixer);
        Assert.Greater(mixer.LayerVolume(0), 0f, "Контрольный микс не зависит от напряжения и не должен замолкать.");
    }

    [Test]
    public void HubRunAndBoss_RemainDeliberateContextSwitches()
    {
        var existing = LayeredSet("Combat_jennifer");
        existing.Add("Hub");
        existing.Add("Combat_Boss");
        var (mixer, output) = Rig(existing);

        mixer.PlayContext(MusicRequest.Hub());
        Assert.AreEqual("Hub", mixer.CurrentContextKey);
        Assert.IsFalse(mixer.IsLayered, "У деревни слоёв нет — играет одиночный трек.");

        mixer.PlayContext(MusicRequest.Run("jennifer"));
        Assert.AreEqual("Combat_jennifer", mixer.CurrentContextKey);
        CollectionAssert.AreEqual(
            new[]
            {
                MusicCatalog.LayerTrackName("Combat_jennifer", MusicLayerMix.Harmony),
                MusicCatalog.LayerTrackName("Combat_jennifer", MusicLayerMix.Drums),
                MusicCatalog.LayerTrackName("Combat_jennifer", MusicLayerMix.Lead)
            },
            output.Prepared,
            "Забег переключается на набор слоёв героини — это намеренная смена контекста.");

        mixer.PlayContext(MusicRequest.Combat("jennifer", isBoss: true));
        Assert.AreEqual("Combat_Boss", mixer.CurrentContextKey);

        mixer.PlayContext(MusicRequest.Hub());
        Assert.AreEqual("Hub", mixer.CurrentContextKey);
        Assert.AreEqual(4, output.PrepareCalls, "Каждая из четырёх смен контекста — ровно одна загрузка набора.");
    }

    [Test]
    public void SilentContext_StopsEverythingAndIsIdempotent()
    {
        var (mixer, output) = Rig(LayeredSet("Combat_jennifer"));
        mixer.PlayContext(MusicRequest.Run("jennifer"));

        mixer.StopForTest();
        Assert.AreEqual(1, output.StopAllCalls);

        mixer.StopForTest();
        Assert.AreEqual(1, output.StopAllCalls, "Повторная тишина не должна ничего делать.");
        Assert.IsNull(mixer.CurrentContextKey);
    }
}

// Намеренная тишина в тестах читается как отдельное действие, а не как «ещё один контекст».
static class MusicMixerTestExtensions
{
    public static void StopForTest(this MusicMixer mixer) => mixer.PlayContext(MusicRequest.Silent());
}
