using System;
using System.Collections.Generic;

public static class NarrativeSceneValidator
{
    public const int MaxVisibleActors = 5;

    public static List<string> Validate(NarrativeSceneData scene, string source = null)
    {
        var errors = new List<string>();
        string prefix = string.IsNullOrWhiteSpace(source) ? "Narrative scene" : source;

        if (scene == null)
        {
            errors.Add($"{prefix}: JSON did not produce a scene.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(scene.id)) errors.Add($"{prefix}: id is required.");
        if (string.IsNullOrWhiteSpace(scene.title)) errors.Add($"{prefix}: title is required.");
        if (scene.lines == null || scene.lines.Length == 0) errors.Add($"{prefix}: at least one line is required.");

        var actorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var occupiedSlots = new HashSet<int>();
        var stageActors = new Dictionary<string, StageActorState>(StringComparer.OrdinalIgnoreCase);
        int visibleActorCount = 0;

        if (scene.actors != null)
        {
            for (int i = 0; i < scene.actors.Length; i++)
            {
                var actor = scene.actors[i];
                if (actor == null)
                {
                    errors.Add($"{prefix}: actors[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(actor.characterId))
                {
                    errors.Add($"{prefix}: actors[{i}].characterId is required.");
                }
                else if (!actorIds.Add(actor.characterId))
                {
                    errors.Add($"{prefix}: duplicate actor characterId '{actor.characterId}'.");
                }

                if (actor.slot < 0 || actor.slot >= MaxVisibleActors)
                {
                    errors.Add($"{prefix}: actor '{actor.characterId}' uses slot {actor.slot}; valid slots are 0-4.");
                }
                else if (!actor.hidden && !occupiedSlots.Add(actor.slot))
                {
                    errors.Add($"{prefix}: more than one initially visible actor uses slot {actor.slot}.");
                }

                if (!actor.hidden) visibleActorCount++;
                if (!string.IsNullOrWhiteSpace(actor.characterId) && !stageActors.ContainsKey(actor.characterId))
                {
                    stageActors.Add(actor.characterId, new StageActorState(actor.slot, !actor.hidden));
                }
            }
        }

        if (visibleActorCount > MaxVisibleActors)
        {
            errors.Add($"{prefix}: {visibleActorCount} actors are initially visible; maximum is {MaxVisibleActors}.");
        }

        if (scene.lines != null)
        {
            for (int i = 0; i < scene.lines.Length; i++)
            {
                var line = scene.lines[i];
                if (line == null)
                {
                    errors.Add($"{prefix}: lines[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line.speaker)) errors.Add($"{prefix}: lines[{i}].speaker is required.");
                if (string.IsNullOrWhiteSpace(line.text)) errors.Add($"{prefix}: lines[{i}].text is required.");

                if (line.stage == null) continue;
                for (int commandIndex = 0; commandIndex < line.stage.Length; commandIndex++)
                {
                    var command = line.stage[commandIndex];
                    if (command == null)
                    {
                        errors.Add($"{prefix}: lines[{i}].stage[{commandIndex}] is null.");
                        continue;
                    }

                    string action = command.action?.Trim().ToLowerInvariant();
                    if (action != "show" && action != "hide" && action != "move" && action != "emotion")
                    {
                        errors.Add($"{prefix}: lines[{i}].stage[{commandIndex}] has unknown action '{command.action}'.");
                    }

                    if (string.IsNullOrWhiteSpace(command.characterId))
                    {
                        errors.Add($"{prefix}: lines[{i}].stage[{commandIndex}].characterId is required.");
                    }

                    if ((action == "show" || action == "move") && (command.slot < 0 || command.slot >= MaxVisibleActors))
                    {
                        errors.Add($"{prefix}: lines[{i}].stage[{commandIndex}] uses slot {command.slot}; valid slots are 0-4.");
                    }

                    if (string.IsNullOrWhiteSpace(command.characterId) || string.IsNullOrWhiteSpace(action)) continue;
                    if (!stageActors.TryGetValue(command.characterId, out var actorState))
                    {
                        if (action != "show")
                        {
                            errors.Add($"{prefix}: lines[{i}].stage[{commandIndex}] references unknown actor '{command.characterId}'.");
                            continue;
                        }

                        actorState = new StageActorState(command.slot, false);
                        stageActors.Add(command.characterId, actorState);
                    }

                    switch (action)
                    {
                        case "show":
                            actorState.slot = command.slot;
                            actorState.visible = true;
                            break;
                        case "hide":
                            actorState.visible = false;
                            break;
                        case "move":
                            actorState.slot = command.slot;
                            break;
                    }
                }

                ValidateStageState(stageActors, errors, prefix, i);
            }
        }

        return errors;
    }

    static void ValidateStageState(Dictionary<string, StageActorState> stageActors, List<string> errors, string prefix, int lineIndex)
    {
        var slots = new Dictionary<int, string>();
        int visibleCount = 0;
        foreach (var pair in stageActors)
        {
            var actor = pair.Value;
            if (!actor.visible) continue;
            visibleCount++;
            if (actor.slot < 0 || actor.slot >= MaxVisibleActors) continue;
            if (slots.TryGetValue(actor.slot, out var occupant))
            {
                errors.Add($"{prefix}: after lines[{lineIndex}], actors '{occupant}' and '{pair.Key}' both use slot {actor.slot}.");
            }
            else
            {
                slots.Add(actor.slot, pair.Key);
            }
        }

        if (visibleCount > MaxVisibleActors)
        {
            errors.Add($"{prefix}: after lines[{lineIndex}], {visibleCount} actors are visible; maximum is {MaxVisibleActors}.");
        }
    }

    sealed class StageActorState
    {
        public int slot;
        public bool visible;

        public StageActorState(int slot, bool visible)
        {
            this.slot = slot;
            this.visible = visible;
        }
    }
}
