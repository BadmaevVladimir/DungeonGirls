using UnityEngine;
using UnityEngine.UIElements;

// Общий runtime-слой настроек звука для хаба и забега, по образцу TutorialManager: компонент
// создаётся на том же GameObject, что и UIDocument, поэтому оба существующих оркестратора
// (HubManager, RunFlowController) получают один и тот же инстанс поверх общего VisualTree.
// Сейчас регулируется только Master (через AudioListener.volume — действует на весь звук игры
// сразу, без необходимости назначать AudioMixer-группы существующим AudioSource). Per-category
// громкости (Music/SFX/Voice) читаются из PlayerPrefs уже сейчас на случай будущих слайдеров —
// TaggedAudio.PlayOneShot их учитывает, хотя UI для них пока не показан.
public class AudioSettingsManager : MonoBehaviour
{
    public static AudioSettingsManager Instance { get; private set; }

    const string MasterVolumeKey = "Audio.MasterVolume";
    const string CategoryVolumeKeyPrefix = "Audio.CategoryVolume.";
    const float DefaultVolume = 1f;

    VisualElement settingsScreen;
    Slider masterVolumeSlider;
    Label masterVolumeValueLabel;
    Button settingsCloseButton;
    bool initialized;

    public static AudioSettingsManager GetOrCreate(UIDocument document)
    {
        if (Instance == null)
        {
            Instance = document.GetComponent<AudioSettingsManager>();
            if (Instance == null) Instance = document.gameObject.AddComponent<AudioSettingsManager>();
        }

        Instance.Initialize(document);
        return Instance;
    }

    public void Initialize(UIDocument document)
    {
        if (initialized || document == null) return;

        var root = document.rootVisualElement;
        settingsScreen = root.Q<VisualElement>("SettingsScreen");
        masterVolumeSlider = root.Q<Slider>("MasterVolumeSlider");
        masterVolumeValueLabel = root.Q<Label>("MasterVolumeValueLabel");
        settingsCloseButton = root.Q<Button>("SettingsCloseButton");

        if (settingsScreen == null || masterVolumeSlider == null || masterVolumeValueLabel == null || settingsCloseButton == null)
        {
            Debug.LogError("[AudioSettings] В GameRoot.uxml отсутствуют обязательные элементы SettingsScreen.");
            return;
        }

        masterVolumeSlider.lowValue = 0f;
        masterVolumeSlider.highValue = 1f;
        masterVolumeSlider.value = MasterVolume;
        UpdateMasterVolumeLabel(MasterVolume);
        ApplyMasterVolume(MasterVolume);
        masterVolumeSlider.RegisterValueChangedCallback(OnMasterVolumeChanged);

        settingsCloseButton.clicked += CloseSettings;
        foreach (var buttonName in new[] { "SettingsButton", "PauseSettingsButton" })
        {
            var button = root.Q<Button>(buttonName);
            if (button != null) button.clicked += OpenSettings;
        }

        initialized = true;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static float MasterVolume => PlayerPrefs.GetFloat(MasterVolumeKey, DefaultVolume);

    public static float GetCategoryVolume(AudioCategory category) =>
        PlayerPrefs.GetFloat(CategoryVolumeKeyPrefix + category, DefaultVolume);

    void OnMasterVolumeChanged(ChangeEvent<float> evt)
    {
        SetMasterVolume(evt.newValue);
    }

    public void SetMasterVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MasterVolumeKey, volume);
        UpdateMasterVolumeLabel(volume);
        ApplyMasterVolume(volume);
    }

    void UpdateMasterVolumeLabel(float volume)
    {
        if (masterVolumeValueLabel != null) masterVolumeValueLabel.text = $"{Mathf.RoundToInt(volume * 100f)}%";
    }

    static void ApplyMasterVolume(float volume)
    {
        AudioListener.volume = volume;
    }

    public void OpenSettings()
    {
        if (!initialized) return;
        settingsScreen.style.display = DisplayStyle.Flex;
        settingsScreen.BringToFront();
    }

    void CloseSettings()
    {
        if (!initialized) return;
        settingsScreen.style.display = DisplayStyle.None;
    }
}
