using UnityEngine;

// Обёртка над AudioSource.PlayOneShot, которая помечает звук категорией (AudioCategory) —
// см. AudioSettingsManager. Master уже действует на весь звук через AudioListener.volume, эта
// обёртка домножает на будущую per-category громкость, чтобы её можно было включить позже без
// повторного прохода по местам воспроизведения звука.
public static class TaggedAudio
{
    public static void PlayOneShot(AudioSource source, AudioClip clip, AudioCategory category, float volumeScale = 1f)
    {
        if (source == null || clip == null) return;
        source.PlayOneShot(clip, volumeScale * AudioSettingsManager.GetCategoryVolume(category));
    }

    // В отличие от PlayOneShot, использует source.clip/Play() — даёт возможность потом сикать
    // source.time (см. ChestRevealAnimator.JingleBuildupDuration и обработку Skip в
    // RunFlowController.Reward.cs/HubManager.Gacha.cs).
    public static void Play(AudioSource source, AudioClip clip, AudioCategory category, float volumeScale = 1f)
    {
        if (source == null || clip == null) return;
        source.clip = clip;
        source.volume = volumeScale * AudioSettingsManager.GetCategoryVolume(category);
        source.time = 0f;
        source.Play();
    }
}
