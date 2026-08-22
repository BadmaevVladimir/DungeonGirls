using UnityEngine;

public class GameManager : MonoBehaviour
{
    public HubManager HubManager;
    public DungeonManager DungeonManager;
    public SaveManager SaveManager;
    public RunFlowController RunFlowController;

    public void StartRun()
    {
        RunFlowController.BeginRunFromMenu();
    }

    public void ReturnToHub()
    {
        // Хаб вне скоупа Фазы 4 — возврат в неё сейчас означает возврат в главное меню.
        RunFlowController.ReturnToMainMenu();
    }
}
