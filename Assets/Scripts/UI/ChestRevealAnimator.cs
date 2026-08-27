using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

// GDD 8.2/11.1: общая механика тряски, ленты и UI Toolkit-burst для сундука и гачи.
public static class ChestRevealAnimator
{
    public const int ReelPadding = 3;
    public const int ReelLength = 20;
    public const int WinningLogicalIndex = 16; // GDD 11.1: слот 17 из 20 (индексация с нуля).
    public const float IconWidth = 64f;

    public static IEnumerator Shake(VisualElement element, float duration, Vector3 amplitude, int vibrato)
    {
        Vector3 shakeOffset = Vector3.zero;
        bool complete = false;
        DG.Tweening.DOTween.Punch(
            () => shakeOffset,
            value =>
            {
                shakeOffset = value;
                element.style.translate = new Translate(value.x, value.y, 0);
            },
            amplitude,
            duration,
            vibrato
        ).OnComplete(() => complete = true);

        while (!complete) yield return null;
        element.style.translate = new Translate(0, 0, 0);
    }

    public static IEnumerator ShakeChest(VisualElement chestSprite) =>
        Shake(chestSprite, 1f, new Vector3(6f, 4f, 0f), 10);

    public static IEnumerator PlayReel(
        VisualElement strip,
        VisualElement viewport,
        Action<int, bool> onBuildSlot,
        Button skipButton,
        int winningIndex)
    {
        int totalIcons = ReelLength + ReelPadding * 2;
        for (int i = 0; i < totalIcons; i++) onBuildSlot(i, i == winningIndex);

        yield return null;
        float viewportCenter = viewport.resolvedStyle.width / 2f;
        strip.style.left = viewportCenter - IconWidth / 2f - ReelPadding * IconWidth;

        bool skipped = false;
        void OnSkip() => skipped = true;
        if (skipButton != null) skipButton.clicked += OnSkip;

        float targetLeft = viewportCenter - IconWidth / 2f - winningIndex * IconWidth;
        bool tweenComplete = false;
        var tween = DG.Tweening.DOTween.To(
            () => strip.style.left.value.value,
            value => strip.style.left = value,
            targetLeft,
            4f
        ).SetEase(DG.Tweening.Ease.OutCubic).OnComplete(() => tweenComplete = true);

        while (!tweenComplete && !skipped) yield return null;
        if (skipped)
        {
            tween.Kill();
            strip.style.left = targetLeft;
        }

        if (skipButton != null) skipButton.clicked -= OnSkip;
    }

    public static void SpawnBurst(VisualElement anchor, VisualElement container)
    {
        const int burstCount = 8;
        const float burstDistance = 48f;
        const float burstDuration = 0.5f;
        const float dotSize = 8f;
        var burstColor = new Color(1f, 217f / 255f, 51f / 255f, 1f);
        float centerX = anchor.layout.x + anchor.layout.width / 2f;
        float centerY = anchor.layout.y + anchor.layout.height / 2f;

        for (int i = 0; i < burstCount; i++)
        {
            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = dotSize;
            dot.style.height = dotSize;
            dot.style.borderTopLeftRadius = dotSize / 2f;
            dot.style.borderTopRightRadius = dotSize / 2f;
            dot.style.borderBottomLeftRadius = dotSize / 2f;
            dot.style.borderBottomRightRadius = dotSize / 2f;
            dot.style.backgroundColor = burstColor;
            dot.style.left = centerX - dotSize / 2f;
            dot.style.top = centerY - dotSize / 2f;
            container.Add(dot);

            float angle = i / (float)burstCount * Mathf.PI * 2f + UnityEngine.Random.Range(-0.2f, 0.2f);
            float dx = Mathf.Cos(angle) * burstDistance;
            float dy = Mathf.Sin(angle) * burstDistance;
            float progress = 0f;
            DG.Tweening.DOTween.To(() => progress, value => progress = value, 1f, burstDuration)
                .SetEase(DG.Tweening.Ease.OutCubic)
                .OnUpdate(() =>
                {
                    dot.style.left = centerX - dotSize / 2f + dx * progress;
                    dot.style.top = centerY - dotSize / 2f + dy * progress;
                    dot.style.opacity = 1f - progress;
                })
                .OnComplete(() =>
                {
                    if (dot.parent != null) dot.RemoveFromHierarchy();
                });
        }
    }
}
