using System;
using UnityEngine;

public interface ICombatRandom
{
    float Value01();
    float Range(float minimum, float maximum);
}

public sealed class UnityCombatRandom : ICombatRandom
{
    public float Value01() => UnityEngine.Random.value;
    public float Range(float minimum, float maximum) => UnityEngine.Random.Range(minimum, maximum);
}

// System.Random is deliberately wrapped instead of exposed. A simulation owns one instance per
// run, so neither Unity's global RNG nor another attestation run can affect its sequence.
public sealed class DeterministicCombatRandom : ICombatRandom
{
    readonly System.Random random;

    public DeterministicCombatRandom(int seed)
    {
        random = new System.Random(seed);
    }

    public float Value01() => (float)random.NextDouble();

    public float Range(float minimum, float maximum)
    {
        if (maximum <= minimum) return minimum;
        return minimum + (maximum - minimum) * Value01();
    }
}
