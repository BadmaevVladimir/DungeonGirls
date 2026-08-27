using UnityEngine;

public class GameManager : MonoBehaviour
{
    public HubManager HubManager;
    public DungeonManager DungeonManager;
    public SaveManager SaveManager;
    public RunFlowController RunFlowController;

    public void StartRun()
    {
        // Task 4: BeginRunFromMenu удалён — теперь между меню и стартом забега есть экран выбора
        // персонажа (Плут/Варвар иначе оставались бы недостижимы из этого пути тоже).
        RunFlowController.OpenCharacterSelect();
    }

    public void ReturnToHub()
    {
        // Хаб вне скоупа Фазы 4 — возврат в неё сейчас означает возврат в главное меню.
        RunFlowController.ReturnToMainMenu();
    }
}
