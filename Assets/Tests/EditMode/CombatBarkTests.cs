using NUnit.Framework;
using UnityEngine;

// Реплики в бою с боссом: чистая логика выбора фразы (CombatBarkSelector). Данные лежат на
// BossKitData.encounterBarks, поэтому кит собирается прямо здесь, без ассетов на диске —
// тем же приёмом, что BossEncounterTests.MakeKit.
public class CombatBarkSelectorTests
{
    static BossKitData MakeKit(params EncounterBark[] barks)
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.encounterBarks.AddRange(barks);
        return kit;
    }

    static EncounterBark Bark(string characterId, EncounterBarkTrigger trigger, int phaseIndex, string heroLine)
        => new EncounterBark
        {
            characterId = characterId,
            trigger = trigger,
            phaseIndex = phaseIndex,
            heroLine = heroLine
        };

    [Test]
    public void Select_ExactCharacterId_BeatsGenericEntry()
    {
        var kit = MakeKit(
            Bark("", EncounterBarkTrigger.CombatStart, 0, "Общая фраза"),
            Bark("jennifer", EncounterBarkTrigger.CombatStart, 0, "Фраза Дженифер"));

        var bark = CombatBarkSelector.Select(kit, "jennifer", EncounterBarkTrigger.CombatStart, 0);

        Assert.IsNotNull(bark);
        Assert.AreEqual("Фраза Дженифер", bark.heroLine);
    }

    [Test]
    public void Select_NoEntryForCharacter_FallsBackToGenericEntry()
    {
        var kit = MakeKit(
            Bark("", EncounterBarkTrigger.CombatStart, 0, "Общая фраза"),
            Bark("jennifer", EncounterBarkTrigger.CombatStart, 0, "Фраза Дженифер"));

        var bark = CombatBarkSelector.Select(kit, "sasha", EncounterBarkTrigger.CombatStart, 0);

        Assert.IsNotNull(bark);
        Assert.AreEqual("Общая фраза", bark.heroLine);
    }

    [Test]
    public void Select_TriggerDoesNotMatch_ReturnsNull()
    {
        var kit = MakeKit(Bark("jennifer", EncounterBarkTrigger.CombatStart, 0, "Фраза на вход"));

        Assert.IsNull(CombatBarkSelector.Select(kit, "jennifer", EncounterBarkTrigger.PhaseChanged, 1));
    }

    [Test]
    public void Select_PhaseChanged_MatchesOnlyRequestedPhase()
    {
        var kit = MakeKit(
            Bark("jennifer", EncounterBarkTrigger.PhaseChanged, 1, "Вторая фаза"),
            Bark("jennifer", EncounterBarkTrigger.PhaseChanged, 2, "Третья фаза"));

        Assert.AreEqual("Третья фаза",
            CombatBarkSelector.Select(kit, "jennifer", EncounterBarkTrigger.PhaseChanged, 2).heroLine);
        Assert.IsNull(CombatBarkSelector.Select(kit, "jennifer", EncounterBarkTrigger.PhaseChanged, 3));
    }

    // phaseIndex осмыслен только для PhaseChanged: у входа в бой фаза всегда нулевая, и требовать
    // от контент-редактора проставлять там ноль — лишний повод сломать данные молча.
    [Test]
    public void Select_CombatStart_IgnoresPhaseIndexOnEntry()
    {
        var kit = MakeKit(Bark("jennifer", EncounterBarkTrigger.CombatStart, 7, "Фраза на вход"));

        Assert.AreEqual("Фраза на вход",
            CombatBarkSelector.Select(kit, "jennifer", EncounterBarkTrigger.CombatStart, 0).heroLine);
    }

    // Пустая запись — это недозаполненный ассет, а не немая реплика: показывать нечего, и
    // перекрывать собой общую фразу она не должна.
    [Test]
    public void Select_EntryWithoutLines_IsSkipped()
    {
        var kit = MakeKit(
            Bark("", EncounterBarkTrigger.CombatStart, 0, "Общая фраза"),
            Bark("jennifer", EncounterBarkTrigger.CombatStart, 0, ""));

        Assert.AreEqual("Общая фраза",
            CombatBarkSelector.Select(kit, "jennifer", EncounterBarkTrigger.CombatStart, 0).heroLine);
    }

    [Test]
    public void Select_BossLineOnly_StillCountsAsBark()
    {
        var kit = MakeKit(new EncounterBark
        {
            characterId = "jennifer",
            trigger = EncounterBarkTrigger.CombatStart,
            bossLine = "Реплика босса"
        });

        var bark = CombatBarkSelector.Select(kit, "jennifer", EncounterBarkTrigger.CombatStart, 0);

        Assert.IsNotNull(bark);
        Assert.AreEqual("Реплика босса", bark.bossLine);
    }

    [Test]
    public void Select_EmptyKit_ReturnsNull()
    {
        Assert.IsNull(CombatBarkSelector.Select(MakeKit(), "jennifer", EncounterBarkTrigger.CombatStart, 0));
        Assert.IsNull(CombatBarkSelector.Select(null, "jennifer", EncounterBarkTrigger.CombatStart, 0));
    }
}

// Связка «данные забега -> заявка в очередь»: одна точка, которая решает, звучит ли реплика
// прямо сейчас и чьим голосом. Держится чистой функцией ровно ради этих тестов —
// RunFlowController только зовёт её и рисует результат.
public class CombatBarkRequestTests
{
    static BossKitData MakeKit(params EncounterBark[] barks)
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.encounterBarks.AddRange(barks);
        return kit;
    }

    static MonsterData MakeBoss()
    {
        var boss = ScriptableObject.CreateInstance<MonsterData>();
        boss.monsterName = "Амальгама";
        boss.isBoss = true;
        return boss;
    }

    static RunCharacterProgress MakeProgress() =>
        new RunCharacterProgress(ScriptableObject.CreateInstance<CharacterData>());

    [Test]
    public void TryBuildRequest_FirstTime_ProducesHeroRequest()
    {
        var kit = MakeKit(new EncounterBark
        {
            characterId = "jennifer",
            trigger = EncounterBarkTrigger.CombatStart,
            heroLine = "Значит, ты и есть Амальгама."
        });

        var built = CombatBarkSelector.TryBuildRequest(
            kit, "jennifer", MakeBoss(), MakeProgress(), EncounterBarkTrigger.CombatStart, 0, out var request);

        Assert.IsTrue(built);
        Assert.AreEqual(BarkSpeaker.Hero, request.Speaker);
        Assert.AreEqual("Значит, ты и есть Амальгама.", request.Text);
    }

    [Test]
    public void TryBuildRequest_SecondTimeForSameBoss_ReturnsFalse()
    {
        var kit = MakeKit(new EncounterBark
        {
            characterId = "jennifer",
            trigger = EncounterBarkTrigger.CombatStart,
            heroLine = "Значит, ты и есть Амальгама."
        });
        var boss = MakeBoss();
        var progress = MakeProgress();

        Assert.IsTrue(CombatBarkSelector.TryBuildRequest(
            kit, "jennifer", boss, progress, EncounterBarkTrigger.CombatStart, 0, out _));
        Assert.IsFalse(CombatBarkSelector.TryBuildRequest(
            kit, "jennifer", boss, progress, EncounterBarkTrigger.CombatStart, 0, out _));
    }

    // Отметка в памяти забега ставится только вместе с реальной заявкой. Иначе бой без фраз
    // молча «съедал» бы реплику, которую контент-редактор допишет позже.
    [Test]
    public void TryBuildRequest_NothingToSay_DoesNotConsumeRunMemory()
    {
        var boss = MakeBoss();
        var progress = MakeProgress();

        Assert.IsFalse(CombatBarkSelector.TryBuildRequest(
            MakeKit(), "jennifer", boss, progress, EncounterBarkTrigger.CombatStart, 0, out _));

        Assert.IsTrue(progress.TryMarkBarkShown(boss, EncounterBarkTrigger.CombatStart, 0));
    }

    // Голос на сцене один. Пока у боссов нет фраз, это чисто теоретическая развилка, но правило
    // должно быть зафиксировано до того, как кто-то допишет bossLine в ассет.
    [Test]
    public void TryBuildRequest_BothLinesFilled_HeroSpeaks()
    {
        var kit = MakeKit(new EncounterBark
        {
            characterId = "jennifer",
            trigger = EncounterBarkTrigger.CombatStart,
            heroLine = "Фраза героини",
            bossLine = "Фраза босса"
        });

        CombatBarkSelector.TryBuildRequest(
            kit, "jennifer", MakeBoss(), MakeProgress(), EncounterBarkTrigger.CombatStart, 0, out var request);

        Assert.AreEqual(BarkSpeaker.Hero, request.Speaker);
        Assert.AreEqual("Фраза героини", request.Text);
    }

    [Test]
    public void TryBuildRequest_OnlyBossLine_BossSpeaks()
    {
        var kit = MakeKit(new EncounterBark
        {
            characterId = "jennifer",
            trigger = EncounterBarkTrigger.PhaseChanged,
            phaseIndex = 1,
            bossLine = "Фраза босса"
        });

        var built = CombatBarkSelector.TryBuildRequest(
            kit, "jennifer", MakeBoss(), MakeProgress(), EncounterBarkTrigger.PhaseChanged, 1, out var request);

        Assert.IsTrue(built);
        Assert.AreEqual(BarkSpeaker.Boss, request.Speaker);
        Assert.AreEqual(1, request.PhaseIndex);
    }

    [Test]
    public void TryBuildRequest_NoProgress_ReturnsFalse()
    {
        var kit = MakeKit(new EncounterBark
        {
            trigger = EncounterBarkTrigger.CombatStart,
            heroLine = "Общая фраза"
        });

        Assert.IsFalse(CombatBarkSelector.TryBuildRequest(
            kit, "jennifer", MakeBoss(), null, EncounterBarkTrigger.CombatStart, 0, out _));
    }
}

// Память забега: какие реплики уже прозвучали. Живёт в RunCharacterProgress рядом с
// DefeatedBosses по той же причине — должна пережить переход между этажами, но новый забег
// начинается с чистого листа сам собой.
public class CombatBarkRunMemoryTests
{
    static MonsterData MakeBoss(string name)
    {
        var boss = ScriptableObject.CreateInstance<MonsterData>();
        boss.monsterName = name;
        boss.isBoss = true;
        return boss;
    }

    static RunCharacterProgress MakeProgress() =>
        new RunCharacterProgress(ScriptableObject.CreateInstance<CharacterData>());

    [Test]
    public void TryMarkBarkShown_SameBarkTwice_OnlyFirstTimeReturnsTrue()
    {
        var progress = MakeProgress();
        var boss = MakeBoss("Амальгама");

        Assert.IsTrue(progress.TryMarkBarkShown(boss, EncounterBarkTrigger.CombatStart, 0));
        Assert.IsFalse(progress.TryMarkBarkShown(boss, EncounterBarkTrigger.CombatStart, 0));
    }

    [Test]
    public void TryMarkBarkShown_DifferentPhases_TrackedSeparately()
    {
        var progress = MakeProgress();
        var boss = MakeBoss("Амальгама");

        Assert.IsTrue(progress.TryMarkBarkShown(boss, EncounterBarkTrigger.PhaseChanged, 1));
        Assert.IsTrue(progress.TryMarkBarkShown(boss, EncounterBarkTrigger.PhaseChanged, 2));
        Assert.IsFalse(progress.TryMarkBarkShown(boss, EncounterBarkTrigger.PhaseChanged, 1));
    }

    [Test]
    public void TryMarkBarkShown_DifferentBosses_TrackedSeparately()
    {
        var progress = MakeProgress();

        Assert.IsTrue(progress.TryMarkBarkShown(MakeBoss("Амальгама"), EncounterBarkTrigger.CombatStart, 0));
        Assert.IsTrue(progress.TryMarkBarkShown(MakeBoss("Свечник"), EncounterBarkTrigger.CombatStart, 0));
    }

    [Test]
    public void TryMarkBarkShown_NullBoss_ReturnsFalse()
    {
        Assert.IsFalse(MakeProgress().TryMarkBarkShown(null, EncounterBarkTrigger.CombatStart, 0));
    }

    // Храм V перезапускает этаж, восстанавливая прогресс из снимка. Приветствие босса при этом
    // повторяться не должно — иначе перезапуск превращает реплику в заевшую пластинку.
    [Test]
    public void Clone_KeepsShownBarks()
    {
        var progress = MakeProgress();
        var boss = MakeBoss("Амальгама");
        progress.TryMarkBarkShown(boss, EncounterBarkTrigger.CombatStart, 0);

        var clone = RunStateClone.Clone(progress);

        Assert.IsFalse(clone.TryMarkBarkShown(boss, EncounterBarkTrigger.CombatStart, 0));
    }

    [Test]
    public void Clone_DoesNotShareTheListWithTheSource()
    {
        var progress = MakeProgress();
        var boss = MakeBoss("Амальгама");

        var clone = RunStateClone.Clone(progress);
        clone.TryMarkBarkShown(boss, EncounterBarkTrigger.CombatStart, 0);

        Assert.IsTrue(progress.TryMarkBarkShown(boss, EncounterBarkTrigger.CombatStart, 0));
    }
}

// Очередь реплик: один голос на сцене, одна ожидающая фраза, гейт по оверлею и паузе.
// Тоже чистый C#-класс — таймингами и рисованием занимается RunFlowController, сюда приходит
// только состояние мира (BarkGate) и уходит решение.
public class CombatBarkQueueTests
{
    static BarkGate RunningGate(int phaseIndex = 0) => new BarkGate
    {
        OverlayVisible = false,
        Paused = false,
        CombatRunning = true,
        CurrentBossPhaseIndex = phaseIndex
    };

    static BarkRequest Start(string text) =>
        new BarkRequest(BarkSpeaker.Hero, text, EncounterBarkTrigger.CombatStart, 0);

    static BarkRequest Phase(string text, int phaseIndex) =>
        new BarkRequest(BarkSpeaker.Hero, text, EncounterBarkTrigger.PhaseChanged, phaseIndex);

    [Test]
    public void TryTake_NothingQueued_ReturnsFalse()
    {
        var queue = new CombatBarkQueue();

        Assert.IsFalse(queue.TryTake(RunningGate(), out _));
    }

    [Test]
    public void TryTake_QueuedBark_ComesOutOnce()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Start("Значит, ты и есть Амальгама."));

        Assert.IsTrue(queue.TryTake(RunningGate(), out var request));
        Assert.AreEqual("Значит, ты и есть Амальгама.", request.Text);
        Assert.IsTrue(queue.HasActive);
        Assert.IsFalse(queue.TryTake(RunningGate(), out _));
    }

    // Ради этого правила всё и затевалось: реплика ждёт закрытия туториала, а не уходит в никуда
    // за оверлеем. Тем же гейтом накрываются справка и любой будущий оверлей.
    [Test]
    public void TryTake_OverlayVisible_HoldsBarkUntilOverlayCloses()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Start("Фраза на вход"));

        var gate = RunningGate();
        gate.OverlayVisible = true;
        Assert.IsFalse(queue.TryTake(gate, out _));

        Assert.IsTrue(queue.TryTake(RunningGate(), out var request));
        Assert.AreEqual("Фраза на вход", request.Text);
    }

    [Test]
    public void TryTake_Paused_HoldsBark()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Start("Фраза на вход"));

        var gate = RunningGate();
        gate.Paused = true;
        Assert.IsFalse(queue.TryTake(gate, out _));

        Assert.IsTrue(queue.TryTake(RunningGate(), out _));
    }

    [Test]
    public void Enqueue_PhaseBarkWhileCombatStartActive_RequestsInterrupt()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Start("Длинное приветствие"));
        Assert.IsTrue(queue.TryTake(RunningGate(), out _));
        Assert.IsFalse(queue.InterruptRequested);

        queue.Enqueue(Phase("Он сбросил броню", 1));

        Assert.IsTrue(queue.InterruptRequested);
    }

    [Test]
    public void Enqueue_CombatStartWhilePhaseBarkActive_DoesNotInterrupt()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Phase("Он сбросил броню", 1));
        Assert.IsTrue(queue.TryTake(RunningGate(1), out _));

        queue.Enqueue(Start("Приветствие"));

        Assert.IsFalse(queue.InterruptRequested);
    }

    [Test]
    public void MarkFinished_ClearsActiveAndInterrupt()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Start("Приветствие"));
        Assert.IsTrue(queue.TryTake(RunningGate(), out _));
        queue.Enqueue(Phase("Он сбросил броню", 1));

        queue.MarkFinished();

        Assert.IsFalse(queue.HasActive);
        Assert.IsFalse(queue.InterruptRequested);
        Assert.IsTrue(queue.TryTake(RunningGate(1), out var request));
        Assert.AreEqual("Он сбросил броню", request.Text);
    }

    // Ожидающая реплика ровно одна: копить их бессмысленно, объяснение к фазе 2 посреди фазы 3
    // только сбивает с толку.
    [Test]
    public void Enqueue_EqualPriorityWhilePendingOccupied_IsDropped()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Phase("Первая", 1));
        queue.Enqueue(Phase("Вторая", 1));

        Assert.IsTrue(queue.TryTake(RunningGate(1), out var request));
        Assert.AreEqual("Первая", request.Text);
        queue.MarkFinished();
        Assert.IsFalse(queue.TryTake(RunningGate(1), out _));
    }

    [Test]
    public void Enqueue_HigherPriorityReplacesPending()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Start("Приветствие"));
        queue.Enqueue(Phase("Он сбросил броню", 1));

        Assert.IsTrue(queue.TryTake(RunningGate(1), out var request));
        Assert.AreEqual("Он сбросил броню", request.Text);
    }

    [Test]
    public void TryTake_PhaseBarkAfterBossEnteredNextPhase_IsDropped()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Phase("Он сбросил броню", 1));

        // Пока бабл ждал закрытия оверлея, босс успел уйти в третью фазу.
        Assert.IsFalse(queue.TryTake(RunningGate(2), out _));
        Assert.IsFalse(queue.TryTake(RunningGate(2), out _));
    }

    [Test]
    public void TryTake_CombatEnded_DropsPending()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Start("Приветствие"));

        var gate = RunningGate();
        gate.CombatRunning = false;
        Assert.IsFalse(queue.TryTake(gate, out _));

        Assert.IsFalse(queue.TryTake(RunningGate(), out _));
    }

    [Test]
    public void Reset_ClearsEverything()
    {
        var queue = new CombatBarkQueue();
        queue.Enqueue(Start("Приветствие"));
        Assert.IsTrue(queue.TryTake(RunningGate(), out _));
        queue.Enqueue(Phase("Он сбросил броню", 1));

        queue.Reset();

        Assert.IsFalse(queue.HasActive);
        Assert.IsFalse(queue.InterruptRequested);
        Assert.IsFalse(queue.TryTake(RunningGate(1), out _));
    }
}

// Реплики в ассетах: инварианты, которые нельзя выразить полем в инспекторе. Ловят типовую
// ошибку контента — запись есть, а фразы в ней нет, или повод «смена фазы» указывает на фазу,
// которой у этого босса не существует.
public class CombatBarkContentTests
{
    static System.Collections.Generic.List<BossKitData> LoadAllKits()
    {
        var kits = new System.Collections.Generic.List<BossKitData>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:BossKitData"))
        {
            var kit = UnityEditor.AssetDatabase.LoadAssetAtPath<BossKitData>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (kit != null) kits.Add(kit);
        }

        return kits;
    }

    [Test]
    public void EveryBark_HasALineAndPointsAtARealPhase()
    {
        foreach (var kit in LoadAllKits())
        {
            foreach (var bark in kit.encounterBarks)
            {
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(bark.heroLine) && string.IsNullOrWhiteSpace(bark.bossLine),
                    $"{kit.name}: запись реплики без единой строки — молчаливая запись только " +
                    "перекрывает общую фразу и ничего не показывает");

                if (bark.trigger != EncounterBarkTrigger.PhaseChanged) continue;

                Assert.Less(bark.phaseIndex, kit.phases.Count,
                    $"{kit.name}: реплика указывает на фазу {bark.phaseIndex}, которой у босса нет");
                Assert.GreaterOrEqual(bark.phaseIndex, 0, $"{kit.name}: отрицательный индекс фазы");
            }
        }
    }
}
