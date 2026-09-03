# Active Skills Panel — дизайн

Дата: 2026-09-03
Статус: черновик, ждёт ревью пользователя

## 1. Контекст и цель

Сейчас у персонажа ровно один активный навык («уникальная активка» в терминах ГДД 3.9/3.11),
жёстко закодированный как набор плоских полей в `CombatManager` и один текстовый `Button` в
`GameRoot.uxml`. Берсерк Варвара — отдельный, полностью отличный по коду путь (ручной тумблер,
не проходит через `ConfigureUniqueActiveSkill`/`TryActivateUniqueActiveSkill`).

Цель этой задачи:

1. Авто-режим применения активки выключен по умолчанию (сейчас включён).
2. Кулдаун-активка готова сразу в начале комнаты/боя (сейчас — уходит в полный кулдаун при
   старте, чтобы избежать мгновенного применения; это поведение сознательно вводилось раньше и
   теперь сознательно отменяется, т.к. активация становится ручной).
3. Скиллы получают иконки и визуально оформленную панель в духе автобатлеров (Cookie Kingdom
   Run и т.п.) внизу по центру экрана, с хоткеем.
4. Инфраструктура готова к **нескольким** активным скиллам на панели одновременно (сейчас у
   каждого класса всего один, это не меняется контентно — меняется только код).
5. Инфраструктура поддерживает два типа скиллов с явно разными состояниями UI:
   - **Cooldown** — как «3 быстрые атаки»/«Дымовая граната»: активируется, уходит в кулдаун,
     ждёт готовности.
   - **Toggle** — как «Берсерк»: включается/выключается вручную, кулдауна не имеет, состояние
     «включено»/«выключено» должно быть визуально безошибочно различимо.

Контентно ничего не меняется: каждый класс по-прежнему конфигурируется с одним элементом в
списке скиллов. Меняется только то, что код перестаёт предполагать «ровно один скилл, ровно один
спецкейс для Берсерка».

## 2. Данные: `ActiveSkillData`

Добавляем два поля:

```csharp
public enum ActiveSkillType { Cooldown, Toggle }

public class ActiveSkillData : ScriptableObject
{
    public string skillName;
    public SkillId skillId;
    [TextArea(3, 10)] public string effectDescription;
    public int maxLevel;
    public float cooldownSeconds;       // Toggle-скиллы игнорируют это поле
    public ActiveSkillTargetType targetType;

    public ActiveSkillType skillType;   // НОВОЕ
    public Sprite icon;                 // НОВОЕ
}
```

`Skill_Berserk.asset` получает `skillType = Toggle`. `Skill_ThreeQuickStrikes.asset` и
`Skill_SmokeBomb.asset` — `skillType = Cooldown` (поведение не меняется).

## 3. Рантайм: `CombatManager`

### 3.1 Новый тип состояния

```csharp
public class ActiveSkillRuntimeState
{
    public ActiveSkillData Data;
    public int HitCount;                 // для Cooldown hit-loop скиллов (0 для Дымовой гранаты)
    public float DamageMultiplierPerHit;
    public float CooldownTimer;          // только Cooldown
    public bool IsToggleActive;          // только Toggle
    public bool AutoMode;                // только Cooldown; Toggle не имеет авто-режима
}
```

### 3.2 Замена плоских полей

Поля `activeSkillHitCount/…/activeSkillAutoMode` и метод `ConfigureUniqueActiveSkill` заменяются на:

```csharp
public List<ActiveSkillRuntimeState> ActiveSkills { get; private set; } = new();

public void ConfigureActiveSkills(IEnumerable<ActiveSkillConfigEntry> skills)
{
    ActiveSkills.Clear();
    foreach (var entry in skills)
    {
        ActiveSkills.Add(new ActiveSkillRuntimeState
        {
            Data = entry.Data,
            HitCount = entry.HitCount,
            DamageMultiplierPerHit = entry.DamageMultiplierPerHit,
            CooldownTimer = 0f,          // п.2: готов с начала комнаты, не в кулдауне
            IsToggleActive = false,
            AutoMode = entry.AutoMode,
        });
    }
}
```

`ActiveSkillConfigEntry` — маленькая struct-обёртка (Data/HitCount/DamageMultiplierPerHit/AutoMode),
передаваемая вызывающей стороной (`RunFlowController.Combat.cs`), чтобы не тащить level-зависимую
арифметику урона внутрь `CombatManager`.

Сегодня `RunFlowController.Combat.cs` вызывает `ConfigureActiveSkills` со списком из ОДНОГО
элемента (для не-Барбариан классов) либо с одним `Toggle`-элементом (Берсерк, вместо текущего
`isBarbarianCombat` if/else в UI-коде). `ClearUniqueActiveSkillConfiguration()` заменяется на
`ActiveSkills.Clear()`.

### 3.3 Активация

```csharp
public bool TryActivateSkill(int slotIndex)
{
    if (!IsCombatActive || slotIndex < 0 || slotIndex >= ActiveSkills.Count) return false;
    var slot = ActiveSkills[slotIndex];

    return slot.Data.skillType switch
    {
        ActiveSkillType.Toggle => TryToggleSkill(slot),
        _ => TryActivateCooldownSkill(slot),
    };
}
```

- `TryActivateCooldownSkill` — переносит текущее тело `TryActivateUniqueActiveSkill` (hit-loop,
  спецкейс Дымовой гранаты по `skillId`, установка `slot.CooldownTimer = slot.Data.cooldownSeconds`
  вместо `Player.ActiveSkillCooldownTimer`).
- `TryToggleSkill` — переносит тело `SetBerserkActive`, но по `skillId` (switch, как и раньше для
  эффектов), инвертируя `slot.IsToggleActive` вместо чтения аргумента `bool active` — вызывающая
  сторона (клик по иконке) всегда просит именно "переключить", а не "установи такое-то значение".
  Guard «нельзя включить неизученный навык» (`UniqueBerserkLevel <= 0`) остаётся.

`Player.ActiveSkillCooldownTimer` и `Player.IsBerserkActive` (поля на `CombatantRuntime`) остаются
как есть для остальной боевой логики (сопротивления, самоурон и т.д.), но перестают быть
источником истины для UI/готовности — источник истины теперь `ActiveSkillRuntimeState` в списке.
Фактически `IsBerserkActive`/`ActiveSkillCooldownTimer` синхронизируются из
`TryToggleSkill`/`TryActivateCooldownSkill` в момент активации (не дублируем математику, просто
проставляем оба места).

`IsActiveSkillReady`/`ActiveSkillCooldownRemaining` заменяются на версии с индексом слота:
`IsSkillReady(int slotIndex)`, `SkillCooldownRemaining(int slotIndex)`.

### 3.4 `Tick()`

Вместо одного `if (IsActiveSkillConfigured && activeSkillAutoMode && IsActiveSkillReady)`:

```csharp
for (int i = 0; i < ActiveSkills.Count; i++)
{
    var slot = ActiveSkills[i];
    if (slot.CooldownTimer > 0f) slot.CooldownTimer -= deltaTime;
    if (slot.Data.skillType == ActiveSkillType.Cooldown && slot.AutoMode && slot.CooldownTimer <= 0f)
    {
        TryActivateSkill(i);
    }
}
```

Существующий тик самоурона Берсерка (`Player.IsBerserkActive`, раз в секунду) не переносится в
этот цикл — он завязан на конкретный игровой эффект, а не на общую механику toggle-скиллов;
остаётся в `Tick()` как есть, читая `Player.IsBerserkActive`.

### 3.5 `StartCombat`

Строка `Player.ActiveSkillCooldownTimer = activeSkillCooldownSeconds;` удаляется вместе с
комментарием — `ConfigureActiveSkills` уже ставит `CooldownTimer = 0f` (готов сразу), что и есть
требуемое поведение п.2.

## 4. UI

### 4.1 Разметка (`GameRoot.uxml`)

`combat-controls-row` (сейчас: Toggle + Button + Toggle, все текстовые) заменяется на контейнер
`SkillPanelContainer`, закреплённый снизу по центру `CombatStage`:

```xml
<ui:VisualElement name="SkillPanelContainer" class="skill-panel-container" />
```

Слоты внутри создаются в коде (не статично в UXML), по одному на элемент `ActiveSkills`, из
шаблона `SkillSlotTemplate.uxml`:

```xml
<ui:VisualElement class="skill-slot">
    <ui:VisualElement name="AutoModeToggle" class="skill-auto-toggle" />   <!-- только Cooldown -->
    <ui:VisualElement name="SkillIconFrame" class="skill-icon-frame">
        <ui:Image name="SkillIcon" class="skill-icon" />
        <ui:VisualElement name="CooldownOverlay" class="skill-cooldown-overlay" />
        <ui:Label name="CooldownText" class="skill-cooldown-text" />
        <ui:Label name="HotkeyLabel" class="skill-hotkey-label" />
    </ui:VisualElement>
</ui:VisualElement>
```

### 4.2 Визуальные состояния (`GameStyles.uss`)

- **Cooldown, не готов:** `CooldownOverlay` — полупрозрачная тёмная заливка, высота = доля
  оставшегося времени (`style.height = Percent(remaining/max*100)`, обновляется каждый кадр из
  `UpdateCombatUI`/новый `UpdateSkillPanel`), таймер в секундах поверх иконки.
  Хоткей-лейбл виден, но приглушён.
- **Cooldown, готов:** оверлей скрыт, рамка иконки получает CSS-класс `skill-icon-ready` —
  светлая пульсирующая обводка (USS-transition/`,scale` анимация через class toggle, без
  кастомных шейдеров — тем же приёмом, что уже используется для `skill-activation-banner`, если
  там есть анимация классом; иначе — простая smooth transition на border-color).
- **Toggle, выключен:** `skill-icon-frame` в приглушённых тонах (уменьшенная яркость/сепия через
  USS `background-color`/`opacity` на затемняющем оверлее — не полагаемся на `tint`, которого нет
  в UI Toolkit), обычная тонкая серая рамка.
  Overlay/CooldownText скрыты — они не имеют смысла для toggle.
- **Toggle, включён:** насыщенная, без затемнения; рамка — яркая, отличного (акцентного) цвета +
  лёгкое свечение (`box-shadow`-подобный приём через дополнительный `VisualElement`-подложку с
  blur, как это принято в UI Toolkit). Обязательно другой визуальный язык, чем "скилл готов" у
  Cooldown-слотов, чтобы игрок на глаз отличал "можно нажать" от "уже активно".

`AutoModeToggle` в каждом Cooldown-слоте — маленькая кнопка-иконка (не текстовый `ui:Toggle`),
две визуальных фазы (вкл/выкл), по умолчанию выкл (п.1). У Toggle-слотов элемент отсутствует.

### 4.3 Хоткеи

Слот с индексом `i` получает хоткей из фиксированного массива `[KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R]`
(4 достаточно с большим запасом сверх текущего максимума в 1 скилл на персонажа). Подпись хоткея
рисуется в `HotkeyLabel`. Обработка: `RunFlowController.Combat.cs` в `Tick`/`Update` проверяет
`Input.GetKeyDown` для каждого сконфигурированного слота и вызывает `combatManager.TryActivateSkill(i)`
с той же проверкой готовности/auto-mode, что и клик по иконке (`SkillIconFrame.RegisterCallback<ClickEvent>`).

### 4.4 Обновление панели

`UpdateCombatUI()` получает новый под-метод `UpdateSkillPanel()`: пересоздаёт/переиспользует слоты
из `combatManager.ActiveSkills` (пересоздаёт только при смене количества/состава — например, при
входе в новый бой; на каждый кадр только обновляет оверлеи/классы), симметрично существующему
паттерну `UpdateBerserkAura`.

## 5. Контент (иконки)

Три иконки под существующие скиллы, сгенерированные через PixelLab в стиле, согласованном с уже
существующими combat-спрайтами персонажей (`Assets/Resources/CharacterAnimations/...`):

- Three Quick Strikes (Дженнифер/Воин) — Cooldown
- Smoke Bomb (Плут) — Cooldown
- Berserk (Варвар) — Toggle

Хранятся в `Assets/Sprites/UI/SkillIcons/`, привязываются полем `icon` на соответствующих
`.asset`-файлах в `Assets/ScriptableObjects/Skills/Unique/`.

## 6. Затронутые файлы

- `Assets/Scripts/Data/ActiveSkillData.cs` — новые поля `skillType`, `icon`; новый enum `ActiveSkillType`.
- `Assets/Scripts/Managers/CombatManager.cs` — замена плоских полей на `List<ActiveSkillRuntimeState>`,
  `ConfigureActiveSkills`, `TryActivateSkill`, `TryActivateCooldownSkill`, `TryToggleSkill`,
  `IsSkillReady`/`SkillCooldownRemaining`, обновлённый `Tick()`, `StartCombat()` без принудительного
  кулдауна при старте.
- `Assets/Scripts/UI/RunFlowController.Combat.cs` — вызов `ConfigureActiveSkills` с одним элементом
  (Cooldown или Toggle в зависимости от класса) вместо `ConfigureUniqueActiveSkill`/спецкейса Берсерка;
  `UpdateSkillPanel()`; обработка хоткеев Q/W/E/R; удаление прямых ссылок на старые UXML-элементы
  `AutoModeToggle`/`ActiveSkillButton`/`BerserkToggle`.
- `Assets/UI/GameRoot.uxml` — `SkillPanelContainer` вместо `combat-controls-row`.
- Новый `Assets/UI/SkillSlotTemplate.uxml` (или программная сборка `VisualElement`, если проще без
  отдельного `.uxml`-шаблона — решается на этапе плана реализации).
- `Assets/UI/GameStyles.uss` — стили слота, состояний Cooldown/Toggle, ready-пульсации, авто-тумблера.
- `Assets/ScriptableObjects/Skills/Unique/Skill_ThreeQuickStrikes.asset`, `Skill_SmokeBomb.asset`,
  `Skill_Berserk.asset` — `skillType` + `icon`.
- Новые PNG-иконки в `Assets/Sprites/UI/SkillIcons/` (через PixelLab).

## 7. Тестирование

- EditMode-тесты на `CombatManager`: `ConfigureActiveSkills` ставит `CooldownTimer = 0` для
  Cooldown-скиллов и `IsToggleActive = false` для Toggle; `TryActivateSkill` корректно
  диспатчит по типу; авто-режим по умолчанию выключен (`AutoMode` из конфигурации, не хардкод
  `true`); `Tick()` авто-кастует только когда `AutoMode` включён явно.
- Ручной плейтест в редакторе: иконки видно и отличимо в обоих состояниях (Cooldown/Toggle),
  клик и хоткей Q активируют скилл, авто-тумблер по умолчанию выключен и его можно включить,
  Берсерк визуально отличим включён/выключен без чтения текста.
- PlayMode smoke test — без `-quit`, с бэкапом save-файла (см. память по этому риску).

## 8. Открытые вопросы / решения, принятые в ходе брейнсторминга

- Многослотовость — только инфраструктура, контент не меняется (ни один класс сегодня не
  получает второй скилл).
- Тип скилла моделируется общим `enum ActiveSkillType`, Берсерк перестаёт быть спецкейсом на
  уровне конфигурации/готовности (но сохраняет уникальный игровой эффект — самоурон, сопротивления
  — через switch по `skillId`, как и остальные уникальные скиллы).
- Кулдаун-скилл готов сразу в начале боя (откат более раннего фикса, сознательно, т.к. активация
  больше не автоматическая по умолчанию).
- Хоткеи: Q для первого слота, дальше W/E/R по порядку.
