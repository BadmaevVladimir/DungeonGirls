// Категория звука для будущих отдельных ползунков громкости (Музыка/Эффекты/Голос). Сейчас
// используется только Master (AudioSettingsManager.MasterVolume применяется глобально через
// AudioListener.volume), но новые места воспроизведения звука уже помечают категорию через
// TaggedAudio.PlayOneShot, чтобы добавление Music/SFX/Voice-слайдеров позже не требовало
// повторного прохода по всем ассетам.
public enum AudioCategory
{
    Music,
    SFX,
    Voice
}
