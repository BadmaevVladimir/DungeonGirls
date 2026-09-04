using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "VeteranAttestationConfig", menuName = "DungeonGirls/Veteran Attestation Config")]
public class VeteranAttestationConfig : ScriptableObject
{
    public string ratingVersion;
    public VeteranReferenceThreatProfile referenceProfile = new VeteranReferenceThreatProfile();
    public List<VeteranTierConfig> tiers = new List<VeteranTierConfig>();
    public List<VeteranTrialConfig> trials = new List<VeteranTrialConfig>();
    public float virtualTimeLimitSeconds;
    public int processedEventLimit;
    public int realHardTimeoutMilliseconds;
    public bool logSimulationDetails;
    public float ceremonyMinimumSeconds;
    public float ceremonySkipDelaySeconds;

    public bool TryValidate(out string error)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ratingVersion)) errors.Add("ratingVersion is empty");
        if (referenceProfile == null || referenceProfile.maxHp <= 0f || referenceProfile.hitDamage <= 0f ||
            referenceProfile.attacksPerSecond <= 0f) errors.Add("reference profile is incomplete");
        if (tiers == null || tiers.Count != 4) errors.Add("exactly four tiers are required");
        if (trials == null || trials.Count != 3) errors.Add("exactly three trials are required");
        if (virtualTimeLimitSeconds <= 0f || processedEventLimit <= 0 || realHardTimeoutMilliseconds <= 0)
            errors.Add("simulation limits must be positive");

        var trialIds = new HashSet<string>(StringComparer.Ordinal);
        if (trials != null)
            foreach (var trial in trials)
                if (trial == null || string.IsNullOrWhiteSpace(trial.trialId) || !trialIds.Add(trial.trialId))
                    errors.Add("trial ids must be non-empty and unique");

        VeteranRank[] expected = { VeteranRank.B, VeteranRank.A, VeteranRank.S, VeteranRank.SPlus };
        if (tiers != null)
        {
            for (int i = 0; i < tiers.Count; i++)
            {
                var tier = tiers[i];
                if (tier == null) { errors.Add($"tier {i} is null"); continue; }
                if (i < expected.Length && tier.rank != expected[i]) errors.Add($"tier {i} must be {expected[i]}");
                if (tier.requiredPassingTrials < 1 || tier.requiredPassingTrials > trialIds.Count)
                    errors.Add($"tier {tier.tierId} requiredPassingTrials must be between 1 and {trialIds.Count}");
                if (tier.trialSeeds == null || tier.trialSeeds.Count != trialIds.Count)
                    errors.Add($"tier {tier.tierId} must configure every trial");
                else foreach (var seedSet in tier.trialSeeds)
                    if (seedSet == null || !trialIds.Contains(seedSet.trialId) || seedSet.seeds == null || seedSet.seeds.Length != 3)
                        errors.Add($"tier {tier.tierId} has invalid seed set");

                if (i > 0)
                {
                    var previous = tiers[i - 1];
                    if (previous != null && (tier.healthMultiplier < previous.healthMultiplier ||
                        tier.pressureMultiplier < previous.pressureMultiplier ||
                        tier.escalationMultiplier < previous.escalationMultiplier ||
                        tier.requiredPassingTrials < previous.requiredPassingTrials))
                        errors.Add($"tier {tier.tierId} is easier than {previous.tierId}");
                }
            }
        }

        error = string.Join("; ", errors);
        return errors.Count == 0;
    }

    void OnValidate()
    {
        if (!TryValidate(out string error)) Debug.LogWarning($"[VeteranAttestationConfig] {error}", this);
    }
}
