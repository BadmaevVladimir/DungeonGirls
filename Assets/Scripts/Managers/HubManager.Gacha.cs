using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

public partial class HubManager
{
    // ==================== Гача (8.5/11.1) ====================

    void RefreshGachaScreen()
    {
        gachaCurrencyLabel.text = $"Гача-валюта: {saveManager.Data.gachaCurrency}";
        gachaPullButton.SetEnabled(!gachaPullInProgress && HasValidGachaCharacterPool() && saveManager.Data.gachaCurrency >= GachaPullCost);
    }

    void TryPullGacha()
    {
        if (gachaPullInProgress || !HasValidGachaCharacterPool()) return;
        if (!GachaPool.RollResult(Random.value, Random.value, out var result)) return;

        CharacterData character = result.IsCharacter ? gachaCharacters[result.CharacterIndex] : null;
        int metaCurrencyAmount = result.IsCharacter ? 0 : result.CurrencyAmount;
        if (!saveManager.TryApplyGachaPull(GachaPullCost, character != null ? character.characterId : null, metaCurrencyAmount, out int copies))
        {
            RefreshGachaScreen();
            return;
        }

        // Результат уже атомарно сохранён вместе со списанием стоимости. Анимация ниже — только
        // презентация и может быть безопасно пропущена/прервана без потери награды.
        gachaPullInProgress = true;
        gachaResultPopup.style.display = DisplayStyle.None;
        RefreshGachaScreen();
        StartCoroutine(GachaPullFlow(result, character, copies));
    }

    IEnumerator GachaPullFlow(GachaPool.Result result, CharacterData character, int copies)
    {
        gachaRevealContainer.style.display = DisplayStyle.Flex;
        gachaChestSpriteImage.image = gachaChestClosedTexture;
        gachaChestSpriteImage.style.translate = new Translate(0, 0, 0);
        gachaReelStrip.Clear();
        gachaBackButton.SetEnabled(false);

        yield return ChestRevealAnimator.ShakeChest(gachaChestSpriteImage);
        gachaChestSpriteImage.image = gachaChestOpenTexture;

        VisualElement winningSlot = null;
        Image winningPortrait = null;
        int winningIndex = ChestRevealAnimator.ReelPadding + ChestRevealAnimator.WinningLogicalIndex;

        ItemTier jingleTier = result.IsCharacter ? ItemTier.Epic : result.CurrencyTier;
        AudioClip openClip = GachaOpenClipFor(jingleTier);
        TaggedAudio.Play(gachaOpenAudioSource, openClip, AudioCategory.SFX);

        void BuildSlot(int index, bool isWinning)
        {
            var slot = new VisualElement();
            slot.AddToClassList("chest-reel-icon");

            if (isWinning && result.IsCharacter)
            {
                if (characterSilhouetteRayOverlay != null)
                {
                    var ray = new Image { sprite = characterSilhouetteRayOverlay, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    ray.style.position = Position.Absolute;
                    ray.style.left = 0;
                    ray.style.right = 0;
                    ray.style.top = 0;
                    ray.style.bottom = 0;
                    slot.Add(ray);
                }

                winningPortrait = new Image { sprite = character.portrait, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                winningPortrait.style.width = Length.Percent(100);
                winningPortrait.style.height = Length.Percent(100);
                winningPortrait.style.unityBackgroundImageTintColor = Color.black;
                slot.Add(winningPortrait);
            }
            else if (isWinning)
            {
                slot.AddToClassList(ReelBackgroundClass(result.CurrencyTier));
                var amount = new Label($"+{result.CurrencyAmount}");
                amount.style.unityTextAlign = TextAnchor.MiddleCenter;
                amount.style.flexGrow = 1;
                slot.Add(amount);
            }
            else if (currencyIconCatalog != null && currencyIconCatalog.items != null && currencyIconCatalog.items.Length > 0)
            {
                var noiseItem = currencyIconCatalog.items[Random.Range(0, currencyIconCatalog.items.Length)];
                if (noiseItem != null)
                {
                    var noise = new Image { sprite = noiseItem.icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    noise.style.width = Length.Percent(100);
                    noise.style.height = Length.Percent(100);
                    slot.Add(noise);
                }
            }

            if (isWinning) winningSlot = slot;
            gachaReelStrip.Add(slot);
        }

        yield return ChestRevealAnimator.PlayReel(gachaReelStrip, gachaReelViewport, BuildSlot, gachaSkipButton, winningIndex, JumpGachaAudioToEnding);
        if (winningSlot != null) ChestRevealAnimator.SpawnBurst(winningSlot, gachaRevealContainer);

        if (winningPortrait != null)
        {
            Color tint = Color.black;
            bool revealComplete = false;
            DG.Tweening.DOTween.To(() => tint, value =>
            {
                tint = value;
                winningPortrait.style.unityBackgroundImageTintColor = value;
            }, Color.white, 0.18f).OnComplete(() => revealComplete = true);
            while (!revealComplete) yield return null;
        }

        yield return new WaitForSeconds(0.3f);
        gachaRevealContainer.style.display = DisplayStyle.None;

        // Первые встречи Вайолет и Саши запускаются сразу после первой копии, ещё до
        // текстового результата гачи. Это хаб-сцены: они сохраняются как просмотренные, но
        // не начисляют отношения (очки дают только сцены внутри забега).
        string firstSceneId = result.IsCharacter && character != null && copies == 1
            ? string.Equals(character.characterId, "violet", System.StringComparison.OrdinalIgnoreCase) ? VioletFirstGachaSceneId
            : string.Equals(character.characterId, "sasha", System.StringComparison.OrdinalIgnoreCase) ? SashaFirstGachaSceneId
            : null
            : null;
        if (!string.IsNullOrWhiteSpace(firstSceneId) && !saveManager.HasSeenVNScene(character.characterId, firstSceneId) &&
            vnManager != null && vnManager.TryPlayScene(firstSceneId))
        {
            while (vnManager.IsPlaying) yield return null;
        }

        gachaResultPopup.style.display = DisplayStyle.Flex;

        if (result.IsCharacter)
        {
            gachaResultLabel.text = $"Персонаж: {character.characterName} (копия №{copies})";
        }
        else
        {
            int shownAmount = 0;
            gachaResultLabel.text = $"Мета-валюта: +0 ({DisplayFormat.RarityLabel(result.CurrencyTier)})";
            bool countComplete = false;
            DG.Tweening.DOTween.To(() => shownAmount, value =>
            {
                shownAmount = value;
                gachaResultLabel.text = $"Мета-валюта: +{shownAmount} ({DisplayFormat.RarityLabel(result.CurrencyTier)})";
            }, result.CurrencyAmount, 0.5f).OnComplete(() => countComplete = true);
            while (!countComplete) yield return null;
        }

        gachaBackButton.SetEnabled(true);
        gachaPullInProgress = false;
        RefreshGachaScreen();
    }

    bool HasValidGachaCharacterPool()
    {
        if (gachaCharacters == null || gachaCharacters.Length != GachaPool.CharacterCount) return false;
        var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var character in gachaCharacters)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.characterId) || !ids.Add(character.characterId)) return false;
        }
        return true;
    }

    static string ReelBackgroundClass(ItemTier tier) => tier switch
    {
        ItemTier.Common => "chest-reel-icon-common",
        ItemTier.Rare => "chest-reel-icon-rare",
        _ => "chest-reel-icon-epic"
    };

    AudioClip GachaOpenClipFor(ItemTier tier) => tier switch
    {
        ItemTier.Common => gachaOpenCommonClip,
        ItemTier.Rare => gachaOpenRareClip,
        _ => gachaOpenEpicClip
    };

    // См. RunFlowController.Reward.JumpChestAudioToEnding — тот же джингл, тот же приём.
    void JumpGachaAudioToEnding()
    {
        if (gachaOpenAudioSource == null) return;
        if (ChestRevealAnimator.ShouldJumpToEnding(gachaOpenAudioSource.isPlaying, gachaOpenAudioSource.time))
        {
            gachaOpenAudioSource.time = ChestRevealAnimator.JingleBuildupDuration;
        }
    }
}
