# Floor Boss System — Design & Implementation Plan

Status: **design only, no code changed**. Audit performed against `main` @ `c92c026` (2026-09-01). Written so a follow-up implementation task can start directly from Section 8 without re-auditing the codebase.

Scope note: floor map generation, `FloorMap`/`FloorMapGenerator`, room selection, and the new map UI (`RunFlowController.Map.cs`) are being built concurrently by another agent and were **not modified or designed against** beyond noting integration risk (Section 9).

---

## 1. Current Boss Architecture

A "boss" today is a single `MonsterData` ScriptableObject (`Assets/ScriptableObjects/Monsters/Monster_Boss.asset`) with `isBoss = true`, referenced once from `RunFlowController.cs:27` (`bossData`) and spawned as the sole enemy whenever the floor's terminal room resolves as `RoomType.Boss` (`RunFlowController.Combat.cs:16-18`).

`isBoss` gates exactly four things, all hardcoded:
- Skips monster-level scaling / modifier rolls (`CombatantFactory.cs:175-178`).
- Enables a second, boss-only "Heavy Attack" timer (5s fixed) whose multiplier scales by floor tier (1.5×/1.75×/2×) — the *only* boss-unique combat mechanic that exists (`CombatManager.cs:359-388`, `CombatantFactory.cs:193-200`).
- Reward multipliers: currency ×5, guaranteed Rare+ item, flat 50 XP (`RewardManager.cs:44-71,224-234`).
- UI labeling: map pip icon, "Босс" text, tutorial trigger (`RunFlowController.cs:621-624`, `TutorialContent.cs`).

There is exactly one boss asset in the whole game, reused unscaled-in-kind across every floor (only numeric stats change via the shared floor-scaling formula). No phases, no scripted abilities, no telegraphs, no visual distinction beyond a static sprite. This matches the brief's description precisely: "a normal enemy with bigger numbers."

Key classes: `MonsterData` (data), `CombatantRuntime` (runtime state, shared with the player), `CombatantFactory` (spawns), `CombatManager` (the ~1200-line MonoBehaviour driving all combat), `RunFlowController` / `RunFlowController.Combat.cs` (room flow, spawn call site, UI wiring), `RewardManager` (post-combat rewards).

## 2. Reusable Existing Systems

These can be used **as-is or with light extension** — no new subsystem required:

- **Multi-enemy combat loop.** `CombatManager.Enemies` is already `List<CombatantRuntime>`; every tick/attack/status loop already iterates it. Normal rooms already spawn 1–3 monsters. A boss fight with adds is structurally supported for the "already present at start" case.
- **Multi-target damage pattern.** The Piercing weapon passive (`CombatManager.cs:778-795`) already demonstrates "resolve damage against every other living enemy, fire `HitResolved` per target" — the exact shape needed for a boss AoE, just currently hardcoded to one player passive.
- **`HitResolved` / `ActiveSkillActivated` / `LogMessage` events.** Already generic `Action<...>` events on `CombatManager`, already consumed by UI for floating damage numbers, shake tweens, and a skill-name banner. The banner is already reused today for the boss heavy attack. This is the best-shaped hook in the codebase and should be the backbone of boss ability presentation.
- **`ActiveDebuff` list pattern** (`CombatantRuntime.ActiveDebuffs`: id + timer + multiplier + isBuff). Reusable template for new timed modifiers, though currently limited to attack-speed multiplier only.
- **Room flow plumbing.** `RunFlowController.ResolveRoom` → `CombatRoomFlow(isBoss)` → `FloorState.CombatResolve` is simple, linear, coroutine-based. Adding pre/post-boss beats (arena intro, defeat cinematic) is a small, low-risk extension of an already-simple pattern.
- **Reward differentiation.** Already boolean/enum-gated (`isBoss`, `ExperienceSource.Boss`) with no other coupling — trivial to extend per-boss if ever wanted, and needs no change for a shared framework.
- **`SkillId` enum + `PassiveSkillData`/`ActiveSkillData` metadata pattern.** Not a real ability system, but the pattern of "stable enum id + SO for name/description + hardcoded mechanical branch" is the established codebase idiom. A boss ability system should follow the same shape (data carries identity/telegraph text, `CombatManager` carries mechanics) rather than inventing a parallel convention.

## 3. Missing Boss Hooks

Nothing below exists today; all would need to be added:

- **HP-threshold / phase-change trigger.** No code anywhere checks `CurrentHP` against a fraction and fires an event. Must be added to the tick loop.
- **Per-combatant death event.** Death is only detected by polling `IsAlive` at `CheckCombatEnd`; there is no `OnCombatantDied` callback to hang a boss-death cinematic or "on-ally-death enrage" on.
- **Generic conditional/scripted ability trigger.** Everything conditional today is a hand-written `switch(SkillId)` branch inside specific methods (`ApplyMonsterPassiveOnAttack`, `TickMonsterPeriodicPassives`, `TickBossHeavyAttacks`). There is no "evaluate condition → fire ability" abstraction; a boss framework needs at least a minimal one to avoid an unbounded number of bespoke `if` branches in `CombatManager`.
- **Attack telegraph UI.** Combat is real-time/automatic with zero windup/warning display. This is the single largest gap relative to the brief's "readable telegraphs" goal — must be built from scratch (data field + UI element + timing hook).
- **Visual phase / animation states.** `MonsterData.sprite` is one static `Sprite`; no `Animator`, no sprite-swap array, no phase-driven visual change support.
- **Mid-fight minion summon API.** `Enemies` is populated once in `StartCombat` and never mutated after (`Enemies.Add`/`Remove` occur nowhere else in the codebase). A `CombatManager.SpawnAdditionalEnemy(...)` method would need to be added.
- **Multi-ability-slot monster data.** `MonsterData.passiveSkill` is a single optional field. Bosses need an ordered list of abilities with trigger conditions.
- **VFX/SFX asset hookup.** No `AudioSource`/`AudioClip` usage anywhere in combat code today (audio only exists in Hub/Gacha). A boss ability firing a sound/particle effect needs this wired for the first time, though the event backbone to trigger it (`HitResolved`/new ability events) already exists.

## 4. Recommended Architecture

**Principle: extend, don't fork.** Do not introduce a parallel "boss combat engine." Bosses should be monsters that carry a richer, data-driven ability kit, resolved by a small number of new generic hooks added to the existing `CombatManager` tick loop — following the same idiom (`SkillId` + SO metadata + mechanical branch) the codebase already uses, just generalized enough that N bosses don't mean N sets of bespoke `switch` branches.

Answers to the 9 questions:

1. **Can existing MonsterData + Effect/Skill systems implement bosses?** Partially. The *numeric* effects (damage, resistances, DoT, attack-speed debuffs) can reuse existing formulas/fields. The *scripting* (when abilities fire, phases, telegraphs) cannot — there's no generic trigger system, so this must be added. Reuse the effect execution, add a new triggering layer on top.

2. **Which mechanics need new hooks?** HP-threshold phase change, per-kill/death event, telegraph timing/UI, mid-fight minion spawn, multi-ability scripted sequencing. (Full mapping in Section 5.)

3. **New `BossData` or extend `MonsterData`?** Add a new **optional companion asset**, `BossKitData : ScriptableObject`, referenced from `MonsterData` via an optional field (e.g. `MonsterData.bossKit`), rather than bloating `MonsterData` itself with boss-only fields that are `null`/unused for 95% of monster assets. This keeps `MonsterData` the single source of truth for base stats/visuals (shared with normal monsters) while isolating boss-specific authoring (ability list, phases, telegraphs) in its own asset type that only bosses use. `isBoss` stays as-is (still gates reward/UI branches); `bossKit != null` gates the new ability system. A boss asset with no kit degrades gracefully to today's "big dumb monster" behavior — useful for the interim while only one or two bosses are authored.

4. **Need a `BossController`/`PhaseController`?** Yes, but keep it thin: one new class, `BossEncounterState` (plain C# class, not a `MonoBehaviour`), owned per-combat by `CombatManager` (one instance per boss combatant currently in `Enemies`, null for non-bosses). It holds: current phase index, per-ability cooldown/charge timers, and evaluates trigger conditions once per `Tick`. It should **not** own damage math, VFX, or UI — those stay in existing `CombatManager`/`DamageCalculator`/UI code, invoked by ability id the same way monster passives are today. This avoids overengineering: no interfaces-per-mechanic, no ability "plugin" architecture — just a data-driven trigger evaluator plus the existing hardcoded-branch-by-id execution pattern, generalized to support a *list* of ids with *conditions* instead of one unconditional passive.

5. **How to describe phases?** Support a **small closed set of trigger kinds** on each ability entry, not a scripting language:
   - `HpThreshold` (fires once when boss HP crosses below X%)
   - `TurnCount` / elapsed-time interval (fires every N seconds, mirrors existing periodic-passive pattern)
   - `OnCombatStart` (opening move / intro telegraph)
   - `Always` with a cooldown (baseline attack pattern, replaces/extends today's hardcoded heavy-attack timer)

   Skip "state" and "scripted sequence" as generic trigger kinds — they invite overengineering. If a boss genuinely needs a fixed scripted sequence (e.g. "always A→B→C→A→B→C"), model it as an explicit small ordered list consumed in order rather than a general state machine. Combination triggers (e.g. "HP threshold AND cooldown ready") fall out naturally since each entry already carries a cooldown *and* a trigger kind — no separate combinator needed.

6. **How to store telegraphs?** As data on the ability entry itself: a short telegraph duration (seconds) + a display string/icon id, read by a new lightweight UI element (a fixed-position banner/bar, following the existing skill-banner pattern in `RunFlowController.Combat.cs`) that shows "Boss is winding up: <name>" for that duration before the ability resolves. No separate telegraph asset type needed — it's 2 fields on the same ability entry.

7. **How to store unique VFX/SFX?** As optional fields on the ability entry (`AudioClip`, and either a `ParticleSystem` prefab reference or a simple color-flash/shake parameter set, matching the existing DOTween shake precedent). Fire them from the same place the ability's `ActiveSkillActivated`-style event is raised, exactly mirroring how the boss heavy-attack banner already fires today. Don't build a generic VFX sequencing system — one clip + one optional particle prefab per ability is enough for this scope.

8. **How to make it data-driven enough that a new boss ≈ no bespoke code?** A new boss should require: one `MonsterData` asset (stats/sprite, as today) + one `BossKitData` asset listing ability entries (id, trigger kind, threshold/interval/cooldown value, telegraph text/duration, VFX/SFX refs, target/effect parameters). The *execution* of each ability id still lives in code (a `switch(BossAbilityId)` in `CombatManager`, mirroring `ApplyMonsterPassiveOnAttack`), but authoring a new boss from an *existing* ability vocabulary (Section 5's mechanics table) is pure data — no new C# for reusing mechanics that already exist, code only needed the first time a genuinely new mechanic is introduced.

9. **What's still worth its own component instead of pure data?** Anything with meaningfully different state shape than "timer + magnitude": minion summon/despawn bookkeeping (needs to track spawned combatant references for cleanup on boss death), and multi-phase visual/animation swapping if it grows beyond a 2–3 sprite swap (start with sprite-array-by-phase-index as data; only build a real animation controller if/when a boss needs more than that). Both should be small dedicated helper methods on `BossEncounterState`, not separate MonoBehaviours.

## 5. Reusable Boss Mechanics Table

Complexity/risk assessed against the recommended architecture in Section 4 (i.e. assuming the phase-trigger hook, telegraph UI, and minion-spawn API from Section 3 get built once as shared infrastructure).

| Mechanic | Buildable with current systems? | What must be added | Complexity | Regression risk | Reuse value |
|---|---|---|---|---|---|
| Phase change on HP threshold | No | HP-threshold trigger check in tick loop (Section 3) | Low (once hook exists) | Low | High — core to nearly every boss |
| Power-up after N turns/time elapsed | Partially (periodic-passive timer pattern exists) | Generalize existing pattern to arbitrary abilities | Low | Low | High |
| Chargeable/telegraphed heavy attack | Partially (boss heavy attack exists, but not telegraphed) | Telegraph UI + duration field | Medium | Low (isolated new UI element) | High |
| Telegraph of next attack (generic) | No | Telegraph UI + per-ability display text | Medium | Low | Very high — addresses the brief's top priority |
| Attack-pattern switching (e.g. alternate between two moves) | Partially (ordered ability list) | Simple index-cycling on the ability list | Low | Low | Medium |
| Temporary invulnerability | No | New boolean flag on `CombatantRuntime` (`IsInvulnerable` + timer), checked in `ApplyDamage` | Low | Low-Medium (touches shared damage path) | Medium |
| Shield (absorb-before-HP) | Partially (armor/shield resolution exists in `DamageCalculator`) | Boss-specific shield pool, reuse existing shield math if compatible, else new field | Medium | Medium (shared damage code) | Medium-High |
| Resource accumulation (e.g. "rage" building to a finisher) | Partially (Rage computed property exists for Barbarian, different purpose) | New boss-side resource field + threshold trigger reuse | Low | Low | Medium |
| Counter mechanic (punish a player action) | No | New condition check on player's last action; needs an "observe player action" hook | High | Medium (couples boss to player action internals) | Low-Medium — easy to make frustrating, use sparingly |
| Minion summoning | Partially (multi-enemy loop exists, add/remove API does not) | `CombatManager.SpawnAdditionalEnemy`, cleanup on boss death | Medium-High | Medium (touches core `Enemies` list lifecycle, `CheckCombatEnd`) | High |
| Buff allies (minions) | Partially (ActiveDebuff list supports attack-speed buffs) | Extend to allow boss→ally application (currently self/player-only conceptually) | Low-Medium | Low | Medium |
| Debuff player (generic) | Yes (ActiveDebuffs list, resistance fields) | Nothing new for existing debuff types; new field only for a genuinely new debuff kind | Low | Low | High |
| DoT (boss-applied) | Yes (Bleed/Poison patterns exist) | Reuse Bleed/Poison application from a boss ability id | Low | Low | High |
| Marks (e.g. "next hit on marked target crits") | No | New timed flag + check at damage-resolution time | Medium | Medium (touches `ResolveAttack`) | Medium |
| Healing reduction on player | No | New timed debuff field, checked wherever player healing is applied | Low-Medium | Low | Medium — directly tests sustain/healing builds |
| Damage reflection (thorns-style) | Yes (Thorns reflect already exists for player) | Reuse pattern for boss-applied reflect | Low | Low | Low-Medium |
| Attack scaling with player's current HP (execute-style) | Partially (Execution passive precedent exists player-side) | Boss-side mirror of the existing execution-bonus pattern | Low | Low | Medium — tests burst/sustain tension |
| Punish specific player action (e.g. dodge-punish, heal-punish) | No | Same "observe player action" hook as Counter mechanic | High | Medium-High | Low — highest frustration risk, use rarely and telegraph clearly |
| Enrage (post-timer damage/speed ramp) | Partially (periodic timer + stat multiplier pattern exists) | Generalize timer→multiplier application to bosses | Low | Low | Medium — mostly a "prevent stalling" safety valve |
| Environmental effect (e.g. periodic room-wide damage tick) | No | New periodic self-damage-to-player tick, independent of attack timers | Low-Medium | Low | Low-Medium — flavorful but easy to skip for v1 |
| Temporary rule change (e.g. "healing disabled this phase") | No | Global combat-state flag checked at the specific system it affects | Medium | Medium (cross-cutting flag, easy to leave stale) | Low — use very sparingly, high risk of confusing/frustrating |

Overall takeaway: **HP-threshold phases, telegraphs, DoT/debuffs, power-ramps, and enrage are cheap and safe** — build these first as the shared vocabulary. **Minion summon and shields are medium-cost but high-value** — worth building once, reusable everywhere after. **Counter mechanics, action-punishment, and rule changes are expensive and risk player frustration** — use at most once or twice across the whole boss roster, and only with very clear telegraphing.

## 6. Boss Concepts

Eight concepts, each targeting a distinct build axis per the brief.

**1. The Warden (Floor 1–2 tier) — tests: nothing special / onboarding**
Visual: an oversized armored guard, shield raised. Idea: alternates between a slow, heavily telegraphed overhead slam (big damage, long telegraph) and raising a shield (temporary damage reduction) every ~15s. Abilities: (a) Overhead Slam — 2s telegraph, high damage; (b) Shield Up — periodic, -50% damage taken for 4s; (c) Phase 2 at 50% HP: slam telegraph shortens. Phases: 2 (HP threshold). Player read: telegraph banner + sprite raise-arm pose. Tests: basic reaction to telegraphs, patience (don't waste burst into the shield window). Frustration point: if shield uptime is too high, fights drag. Mitigate: shield only active ~25% of fight duration. Complexity: Low.

**2. The Hollow Duelist (burst check)**
Visual: rapier-wielding rogue-type silhouette. Idea: very fast, low-damage-per-hit baseline, but charges a single unavoidable "riposte" if the player doesn't kill it fast enough. Abilities: (a) rapid basic attacks; (b) Enrage at turn-count threshold — attack speed ramps; (c) at 20% HP, brief invulnerability window (forces player to hold burst or waste it). Phases: HP threshold (invuln) + time-based enrage. Player read: invuln telegraphed by a color flash + banner. Tests: burst DPS and burst timing/patience. Frustration point: punishing all-in burst players who pop invuln during the window. Mitigate: invuln window is short (~2s) and clearly telegraphed 1s in advance. Complexity: Medium.

**3. The Plague Mother (sustain/DoT/cleanse check)**
Visual: bloated, corrupted enemy with dripping particle effect. Idea: stacks poison/DoT on the player instead of hitting hard directly; punishes lack of cleanse/healing. Abilities: (a) Toxic Spit — applies stacking poison DoT (reuses existing Poison system); (b) Healing Reduction aura — periodic debuff cutting player healing for a window; (c) at 50% HP, spawns 1–2 small "spore" minions that also apply weak DoT. Phases: HP threshold triggers minion spawn. Player read: poison stack counter already exists in status UI; healing-reduction shows as a new debuff icon. Tests: sustain/healing builds, DoT management. Frustration: players with zero cleanse/heal-sustain get ground down slowly. Mitigate: cap total DoT stacks, make healing-reduction partial not full block. Complexity: Medium (needs minion spawn + new healing-reduction field).

**4. The Iron Sentinel (armor/mitigation check)**
Visual: golem/construct. Idea: hits very hard but rarely, and telegraphs every attack clearly — tests whether the player's armor/mitigation build can survive a few big hits rather than DPS race. Abilities: (a) single heavy attack on a long (~6s) telegraphed cooldown, scaled to be survivable only with armor investment; (b) Shield mechanic — briefly untargetable/reduced damage after each big hit (recovery); (c) Enrage past a time limit to prevent stalling. Phases: single phase + enrage safety valve. Player read: long, obvious wind-up animation/telegraph banner. Tests: armor/mitigation, patience (bait-and-punish rhythm). Frustration: undergeared armor players get one-shot-ish. Mitigate: telegraph gives a full extra "safe" window if reacted to (e.g. block/dodge item procs), damage tuned to ~2-3x normal hit not instant-kill. Complexity: Low-Medium.

**5. The Twin Shades (multi-target/adaptation check)**
Visual: two linked shadow enemies sharing one healthbar-adjacent "linked" indicator, or two independent small healthbars. Idea: tests target-priority decisions and multi-target damage. Abilities: (a) both attack independently at moderate speed; (b) if one dies, the other enrages (damage/speed buff); (c) periodic combined attack (both telegraph together, hits once). Phases: state-based (one alive vs two alive) rather than HP-threshold. Player read: enrage clearly telegraphed by visual change on the survivor. Tests: adaptation/target-priority, moderate burst. Frustration: enrage after first kill can feel like a punish for doing damage. Mitigate: telegraph the enrage clearly as an intentional trade-off, keep enrage magnitude modest. Complexity: Medium-High (two combatants tagged as one boss encounter; uses the existing multi-enemy list, so no new spawn API needed — both are pre-placed at `StartCombat`).

**7 remaining as summary of intent, per this same template — abbreviated for report length; expand on request. Concepts 6–8 below.**

**6. The Gilded Merchant (resource-management check)**
Visual: opulent, coin-covered enemy. Idea: has a "shield" resource pool that regenerates unless interrupted by the player landing crits; punishes passive/slow builds, rewards crit/burst timing without requiring pure DPS race. Abilities: (a) Coin Shield — absorbs damage, regenerates over time; (b) Bribe (periodic) — briefly buffs its own damage; (c) at low shield-uptime-remaining, becomes briefly vulnerable (bonus damage window). Phases: resource-threshold driven, not HP. Tests: crit/burst reliability, resource-denial thinking. Frustration: pure sustain builds without crit feel like they can't "open" the boss. Mitigate: shield eventually decays even without crits, just slower. Complexity: Medium (new shield-resource field).

**7. The Frostbound Wraith (tempo/dodge check)**
Visual: fast, semi-transparent, wind-particle sprite. Idea: very high attack speed but low per-hit damage and periodic unavoidable-unless-dodged "frost nova" telegraphed AoE. Tests evasion/dodge builds and tempo management. Abilities: (a) fast weak basic attacks; (b) Frost Nova — clearly telegraphed, applies Freeze stacks (existing system) if it lands; (c) phase 2 (50% HP) attack speed increases further. Player read: nova telegraph banner + sprite flash before cast. Tests: evasion, freeze-resistance/cleanse, tempo. Frustration: freeze-stack lockout chains feel bad if back-to-back. Mitigate: freeze-immune window after a freeze expires (system already exists: `FreezeImmune`/`FreezeImmuneTimer`). Complexity: Low-Medium (mostly reuses existing Freeze system).

**8. The Corrupted Choir (adaptation/rule-change check, use sparingly per Section 5)**
Visual: multiple small linked singer-enemies + one central "conductor." Idea: the conductor periodically changes an active "rule" (e.g. "healing disabled," "next hit always crits against you," "damage type X is boosted") signposted clearly by a UI banner, forcing the player to adapt approach turn-to-turn. Abilities: (a) conductor is low-HP/fragile but shielded by singers; (b) killing a singer removes one active rule; (c) rule rotates every ~20s if singers aren't killed. Phases: singer-count-driven. Tests: adaptation, prioritization under changing constraints. Frustration: highest of the roster — arbitrary rule changes can feel unfair. Mitigate: always exactly one rule active, always clearly banner-announced with a few seconds' notice before it takes effect, keep rule pool small (3-4 well-understood options). Complexity: High — save for later in the roster, after the shared framework is proven on simpler bosses.

## 7. Recommended First Boss: The Warden

Chosen because it's the cleanest test of the framework itself: needs phase-change (HP threshold), telegraph UI (new), and a shield-style mitigation window (new but simple), while staying single-combatant (no minion-spawn API needed for v1) and using only well-understood existing systems (temporary damage reduction is a straightforward multiplier, not a new resource pool). It is visibly and mechanically distinct from today's stub without requiring the highest-risk pieces (minion spawn, action-punish, rule-change) on the very first attempt.

- **Stats philosophy.** Keep the existing floor-scaling formula for base HP/damage (don't invent new balance math without more data); the differentiator is entirely the ability kit, not bigger numbers. Numeric telegraph/cooldown/multiplier values intentionally left as placeholders for a numbers pass once playtested.
- **Phase structure.** Two phases via one HP threshold (50%). Phase 1: baseline attack + periodic Shield Up. Phase 2: baseline attack (slightly faster) + Shield Up (slightly shorter cooldown) + Overhead Slam telegraph duration shortened (harder to react, still always telegraphed — never removes the telegraph).
- **Ability cycle.** Baseline weapon attack ticks normally via existing `WeaponAttackState` (no change). In parallel: Shield Up fires on its own periodic timer (mirrors `TickMonsterPeriodicPassives`/`TickBossHeavyAttacks` pattern) — grants a timed damage-reduction flag. Overhead Slam fires on its own periodic timer, but resolution is delayed by a telegraph duration after the trigger fires (new: two-step "announce, then resolve after N seconds" instead of today's instant-resolve heavy attack).
- **Telegraphs.** Overhead Slam: banner + sprite pose change (if a simple alternate sprite is authored) 2s (phase 1) / 1.2s (phase 2) before it lands. Shield Up: banner only, no dodge implication (informational, not a threat).
- **Cooldowns/triggers.** Shield Up: periodic timer, ~15s phase 1 / ~10s phase 2, lasts ~4s. Overhead Slam: periodic timer, ~12s phase 1 / ~9s phase 2 (values are placeholders for playtesting).
- **Failure states.** Player takes the full Slam because they didn't notice the telegraph (expected — it's a punishing-but-fair hit, not a wipe); player burns cooldown-based cleanse/burst into a Shield Up window (soft punish, not a hard wall since it's a percentage reduction, not full immunity).
- **Interactions with player statuses.** Shield Up should reduce incoming damage as a straightforward percentage on top of existing armor/shield resolution in `DamageCalculator` — verify it composes correctly with the player's own damage modifiers (execution, giant slayer, etc.) rather than being bypassed or double-applied. Freeze/Bleed/etc. applied *by* the player onto the Warden should work unmodified (no special immunity) — nothing about this boss needs to resist player status effects; that's a lever for a future boss (e.g. a "cleansing" test), not this one.
- **Edge cases.** Player kills the Warden mid-telegraph (Slam should simply never resolve — needs a null-check on target liveness at telegraph-resolve time, mirroring existing `IsAlive` checks used elsewhere). Combat ends externally (player death) mid-telegraph — telegraph coroutine/timer must not fire after `CheckCombatEnd` ends combat.
- **Rewards.** No boss-specific reward changes for v1 — reuse existing `RewardManager` boss branch unchanged.
- **Data needed.** One `MonsterData` (existing asset, reused/adjusted), one new `BossKitData` asset with two ability entries (Shield Up, Overhead Slam) each carrying: trigger kind (`Always`+cooldown), cooldown value per phase, telegraph duration, telegraph text, magnitude (damage-reduction % / damage multiplier), optional VFX/SFX refs.
- **New runtime components.** `BossEncounterState` (Section 4) tracking phase index + per-ability cooldown timers + pending-telegraph state; small additions to `CombatantRuntime` (`IsDamageReduced`/`DamageReductionPercent`+timer, or reuse a generalized version of the existing `ActiveDebuff`-style list if it's extended to support damage% rather than only attack-speed%).
- **UI needed.** One new telegraph banner element (can likely reuse/adapt the existing skill-banner styling in `RunFlowController.Combat.cs` rather than building new UXML/USS from scratch) showing ability name + a shrinking countdown or pulse during the telegraph window.
- **VFX/SFX hooks.** Reuse `ActiveSkillActivated`-style event firing at telegraph-start (for banner) and a second point at resolve-time (for the actual hit VFX/SFX and shake), following the exact pattern the boss heavy-attack banner already uses today.

## 8. Implementation Plan

1. **Data model**
   - Likely changed: `MonsterData.cs` (add optional `BossKitData bossKit` field).
   - New: `BossKitData.cs` (ScriptableObject: ability entry list — id, trigger kind, cooldown/threshold values per phase, telegraph text/duration, magnitude, VFX/SFX refs), `BossAbilityId` enum (new, separate from `SkillId` to avoid overloading player/monster passive semantics — or extend `SkillId` if the team prefers one enum; recommend a separate enum for clarity given `SkillId` is already large).
   - Not touched: `PassiveSkillData`/`ActiveSkillData` (unrelated concept, leave as-is), `RoomType`/`FloorMapNodeKind` (owned by the concurrent map-gen work).
   - Dependency: none blocking; can be authored independently of the map-gen work.

2. **Runtime boss logic**
   - New: `BossEncounterState` (plain C# class) — phase tracking, per-ability timers, pending-telegraph state.
   - Likely changed: `CombatantRuntime.cs` (add damage-reduction timer field, or generalize `ActiveDebuff` to carry a damage% axis alongside the existing attack-speed% axis — prefer generalizing over adding a parallel single-purpose field, since a future boss will want the same shape for other stats).
   - Not touched: player-side skill logic, `WeaponAttackState` (baseline attacks stay exactly as today).
   - Potential conflict: if `ActiveDebuff` is generalized, every existing consumer (`GetEffectiveAttackSpeed`, `CombatantStatusEffects` labels) needs review to ensure old attack-speed-only behavior is unaffected — regression risk, needs the existing `CombatManagerTests` (item 18) to stay green plus new tests.

3. **Combat integration**
   - Likely changed: `CombatManager.cs` — new `TickBossEncounter` method (parallel to existing `TickBossHeavyAttacks`, `TickMonsterPeriodicPassives`; could eventually replace `TickBossHeavyAttacks` once the Warden ships, or coexist during transition), HP-threshold check added to the main tick loop, new `switch(BossAbilityId)` execution branch mirroring `ApplyMonsterPassiveOnAttack`'s shape, damage-reduction percentage applied in `DamageCalculator`/`ResolveAttack` damage pipeline.
   - Likely changed: `CombatantFactory.cs` — initialize `BossEncounterState` when spawning a combatant whose `MonsterData.bossKit != null`.
   - Not touched: `MonsterEncounterBudget`, normal-room monster rolling, player active-skill system.
   - Potential conflict: `ResolveAttack` is already a ~340-line monolith (Section, item 4 of audit) — inserting a damage-reduction check must be done carefully to avoid ordering bugs with existing modifiers (execution, giant slayer, etc.); recommend adding it as an early multiplier stage rather than interleaving with existing conditional chains.
   - Dependency: needs item 2 (runtime fields) done first.

4. **Telegraph UI**
   - New: a telegraph banner UI element/behavior in `RunFlowController.Combat.cs` (can likely extend the existing `ShowSkillBanner` mechanism rather than building a parallel one).
   - Likely changed: `RunFlowController.Combat.cs` (subscribe to a new "telegraph started" event, mirroring existing `OnActiveSkillActivated`), possibly `Assets/UI/GameRoot.uxml` / `GameStyles.uss` if a visually distinct (vs. the existing skill banner) telegraph style is wanted.
   - Not touched: map UI (`RunFlowController.Map.cs` — owned by concurrent work).
   - Dependency: needs a new `CombatManager` event (e.g. `TelegraphStarted`) added alongside `HitResolved`/`ActiveSkillActivated`.

5. **Animation/VFX**
   - New (content, not code): optional alternate sprite(s) for phase 2 / telegraph pose, `AudioClip`(s) for Shield Up / Overhead Slam.
   - Likely changed: minimal code — reuse existing DOTween shake precedent (`ChestRevealAnimator.Shake`) for the Slam hit; add a simple `AudioSource.PlayOneShot` call at the event-firing point (first-ever audio hookup in combat code — confirm an `AudioSource` exists/is added to the combat UI hierarchy).
   - Not touched: Hub/Gacha audio systems (separate, already-existing pattern to mirror, not modify).

6. **Boss room integration**
   - Likely changed: `RunFlowController.Combat.cs` (`CombatRoomFlow`) — none structurally required for the Warden (single combatant, no new room states needed); if summon-capable bosses are added later, this is where a `SpawnAdditionalEnemy` call site would eventually live.
   - Not touched: `RunFlowController.cs` boss-room detection, `FloorState`, all map-gen-owned files — explicitly out of scope per constraints.
   - Integration risk flagged in Section 9 re: linear-vs-nonlinear map assumption.

7. **Tests**
   - New: `BossEncounterStateTests.cs` (phase transition on HP threshold, ability cooldown timing, telegraph-then-resolve sequencing, no-resolve-on-dead-target edge case) in `Assets/Tests/EditMode/`, following the existing `CombatManagerTests.cs` style (directly calling `Tick`).
   - Likely changed: none of the existing test files need behavior changes if `TickBossHeavyAttacks` is left alone during transition; if it's replaced, `CombatManagerTests.cs` boss-adjacent assumptions (there are none currently, per audit item 18) should still be checked.
   - Not touched: `FloorMapGeneratorTests.cs` (unrelated, map-gen owned).

8. **Content setup**
   - New: `BossKitData` asset for the Warden, wired into a `MonsterData` boss asset (either the existing `Monster_Boss.asset` or a new floor-1-specific asset — recommend a new asset once multiple bosses exist, but reusing the existing one is fine for the first prototype).
   - Dependency: needs items 1–5 complete.

9. **Regression checks**
   - Run full `Assets/Tests/EditMode` suite (existing `CombatManagerTests`, `DamageCalculatorTests`, `BleedRulesTests`, `BalanceClampsTests`, `CombatResourceVisibilityTests`) to confirm no change to non-boss combat behavior, especially if `ActiveDebuff` is generalized (item 2) or `ResolveAttack`'s modifier order changes (item 3).
   - Manual playtest: normal (non-boss) combat rooms unaffected; existing boss heavy-attack behavior either still works or is cleanly superseded, not left in a half-migrated state.
   - Confirm `PlayModeSmokeTest.cs` still passes (per memory, batchmode smoke tests are sensitive to scene state — verify in an interactive Editor run, not just batchmode, per existing project feedback memory on this).

## 9. Risks

- **Linear-map coupling.** `RunFlowController.cs:495` currently hardcodes "boss room is always the floor's terminal node," and `FloorMapGenerator` (concurrent work) still appears to preserve "exactly one boss node per floor, always at max depth" as an invariant per the audit. If the non-linear map work changes this assumption (e.g. optional bosses, multiple boss-eligible nodes, mid-floor bosses), the boss room flow entry point (`ResolveRoom`'s `RoomType.Boss` branch) and the "camp offered after boss" logic may need rework beyond this design's scope. **Flagging, not resolving** — coordinate with the map-gen work before implementation starts.
- **`ResolveAttack` monolith risk.** Adding a damage-reduction stage to an already ~340-line method with many interacting conditional modifiers risks subtle ordering bugs (e.g. reduction applying before vs. after armor, before vs. after execution bonus). Mitigate with focused unit tests on the interaction, not just the isolated mechanic.
- **`ActiveDebuff` generalization risk.** If chosen (recommended) over a single-purpose field, every existing consumer must be re-verified — this touches player-facing systems (Intimidation, Warlock Slow, event/trap debuffs), not just boss code.
- **Frustration risk in the mechanics table.** Counter/action-punish and mid-fight rule-change mechanics (Concepts 8, and the Counter/Punish rows in Section 5) are explicitly flagged as high player-frustration risk — recommend deferring them until simpler bosses have validated the framework and there's playtest signal on pacing.
- **No existing audio system in combat.** First-time wiring of `AudioSource`/`AudioClip` into combat code; scope this as its own small task rather than bundling with ability logic, so an audio-specific bug doesn't block ability-logic verification.
- **Balance numbers are unset.** No numeric telegraph/cooldown/multiplier values are finalized in this doc (per instructions) — a playtest/numbers pass is required before this ships, separate from the architecture work.

## 10. Questions for Designer

1. Should the first boss (Warden) replace the existing single shared `Monster_Boss.asset`/`bossData` reference immediately, or coexist as a second boss option while `bossData` stays a fallback during the transition? (Affects whether `RunFlowController.bossData` becomes a list/lookup now or later.)
2. Is a per-floor boss roster (one unique boss per floor number) meant to be authored by hand per floor, or should there eventually be a random/pool selection among boss-eligible bosses for a given floor tier? This affects whether `RunFlowController` needs a boss-selection step now or can keep a single serialized reference for the first prototype.
3. Given the concurrent non-linear map work: will boss placement stay "always exactly one, always the terminal node," or is that expected to change? This gates whether Section 6's room-flow integration plan (Section 8, item 6) needs revisiting before implementation starts.
4. Is background music/audio infrastructure for combat planned elsewhere already (e.g. by the art/audio integration work referenced in project memory), or should the boss system be the one to introduce the first `AudioSource` in combat code?

## 11. Suggested Work Breakdown

Small, independently landable/commit-sized tasks, in dependency order:

1. `BossKitData` ScriptableObject + `BossAbilityId` enum + `MonsterData.bossKit` field (data-only, no behavior change).
2. Generalize `CombatantRuntime.ActiveDebuff` to carry damage% (or add a narrowly-scoped parallel field if generalization is deemed too risky) + regression tests for existing attack-speed consumers.
3. `BossEncounterState` class + HP-threshold phase-change detection + unit tests (no UI/VFX yet, log-only verification).
4. `CombatManager.TickBossEncounter` + `switch(BossAbilityId)` execution stub (Shield Up only, no telegraph) + tests, wired to spawn only for `bossKit != null` combatants.
5. Telegraph event (`TelegraphStarted` on `CombatManager`) + two-step announce/resolve timing for Overhead Slam + tests for the dead-target/combat-ended edge cases.
6. Telegraph banner UI in `RunFlowController.Combat.cs` (reusing/extending `ShowSkillBanner`).
7. VFX/SFX hookup for the two Warden abilities (first `AudioSource` wiring + shake/particle on Slam resolve).
8. Content: author the Warden `BossKitData` asset + (optional) alternate phase-2 sprite; wire into a boss `MonsterData` asset.
9. Full regression pass (existing EditMode suite + manual interactive playtest of a boss room) + `PlayModeSmokeTest` check.
10. (Follow-up, separate from this breakdown) Second boss concept from Section 6 to prove the framework generalizes without new hooks — best candidate: Frostbound Wraith (reuses existing Freeze system most heavily, lowest new-code footprint).
