using NUnit.Framework;
using UnityEngine;

public class AudioSettingsManagerTests
{
    const string MasterVolumeKey = "Audio.MasterVolume";
    const string SfxCategoryKey = "Audio.CategoryVolume.SFX";
    float savedMasterVolume;
    float savedSfxVolume;
    bool hadSavedMasterVolume;
    bool hadSavedSfxVolume;
    float savedAudioListenerVolume;

    [SetUp]
    public void SetUp()
    {
        hadSavedMasterVolume = PlayerPrefs.HasKey(MasterVolumeKey);
        if (hadSavedMasterVolume) savedMasterVolume = PlayerPrefs.GetFloat(MasterVolumeKey);
        hadSavedSfxVolume = PlayerPrefs.HasKey(SfxCategoryKey);
        if (hadSavedSfxVolume) savedSfxVolume = PlayerPrefs.GetFloat(SfxCategoryKey);
        savedAudioListenerVolume = AudioListener.volume;

        PlayerPrefs.DeleteKey(MasterVolumeKey);
        PlayerPrefs.DeleteKey(SfxCategoryKey);
    }

    [TearDown]
    public void TearDown()
    {
        if (hadSavedMasterVolume) PlayerPrefs.SetFloat(MasterVolumeKey, savedMasterVolume);
        else PlayerPrefs.DeleteKey(MasterVolumeKey);
        if (hadSavedSfxVolume) PlayerPrefs.SetFloat(SfxCategoryKey, savedSfxVolume);
        else PlayerPrefs.DeleteKey(SfxCategoryKey);
        AudioListener.volume = savedAudioListenerVolume;
    }

    [Test]
    public void MasterVolume_DefaultsToHalf_WhenNoPrefSaved()
    {
        Assert.AreEqual(0.5f, AudioSettingsManager.MasterVolume);
    }

    [Test]
    public void SetMasterVolume_PersistsToPlayerPrefsAndAppliesToAudioListener()
    {
        var go = new GameObject("AudioSettingsManagerTestHost");
        var manager = go.AddComponent<AudioSettingsManager>();

        manager.SetMasterVolume(0.35f);

        Assert.AreEqual(0.35f, AudioSettingsManager.MasterVolume, 0.0001f);
        Assert.AreEqual(0.35f, AudioListener.volume, 0.0001f);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void SetMasterVolume_ClampsToZeroOneRange()
    {
        var go = new GameObject("AudioSettingsManagerTestHost");
        var manager = go.AddComponent<AudioSettingsManager>();

        manager.SetMasterVolume(-0.5f);
        Assert.AreEqual(0f, AudioSettingsManager.MasterVolume, 0.0001f);

        manager.SetMasterVolume(2f);
        Assert.AreEqual(1f, AudioSettingsManager.MasterVolume, 0.0001f);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void GetCategoryVolume_DefaultsToFull_WhenNoPrefSaved()
    {
        Assert.AreEqual(1f, AudioSettingsManager.GetCategoryVolume(AudioCategory.SFX));
    }

    [Test]
    public void GetCategoryVolume_ReadsPersistedPref()
    {
        PlayerPrefs.SetFloat(SfxCategoryKey, 0.6f);
        Assert.AreEqual(0.6f, AudioSettingsManager.GetCategoryVolume(AudioCategory.SFX), 0.0001f);
    }
}
