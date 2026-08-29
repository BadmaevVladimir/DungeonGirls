using System;
using System.IO;
using System.Linq;
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
            NarrativeSetupTool.CreateOrUpdateDefaultVisualLibrary();
            Directory.CreateDirectory(testDirectory);
            File.WriteAllText(Path.Combine(testDirectory, "valid.json"), ValidSceneJson());

            var repository = new NarrativeSceneRepository(testDirectory);
            repository.Reload();
            Check(repository.Errors.Count == 0, "valid JSON repository has no errors");
            Check(repository.TryGetScene("vn_smoke", out var scene), "scene can be loaded by stable id");
            Check(scene.actors.Length == 2 && scene.lines.Length == 3, "actors and lines deserialize");

            var shippedRepository = new NarrativeSceneRepository();
            shippedRepository.Reload();
            Check(shippedRepository.Errors.Count == 0, "shipped narrative repository has no validation errors");
            Check(shippedRepository.TryGetScene("jennifer_intro_tavern", out var openingScene),
                "Jennifer opening scene loads by stable id");
            Check(openingScene != null && openingScene.lines.Length == 10 && openingScene.background == "tavern_bg" &&
                  openingScene.actors.Length == 1 && openingScene.actors[0].hidden && openingScene.actors[0].slot == 2 &&
                  openingScene.lines[0].speaker == "narrator" && openingScene.lines[3].speaker == "narrator" &&
                  openingScene.lines[6].cg == "Jennifer_01",
                "opening scene preserves four authored narrator paragraphs, centered hidden Jennifer, tavern background and CG cue");

            string[] newSceneIds =
            {
                "jennifer_camp_low", "jennifer_camp_high", "jennifer_hot_springs_low", "jennifer_hot_springs_high",
                "violet_intro_gacha", "violet_camp_low", "violet_camp_high"
            };
            Check(newSceneIds.All(id => shippedRepository.TryGetScene(id, out var authored) && authored != null && authored.lines.Length > 0),
                "all seven newly authored Jennifer/Violet scenes load and validate");

            var tooManyActors = JsonUtility.FromJson<NarrativeSceneData>(TooManyActorsJson());
            Check(NarrativeSceneValidator.Validate(tooManyActors).Exists(error => error.Contains("maximum is 5")), "validator rejects more than five visible actors");

            var stageCollision = JsonUtility.FromJson<NarrativeSceneData>(StageCollisionJson());
            Check(NarrativeSceneValidator.Validate(stageCollision).Exists(error => error.Contains("both use slot 1")), "validator rejects stage-command slot collisions");

            var root = new VisualElement();
            var gameObject = new GameObject("NarrativeSmokeTest_VNManager");
            var manager = gameObject.AddComponent<VNManager>();
            var sourceLibrary = Resources.Load<NarrativeVisualLibrary>("NarrativeVisualLibrary");
            Sprite fallbackPortrait = null;
            Sprite tavernBackground = null;
            Sprite jenniferCg = null;
            Check(sourceLibrary != null && sourceLibrary.TryGetPortrait("jennifer", "smile", out fallbackPortrait) && fallbackPortrait != null,
                "visual library resolves a missing emotion to the neutral portrait");
            Check(sourceLibrary != null && sourceLibrary.TryGetBackground("tavern_bg", out tavernBackground) && tavernBackground != null,
                "visual library contains tavern_bg");
            Check(sourceLibrary != null && sourceLibrary.TryGetCg("Jennifer_01", out jenniferCg) && jenniferCg != null,
                "visual library contains Jennifer_01 CG");
            Check(sourceLibrary != null && sourceLibrary.TryGetBackground("Dungeon_dialog", out var dungeonBackground) && dungeonBackground != null &&
                  sourceLibrary.TryGetBackground("Hot_springs_bg", out var springsBackground) && springsBackground != null &&
                  sourceLibrary.TryGetCg("Jennifer_05", out var jenniferCg05) && jenniferCg05 != null &&
                  sourceLibrary.TryGetCg("Violet_03", out var violetCg03) && violetCg03 != null,
                "visual library contains new dungeon/hot-springs backgrounds and Jennifer/Violet CGs");

            var testLibrary = UnityEngine.Object.Instantiate(sourceLibrary);
            var testTexture = new Texture2D(2, 2);
            var testSprite = Sprite.Create(testTexture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            testLibrary.backgrounds = new[]
            {
                new NarrativeVisualLibrary.KeyedSprite { id = "tavern", sprite = testSprite },
                new NarrativeVisualLibrary.KeyedSprite { id = "tavern_bg", sprite = tavernBackground }
            };
            testLibrary.cgs = new[]
            {
                new NarrativeVisualLibrary.KeyedSprite { id = "test_cg", sprite = testSprite },
                new NarrativeVisualLibrary.KeyedSprite { id = "Jennifer_01", sprite = jenniferCg }
            };
            manager.Initialize(root, testLibrary, repository);

            string finishedId = null;
            bool wasSkipped = true;
            NarrativeSceneData completedScene = null;
            manager.SceneFinished += (id, skipped) => { finishedId = id; wasSkipped = skipped; };
            manager.SceneCompleted += (finishedScene, skipped) => completedScene = finishedScene;

            Time.timeScale = 1f;
            Check(manager.PlayScene(scene), "VNManager accepts a valid scene");
            Check(Time.timeScale == 0f, "VN scene pauses scaled gameplay while open");
            Check(manager.IsPlaying && manager.CurrentLine.text == "Первая реплика", "first line is shown immediately");
            Check(root.Q<VisualElement>("VNOverlay") != null, "full-screen VN overlay is created");
            Check(root.Q<Button>("VNSkipButton") != null, "every VN scene has a visible skip button");
            Check(root.Q<Label>("VNSpeakerName").text == "Дженифер", "speaker name is displayed above dialogue");
            Check(root.Q<Image>("VNActor0") != null && root.Q<Image>("VNActor4") != null, "five actor slots are available");
            Check(root.Q<Image>("VNActor1").sprite == fallbackPortrait, "line emotion uses the configured portrait fallback");
            Check(Mathf.Approximately(root.Q<Image>("VNActor1").resolvedStyle.scale.value.x, 1f) &&
                  root.Q<Image>("VNActor3").resolvedStyle.unityBackgroundImageTintColor.r < 1f,
                "speaker keeps a stable size while non-speakers are dimmed");

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
            Check(!manager.IsPlaying && finishedId == "vn_smoke" && !wasSkipped && completedScene == scene && Time.timeScale == 1f,
                "scene completion reports the full scene and restores gameplay time");

            finishedId = null;
            completedScene = null;
            Check(manager.PlayScene(openingScene), "shipped Jennifer opening scene starts in VNManager");
            Check(root.Q<Image>("VNBackground").sprite == tavernBackground &&
                  root.Q<Image>("VNActor2").resolvedStyle.display == DisplayStyle.None,
                "opening scene begins with tavern background and hidden Jennifer");
            for (int i = 0; i < 4; i++) manager.Advance();
            Check(root.Q<Image>("VNActor2").resolvedStyle.display == DisplayStyle.Flex &&
                  Mathf.Approximately(root.Q<Image>("VNActor2").resolvedStyle.scale.value.x, 1f),
                "Jennifer appears in the center on her first line");
            manager.Advance();
            Check(root.Q<Image>("VNActor2").resolvedStyle.unityBackgroundImageTintColor.r < 1f,
                "visible Jennifer is dimmed while the off-screen Hero speaks");
            manager.Advance();
            Check(root.Q<Image>("VNCg").sprite == jenniferCg &&
                  root.Q<Image>("VNCg").resolvedStyle.display == DisplayStyle.Flex &&
                  root.Q<VisualElement>("VNActorLayer").resolvedStyle.display == DisplayStyle.None,
                "Jennifer CG replaces the sprite stage on the authored line");
            manager.Skip();
            Check(!manager.IsPlaying && finishedId == "jennifer_intro_tavern" && wasSkipped && completedScene == openingScene,
                "skip finishes the scene and reports it for seen-scene persistence");

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
