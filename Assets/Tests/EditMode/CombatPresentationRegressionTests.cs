using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public class CombatPresentationRegressionTests
{
    GameObject host;
    RunFlowController flow;
    CombatManager combat;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("Combat presentation regression");
        // Avoid scene/UI initialization; these tests exercise event handling and badge updates.
        host.SetActive(false);
        flow = host.AddComponent<RunFlowController>();
        combat = host.AddComponent<CombatManager>();
        Set("combatManager", combat);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(host);

    void Set(string name, object value) => typeof(RunFlowController).GetField(name, PrivateInstance).SetValue(flow, value);
    object Get(string name) => typeof(RunFlowController).GetField(name, PrivateInstance).GetValue(flow);
    void Invoke(string name, params object[] args) => typeof(RunFlowController).GetMethod(name, PrivateInstance).Invoke(flow, args);

    [TestCase(1f)]
    [TestCase(20f)]
    public void RegularAttackDuringSkill_DoesNotReplaceSkillVisual(float attackSpeed)
    {
        var player = new CombatantRuntime { DisplayName = "Дженифер", IsPlayer = true, MaxHP = 100f, CurrentHP = 100f };
        player.Weapons.Add(new WeaponAttackState { AttackSpeed = attackSpeed });
        combat.StartCombat(player, new List<CombatantRuntime>());
        Set("playerSkillAnimationPlaying", true);

        Invoke("OnAttackPerformed", player, true);

        Assert.That(Get("playerSkillAnimationPlaying"), Is.True);
        Assert.That(Get("playerInFastAttackMode"), Is.False);
        Assert.That(Get("capturingAttackHits"), Is.False);
        Assert.That(Get("playerFlipbookCoroutine"), Is.Null, "No replacement coroutine may cancel the skill's completion callback.");
    }

    [Test]
    public void UnchangedStatuses_ReuseBadges_ExpiredStatusesAreRemoved()
    {
        var container = new VisualElement();
        var target = new CombatantRuntime { PoisonStacks = 2, PoisonTimer = 3f };
        Invoke("PopulateStatusContainer", container, target, false);
        Assert.That(container.childCount, Is.GreaterThan(0));
        var badge = container[0];
        Invoke("PopulateStatusContainer", container, target, false);
        Assert.That(container[0], Is.SameAs(badge));
        target.PoisonStacks = 0;
        target.PoisonTimer = 0f;
        Invoke("PopulateStatusContainer", container, target, false);
        Assert.That(container.childCount, Is.Zero);
        Assert.That(container.ClassListContains("hidden"), Is.True);
    }

    [Test]
    public void CombatJournal_RemainsAvailableWithoutConsoleMirroring()
    {
        var messages = new List<string>();
        combat.LogMessage += messages.Add;
        combat.StartCombat(new CombatantRuntime { MaxHP = 10f, CurrentHP = 10f }, new List<CombatantRuntime>());
        combat.EndCombat();
        Assert.That(messages.Count, Is.EqualTo(2));
        Assert.That(messages[1], Does.Contain("Бой окончен"));
    }
}
