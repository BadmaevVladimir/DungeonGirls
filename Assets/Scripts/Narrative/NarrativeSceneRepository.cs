using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class NarrativeSceneRepository
{
    readonly string scenesDirectory;
    readonly Dictionary<string, NarrativeSceneData> scenes = new Dictionary<string, NarrativeSceneData>(StringComparer.OrdinalIgnoreCase);
    readonly List<string> errors = new List<string>();
    bool loaded;

    public IReadOnlyList<string> Errors => errors;
    public IReadOnlyCollection<NarrativeSceneData> Scenes => scenes.Values;

    public NarrativeSceneRepository(string scenesDirectory = null)
    {
        this.scenesDirectory = string.IsNullOrWhiteSpace(scenesDirectory)
            ? Path.Combine(Application.streamingAssetsPath, "Content", "Scenes")
            : scenesDirectory;
    }

    public void Reload()
    {
        loaded = true;
        scenes.Clear();
        errors.Clear();

        if (!Directory.Exists(scenesDirectory))
        {
            errors.Add($"Narrative scenes directory does not exist: {scenesDirectory}");
            return;
        }

        foreach (string filePath in Directory.GetFiles(scenesDirectory, "*.json", SearchOption.AllDirectories))
        {
            NarrativeSceneData scene;
            try
            {
                scene = JsonUtility.FromJson<NarrativeSceneData>(File.ReadAllText(filePath));
            }
            catch (Exception exception)
            {
                errors.Add($"{filePath}: failed to read JSON: {exception.Message}");
                continue;
            }

            var validationErrors = NarrativeSceneValidator.Validate(scene, filePath);
            if (validationErrors.Count > 0)
            {
                errors.AddRange(validationErrors);
                continue;
            }

            if (scenes.ContainsKey(scene.id))
            {
                errors.Add($"{filePath}: duplicate scene id '{scene.id}'.");
                continue;
            }

            scenes.Add(scene.id, scene);
        }
    }

    public bool TryGetScene(string sceneId, out NarrativeSceneData scene)
    {
        if (!loaded) Reload();
        return scenes.TryGetValue(sceneId ?? string.Empty, out scene);
    }
}
