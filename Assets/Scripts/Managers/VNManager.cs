using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class VNManager : MonoBehaviour
{
    [SerializeField] UIDocument uiDocument;
    [SerializeField] NarrativeVisualLibrary visualLibrary;
    [SerializeField, Range(1f, 1.2f)] float speakingScale = 1.06f;
    [SerializeField, Range(0.1f, 1f)] float inactiveBrightness = 0.45f;

    readonly Dictionary<string, ActorView> actors = new Dictionary<string, ActorView>(StringComparer.OrdinalIgnoreCase);
    readonly Image[] actorSlots = new Image[NarrativeSceneValidator.MaxVisibleActors];

    NarrativeSceneRepository repository;
    NarrativeSceneData currentScene;
    int currentLineIndex = -1;

    VisualElement overlay;
    Image backgroundImage;
    Image cgImage;
    VisualElement actorLayer;
    Label speakerLabel;
    Label dialogueLabel;
    Button continueButton;

    public bool IsPlaying => currentScene != null;
    public string CurrentSceneId => currentScene?.id;
    public NarrativeLineData CurrentLine => IsPlaying && currentLineIndex >= 0 && currentLineIndex < currentScene.lines.Length
        ? currentScene.lines[currentLineIndex]
        : null;

    public event Action<string> SceneStarted;
    public event Action<NarrativeLineData> LineShown;
    public event Action<string, bool> SceneFinished;

    void Awake()
    {
        if (visualLibrary == null) visualLibrary = Resources.Load<NarrativeVisualLibrary>("NarrativeVisualLibrary");
        repository = new NarrativeSceneRepository();
        EnsureUI();
    }

    void Update()
    {
        if (!IsPlaying) return;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) Advance();
        if (Input.GetKeyDown(KeyCode.Escape)) FinishScene(true);
    }

    public void PlayScene(string sceneId)
    {
        TryPlayScene(sceneId);
    }

    public bool TryPlayScene(string sceneId)
    {
        repository ??= new NarrativeSceneRepository();
        if (!repository.TryGetScene(sceneId, out var scene))
        {
            Debug.LogError($"[VN] Scene '{sceneId}' was not found. {string.Join(" | ", repository.Errors)}");
            return false;
        }

        return PlayScene(scene);
    }

    public bool PlayScene(NarrativeSceneData scene)
    {
        var errors = NarrativeSceneValidator.Validate(scene);
        if (errors.Count > 0)
        {
            Debug.LogError("[VN] Cannot play invalid scene: " + string.Join(" | ", errors));
            return false;
        }

        if (!EnsureUI())
        {
            Debug.LogError("[VN] Cannot play a scene because no UIDocument/rootVisualElement is available.");
            return false;
        }

        currentScene = scene;
        currentLineIndex = -1;
        BuildInitialStage(scene);
        overlay.style.display = DisplayStyle.Flex;
        SceneStarted?.Invoke(scene.id);
        Advance();
        return true;
    }

    public void PlayQuest(string questId)
    {
        // The JSON data model is shared with quests, while branching quest outcomes remain a separate integration phase.
        PlayScene(questId);
    }

    public void Advance()
    {
        if (!IsPlaying) return;
        currentLineIndex++;
        if (currentLineIndex >= currentScene.lines.Length)
        {
            FinishScene(false);
            return;
        }

        ShowLine(currentScene.lines[currentLineIndex]);
    }

    public void Skip() => FinishScene(true);

    public void ReloadContent()
    {
        repository ??= new NarrativeSceneRepository();
        repository.Reload();
    }

    // Test/preview hook: does not touch scene navigation or SaveData.
    public void Initialize(VisualElement root, NarrativeVisualLibrary library = null, NarrativeSceneRepository sceneRepository = null)
    {
        visualLibrary = library != null ? library : visualLibrary;
        if (visualLibrary == null) visualLibrary = Resources.Load<NarrativeVisualLibrary>("NarrativeVisualLibrary");
        repository = sceneRepository ?? repository ?? new NarrativeSceneRepository();
        BuildUI(root);
    }

    bool EnsureUI()
    {
        if (overlay != null) return true;
        if (uiDocument == null) uiDocument = FindAnyObjectByType<UIDocument>();
        var root = uiDocument != null ? uiDocument.rootVisualElement : null;
        if (root == null) return false;
        BuildUI(root);
        return true;
    }

    void BuildUI(VisualElement root)
    {
        overlay?.RemoveFromHierarchy();

        overlay = new VisualElement { name = "VNOverlay", pickingMode = PickingMode.Position };
        SetFullScreen(overlay);
        overlay.style.display = DisplayStyle.None;
        overlay.style.backgroundColor = Color.black;

        backgroundImage = CreateFullScreenImage("VNBackground", ScaleMode.ScaleAndCrop);
        actorLayer = new VisualElement { name = "VNActorLayer", pickingMode = PickingMode.Ignore };
        SetFullScreen(actorLayer);

        for (int i = 0; i < actorSlots.Length; i++)
        {
            var image = new Image { name = $"VNActor{i}", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.position = Position.Absolute;
            image.style.left = Length.Percent(i * 18f - 1f);
            image.style.top = Length.Percent(2f);
            image.style.width = Length.Percent(29f);
            image.style.height = Length.Percent(78f);
            image.style.display = DisplayStyle.None;
            actorSlots[i] = image;
            actorLayer.Add(image);
        }

        cgImage = CreateFullScreenImage("VNCg", ScaleMode.ScaleAndCrop);
        cgImage.style.display = DisplayStyle.None;

        var dialoguePanel = new VisualElement { name = "VNDialoguePanel" };
        dialoguePanel.style.position = Position.Absolute;
        dialoguePanel.style.left = Length.Percent(5f);
        dialoguePanel.style.right = Length.Percent(5f);
        dialoguePanel.style.bottom = Length.Percent(4f);
        dialoguePanel.style.minHeight = Length.Percent(22f);
        dialoguePanel.style.paddingLeft = 32f;
        dialoguePanel.style.paddingRight = 32f;
        dialoguePanel.style.paddingTop = 26f;
        dialoguePanel.style.paddingBottom = 22f;
        dialoguePanel.style.backgroundColor = new Color(0.035f, 0.03f, 0.055f, 0.94f);
        dialoguePanel.style.borderTopLeftRadius = 16f;
        dialoguePanel.style.borderTopRightRadius = 16f;
        dialoguePanel.style.borderBottomLeftRadius = 16f;
        dialoguePanel.style.borderBottomRightRadius = 16f;
        dialoguePanel.style.borderTopWidth = 2f;
        dialoguePanel.style.borderRightWidth = 2f;
        dialoguePanel.style.borderBottomWidth = 2f;
        dialoguePanel.style.borderLeftWidth = 2f;
        var borderColor = new Color(0.68f, 0.55f, 0.28f, 0.95f);
        dialoguePanel.style.borderTopColor = borderColor;
        dialoguePanel.style.borderRightColor = borderColor;
        dialoguePanel.style.borderBottomColor = borderColor;
        dialoguePanel.style.borderLeftColor = borderColor;

        speakerLabel = new Label { name = "VNSpeakerName" };
        speakerLabel.style.position = Position.Absolute;
        speakerLabel.style.left = 26f;
        speakerLabel.style.top = -24f;
        speakerLabel.style.paddingLeft = 18f;
        speakerLabel.style.paddingRight = 18f;
        speakerLabel.style.height = 42f;
        speakerLabel.style.fontSize = 25f;
        speakerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        speakerLabel.style.color = new Color(1f, 0.86f, 0.45f);
        speakerLabel.style.backgroundColor = new Color(0.035f, 0.03f, 0.055f, 0.98f);

        dialogueLabel = new Label { name = "VNDialogueText" };
        dialogueLabel.style.whiteSpace = WhiteSpace.Normal;
        dialogueLabel.style.fontSize = 28f;
        dialogueLabel.style.color = Color.white;
        dialogueLabel.style.flexGrow = 1f;

        continueButton = new Button(Advance) { name = "VNContinueButton", text = "Продолжить ▶" };
        continueButton.style.alignSelf = Align.FlexEnd;
        continueButton.style.minWidth = 190f;
        continueButton.style.height = 46f;
        continueButton.style.fontSize = 20f;

        dialoguePanel.Add(speakerLabel);
        dialoguePanel.Add(dialogueLabel);
        dialoguePanel.Add(continueButton);
        overlay.Add(backgroundImage);
        overlay.Add(actorLayer);
        overlay.Add(cgImage);
        overlay.Add(dialoguePanel);
        root.Add(overlay);
    }

    static Image CreateFullScreenImage(string name, ScaleMode scaleMode)
    {
        var image = new Image { name = name, scaleMode = scaleMode, pickingMode = PickingMode.Ignore };
        SetFullScreen(image);
        return image;
    }

    static void SetFullScreen(VisualElement element)
    {
        element.style.position = Position.Absolute;
        element.style.left = 0f;
        element.style.right = 0f;
        element.style.top = 0f;
        element.style.bottom = 0f;
    }

    void BuildInitialStage(NarrativeSceneData scene)
    {
        actors.Clear();
        foreach (var slot in actorSlots)
        {
            slot.sprite = null;
            slot.style.display = DisplayStyle.None;
            slot.style.scale = new Scale(Vector3.one);
        }

        SetBackground(scene.background);
        HideCg();

        if (scene.actors == null) return;
        foreach (var actor in scene.actors)
        {
            var view = new ActorView
            {
                characterId = actor.characterId,
                displayName = string.IsNullOrWhiteSpace(actor.displayName) ? actor.characterId : actor.displayName,
                emotion = string.IsNullOrWhiteSpace(actor.emotion) ? "neutral" : actor.emotion,
                slot = actor.slot,
                visible = !actor.hidden,
                image = actorSlots[actor.slot]
            };
            actors[actor.characterId] = view;
            RefreshActor(view);
        }
    }

    void ShowLine(NarrativeLineData line)
    {
        if (!string.IsNullOrWhiteSpace(line.background)) SetBackground(line.background);
        if (line.returnToStage) HideCg();
        if (!string.IsNullOrWhiteSpace(line.cg)) ShowCg(line.cg);

        if (line.stage != null)
        {
            foreach (var command in line.stage) ApplyStageCommand(command);
        }

        if (!string.IsNullOrWhiteSpace(line.emotion) && actors.TryGetValue(line.speaker, out var speakerActor))
        {
            speakerActor.emotion = line.emotion;
            RefreshActor(speakerActor);
        }

        speakerLabel.text = ResolveSpeakerName(line);
        dialogueLabel.text = line.text;
        ApplySpeakerFocus(line.speaker);
        LineShown?.Invoke(line);
    }

    string ResolveSpeakerName(NarrativeLineData line)
    {
        if (!string.IsNullOrWhiteSpace(line.speakerName)) return line.speakerName;
        return actors.TryGetValue(line.speaker, out var actor) ? actor.displayName : line.speaker;
    }

    void ApplyStageCommand(NarrativeStageCommand command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.characterId)) return;
        string action = command.action?.Trim().ToLowerInvariant();

        if (!actors.TryGetValue(command.characterId, out var actor))
        {
            if (action != "show")
            {
                Debug.LogWarning($"[VN] Stage command '{action}' references unknown actor '{command.characterId}'.");
                return;
            }

            actor = new ActorView
            {
                characterId = command.characterId,
                displayName = command.characterId,
                emotion = string.IsNullOrWhiteSpace(command.emotion) ? "neutral" : command.emotion,
                slot = command.slot,
                visible = true,
                image = actorSlots[command.slot]
            };
            actors.Add(actor.characterId, actor);
        }

        switch (action)
        {
            case "show":
                MoveActor(actor, command.slot);
                actor.visible = true;
                if (!string.IsNullOrWhiteSpace(command.emotion)) actor.emotion = command.emotion;
                break;
            case "hide":
                actor.visible = false;
                break;
            case "move":
                MoveActor(actor, command.slot);
                break;
            case "emotion":
                if (!string.IsNullOrWhiteSpace(command.emotion)) actor.emotion = command.emotion;
                break;
        }

        RefreshActor(actor);
    }

    void MoveActor(ActorView actor, int newSlot)
    {
        if (actor.slot == newSlot) return;
        actor.image.style.display = DisplayStyle.None;
        actor.slot = newSlot;
        actor.image = actorSlots[newSlot];
    }

    void RefreshActor(ActorView actor)
    {
        actor.image.style.display = actor.visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (!actor.visible) return;

        if (visualLibrary != null && visualLibrary.TryGetPortrait(actor.characterId, actor.emotion, out var portrait))
        {
            actor.image.sprite = portrait;
        }
        else
        {
            actor.image.sprite = null;
            Debug.LogWarning($"[VN] No portrait found for '{actor.characterId}' emotion '{actor.emotion}'.");
        }
    }

    void ApplySpeakerFocus(string speakerId)
    {
        bool hasVisibleSpeaker = actors.TryGetValue(speakerId ?? string.Empty, out var speaker) && speaker.visible;
        foreach (var actor in actors.Values)
        {
            if (!actor.visible) continue;
            bool isSpeaker = hasVisibleSpeaker && ReferenceEquals(actor, speaker);
            actor.image.style.scale = new Scale(isSpeaker ? Vector3.one * speakingScale : Vector3.one);
            float brightness = !hasVisibleSpeaker || isSpeaker ? 1f : inactiveBrightness;
            actor.image.style.unityBackgroundImageTintColor = new Color(brightness, brightness, brightness, 1f);
        }
    }

    void SetBackground(string backgroundId)
    {
        if (string.IsNullOrWhiteSpace(backgroundId))
        {
            backgroundImage.sprite = null;
            return;
        }

        if (visualLibrary != null && visualLibrary.TryGetBackground(backgroundId, out var background))
        {
            backgroundImage.sprite = background;
        }
        else
        {
            backgroundImage.sprite = null;
            Debug.LogWarning($"[VN] Background '{backgroundId}' was not found in the visual library.");
        }
    }

    void ShowCg(string cgId)
    {
        if (visualLibrary != null && visualLibrary.TryGetCg(cgId, out var cg))
        {
            cgImage.sprite = cg;
            cgImage.style.display = DisplayStyle.Flex;
            actorLayer.style.display = DisplayStyle.None;
        }
        else
        {
            Debug.LogWarning($"[VN] CG '{cgId}' was not found in the visual library.");
        }
    }

    void HideCg()
    {
        cgImage.sprite = null;
        cgImage.style.display = DisplayStyle.None;
        actorLayer.style.display = DisplayStyle.Flex;
    }

    void FinishScene(bool skipped)
    {
        if (!IsPlaying) return;
        string sceneId = currentScene.id;
        currentScene = null;
        currentLineIndex = -1;
        overlay.style.display = DisplayStyle.None;
        SceneFinished?.Invoke(sceneId, skipped);
    }

    class ActorView
    {
        public string characterId;
        public string displayName;
        public string emotion;
        public int slot;
        public bool visible;
        public Image image;
    }
}
