using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Одноразовый батч-визард: проставляет RunFlowController.bossPool в SampleScene. Поле приватное
// ([SerializeField]), поэтому пишем через SerializedObject — родной сериализацией Unity, а не
// правкой YAML сцены руками. Запуск:
//   Unity.exe -batchmode -nographics -projectPath <проект> -executeMethod BossPoolSceneWirer.Run
//
// ВАЖНО (стоило двух неудачных прогонов): ассет пула грузится ТОЛЬКО ПОСЛЕ OpenScene. OpenScene в
// режиме Single выгружает несвязанные ассеты, поэтому ссылка, взятая до него, приходит к моменту
// присваивания уже уничтоженной — и в поле молча уезжает null.
public static class BossPoolSceneWirer
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string PoolPath = "Assets/ScriptableObjects/Bosses/BossPool_Main.asset";

    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var pool = AssetDatabase.LoadAssetAtPath<BossPoolData>(PoolPath);
        if (pool == null)
        {
            Debug.LogError($"[BossPoolSceneWirer] Не найден пул: {PoolPath}");
            EditorApplication.Exit(1);
            return;
        }

        var controller = Object.FindAnyObjectByType<RunFlowController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            Debug.LogError("[BossPoolSceneWirer] В сцене нет RunFlowController");
            EditorApplication.Exit(1);
            return;
        }

        var so = new SerializedObject(controller);
        var property = so.FindProperty("bossPool");
        if (property == null)
        {
            Debug.LogError("[BossPoolSceneWirer] У RunFlowController нет сериализованного поля bossPool");
            EditorApplication.Exit(1);
            return;
        }

        property.objectReferenceValue = pool;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // Перечитываем сцену с диска: единственная проверка, которой стоит верить.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var reread = Object.FindAnyObjectByType<RunFlowController>(FindObjectsInactive.Include);
        var verified = new SerializedObject(reread).FindProperty("bossPool").objectReferenceValue;

        if (verified == null)
        {
            Debug.LogError("[BossPoolSceneWirer] ПРОВАЛ: после сохранения и перечитывания bossPool всё ещё null");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[BossPoolSceneWirer] УСПЕХ: bossPool = {verified.name}");
        EditorApplication.Exit(0);
    }
}
