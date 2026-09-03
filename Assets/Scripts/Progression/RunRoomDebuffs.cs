using System;
using UnityEngine;

public sealed class ActiveRunRoomDebuff
{
    public const string MushroomPoisonId = "mushroom_cave_poison";

    public string DebuffId { get; private set; }
    public int RemainingRooms { get; private set; }
    public float ReceivedHealingPenaltyPercent { get; private set; }
    public bool IsActive => !string.IsNullOrWhiteSpace(DebuffId) && RemainingRooms > 0;

    CombatantRuntime boundRuntime;
    bool skipApplyingRoomCompletion;

    public void ApplyMushroomPoison(RareRoomConfig config, CombatantRuntime runtime)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        Clear();
        DebuffId = MushroomPoisonId;
        RemainingRooms = Mathf.Max(1, config.mushroomPoisonDurationRooms);
        ReceivedHealingPenaltyPercent = Mathf.Max(0f, config.mushroomPoisonHealingPenaltyPercent);
        skipApplyingRoomCompletion = true;
        Bind(runtime);
    }

    public void Bind(CombatantRuntime runtime)
    {
        Unbind();
        if (!IsActive || runtime == null) return;
        boundRuntime = runtime;
        boundRuntime.RunReceivedHealingPercent -= ReceivedHealingPenaltyPercent;
    }

    public void CompleteRoom()
    {
        if (!IsActive) return;
        if (skipApplyingRoomCompletion)
        {
            skipApplyingRoomCompletion = false;
            return;
        }
        RemainingRooms--;
        if (RemainingRooms <= 0) Clear();
    }

    public void Clear()
    {
        Unbind();
        DebuffId = null;
        RemainingRooms = 0;
        ReceivedHealingPenaltyPercent = 0f;
        skipApplyingRoomCompletion = false;
    }

    void Unbind()
    {
        if (boundRuntime == null) return;
        boundRuntime.RunReceivedHealingPercent += ReceivedHealingPenaltyPercent;
        boundRuntime = null;
    }
}
