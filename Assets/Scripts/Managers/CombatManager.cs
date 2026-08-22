using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    public CombatantRuntime Player { get; private set; }
    public List<CombatantRuntime> Enemies { get; private set; } = new List<CombatantRuntime>();
    public bool IsCombatActive { get; private set; }

    public void StartCombat(CombatantRuntime player, List<CombatantRuntime> enemies)
    {
        Player = player;
        Enemies = enemies ?? new List<CombatantRuntime>();
        IsCombatActive = true;

        Player.AttackTimer = 0f;
        foreach (var enemy in Enemies)
        {
            enemy.AttackTimer = 0f;
        }

        Player.Target = GetDefaultTarget();

        Debug.Log($"[Combat] Бой начался: {Player.DisplayName} (HP {Player.CurrentHP:F1}) против {Enemies.Count} противников.");
    }

    public void EndCombat()
    {
        if (!IsCombatActive)
        {
            return;
        }

        IsCombatActive = false;

        // 3.3: магический щит восстанавливается до максимума после каждого боя; физ. защита — нет.
        Player.RestoreMagicShield();

        Debug.Log(Player.IsAlive
            ? $"[Combat] Бой окончен. {Player.DisplayName} побеждает."
            : $"[Combat] Бой окончен. {Player.DisplayName} погибает.");
    }

    void Update()
    {
        if (IsCombatActive)
        {
            Tick(Time.deltaTime);
        }
    }

    // 10.2: у каждого участника боя свой таймер атаки, срабатывающий по достижении
    // интервала = 1 / СкоростьАтаки. Вынесен из Update() отдельным методом, чтобы бой
    // можно было тикать и без сцены/плеймода (например, из редакторских тестов).
    public void Tick(float deltaTime)
    {
        if (!IsCombatActive)
        {
            return;
        }

        TickCombatant(Player, deltaTime);

        foreach (var enemy in Enemies)
        {
            TickCombatant(enemy, deltaTime);
        }

        CheckCombatEnd();
    }

    void TickCombatant(CombatantRuntime attacker, float deltaTime)
    {
        if (!attacker.IsAlive)
        {
            return;
        }

        attacker.AttackTimer += deltaTime;
        float interval = attacker.AttackInterval;

        while (IsCombatActive && attacker.IsAlive && attacker.AttackTimer >= interval)
        {
            attacker.AttackTimer -= interval;
            ResolveAttack(attacker);
        }
    }

    // 4.1-4.2: игрок атакует выбранную вручную цель либо (по умолчанию) первого живого
    // противника в списке; противники всегда атакуют персонажа.
    void ResolveAttack(CombatantRuntime attacker)
    {
        CombatantRuntime target = attacker.IsPlayer ? GetPlayerTarget() : Player;

        if (target == null || !target.IsAlive)
        {
            return;
        }

        float damage = Random.Range(attacker.DamageMin, attacker.DamageMax);
        var result = DamageCalculator.ApplyDamage(target, damage, attacker.DamageType);

        if (result.WasBlocked)
        {
            Debug.Log($"[Combat] {attacker.DisplayName} атакует {target.DisplayName}: урон {damage:F1} полностью заблокирован.");
        }
        else
        {
            Debug.Log($"[Combat] {attacker.DisplayName} атакует {target.DisplayName}: {result.DamageToHP:F1} урона по HP (осталось {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");
        }

        if (!target.IsAlive)
        {
            Debug.Log($"[Combat] {target.DisplayName} погибает.");
        }
    }

    CombatantRuntime GetPlayerTarget()
    {
        if (Player.Target == null || !Player.Target.IsAlive)
        {
            Player.Target = GetDefaultTarget();
        }

        return Player.Target;
    }

    CombatantRuntime GetDefaultTarget()
    {
        return Enemies.FirstOrDefault(e => e.IsAlive);
    }

    // 4.2: программный аналог клика по противнику — ручной выбор цели игроком.
    public void SetPlayerTarget(CombatantRuntime target)
    {
        if (target != null && target.IsAlive && Enemies.Contains(target))
        {
            Player.Target = target;
        }
    }

    void CheckCombatEnd()
    {
        if (!Player.IsAlive || Enemies.All(e => !e.IsAlive))
        {
            EndCombat();
        }
    }
}
