using System;

[Serializable]
public class NarrativeSceneData
{
    public string id;
    public string title;
    public string characterId;
    public string background;
    public NarrativeActorData[] actors = Array.Empty<NarrativeActorData>();
    public NarrativeLineData[] lines = Array.Empty<NarrativeLineData>();
}

[Serializable]
public class NarrativeActorData
{
    public string characterId;
    public string displayName;
    public int slot;
    public string emotion = "neutral";
    public bool hidden;
}

[Serializable]
public class NarrativeLineData
{
    public string speaker;
    public string speakerName;
    public string emotion;
    public string text;

    // Optional visual changes applied before this line is displayed.
    public string background;
    public string cg;
    public bool returnToStage;
    public NarrativeStageCommand[] stage = Array.Empty<NarrativeStageCommand>();
}

[Serializable]
public class NarrativeStageCommand
{
    // Supported actions: show, hide, move, emotion.
    public string action;
    public string characterId;
    public int slot = -1;
    public string emotion;
}
