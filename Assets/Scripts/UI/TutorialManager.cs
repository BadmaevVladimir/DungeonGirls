using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Общий runtime-слой обучения для хаба и забега. Компонент создаётся на том же GameObject, что
// UIDocument, поэтому отдельная сценовая ссылка не нужна и оба существующих оркестратора получают
// один экземпляр поверх общего VisualTree.
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    UIDocument uiDocument;
    SaveManager saveManager;
    VisualElement root;
    VisualElement tutorialOverlay;
    Label tutorialTitle;
    Label tutorialBody;
    Button tutorialContinueButton;
    VisualElement helpScreen;
    ScrollView helpScrollView;
    Button helpCloseButton;
    VisualElement globalTooltip;
    Label globalTooltipTitle;
    Label globalTooltipBody;

    readonly Queue<string> queuedHints = new Queue<string>();
    readonly HashSet<string> queuedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<VisualElement> tooltipTargets = new HashSet<VisualElement>();
    string activeHintId;
    bool activeIsReference;
    bool pausedByOverlay;
    float timeScaleBeforeOverlay = 1f;
    bool initialized;

    public static TutorialManager GetOrCreate(UIDocument document, SaveManager saves)
    {
        if (Instance == null)
        {
            Instance = document.GetComponent<TutorialManager>();
            if (Instance == null) Instance = document.gameObject.AddComponent<TutorialManager>();
        }

        Instance.Initialize(document, saves);
        return Instance;
    }

    public void Initialize(UIDocument document, SaveManager saves)
    {
        uiDocument = document != null ? document : uiDocument;
        saveManager = saves != null ? saves : saveManager;
        if (initialized || uiDocument == null) return;

        root = uiDocument.rootVisualElement;
        tutorialOverlay = root.Q<VisualElement>("TutorialOverlay");
        tutorialTitle = root.Q<Label>("TutorialTitle");
        tutorialBody = root.Q<Label>("TutorialBody");
        tutorialContinueButton = root.Q<Button>("TutorialContinueButton");
        helpScreen = root.Q<VisualElement>("HelpScreen");
        helpScrollView = root.Q<ScrollView>("HelpScrollView");
        helpCloseButton = root.Q<Button>("HelpCloseButton");
        globalTooltip = root.Q<VisualElement>("GlobalTooltip");
        globalTooltipTitle = root.Q<Label>("GlobalTooltipTitle");
        globalTooltipBody = root.Q<Label>("GlobalTooltipBody");

        if (tutorialOverlay == null || tutorialTitle == null || tutorialBody == null || tutorialContinueButton == null ||
            helpScreen == null || helpScrollView == null || helpCloseButton == null || globalTooltip == null)
        {
            Debug.LogError("[Tutorial] В GameRoot.uxml отсутствуют обязательные элементы обучения.");
            return;
        }

        tutorialContinueButton.clicked += CloseCurrentHint;
        helpCloseButton.clicked += CloseHelp;
        foreach (var buttonName in new[] { "HelpButton", "RunHelpButton", "ResultsHelpButton" })
        {
            var button = root.Q<Button>(buttonName);
            if (button != null) button.clicked += OpenHelp;
        }

        globalTooltip.pickingMode = PickingMode.Ignore;
        BuildHelpContent();
        initialized = true;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RestoreTimeScale();
    }

    public void QueueOnce(string hintId)
    {
        if (!initialized || string.IsNullOrWhiteSpace(hintId) || !TutorialContent.TryGet(hintId, out _) ||
            saveManager == null || saveManager.HasSeenTutorialHint(hintId) || activeHintId == hintId || !queuedIds.Add(hintId))
        {
            return;
        }

        queuedHints.Enqueue(hintId);
        TryShowNext();
    }

    public void ShowReference(string title, string body)
    {
        if (!initialized || tutorialOverlay.style.display == DisplayStyle.Flex || helpScreen.style.display == DisplayStyle.Flex) return;
        activeHintId = null;
        activeIsReference = true;
        ShowOverlay(title, body);
    }

    void TryShowNext()
    {
        if (!initialized || tutorialOverlay.style.display == DisplayStyle.Flex || helpScreen.style.display == DisplayStyle.Flex || queuedHints.Count == 0) return;

        string id = queuedHints.Dequeue();
        queuedIds.Remove(id);
        if (saveManager != null && saveManager.HasSeenTutorialHint(id))
        {
            TryShowNext();
            return;
        }

        if (!TutorialContent.TryGet(id, out var entry)) return;
        activeHintId = id;
        activeIsReference = false;
        ShowOverlay(entry.Title, entry.Body);
    }

    void ShowOverlay(string title, string body)
    {
        HideTooltip();
        PauseTimeScale();
        tutorialTitle.text = title;
        tutorialBody.text = body;
        tutorialOverlay.style.display = DisplayStyle.Flex;
        tutorialOverlay.BringToFront();
        tutorialContinueButton.Focus();
    }

    void CloseCurrentHint()
    {
        if (tutorialOverlay.style.display != DisplayStyle.Flex) return;
        tutorialOverlay.style.display = DisplayStyle.None;
        if (!activeIsReference && !string.IsNullOrWhiteSpace(activeHintId) && saveManager != null)
        {
            saveManager.MarkTutorialHintSeen(activeHintId);
        }

        activeHintId = null;
        activeIsReference = false;
        RestoreTimeScale();
        TryShowNext();
    }

    public void OpenHelp()
    {
        if (!initialized || tutorialOverlay.style.display == DisplayStyle.Flex) return;
        HideTooltip();
        PauseTimeScale();
        helpScreen.style.display = DisplayStyle.Flex;
        helpScreen.BringToFront();
        helpCloseButton.Focus();
    }

    void CloseHelp()
    {
        if (helpScreen.style.display != DisplayStyle.Flex) return;
        helpScreen.style.display = DisplayStyle.None;
        RestoreTimeScale();
        TryShowNext();
    }

    void BuildHelpContent()
    {
        helpScrollView.Clear();
        foreach (var entry in TutorialContent.HelpEntries)
        {
            var section = new VisualElement();
            section.AddToClassList("help-section");
            var title = new Label(entry.Title);
            title.AddToClassList("help-section-title");
            var body = new Label(entry.Body);
            body.AddToClassList("help-section-body");
            section.Add(title);
            section.Add(body);
            helpScrollView.Add(section);
        }
    }

    void PauseTimeScale()
    {
        if (pausedByOverlay) return;
        timeScaleBeforeOverlay = Time.timeScale;
        if (Time.timeScale > 0f)
        {
            Time.timeScale = 0f;
            pausedByOverlay = true;
        }
    }

    void RestoreTimeScale()
    {
        if (!pausedByOverlay) return;
        Time.timeScale = timeScaleBeforeOverlay;
        pausedByOverlay = false;
    }

    public void BindTooltip(VisualElement target, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        BindTooltip(target, title, () => body);
    }

    public void BindTooltip(VisualElement target, string title, Func<string> bodyProvider)
    {
        if (!initialized || target == null || bodyProvider == null || !tooltipTargets.Add(target)) return;

        target.RegisterCallback<PointerEnterEvent>(evt =>
        {
            string body = bodyProvider();
            if (!string.IsNullOrWhiteSpace(body)) ShowTooltip(title, body, evt.position);
        });
        target.RegisterCallback<PointerMoveEvent>(evt => PositionTooltip(evt.position));
        target.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
        target.RegisterCallback<DetachFromPanelEvent>(_ => HideTooltip());
    }

    void ShowTooltip(string title, string body, Vector2 panelPosition)
    {
        if (tutorialOverlay.style.display == DisplayStyle.Flex || helpScreen.style.display == DisplayStyle.Flex) return;
        globalTooltipTitle.text = title ?? string.Empty;
        globalTooltipBody.text = body;
        globalTooltip.style.display = DisplayStyle.Flex;
        PositionTooltip(panelPosition);
        globalTooltip.BringToFront();
    }

    void PositionTooltip(Vector2 panelPosition)
    {
        if (globalTooltip.style.display != DisplayStyle.Flex) return;
        const float offset = 14f;
        const float fallbackWidth = 390f;
        const float fallbackHeight = 180f;
        float rootWidth = root.resolvedStyle.width > 0f ? root.resolvedStyle.width : Screen.width;
        float rootHeight = root.resolvedStyle.height > 0f ? root.resolvedStyle.height : Screen.height;
        float width = globalTooltip.resolvedStyle.width > 0f ? globalTooltip.resolvedStyle.width : fallbackWidth;
        float height = globalTooltip.resolvedStyle.height > 0f ? globalTooltip.resolvedStyle.height : fallbackHeight;
        globalTooltip.style.left = Mathf.Clamp(panelPosition.x + offset, 8f, Mathf.Max(8f, rootWidth - width - 8f));
        globalTooltip.style.top = Mathf.Clamp(panelPosition.y + offset, 8f, Mathf.Max(8f, rootHeight - height - 8f));
    }

    public void HideTooltip()
    {
        if (globalTooltip != null) globalTooltip.style.display = DisplayStyle.None;
    }
}
