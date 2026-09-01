using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Бой (раздел 4, 7.2) ====================

    // (доп.): проигрывает кадры Sprite[] на UI Toolkit Image по FPS — замена Animator/AnimationClip,
    // т.к. боевые спрайты живут в UI Toolkit (VisualElement), а не на GameObject/SpriteRenderer,
    // на которых работает штатный Animator (см. обсуждение с пользователем).
    static class SpriteFlipbook
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

    // (доп.): true, пока у играющего персонажа есть готовые анимации (см. PlayableCharacterAnimations
    // — распознаётся по DisplayName, тем же паттерном, что "Дымовая граната"/"3 быстрые атаки" выше
    // по коду, т.к. у CombatantRuntime нет отдельного characterId).
    bool HasAnimatedSprite => combatManager.Player != null && PlayableCharacterAnimations.Idle(combatManager.Player.DisplayName) != null;

    void StartPlayerIdleFlipbook()
    {
        if (!HasAnimatedSprite) return;
        var frames = PlayableCharacterAnimations.Idle(combatManager.Player.DisplayName);
        if (playerFlipbookCoroutine != null) StopCoroutine(playerFlipbookCoroutine);
        playerFlipbookCoroutine = StartCoroutine(SpriteFlipbook.Play(playerStageSprite, frames, 6f, loop: true));
    }

    void PlayPlayerOneShotFlipbook(Sprite[] frames, float fps, System.Action onComplete = null)
    {
        if (!HasAnimatedSprite || frames == null || frames.Length == 0)
        {
            onComplete?.Invoke();
            return;
        }
        if (playerFlipbookCoroutine != null) StopCoroutine(playerFlipbookCoroutine);
        playerFlipbookCoroutine = StartCoroutine(SpriteFlipbook.Play(playerStageSprite, frames, fps, loop: false, onComplete: () =>
        {
            StartPlayerIdleFlipbook();
            onComplete?.Invoke();
        }));
    }

    void StopPlayerFlipbook()
    {
        if (playerFlipbookCoroutine != null)
        {
            StopCoroutine(playerFlipbookCoroutine);
            playerFlipbookCoroutine = null;
        }

        // Защита от зависшей блокировки, если бой закончился/прервался прямо во время анимации
        // скилла (её onComplete тогда не успевает снять AttackLocked сам) — Player переиспользуется
        // между боями, залипший флаг иначе перманентно отключил бы обычные атаки во всех следующих боях.
        if (combatManager.Player != null)
        {
            combatManager.Player.AttackLocked = false;
        }
        capturingSkillHits = false;
        pendingSkillHits.Clear();
        playerSkillAnimationPlaying = false;
        playerInFastAttackMode = false;

        UpdateBerserkAura(false);
    }

    // (доп.): Саша "Берсерк" — ручной тумфбл без события активации, аура опрашивается каждый кадр
    // (см. UpdateCombatUI) вместо реакции на ActiveSkillActivated. Элемент создаётся один раз лениво
    // и вставляется В НАЧАЛО playerStageWrapper (index 0) — UI Toolkit рисует детей по порядку
    // добавления, так что элемент с меньшим индексом оказывается ПОД спрайтом (который уже есть в
    // UXML как единственный исходный child), а не поверх него.
    void UpdateBerserkAura(bool active)
    {
        if (active == berserkAuraActive) return;
        berserkAuraActive = active;

        if (active)
        {
            if (berserkAuraElement == null)
            {
                var sprite = BerserkAuraVfxSprite;
                if (sprite == null) return;

                berserkAuraElement = new Image { sprite = sprite };
                berserkAuraElement.pickingMode = PickingMode.Ignore;
                berserkAuraElement.style.position = Position.Absolute;
                berserkAuraElement.style.width = new Length(140, LengthUnit.Percent);
                berserkAuraElement.style.height = new Length(140, LengthUnit.Percent);
                berserkAuraElement.style.left = new Length(-20, LengthUnit.Percent);
                berserkAuraElement.style.top = new Length(-20, LengthUnit.Percent);
                playerStageWrapper.Insert(0, berserkAuraElement);
            }

            berserkAuraElement.style.display = DisplayStyle.Flex;
            if (berserkAuraCoroutine == null)
            {
                berserkAuraCoroutine = StartCoroutine(PulseBerserkAura());
            }
        }
        else
        {
            if (berserkAuraCoroutine != null)
            {
                StopCoroutine(berserkAuraCoroutine);
                berserkAuraCoroutine = null;
            }

            if (berserkAuraElement != null)
            {
                berserkAuraElement.style.display = DisplayStyle.None;
            }
        }
    }

    // Пульсирующая яркость вместо настоящих кадров анимации — тот же приём, что и остальные VFX в
    // этом файле (см. FlashDamageTint/SpawnSkillImpactVfx): один статичный спрайт, анимируется кодом.
    IEnumerator PulseBerserkAura()
    {
        const float period = 0.6f;
        while (true)
        {
            float phase = (Time.time % period) / period;
            berserkAuraElement.style.opacity = 0.55f + 0.35f * Mathf.Sin(phase * Mathf.PI * 2f);
            yield return null;
        }
    }

    // (доп.): обычная атака оружием (не удар активного навыка — тот запускается из
    // OnActiveSkillActivated, иначе "3 быстрые атаки" переиграла бы флипбук 3 раза подряд).
    const float AttackAnimationFps = 10f;

    // (доп.): при высокой скорости атаки одиночная анимация (SwordAttack и т.п.) не успевает
    // доиграть до следующего удара — PlayPlayerOneShotFlipbook перезапускает её с нуля каждый раз,
    // визуально она "обрывается". Если эффективный интервал атаки короче длительности одиночной
    // анимации — переключаемся на непрерывную петлю (FastAttackLoop) и просто даём ей играть дальше,
    // не перезапуская на каждый удар. Обычная скорость атаки — прежнее поведение без изменений.
    void OnAttackPerformed(CombatantRuntime attacker, bool isRegularAttack)
    {
        if (!isRegularAttack || attacker != combatManager.Player) return;

        var attackFrames = PlayableCharacterAnimations.Attack(attacker.DisplayName);
        if (attackFrames == null || attackFrames.Length == 0) return;

        float oneShotDuration = attackFrames.Length / AttackAnimationFps;
        float effectiveInterval = attacker.Weapons.Count > 0
            ? attacker.GetEffectiveAttackInterval(attacker.Weapons[0])
            : float.PositiveInfinity;

        if (effectiveInterval < oneShotDuration)
        {
            if (!playerInFastAttackMode)
            {
                var loopFrames = PlayableCharacterAnimations.FastAttackLoop(attacker.DisplayName);
                if (loopFrames != null && loopFrames.Length > 0)
                {
                    playerInFastAttackMode = true;
                    if (playerFlipbookCoroutine != null) StopCoroutine(playerFlipbookCoroutine);
                    playerFlipbookCoroutine = StartCoroutine(SpriteFlipbook.Play(playerStageSprite, loopFrames, 12f, loop: true));
                }
            }
            // уже в режиме петли — ничего не делаем, пусть доигрывает дальше без перезапуска.
        }
        else
        {
            playerInFastAttackMode = false;
            PlayPlayerOneShotFlipbook(attackFrames, AttackAnimationFps);
        }
    }

    int RollMonsterCount(int level) => MonsterEncounterBudget.RollMonsterCount(level);

    IEnumerator CombatRoomFlow(bool isBoss, FloorMapNode roomNode = null)
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
            if (roomNode != null)
            {
                foreach (var data in GetResolvedMonsters(roomNode))
                    enemies.Add(CombatantFactory.CreateMonsterCombatant(data, dungeonManager.CurrentFloorNumber, monsterLevel));
            }
            else
            {
                // Вложенный бой от проваленной «Сигнализации» не является узлом карты и
                // поэтому по-прежнему формируется в момент срабатывания ловушки.
                int count = RollMonsterCount(characterManager.Level);
                int remainingThreatBudget = MonsterEncounterBudget.GetThreatBudget(dungeonManager.CurrentFloorNumber);
                var eligibleMonsters = regularMonsterPool.FindAll(m => m != null && m.minFloorTier <= dungeonManager.CurrentFloorNumber);
                if (eligibleMonsters.Count == 0) eligibleMonsters = regularMonsterPool;
                for (int i = 0; i < count; i++)
                {
                    var data = MonsterEncounterBudget.RollAffordableMonster(eligibleMonsters, remainingThreatBudget);
                    if (data == null) break;

                    enemies.Add(CombatantFactory.CreateMonsterCombatant(data, dungeonManager.CurrentFloorNumber, monsterLevel));
                    remainingThreatBudget -= MonsterEncounterBudget.GetThreatCost(data);
                }
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
        combatManager.AttackPerformed += OnAttackPerformed;
        ShowOnly(combatPanel);
        combatManager.StartCombat(characterManager.Combatant, enemies);
        BuildEnemyStageEntries(enemies);
        StartPlayerIdleFlipbook();
        if (isBoss)
        {
            tutorialManager?.QueueOnce(TutorialContent.Boss);
        }
        else
        {
            // Один оверлей на первый бой: три подряд (основы + броня + активка) останавливали игру
            // трижды до того, как игрок увидел хоть один удар. Активный навык показывается во
            // ВТОРОМ бою, а броня — когда её впервые пробьют (см. UpdateCombatUI ниже).
            if (tutorialManager != null && !tutorialManager.HasSeen(TutorialContent.CombatBasics))
            {
                tutorialManager.QueueOnce(TutorialContent.CombatBasics);
            }
            else
            {
                tutorialManager?.QueueOnce(activeCharacter.characterClass switch
                {
                    CharacterClass.Rogue => TutorialContent.VioletActive,
                    CharacterClass.Barbarian => TutorialContent.SashaActive,
                    _ => TutorialContent.JenniferActive
                });
            }
        }

        while (combatManager.IsCombatActive)
        {
            UpdateCombatUI();
            yield return null;
        }

        UpdateCombatUI();

        // (доп.): CombatManager.CheckCombatEnd() тикает СРАЗУ ПОСЛЕ TryActivateUniqueActiveSkill,
        // в том же кадре — если скилл убивает последнего врага, IsCombatActive гаснет мгновенно,
        // и без этого ожидания StopPlayerFlipbook() ниже оборвал бы анимацию скилла на середине
        // (см. обсуждение с пользователем: "скил проскакивает до анимации... когда убивает противника").
        // Бой уже логически закончен — просто даём доиграть визуал, прежде чем чистить состояние.
        while (playerSkillAnimationPlaying)
        {
            yield return null;
        }

        UnsubscribeCombatEvents();
        StopPlayerFlipbook();

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

        pendingCombatReward = true;
        pendingCombatWasBoss |= isBoss;
    }

    void UnsubscribeCombatEvents()
    {
        combatManager.LogMessage -= OnCombatLog;
        combatManager.HitResolved -= OnHitResolved;
        combatManager.ActiveSkillActivated -= OnActiveSkillActivated;
        combatManager.AttackPerformed -= OnAttackPerformed;
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
        // (доп.): пока флипбук (см. StartPlayerIdleFlipbook/PlayPlayerOneShotFlipbook) держит
        // playerStageSprite сам — эта строка каждый кадр перетирала бы текущий кадр анимации
        // обратно на статичный player.Sprite.
        if (playerFlipbookCoroutine == null)
        {
            playerStageSprite.sprite = player.Sprite;
        }
        playerNameLabel.text = $"{player.DisplayName} (ур. {characterManager.Level})";
        float playerHpPercent = player.MaxHP > 0f ? Mathf.Clamp01(player.CurrentHP / player.MaxHP) * 100f : 0f;
        playerHpFill.style.width = new Length(playerHpPercent, LengthUnit.Percent);
        playerHpText.text = $"{Mathf.Max(player.CurrentHP, 0f):F0}/{player.MaxHP:F0}";
        playerDefenseText.text = $"Защита: {Mathf.Max(player.PhysicalDefenseCurrent, 0f):F0}/{player.PhysicalDefenseMax:F0}";
        playerShieldText.text = $"Щит: {Mathf.Max(player.MagicShieldCurrent, 0f):F0}/{player.MagicShieldMax:F0}";

        // Подсказка про броню — в момент, когда игрок впервые видит, что она просела, а не общим
        // потоком в начале первого боя, где она ещё ничего не значит.
        if (player.PhysicalDefenseMax > 0f && player.PhysicalDefenseCurrent < player.PhysicalDefenseMax)
        {
            tutorialManager?.QueueOnce(TutorialContent.Defenses);
        }

        CharacterClass characterClass = characterManager.Progress.Character.characterClass;
        bool isBarbarianCombat = characterClass == CharacterClass.Barbarian;
        bool showRage = CombatResourceVisibility.ShouldShowRage(characterClass, player);
        float rage = player.Rage;
        rageIndicator.EnableInClassList("hidden", !showRage);
        if (showRage)
        {
            rageText.text = $"ЯРОСТЬ: {rage:F0}%";
            rageFill.style.width = new Length(Mathf.Clamp(rage, 0f, 100f), LengthUnit.Percent);
            rageIndicator.EnableInClassList("rage-indicator-high", rage >= 70f);
        }

        // (доп.): Берсерк — ручной тумблер без события активации (см. CombatManager.SetBerserkActive/
        // TryActivateUniqueActiveSkill), поэтому аура опрашивается тем же поллинг-паттерном, что и
        // Ярость/Скрытность выше, а не через ActiveSkillActivated.
        UpdateBerserkAura(isBarbarianCombat && player.IsBerserkActive);

        bool showStealth = CombatResourceVisibility.ShouldShowStealth(player);
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
            // Модификатор («Бронебойный», «Свирепый»…) виден только как прилагательное в имени —
            // без расшифровки игрок не понимает, чем этот враг опаснее обычного.
            tutorialManager?.BindTransientTooltip(nameLabel, enemy.DisplayName, TutorialContent.ModifierTooltip(enemy.DisplayName));
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

            // Boss framework (минимальный слайс) — смена спрайта между фазами (CombatManager.
            // TickBossEncounters переписывает CombatantRuntime.Sprite при входе в новую фазу; сам
            // Image-элемент строится один раз в BuildEnemyStageEntries, поэтому src нужно перечитывать
            // каждый кадр здесь, как и остальное per-frame состояние). Для не-боссов Sprite не меняется
            // после старта боя — присваивание того же значения каждый кадр безвредно.
            if (entry.Sprite.sprite != entry.Combatant.Sprite)
            {
                entry.Sprite.sprite = entry.Combatant.Sprite;
            }

            UpdateBossTelegraph(entry);
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

            // Boss framework (минимальный слайс) — reusable-телеграф специальной атаки. Собирается для
            // ЛЮБОГО противника (не только боссов), но остаётся скрытым, пока у CombatantRuntime.
            // BossEncounter нет ожидающего телеграфа (см. UpdateBossTelegraph) — обычные враги просто
            // никогда не показывают его.
            var telegraphLabel = new Label();
            telegraphLabel.AddToClassList("boss-telegraph-label");
            telegraphLabel.AddToClassList("hidden");
            wrapper.Add(telegraphLabel);

            var telegraphBarBg = new VisualElement();
            telegraphBarBg.AddToClassList("boss-telegraph-bar-bg");
            telegraphBarBg.AddToClassList("hidden");
            var telegraphBarFill = new VisualElement();
            telegraphBarFill.AddToClassList("boss-telegraph-bar-fill");
            telegraphBarBg.Add(telegraphBarFill);
            wrapper.Add(telegraphBarBg);

            enemyStageRow.Add(wrapper);
            enemyStageEntries.Add(new EnemyStageEntry
            {
                Combatant = enemy,
                Wrapper = wrapper,
                Sprite = sprite,
                StatusLabel = statusLabel,
                TelegraphLabel = telegraphLabel,
                TelegraphBarFill = telegraphBarFill
            });
        }
    }

    // Boss framework (минимальный слайс) — reusable UI-слой для "готовит особую атаку" ЛЮБОГО
    // будущего босса: читает CombatantRuntime.BossEncounter.PendingTelegraph (см. BossEncounterState),
    // ничего не знает о The Warden конкретно. Полингом за кадр, тем же паттерном, что и HP-бары/
    // статус-баджи выше — не отдельная event+coroutine подсистема, чтобы не плодить новый механизм
    // там, где уже есть работающий (см. отчёт по задаче — обсуждение архитектурного выбора).
    void UpdateBossTelegraph(EnemyStageEntry entry)
    {
        var telegraph = entry.Combatant.BossEncounter?.PendingTelegraph;
        bool hasTelegraph = telegraph.HasValue && entry.Combatant.IsAlive;

        entry.TelegraphLabel.EnableInClassList("hidden", !hasTelegraph);
        entry.TelegraphBarFill.parent.EnableInClassList("hidden", !hasTelegraph);
        if (!hasTelegraph)
        {
            return;
        }

        var info = telegraph.Value;
        entry.TelegraphLabel.text = $"⚠ {info.DisplayName}";
        float progressPercent = info.TotalSeconds > 0f
            ? Mathf.Clamp01(1f - info.RemainingSeconds / info.TotalSeconds) * 100f
            : 100f;
        entry.TelegraphBarFill.style.width = new Length(progressPercent, LengthUnit.Percent);
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
            // Бейджи — самая непонятная часть боя: игрок видит «Заморозка ×7» или «Барьер 40/40»
            // и нигде не может узнать, что это значит.
            tutorialManager?.BindTransientTooltip(badge, effect.label, TutorialContent.StatusTooltip(effect.label));
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

    Image FindStageSprite(CombatantRuntime combatant)
    {
        if (combatant == combatManager.Player)
        {
            return playerStageSprite;
        }

        foreach (var entry in enemyStageEntries)
        {
            if (entry.Combatant == combatant)
            {
                return entry.Sprite;
            }
        }

        return null;
    }

    // 4.7: единая точка подписки на CombatManager.HitResolved — всплывающая цифра урона + тряска
    // спрайта цели (тряска пропускается при полном блоке, см. GDD 4.7), плюс (доп.) короткая
    // красная вспышка спрайта при получении урона и VFX "3 линии" для активного навыка Дженифер.
    void OnHitResolved(CombatantRuntime target, float damageToHP, bool isCrit, bool wasBlocked)
    {
        var wrapper = FindStageWrapper(target);
        if (wrapper == null)
        {
            return;
        }

        string text = wasBlocked ? "БЛОК" : damageToHP.ToString("F0");

        // "3 быстрые атаки": урон уже посчитан синхронно (см. CombatManager.
        // TryActivateUniqueActiveSkill), но ВЕСЬ визуальный фидбек этого удара — цифра, тряска,
        // красная вспышка, VFX — откладывается до конца анимации скилла целиком, одним пакетом
        // (см. OnActiveSkillActivated), а не показывается мгновенно: иначе цифра урона/тряска
        // опережали анимацию удара на ~секунду, что выглядело рассинхронизированно.
        // target != Player: захватываем только удары ПО ПРОТИВНИКУ от самого скилла — иначе
        // ответная атака врага по Дженифер в это же окно (см. capturingSkillHits ниже) тоже
        // попала бы в пакет и могла перезаписать цель VFX на саму Дженифер (см. обсуждение бага).
        if (capturingSkillHits && target != combatManager.Player)
        {
            pendingSkillHits.Add(new PendingSkillHit
            {
                Wrapper = wrapper,
                Sprite = FindStageSprite(target),
                Text = text,
                IsCrit = isCrit,
                WasBlocked = wasBlocked
            });
            return;
        }

        StartCoroutine(SpawnFloatingCombatText(wrapper, text, isCrit, wasBlocked));

        if (!wasBlocked)
        {
            StartCoroutine(ChestRevealAnimator.Shake(wrapper, 0.2f, new Vector3(5f, 3f, 0f), 6));

            var sprite = FindStageSprite(target);
            if (sprite != null)
            {
                StartCoroutine(FlashDamageTint(sprite));
            }
        }
    }

    // (доп.) Один захваченный удар "3 быстрые атаки", ждущий конца анимации скилла — см.
    // capturingSkillHits/pendingSkillHits и OnHitResolved/OnActiveSkillActivated.
    class PendingSkillHit
    {
        public VisualElement Wrapper;
        public Image Sprite;
        public string Text;
        public bool IsCrit;
        public bool WasBlocked;
    }

    // (доп.) Общая для игрока и врагов красная вспышка спрайта при получении урона — независимо
    // от источника (обычная атака, активный навык, яд/кровотечение и т.д., см. вызовы HitResolved
    // в CombatManager). Пропускается при блоке (см. OnHitResolved) — блок уже читается через
    // текст "БЛОК" и отсутствие тряски, отдельная вспышка была бы избыточна.
    IEnumerator FlashDamageTint(Image sprite)
    {
        const float duration = 0.18f;
        var flashColor = new Color(1f, 0.35f, 0.35f, 1f);
        sprite.tintColor = flashColor;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            sprite.tintColor = Color.Lerp(flashColor, Color.white, elapsed / duration);
            yield return null;
        }
        sprite.tintColor = Color.white;
    }

    // (доп.) "3 быстрые атаки": вместо анимирования трёх отдельных ударов на самой Дженифер,
    // навык проигрывает один удар + это наложение на ЦЕЛИ — единая картинка с тремя линиями
    // разреза уже визуально читается как "три попадания" (см. обсуждение выбора подхода).
    IEnumerator SpawnSkillImpactVfx(VisualElement wrapper)
    {
        var vfxSprite = SkillImpactVfxSprite;
        if (vfxSprite == null)
        {
            yield break;
        }

        var vfx = new Image { sprite = vfxSprite };
        vfx.pickingMode = PickingMode.Ignore;
        vfx.style.position = Position.Absolute;
        vfx.style.width = new Length(70, LengthUnit.Percent);
        vfx.style.height = new Length(70, LengthUnit.Percent);
        vfx.style.left = new Length(15, LengthUnit.Percent);
        vfx.style.top = new Length(15, LengthUnit.Percent);
        vfx.style.opacity = 0f;
        wrapper.Add(vfx);

        const float fadeIn = 0.08f;
        const float hold = 0.25f;
        const float fadeOut = 0.25f;

        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.deltaTime;
            vfx.style.opacity = Mathf.Clamp01(elapsed / fadeIn);
            yield return null;
        }
        vfx.style.opacity = 1f;

        yield return new WaitForSeconds(hold);

        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.deltaTime;
            vfx.style.opacity = 1f - Mathf.Clamp01(elapsed / fadeOut);
            yield return null;
        }

        if (vfx.parent != null)
        {
            vfx.RemoveFromHierarchy();
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

        // Дженифер "3 быстрые атаки": вместо анимации трёх отдельных ударов на самой Дженифер
        // (не читалось — см. обсуждение), навык играет один яркий удар + VFX "3 линии" на цели.
        // Урон всех 3 ударов считается синхронно ПРЯМО СЕЙЧАС (см. CombatManager), но их
        // визуальный фидбек (HitResolved → OnHitResolved) захватывается, а не показывается сразу —
        // см. capturingSkillHits/pendingSkillHits.
        if (skillName == "3 быстрые атаки")
        {
            // Обычная атака не может начаться и оборвать анимацию скилла (см. CombatantRuntime.
            // AttackLocked / TickCombatant) — снижает ДПС на время анимации, это осознанный выбор.
            // Снимается в onComplete ниже; StopPlayerFlipbook — аварийный сброс, если бой прервётся раньше.
            combatManager.Player.AttackLocked = true;
            capturingSkillHits = true;
            pendingSkillHits.Clear();
            playerSkillAnimationPlaying = true;
            StartCoroutine(CloseSkillHitCapture());

            PlayPlayerOneShotFlipbook(JenniferAnimationFrames.SkillBrightStrike, 12f, onComplete: () =>
            {
                combatManager.Player.AttackLocked = false;
                playerSkillAnimationPlaying = false;

                // Все 3 захваченных удара показываются одним пакетом, синхронно с концом анимации —
                // цифры/тряска/вспышка/VFX появляются вместе, а не вразнобой с самим ударом.
                VisualElement impactWrapper = null;
                foreach (var hit in pendingSkillHits)
                {
                    StartCoroutine(SpawnFloatingCombatText(hit.Wrapper, hit.Text, hit.IsCrit, hit.WasBlocked));
                    if (!hit.WasBlocked)
                    {
                        StartCoroutine(ChestRevealAnimator.Shake(hit.Wrapper, 0.2f, new Vector3(5f, 3f, 0f), 6));
                        if (hit.Sprite != null)
                        {
                            StartCoroutine(FlashDamageTint(hit.Sprite));
                        }
                        impactWrapper = hit.Wrapper;
                    }
                }
                pendingSkillHits.Clear();

                if (impactWrapper != null)
                {
                    StartCoroutine(SpawnSkillImpactVfx(impactWrapper));
                }
            });
        }

        // Вайолет "Дымовая граната": один короткий всплеск дыма на самой Вайолет в момент каста —
        // общий индикатор Скрытности (от ЛЮБОГО источника, не только этого навыка) отдельно
        // реализован через USS-класс .stealth-stage-active (см. GameStyles.uss), здесь только
        // одноразовый VFX самого броска гранаты.
        if (skillName == "Дымовая граната" && playerStageWrapper != null)
        {
            StartCoroutine(SpawnSmokeBombVfx(playerStageWrapper));
        }
    }

    // (доп.): такой же по форме, как SpawnSkillImpactVfx, но появляется на кастующей (Вайолет), а не
    // на цели, и запускается напрямую по имени навыка, а не через захват HitResolved — "Дымовая
    // граната" не наносит урон, HitResolved для неё вообще не фигурирует.
    IEnumerator SpawnSmokeBombVfx(VisualElement wrapper)
    {
        var vfxSprite = SmokeBombVfxSprite;
        if (vfxSprite == null)
        {
            yield break;
        }

        var vfx = new Image { sprite = vfxSprite };
        vfx.pickingMode = PickingMode.Ignore;
        vfx.style.position = Position.Absolute;
        vfx.style.width = new Length(80, LengthUnit.Percent);
        vfx.style.height = new Length(80, LengthUnit.Percent);
        vfx.style.left = new Length(10, LengthUnit.Percent);
        vfx.style.top = new Length(10, LengthUnit.Percent);
        vfx.style.opacity = 0f;
        wrapper.Add(vfx);

        const float fadeIn = 0.1f;
        const float hold = 0.5f;
        const float fadeOut = 0.6f;

        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.deltaTime;
            vfx.style.opacity = Mathf.Clamp01(elapsed / fadeIn) * 0.9f;
            yield return null;
        }

        yield return new WaitForSeconds(hold);

        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.deltaTime;
            vfx.style.opacity = 0.9f * (1f - Mathf.Clamp01(elapsed / fadeOut));
            yield return null;
        }

        if (vfx.parent != null)
        {
            vfx.RemoveFromHierarchy();
        }
    }

    // (доп.): все 3 удара "3 быстрые атаки" резолвятся синхронно, в том же кадре, что и сама
    // активация (см. CombatManager.TryActivateUniqueActiveSkill — цикл ResolveAttack идёт сразу
    // после ActiveSkillActivated, без ожидания). Поэтому окно захвата фидбека закрывается уже на
    // следующем кадре — держать capturingSkillHits открытым на всю анимацию (~0.9с) было ошибкой:
    // любой урон, полученный Дженифер в это время (например, ответная атака врага — она НЕ
    // заблокирована, заблокирована только атака самой Дженифер), тоже захватывался бы в пакет.
    IEnumerator CloseSkillHitCapture()
    {
        yield return null;
        capturingSkillHits = false;
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
