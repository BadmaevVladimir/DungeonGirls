using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// (доп.): проигрывает кадры Sprite[] на UI Toolkit Image по FPS — замена Animator/AnimationClip,
// т.к. спрайты живут в UI Toolkit (VisualElement), а не на GameObject/SpriteRenderer, на которых
// работает штатный Animator (см. обсуждение с пользователем).
//
// Раньше это был вложенный класс RunFlowController.SpriteFlipbook; вынесен в отдельный файл, когда
// тот же приём понадобился хабу (блеск воды на карте деревни, HubManager.Village.cs).
internal static class SpriteFlipbook
{
    public static IEnumerator Play(Image image, Sprite[] frames, float fps, bool loop, System.Action onComplete = null)
    {
        if (image == null || frames == null || frames.Length == 0)
        {
            onComplete?.Invoke();
            yield break;
        }

        float frameDuration = 1f / fps;
        do
        {
            foreach (var frame in frames)
            {
                if (frame == null) continue;
                image.sprite = frame;
                yield return new WaitForSeconds(frameDuration);
            }
        } while (loop);

        onComplete?.Invoke();
    }
}
