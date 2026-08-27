using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class NarrativeSmokeTest
{
    static int passed;
    static int failed;

    public static void Run()
    {
        passed = 0;
        failed = 0;
        string testDirectory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "NarrativeSmokeTest");

        try
        {
            Directory.CreateDirectory(testDirectory);
            File.WriteAllText(Path.Combine(testDirectory, "valid.json"), ValidSceneJson());

            var repository = new NarrativeSceneRepository(testDirectory);
            repository.Reload();
            Check(repository.Errors.Count == 0, "valid JSON repository has no errors");
            Check(repository.TryGetScene("vn_smoke", out var scene), "scene can be loaded by stable id");
            Check(scene.actors.Length == 2 && scene.lines.Length == 3, "actors and lines deserialize");

            var tooManyActors = JsonUtility.FromJson<NarrativeSceneData>(TooManyActorsJson());
            Check(NarrativeSceneValidator.Validate(tooManyActors).Exists(error => error.Contains("maximum is 5")), "validator rejects more than five visible actors");

            var stageCollision = JsonUtility.FromJson<NarrativeSceneData>(StageCollisionJson());
            Check(NarrativeSceneValidator.Validate(stageCollision).Exists(error => error.Contains("both use slot 1")), "validator rejects stage-command slot collisions");

            var root = new VisualElement();
            var gameObject = new GameObject("NarrativeSmokeTest_VNManager");
            var manager = gameObject.AddComponent<VNManager>();
            var sourceLibrary = Resources.Load<NarrativeVisualLibrary>("NarrativeVisualLibrary");
            Sprite fallbackPortrait = null;
            Check(sourceLibrary != null && sourceLibrary.TryGetPortrait("jennifer", "smile", out fallbackPortrait) && fallbackPortrait != null,
                "visual library resolves a missing emotion to the neutral portrait");

            var testLibrary = UnityEngine.Object.Instantiate(sourceLibrary);
            var testTexture = new Texture2D(2, 2);
            var testSprite = Sprite.Create(testTexture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            testLibrary.backgrounds = new[] { new NarrativeVisualLibrary.KeyedSprite { id = "tavern", sprite = testSprite } };
            testLibrary.cgs = new[] { new NarrativeVisualLibrary.KeyedSprite { id = "test_cg", sprite = testSprite } };
            manager.Initialize(root, testLibrary, repository);

            string finishedId = null;
            bool wasSkipped = true;
            manager.SceneFinished += (id, skipped) => { finishedId = id; wasSkipped = skipped; };

            Check(manager.PlayScene(scene), "VNManager accepts a valid scene");
            Check(manager.IsPlaying && manager.CurrentLine.text == "Первая реплика", "first line is shown immediately");
            Check(root.Q<VisualElement>("VNOverlay") != null, "full-screen VN overlay is created");
            Check(root.Q<Label>("VNSpeakerName").text == "Дженифер", "speaker name is displayed above dialogue");
            Check(root.Q<Image>("VNActor0") != null && root.Q<Image>("VNActor4") != null, "five actor slots are available");
            Check(root.Q<Image>("VNActor1").sprite == fallbackPortrait, "line emotion uses the configured portrait fallback");
            Check(root.Q<Image>("VNActor1").resolvedStyle.scale.value.x > 1f &&
                  root.Q<Image>("VNActor3").resolvedStyle.unityBackgroundImageTintColor.r < 1f,
                "speaker is enlarged and non-speakers are dimmed");

            manager.Advance();
            Check(root.Q<Image>("VNCg").resolvedStyle.display == DisplayStyle.Flex &&
                  root.Q<VisualElement>("VNActorLayer").resolvedStyle.display == DisplayStyle.None,
                "CG replaces the sprite stage");
            Check(manager.CurrentLine.text == "CG-реплика", "line order advances");
            manager.Advance();
            Check(manager.CurrentLine.returnToStage && root.Q<Image>("VNCg").resolvedStyle.display == DisplayStyle.None &&
                  root.Q<VisualElement>("VNActorLayer").resolvedStyle.display == DisplayStyle.Flex,
                "CG can return to sprite stage");
            manager.Advance();
            Check(!manager.IsPlaying && finishedId == "vn_smoke" && !wasSkipped, "scene completion event reports a normal finish");

            UnityEngine.Object.DestroyImmediate(gameObject);
            UnityEngine.Object.DestroyImmediate(testSprite);
            UnityEngine.Object.DestroyImmediate(testTexture);
            UnityEngine.Object.DestroyImmediate(testLibrary);
        }
        catch (Exception exception)
        {
            failed++;
            Debug.LogError("[NarrativeSmokeTest] Unhandled exception: " + exception);
        }

        Debug.Log($"[NarrativeSmokeTest] RESULT={(failed == 0 ? "PASS" : "FAIL")} ({passed} OK, {failed} failed)");
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    static void Check(bool condition, string description)
    {
        if (condition)
        {
            passed++;
            Debug.Log("[NarrativeSmokeTest] OK: " + description);
        }
        else
        {
            failed++;
            Debug.LogError("[NarrativeSmokeTest] FAIL: " + description);
        }
    }

    static string ValidSceneJson() => @"{
  ""id"": ""vn_smoke"",
  ""title"": ""VN Smoke"",
  ""characterId"": ""jennifer"",
  ""background"": ""tavern"",
  ""actors"": [
    { ""characterId"": ""jennifer"", ""displayName"": ""Дженифер"", ""slot"": 1, ""emotion"": ""neutral"" },
    { ""characterId"": ""sasha"", ""displayName"": ""Саша"", ""slot"": 3, ""emotion"": ""neutral"" }
  ],
  ""lines"": [
    { ""speaker"": ""jennifer"", ""text"": ""Первая реплика"", ""emotion"": ""smile"" },
    { ""speaker"": ""sasha"", ""text"": ""CG-реплика"", ""cg"": ""test_cg"" },
    { ""speaker"": ""jennifer"", ""text"": ""Назад"", ""returnToStage"": true }
  ]
}";

    static string TooManyActorsJson() => @"{
  ""id"": ""too_many"",
  ""title"": ""Too Many"",
  ""actors"": [
    { ""characterId"": ""a"", ""slot"": 0 },
    { ""characterId"": ""b"", ""slot"": 1 },
    { ""characterId"": ""c"", ""slot"": 2 },
    { ""characterId"": ""d"", ""slot"": 3 },
    { ""characterId"": ""e"", ""slot"": 4 },
    { ""characterId"": ""f"", ""slot"": 0 }
  ],
  ""lines"": [{ ""speaker"": ""a"", ""text"": ""Test"" }]
}";

    static string StageCollisionJson() => @"{
  ""id"": ""stage_collision"",
  ""title"": ""Stage Collision"",
  ""actors"": [
    { ""characterId"": ""a"", ""slot"": 0 },
    { ""characterId"": ""b"", ""slot"": 1 }
  ],
  ""lines"": [{
    ""speaker"": ""a"",
    ""text"": ""Test"",
    ""stage"": [{ ""action"": ""move"", ""characterId"": ""a"", ""slot"": 1 }]
  }]
}";
}
