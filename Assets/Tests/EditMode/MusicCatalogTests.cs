using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// W11 / ГДД 10 (G10): музыка меняется по контексту и не накладывается при переходах.
// MusicCatalog — чистая логика подбора трека: превращает контекст в список имён-кандидатов от
// частного к общему. Играет первый существующий, поэтому добавление файла с более частным именем
// меняет звучание без единой правки кода.
public class MusicCatalogTests
{
    [Test]
    public void Hub_AsksForASingleTrack()
    {
        var names = MusicCatalog.CandidateNames(MusicRequest.Hub());
        CollectionAssert.AreEqual(new[] { "Hub" }, names);
    }

    [Test]
    public void Combat_PrefersHeroineThemeThenGeneric()
    {
        var names = MusicCatalog.CandidateNames(MusicRequest.Combat("jennifer", isBoss: false));
        CollectionAssert.AreEqual(new[] { "Combat_jennifer", "Combat" }, names);
    }

    [Test]
    public void BossCombat_PrefersBossThemeOverHeroineTheme()
    {
        var names = MusicCatalog.CandidateNames(MusicRequest.Combat("violet", isBoss: true));
        CollectionAssert.AreEqual(
            new[] { "Combat_Boss_violet", "Combat_Boss", "Combat_violet", "Combat" }, names);
    }

    [Test]
    public void CharacterId_IsNormalisedToLowerCase()
    {
        var names = MusicCatalog.CandidateNames(MusicRequest.Combat("Jennifer", isBoss: false));
        CollectionAssert.AreEqual(new[] { "Combat_jennifer", "Combat" }, names);
    }

    [Test]
    public void MissingCharacterId_FallsBackToGenericCombat()
    {
        CollectionAssert.AreEqual(new[] { "Combat" },
            MusicCatalog.CandidateNames(MusicRequest.Combat(null, isBoss: false)));
        CollectionAssert.AreEqual(new[] { "Combat_Boss", "Combat" },
            MusicCatalog.CandidateNames(MusicRequest.Combat("   ", isBoss: true)));
    }

    [Test]
    public void SilentContext_AsksForNothing()
    {
        CollectionAssert.IsEmpty(MusicCatalog.CandidateNames(MusicRequest.Silent()));
    }

    // Главное требование сценария: треков ещё нет, и это не должно ломать игру.
    [Test]
    public void ResolveTrackName_ReturnsNullWhenNothingIsPresent()
    {
        Assert.IsNull(MusicCatalog.ResolveTrackName(MusicRequest.Hub(), _ => false));
        Assert.IsNull(MusicCatalog.ResolveTrackName(MusicRequest.Combat("sasha", true), _ => false));
    }

    [Test]
    public void ResolveTrackName_PicksTheMostSpecificPresentTrack()
    {
        var present = new HashSet<string> { "Combat", "Combat_sasha" };
        Assert.AreEqual("Combat_sasha",
            MusicCatalog.ResolveTrackName(MusicRequest.Combat("sasha", isBoss: false), present.Contains));

        // Босс-темы ещё нет — откатываемся на тему героини, а не на общую.
        Assert.AreEqual("Combat_sasha",
            MusicCatalog.ResolveTrackName(MusicRequest.Combat("sasha", isBoss: true), present.Contains));

        present.Add("Combat_Boss");
        Assert.AreEqual("Combat_Boss",
            MusicCatalog.ResolveTrackName(MusicRequest.Combat("sasha", isBoss: true), present.Contains));
    }

    [Test]
    public void ResolveTrackName_FallsBackToGenericWhenHeroineThemeIsAbsent()
    {
        var present = new HashSet<string> { "Combat" };
        Assert.AreEqual("Combat",
            MusicCatalog.ResolveTrackName(MusicRequest.Combat("violet", isBoss: false), present.Contains));
    }

    // Перезапуск того же трека при каждом обновлении экрана — это и есть «музыка накладывается».
    [Test]
    public void ShouldSwitch_IsFalseForTheSameTrack()
    {
        Assert.IsFalse(MusicCatalog.ShouldSwitch("Combat_jennifer", "Combat_jennifer"));
        Assert.IsTrue(MusicCatalog.ShouldSwitch("Combat_jennifer", "Hub"));
        Assert.IsTrue(MusicCatalog.ShouldSwitch(null, "Hub"));
        Assert.IsTrue(MusicCatalog.ShouldSwitch("Hub", null), "Уход в тишину — тоже смена.");
        Assert.IsFalse(MusicCatalog.ShouldSwitch(null, null));
    }

    [Test]
    public void ResourcePath_IsRootedInMusicFolder()
    {
        Assert.AreEqual("Music/Hub", MusicCatalog.ResourcePath("Hub"));
    }

    // Соглашение об именах — единственное, что связывает файл с контекстом: ссылки в инспекторе
    // нет, и переименованный трек просто замолчал бы без единой ошибки. Этот тест ловит именно
    // такой случай для уже существующих тем.
    [TestCase("jennifer")]
    [TestCase("violet")]
    [TestCase("sasha")]
    public void ExistingCombatThemes_AreReachableByConvention(string characterId)
    {
        string expected = $"Combat_{characterId}";
        var names = MusicCatalog.CandidateNames(MusicRequest.Combat(characterId, isBoss: false));
        Assert.AreEqual(expected, names[0]);

        var clip = Resources.Load<AudioClip>(MusicCatalog.ResourcePath(expected));
        Assert.IsNotNull(clip,
            $"Трек {expected} не найден в Assets/Resources/{MusicCatalog.ResourceFolder}/ — бой этой героини останется без музыки.");
    }
}
