using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Бой (раздел 4, 7.2) ====================

    int RollMonsterCount(int level) => MonsterEncounterBudget.RollMonsterCount(level);

    IEnumerator CombatRoomFlow(bool isBoss)
    {
        var enemies = new List<CombatantRuntime>();
        if (isBoss)
        {
            enemies.Add(CombatantFactory.CreateMonsterCombatant(bossData, dungeonManager.CurrentFloorNumber));
        }
        else
        {
            // 2.7/8.4: уровень монстра растёт с позицией уже пройденных комнат этажа в мешке.
            int monsterLevel = 1 + floorManager.RoomsCompletedOnFloor / 3;
            int count = RollMonsterCount(characterManager.Level);
            int remainingThreatBudget = MonsterEncounterBudget.GetThreatBudget(dungeonManager.CurrentFloorNumber);

            // 2.4: тиры суммируются — этаж 5 видит и тир-1, и тир-4 монстров, не только последний
            // открытый тир (см. "черновое распределение по этажам").
            var eligibleMonsters = regularMonsterPool.FindAll(m => m != null && m.minFloorTier <= dungeonManager.CurrentFloorNumber);
            if (eligibleMonsters.Count == 0)
            {
                eligibleMonsters = regularMonsterPool;
            }

            for (int i = 0; i < count; i++)
            {
                var data = MonsterEncounterBudget.RollAffordableMonster(eligibleMonsters, remainingThreatBudget);
                if (data == null)
                {
                    break;
                }

                enemies.Add(CombatantFactory.CreateMonsterCombatant(data, dungeonManager.CurrentFloorNumber, monsterLevel));
                remainingThreatBudget -= MonsterEncounterBudget.GetThreatCost(data);
            }
        }

        // 5.5 "Сигнализация" (провал): бой начинается с бафом +10% урона монстрам.
        if (characterManager.Modifiers.ConsumeMonsterDamageBuff())
        {
            foreach (var enemy in enemies)
            {
                foreach (var weapon in enemy.Weapons)
                {
                    weapon.DamageMin *= 1.1f;
                    weapon.DamageMax *= 1.1f;
                }
                enemy.ActiveDebuffs.Add(new ActiveDebuff
                {
                    Id = "alarm_damage_buff",
                    RemainingTime = float.PositiveInfinity,
                    IsBuff = true
                });
            }
        }

        // 5.5 "Идол" / 5.4 "Меч в камне" (провал): временные штрафы урона/скорости атаки на бой.
        float dmgMult = characterManager.Modifiers.ConsumeCombatDamageMultiplier();
        float spdMult = characterManager.Modifiers.ConsumeCombatAttackSpeedMultiplier();
        var originalStats = new List<(float min, float max, float spd)>();
        foreach (var weapon in characterManager.Combatant.Weapons)
        {
            originalStats.Add((weapon.DamageMin, weapon.DamageMax, weapon.AttackSpeed));
            weapon.DamageMin *= dmgMult;
            weapon.DamageMax *= dmgMult;
            weapon.AttackSpeed *= spdMult;
        }
        if (dmgMult < 0.999f)
        {
            characterManager.Combatant.ActiveDebuffs.Add(new ActiveDebuff
            {
                Id = "event_damage_down",
                RemainingTime = float.PositiveInfinity
            });
        }
        if (spdMult < 0.999f)
        {
            characterManager.Combatant.ActiveDebuffs.Add(new ActiveDebuff
            {
                Id = "event_attack_speed_down",
                RemainingTime = float.PositiveInfinity
            });
        }

        var activeCharacter = characterManager.Progress.Character;
        bool isBarbarian = activeCharacter.characterClass == CharacterClass.Barbarian;

        if (isBarbarian)
        {
            // 3.11 (Варвар) — Берсерк — ручной тумблер, не кулдаун-активка (см. ГДД 3.11, точная
            // цитата: "НЕ работает как обычный активный навык (нет кулдауна, нет авто-режима, нет
            // длительности)"). CombatManager.ConfigureUniqueActiveSkill/TryActivateUniqueActiveSkill
            // не используются для него вовсе — UI использует berserkToggle (см. ниже), не
            // activeSkillButton/autoModeToggle.
            combatManager.SetBerserkActive(false); // сброс на начало боя — тумблер не переносится между боями
        }
        else
        {
            int activeLevel = characterManager.Progress.UniqueActiveLevel;
            float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
            int hitCount = CombatManager.ResolveActiveSkillHitCount(activeCharacter.characterClass);
            combatManager.ConfigureUniqueActiveSkill(hitCount, activeMultiplier, activeCharacter.uniqueActiveSkill.cooldownSeconds, autoModeToggle.value, activeCharacter.uniqueActiveSkill.skillName, activeCharacter.uniqueActiveSkill.skillId);
        }

        combatManager.LogMessage += OnCombatLog;
        combatManager.HitResolved += OnHitResolved;
        combatManager.ActiveSkillActivated += OnActiveSkillActivated;
        ShowOnly(combatPanel);
        combatManager.StartCombat(characterManager.Combatant, enemies);
        BuildEnemyStageEntries(enemies);
        if (isBoss)
        {
            tutorialManager?.QueueOnce(TutorialContent.Boss);
        }
        else
        {
            tutorialManager?.QueueOnce(TutorialContent.CombatBasics);
            tutorialManager?.QueueOnce(TutorialContent.Defenses);
            tutorialManager?.QueueOnce(activeCharacter.characterClass switch
            {
                CharacterClass.Rogue => TutorialContent.VioletActive,
                CharacterClass.Barbarian => TutorialContent.SashaActive,
                _ => TutorialContent.JenniferActive
            });
        }

        while (combatManager.IsCombatActive)
        {
            UpdateCombatUI();
            yield return null;
        }

        UpdateCombatUI();
        UnsubscribeCombatEvents();

        for (int i = 0; i < characterManager.Combatant.Weapons.Count && i < originalStats.Count; i++)
        {
            characterManager.Combatant.Weapons[i].DamageMin = originalStats[i].min;
            characterManager.Combatant.Weapons[i].DamageMax = originalStats[i].max;
            characterManager.Combatant.Weapons[i].AttackSpeed = originalStats[i].spd;
        }

        if (!characterManager.IsAlive)
        {
            yield break;
        }

        // 8.2 (НОВОЕ): короткая пауза после победного удара — игрок успевает увидеть последний
        // эффект/всплывающее число урона (см. 4.7) до того, как сцена начнёт темнеть под награду.
        yield return new WaitForSeconds(0.45f);

        var levelsGained = characterManager.GrantExperience(rewardManager, isBoss ? ExperienceSource.Boss : ExperienceSource.CombatRoom, dungeonManager.CurrentFloorNumber);
        foreach (int reachedLevel in levelsGained)
        {
            bool activeUpgraded = characterManager.Progress.TryAutoUpgradeUniqueActiveAtLevel(reachedLevel);
            string activeUpgradeNotice = activeUpgraded
                ? $"Уникальный активный навык «{characterManager.Progress.Character.uniqueActiveSkill.skillName}» автоматически повышен до ур. {characterManager.Progress.UniqueActiveLevel}."
                : null;
            yield return LevelUpFlow(activeUpgradeNotice);
        }

        yield return ShowRewardChestFlow(dungeonManager.CurrentFloorNumber, isBoss);
    }

    void UnsubscribeCombatEvents()
    {
        combatManager.LogMessage -= OnCombatLog;
        combatManager.HitResolved -= OnHitResolved;
        combatManager.ActiveSkillActivated -= OnActiveSkillActivated;
    }

    void OnCombatLog(string message)
    {
        LogEvent(message);
    }

    // 7.2: общий персистентный лог забега — сюда пишутся боевые события (4.5), результаты
    // комнат/квестов/ловушек, левел-апы и т.д. Виден на отдельной панели вне зависимости от
    // текущей фазы забега (не только во время боя).
    void LogEvent(string message)
    {
        runLogLines.Add(message);
        if (runLogLines.Count > 200)
        {
            runLogLines.RemoveAt(0);
        }

        RefreshRunLog();
    }

    void RefreshRunLog()
    {
        runLogText.text = string.Join("\n", runLogLines);
        runLogScroll.schedule.Execute(() => runLogScroll.scrollOffset = new Vector2(0f, float.MaxValue));
    }

    void UpdateCombatUI()
    {
        ShowOnly(combatPanel);

        var player = combatManager.Player;
        playerStageSprite.sprite = player.Sprite;
        playerNameLabel.text = $"{player.DisplayName} (ур. {characterManager.Level})";
        float playerHpPercent = player.MaxHP > 0f ? Mathf.Clamp01(player.CurrentHP / player.MaxHP) * 100f : 0f;
        playerHpFill.style.width = new Length(playerHpPercent, LengthUnit.Percent);
        playerHpText.text = $"{Mathf.Max(player.CurrentHP, 0f):F0}/{player.MaxHP:F0}";
        playerDefenseText.text = $"Защита: {Mathf.Max(player.PhysicalDefenseCurrent, 0f):F0}/{player.PhysicalDefenseMax:F0}";
        playerShieldText.text = $"Щит: {Mathf.Max(player.MagicShieldCurrent, 0f):F0}/{player.MagicShieldMax:F0}";

        bool isBarbarianCombat = characterManager.Progress.Character.characterClass == CharacterClass.Barbarian;
        float rage = player.Rage;
        rageIndicator.EnableInClassList("hidden", !isBarbarianCombat);
        if (isBarbarianCombat)
        {
            rageText.text = $"ЯРОСТЬ: {rage:F0}%";
            rageFill.style.width = new Length(Mathf.Clamp(rage, 0f, 100f), LengthUnit.Percent);
            rageIndicator.EnableInClassList("rage-indicator-high", rage >= 70f);
        }

        bool isRogueCombat = characterManager.Progress.Character.characterClass == CharacterClass.Rogue;
        bool showStealth = isRogueCombat && player.IsStealthed;
        stealthIndicator.EnableInClassList("hidden", !showStealth);
        playerStageWrapper.EnableInClassList("stealth-stage-active", showStealth);
        if (showStealth)
        {
            string crits = player.SmokeBombGuaranteedCritsRemaining > 0
                ? $" • критов: {player.SmokeBombGuaranteedCritsRemaining}"
                : string.Empty;
            stealthText.text = $"◆ СКРЫТНОСТЬ {Mathf.Max(0f, player.StealthTimer):F1}с{crits}";
        }

        PopulateStatusContainer(playerStatusContainer, player, hideStealth: true);

        enemyListContainer.Clear();
        foreach (var enemy in combatManager.Enemies)
        {
            var box = new VisualElement();
            box.AddToClassList("combatant-box");
            if (enemy == player.Target && enemy.IsAlive)
            {
                box.AddToClassList("combatant-box-target");
            }

            var nameLabel = new Label(enemy.IsAlive ? enemy.DisplayName : $"{enemy.DisplayName} (погиб)");
            nameLabel.AddToClassList("combatant-name");
            box.Add(nameLabel);

            var hpBg = new VisualElement();
            hpBg.AddToClassList("hp-bar-bg");
            var hpFill = new VisualElement();
            hpFill.AddToClassList("hp-bar-fill");
            float hpPercent = enemy.MaxHP > 0f ? Mathf.Clamp01(enemy.CurrentHP / enemy.MaxHP) * 100f : 0f;
            hpFill.style.width = new Length(hpPercent, LengthUnit.Percent);
            hpBg.Add(hpFill);
            box.Add(hpBg);

            var hpText = new Label($"{Mathf.Max(enemy.CurrentHP, 0f):F0}/{enemy.MaxHP:F0}");
            hpText.AddToClassList("hp-text");
            box.Add(hpText);

            var statsText = new Label($"Защита: {Mathf.Max(enemy.PhysicalDefenseCurrent, 0f):F0}/{enemy.PhysicalDefenseMax:F0}  Щит: {Mathf.Max(enemy.MagicShieldCurrent, 0f):F0}/{enemy.MagicShieldMax:F0}");
            statsText.AddToClassList("stat-text");
            box.Add(statsText);

            var enemyStatusContainer = new VisualElement();
            enemyStatusContainer.AddToClassList("combat-status-container");
            PopulateStatusContainer(enemyStatusContainer, enemy);
            box.Add(enemyStatusContainer);

            if (enemy.IsAlive)
            {
                box.RegisterCallback<ClickEvent>(_ => combatManager.SetPlayerTarget(enemy));
            }

            enemyListContainer.Add(box);
        }

        // 7.2/10.6: крупные спрайты на "земле" сцены боя, отдельно от карточек имени/HP выше.
        // Персистентные элементы (построены один раз в BuildEnemyStageEntries) — тут только
        // обновление состояния кадр к кадру, без Clear()/пересоздания (иначе анимации на
        // дочерних элементах, вроде всплывающих цифр урона, уничтожались бы каждый тик).
        float stageFloorGap = GetStageFloorGapFromBottom();
        playerStageWrapper.style.marginBottom = stageFloorGap;

        foreach (var entry in enemyStageEntries)
        {
            entry.Wrapper.style.marginBottom = stageFloorGap;
            entry.Sprite.EnableInClassList("enemy-stage-sprite-dead", !entry.Combatant.IsAlive);
            UpdateStatusLabel(entry.StatusLabel, entry.Combatant);
        }

        activeSkillButton.EnableInClassList("hidden", isBarbarianCombat);
        autoModeToggle.EnableInClassList("hidden", isBarbarianCombat);
        berserkToggle.EnableInClassList("hidden", !isBarbarianCombat);

        if (!isBarbarianCombat)
        {
            bool ready = combatManager.IsActiveSkillReady;
            activeSkillButton.SetEnabled(!autoModeToggle.value && ready);
            activeSkillButton.text = ready ? "Активный навык (готов)" : $"Активный навык ({combatManager.ActiveSkillCooldownRemaining:F1}с)";
        }
        else
        {
            berserkToggle.SetValueWithoutNotify(player.IsBerserkActive);
        }
    }

    // 4.7: строится один раз при старте боя (список противников не меняется в процессе боя,
    // только их IsAlive) — размер спрайта зависит от количества (4.1: 1-3 в обычной комнате).
    void BuildEnemyStageEntries(List<CombatantRuntime> enemies)
    {
        enemyStageRow.Clear();
        enemyStageEntries.Clear();

        int enemyCount = enemies.Count;
        float enemySpriteSize = enemyCount switch
        {
            <= 1 => 384f,
            2 => 260f,
            _ => 190f
        };

        foreach (var enemy in enemies)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("enemy-stage-sprite-wrapper");
            wrapper.style.width = enemySpriteSize;
            wrapper.style.height = enemySpriteSize;

            var sprite = new Image { sprite = enemy.Sprite };
            sprite.AddToClassList("stage-sprite");
            sprite.AddToClassList("enemy-stage-sprite");
            wrapper.Add(sprite);

            var statusLabel = new Label();
            statusLabel.AddToClassList("stage-status-label");
            statusLabel.enableRichText = true;
            wrapper.Add(statusLabel);

            enemyStageRow.Add(wrapper);
            enemyStageEntries.Add(new EnemyStageEntry { Combatant = enemy, Wrapper = wrapper, Sprite = sprite, StatusLabel = statusLabel });
        }
    }

    // 4.7 [ОБНОВЛЕНО]: средне-насыщенные (не пастель, не кислотные) баф/дебафф-подписи — rich-text
    // цвет прямо в тексте лейбла, отдельного лейбла на строку не нужно. Пилюля-подложка (см. USS
    // .stage-status-label) скрывается целиком, когда эффектов нет — иначе висела бы пустой фон.
    void PopulateStatusContainer(VisualElement container, CombatantRuntime combatant, bool hideStealth = false)
    {
        if (container == null) return;
        container.Clear();

        var effects = CombatantStatusEffects.GetActiveEffects(combatant);
        foreach (var effect in effects)
        {
            if (hideStealth && effect.label == "Скрытность") continue;

            var badge = new Label(effect.label);
            badge.AddToClassList("combat-status-badge");
            badge.AddToClassList(effect.isBuff ? "combat-status-buff" : "combat-status-debuff");
            container.Add(badge);
        }

        container.EnableInClassList("hidden", container.childCount == 0);
    }

    void UpdateStatusLabel(Label label, CombatantRuntime combatant)
    {
        var effects = CombatantStatusEffects.GetActiveEffects(combatant);
        label.EnableInClassList("hidden", effects.Count == 0);
        if (effects.Count == 0)
        {
            label.text = string.Empty;
            return;
        }

        label.text = string.Join("\n", effects.ConvertAll(e => $"<color={(e.isBuff ? "#7CD66B" : "#E2645F")}>{e.label}</color>"));
    }

    VisualElement FindStageWrapper(CombatantRuntime combatant)
    {
        if (combatant == combatManager.Player)
        {
            return playerStageWrapper;
        }

        foreach (var entry in enemyStageEntries)
        {
            if (entry.Combatant == combatant)
            {
                return entry.Wrapper;
            }
        }

        return null;
    }

    // 4.7: единая точка подписки на CombatManager.HitResolved — всплывающая цифра урона + тряска
    // спрайта цели (тряска пропускается при полном блоке, см. GDD 4.7).
    void OnHitResolved(CombatantRuntime target, float damageToHP, bool isCrit, bool wasBlocked)
    {
        var wrapper = FindStageWrapper(target);
        if (wrapper == null)
        {
            return;
        }

        string text = wasBlocked ? "БЛОК" : damageToHP.ToString("F0");
        StartCoroutine(SpawnFloatingCombatText(wrapper, text, isCrit, wasBlocked));

        if (!wasBlocked)
        {
            StartCoroutine(ChestRevealAnimator.Shake(wrapper, 0.2f, new Vector3(5f, 3f, 0f), 6));
        }
    }

    // 4.7 (НОВОЕ): небольшой случайный горизонтальный разброс точки появления + небольшая
    // вариация времени появления — иначе несколько одновременных чисел (от пары монстров сразу,
    // от "3 быстрых атак") сливаются в одну нечитаемую массу.
    IEnumerator SpawnFloatingCombatText(VisualElement wrapper, string text, bool isCrit, bool isBlock)
    {
        yield return new WaitForSeconds(Random.Range(0f, 0.12f));

        var label = new Label(text);
        label.AddToClassList("floating-combat-text");
        if (isCrit)
        {
            label.AddToClassList("floating-combat-text-crit");
        }
        else if (isBlock)
        {
            label.AddToClassList("floating-combat-text-block");
        }

        float horizontalJitterPercent = Random.Range(-14f, 14f);
        label.style.left = new Length(50f + horizontalJitterPercent, LengthUnit.Percent);

        wrapper.Add(label);
        yield return FloatAndFadeOut(label);
    }

    IEnumerator FloatAndFadeOut(VisualElement label)
    {
        const float duration = 0.8f;
        const float riseDistance = 40f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            label.style.translate = new Translate(Length.Percent(-50), -riseDistance * progress, 0);
            label.style.opacity = 1f - progress;
            yield return null;
        }

        if (label.parent != null)
        {
            label.RemoveFromHierarchy();
        }
    }

    // 4.7: баннер активации уникального активного навыка — общий на всю боевую сцену, не
    // per-combatant. ~1.15с (fade in 0.15 / hold 0.85 / fade out 0.15), в пределах ГДД 1-1.2с.
    void OnActiveSkillActivated(CombatantRuntime user, string skillName)
    {
        if (skillBannerCoroutine != null)
        {
            StopCoroutine(skillBannerCoroutine);
        }
        skillBannerCoroutine = StartCoroutine(ShowSkillBanner(skillName));
    }

    IEnumerator ShowSkillBanner(string skillName)
    {
        const float fadeIn = 0.15f;
        const float hold = 0.85f;
        const float fadeOut = 0.15f;

        skillActivationBanner.RemoveFromClassList("hidden");
        skillActivationBanner.text = skillName;

        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.deltaTime;
            skillActivationBanner.style.opacity = Mathf.Clamp01(elapsed / fadeIn);
            yield return null;
        }
        skillActivationBanner.style.opacity = 1f;

        yield return new WaitForSeconds(hold);

        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.deltaTime;
            skillActivationBanner.style.opacity = 1f - Mathf.Clamp01(elapsed / fadeOut);
            yield return null;
        }

        skillActivationBanner.style.opacity = 0f;
        skillActivationBanner.AddToClassList("hidden");
        skillBannerCoroutine = null;
    }

    // Баг (2026-08-26): фон боя (Dungeon.png, 1536x1024) рендерится через ScaleAndCrop — на
    // экранах шире исходного соотношения (16:9-21:9 против 3:2 фона, платформа PC standalone)
    // кроп идёт по центру, и линия пола на фоне (~77.8% высоты исходного изображения, найдено
    // измерением пикселей — ряд ~797 из 1024) смещается относительно НИЖНЕГО края контейнера тем
    // сильнее, чем шире экран. Статичный процент в USS не может угнаться за этим на всём диапазоне
    // 16:9-21:9 (в 16:9 пол оказывается на ~17% высоты от низа, в 21:9 — уже на ~6%), поэтому
    // пересчитывается здесь по формуле "cover"-кропа при каждом обновлении боевого UI и
    // применяется как отступ снизу (margin-bottom) к спрайтам поверх их обычного
    // align-items: flex-end позиционирования (влево/вправо не меняется, только высота "ступней").
    const float combatBackgroundImageWidth = 1536f;
    const float combatBackgroundImageHeight = 1024f;
    const float combatBackgroundFloorRowFromTop = 797f;

    float GetStageFloorGapFromBottom()
    {
        float boxWidth = combatPanel.resolvedStyle.width;
        float boxHeight = combatPanel.resolvedStyle.height;
        if (boxWidth <= 0f || boxHeight <= 0f)
        {
            // Первый кадр после ShowOnly(combatPanel) — Yoga-layout ещё не посчитан
            // (resolvedStyle временно 0x0). Само-корректируется на следующем кадре.
            return 0f;
        }

        return ComputeStageFloorGap(boxWidth, boxHeight);
    }

    // Чистая часть формулы — вынесена из GetStageFloorGapFromBottom, чтобы быть тестируемой без
    // живого UIDocument/resolvedStyle. Баг (2026-08-26): фон боя (Dungeon.png, 1536x1024)
    // рендерится через ScaleAndCrop — на экранах шире исходного соотношения (16:9-21:9 против 3:2
    // фона) кроп идёт по центру, и линия пола на фоне смещается относительно нижнего края
    // контейнера тем сильнее, чем шире экран. Пересчитывается по формуле "cover"-кропа.
    public static float ComputeStageFloorGap(float boxWidth, float boxHeight)
    {
        float imageAspect = combatBackgroundImageWidth / combatBackgroundImageHeight;
        float boxAspect = boxWidth / boxHeight;

        float scale;
        float cropTop;
        if (boxAspect > imageAspect)
        {
            // Контейнер шире фона (типичный случай 16:9-21:9 против 3:2) — фон растягивается по
            // ширине контейнера, высота обрезается сверху и снизу поровну (центр-кроп).
            scale = boxWidth / combatBackgroundImageWidth;
            float scaledHeight = combatBackgroundImageHeight * scale;
            cropTop = (scaledHeight - boxHeight) / 2f;
        }
        else
        {
            // Контейнер уже фона (не целевой диапазон платформы, но не должен ломаться) — кроп по
            // высоте, вертикального кропа нет вовсе.
            scale = boxHeight / combatBackgroundImageHeight;
            cropTop = 0f;
        }

        float floorFromTop = combatBackgroundFloorRowFromTop * scale - cropTop;
        return Mathf.Max(0f, boxHeight - floorFromTop);
    }
}
