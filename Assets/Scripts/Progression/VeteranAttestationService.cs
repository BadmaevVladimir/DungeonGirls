using System;
using System.Diagnostics;
using System.Linq;

public sealed class VeteranAttestationService : IVeteranAttestationService
{
    readonly ICombatSimulationEngine simulationEngine;

    public VeteranAttestationService(ICombatSimulationEngine simulationEngine)
    {
        this.simulationEngine = simulationEngine ?? throw new ArgumentNullException(nameof(simulationEngine));
    }

    public VeteranAttestationResult Evaluate(VeteranBuildSnapshot snapshot, VeteranAttestationConfig config,
        AttestationRunMode mode)
    {
        var result = new VeteranAttestationResult { CompletionStatus = AttestationCompletionStatus.Completed };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (snapshot == null) return Fallback(result, "snapshot_missing", stopwatch);
            if (ReferenceEquals(config, null) || !config.TryValidate(out _)) return Fallback(result, "config_invalid", stopwatch);
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
                        if (stopwatch.ElapsedMilliseconds >= config.realHardTimeoutMilliseconds)
                            return Fallback(result, "hard_timeout", stopwatch);

                        CombatSimulationResult simulation;
                        try
                        {
                            simulation = simulationEngine.Simulate(new CombatSimulationRequest
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

                        result.SimulationCount++;
                        result.Runs.Add(new VeteranAttestationRunResult
                        {
                            TierId = tier.tierId,
                            TrialId = trial.trialId,
                            Seed = seed,
                            Simulation = simulation
                        });
                        if (simulation.Outcome == CombatSimulationOutcome.Victory) wins++; else losses++;

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
        catch (Exception exception)
        {
            return Fallback(result, exception.GetType().Name, stopwatch);
        }

        stopwatch.Stop();
        result.CalculationMilliseconds = stopwatch.ElapsedMilliseconds;
        return result;
    }

    static VeteranAttestationResult Fallback(VeteranAttestationResult result, string errorCode, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        result.FinalRank = VeteranRank.C;
        result.QualifyingTrialId = string.Empty;
        result.CompletionStatus = AttestationCompletionStatus.Fallback;
        result.ErrorCode = errorCode;
        result.CalculationMilliseconds = stopwatch.ElapsedMilliseconds;
        return result;
    }
}
