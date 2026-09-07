using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;

public sealed class VeteranAttestationService : IVeteranAttestationService
{
    readonly ICombatSimulationEngine simulationEngine;

    public VeteranAttestationService(ICombatSimulationEngine simulationEngine)
    {
        this.simulationEngine = simulationEngine ?? throw new ArgumentNullException(nameof(simulationEngine));
    }

    // Синхронный расчёт: тот же прогон, что и у пошагового раннера, просто прокрученный до конца
    // сразу. Остаётся для редакторских тестов и любых вызовов вне игрового кадра.
    public VeteranAttestationResult Evaluate(VeteranBuildSnapshot snapshot, VeteranAttestationConfig config,
        AttestationRunMode mode)
    {
        var runner = CreateRunner(snapshot, config, mode);
        while (runner.Step()) { }
        return runner.Result;
    }

    // R04: расчёт, разбитый на шаги. Один Step выполняет не больше одной симуляции, поэтому
    // церемония может анимироваться и принимать нажатия, пока идёт счёт.
    public VeteranAttestationRunner CreateRunner(VeteranBuildSnapshot snapshot, VeteranAttestationConfig config,
        AttestationRunMode mode) => new VeteranAttestationRunner(simulationEngine, snapshot, config, mode);
}

// R04: раньше вся аттестация (до 36 симуляций) прокручивалась одним синхронным вызовом прямо перед
// церемонией — кадр стоял, анимация не шла, кнопка «Пропустить» не отвечала, а результат забега
// фиксировался только ПОСЛЕ церемонии, и выход в этом окне терял всю награду.
//
// Правила оценки здесь НЕ изменены (D01: действующее правило — то, что было в коде): те же
// 1/2/2/3 засчитанных испытания, те же 2 победы из 3 seed, тот же порядок обхода и те же ранние
// выходы. Изменился только способ прокрутки.
public sealed class VeteranAttestationRunner
{
    readonly ICombatSimulationEngine engine;
    readonly VeteranBuildSnapshot snapshot;
    readonly VeteranAttestationConfig config;
    readonly AttestationRunMode mode;

    // Копит ТОЛЬКО время самих симуляций. Раньше это был секундомер синхронного цикла, и его можно
    // было сравнивать с realHardTimeoutMilliseconds напрямую. Растянутый по кадрам расчёт по
    // настенным часам вышел бы за любой лимит мгновенно и всегда падал бы в fallback C.
    readonly Stopwatch computeTime = new Stopwatch();

    readonly VeteranAttestationResult result =
        new VeteranAttestationResult { CompletionStatus = AttestationCompletionStatus.Completed };

    readonly IEnumerator steps;

    public VeteranAttestationRunner(ICombatSimulationEngine engine, VeteranBuildSnapshot snapshot,
        VeteranAttestationConfig config, AttestationRunMode mode)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.snapshot = snapshot;
        this.config = config;
        this.mode = mode;
        steps = EvaluateSteps();
    }

    public bool IsComplete { get; private set; }

    // Достоверен только после завершения; до этого расчёт ещё может закончиться fallback'ом.
    public VeteranAttestationResult Result => IsComplete ? result : null;

    // Сколько симуляций уже выполнено — для индикации прогресса, если она понадобится.
    public int SimulationCount => result.SimulationCount;

    // Возвращает true, пока остаётся работа. Один вызов выполняет не больше одной симуляции.
    public bool Step()
    {
        if (IsComplete) return false;

        // try/catch живёт снаружи итератора намеренно: C# не разрешает yield внутри try с catch,
        // а прежнее поведение «любое неожиданное исключение → fallback C» нужно сохранить.
        try
        {
            if (steps.MoveNext()) return true;
        }
        catch (Exception exception)
        {
            Fail(exception.GetType().Name);
            return false;
        }

        Complete();
        return false;
    }

    // Прокручивает шаги, пока не исчерпан бюджет кадра. Возвращает true, пока расчёт не завершён.
    // Бюджет намеренно мал: цель — плавная церемония, а не «досчитать за один кадр».
    public bool StepWithin(double budgetMilliseconds)
    {
        var frameBudget = Stopwatch.StartNew();
        do
        {
            if (!Step()) return false;
        }
        while (frameBudget.Elapsed.TotalMilliseconds < budgetMilliseconds);
        return !IsComplete;
    }

    IEnumerator EvaluateSteps()
    {
        if (snapshot == null) { Fail("snapshot_missing"); yield break; }
        if (ReferenceEquals(config, null) || !config.TryValidate(out _)) { Fail("config_invalid"); yield break; }
        result.RatingVersion = config.ratingVersion;

        bool sequentialChainAlive = true;
        foreach (var tier in config.tiers)
        {
            int passingTrials = 0;
            string qualifyingTrial = null;

            foreach (var trial in config.trials)
            {
                var seedSet = tier.trialSeeds.First(entry => entry.trialId == trial.trialId);
                int wins = 0;
                int losses = 0;
                foreach (int seed in seedSet.seeds)
                {
                    if (computeTime.ElapsedMilliseconds >= config.realHardTimeoutMilliseconds)
                    {
                        Fail("hard_timeout");
                        yield break;
                    }

                    if (RunOneSimulation(tier, trial, seed)) wins++; else losses++;

                    // Ровно одна симуляция на шаг: уступаем кадр сразу после неё, до проверок
                    // ранних выходов — иначе один Step мог бы прокрутить несколько боёв подряд.
                    yield return null;

                    if (mode == AttestationRunMode.Release && (wins >= 2 || losses >= 2)) break;
                }

                if (wins >= 2)
                {
                    passingTrials++;
                    qualifyingTrial ??= trial.trialId;
                }
                if (mode == AttestationRunMode.Release && passingTrials >= tier.requiredPassingTrials) break;
            }

            bool tierPassed = passingTrials >= tier.requiredPassingTrials;

            if (sequentialChainAlive && tierPassed)
            {
                result.FinalRank = tier.rank;
                result.QualifyingTrialId = qualifyingTrial;
            }
            else
            {
                sequentialChainAlive = false;
                if (mode == AttestationRunMode.Release) break;
            }
        }
    }

    bool RunOneSimulation(VeteranTierConfig tier, VeteranTrialConfig trial, int seed)
    {
        CombatSimulationResult simulation;
        computeTime.Start();
        try
        {
            simulation = engine.Simulate(new CombatSimulationRequest
            {
                Snapshot = snapshot,
                ReferenceProfile = config.referenceProfile,
                Tier = tier,
                Trial = trial,
                Seed = seed,
                VirtualTimeLimitSeconds = config.virtualTimeLimitSeconds,
                ProcessedEventLimit = config.processedEventLimit
            }) ?? new CombatSimulationResult { Outcome = CombatSimulationOutcome.Error, Seed = seed, ErrorCode = "null_result" };
        }
        catch (Exception exception)
        {
            simulation = new CombatSimulationResult
            {
                Outcome = CombatSimulationOutcome.Error,
                Seed = seed,
                ErrorCode = exception.GetType().Name
            };
        }
        finally
        {
            computeTime.Stop();
        }

        result.SimulationCount++;
        result.Runs.Add(new VeteranAttestationRunResult
        {
            TierId = tier.tierId,
            TrialId = trial.trialId,
            Seed = seed,
            Simulation = simulation
        });
        return simulation.Outcome == CombatSimulationOutcome.Victory;
    }

    void Fail(string errorCode)
    {
        computeTime.Stop();
        result.FinalRank = VeteranRank.C;
        result.QualifyingTrialId = string.Empty;
        result.CompletionStatus = AttestationCompletionStatus.Fallback;
        result.ErrorCode = errorCode;
        result.CalculationMilliseconds = computeTime.ElapsedMilliseconds;
        IsComplete = true;
    }

    void Complete()
    {
        if (IsComplete) return;
        computeTime.Stop();
        result.CalculationMilliseconds = computeTime.ElapsedMilliseconds;
        IsComplete = true;
    }
}
