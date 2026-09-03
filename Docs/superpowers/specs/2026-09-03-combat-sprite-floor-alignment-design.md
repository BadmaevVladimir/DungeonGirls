# Боевые спрайты «висят в воздухе» — дизайн

Дата: 2026-09-03
Статус: черновик, ждёт ревью пользователя

## 1. Контекст и цель

26.08.2026 уже был исправлен один класс этого бага: фон боя (`Dungeon.png`, 1536×1024, `ScaleAndCrop`)
на широких экранах обрезается неравномерно, из-за чего статичный процент в USS не мог держать
обёртки спрайтов на нарисованной линии пола на всех соотношениях сторон. `RunFlowController.Combat.cs`
(`GetStageFloorGapFromBottom`/`ComputeStageFloorGap`) считает эту линию по формуле cover-кропа и
выставляет её как `marginBottom` на обёртки игрока/врагов.

Пользователь сообщает, что персонажи всё ещё выглядят подвешенными — и это касается ВСЕХ боевых
спрайтов (игрок и монстры), не только новых. Причина другого порядка: UI Toolkit `Image` всегда
центрирует картинку внутри своей рамки (ни `ScaleToFit`, ни `ScaleAndCrop` не поддерживают
прижатие к нижнему краю). Каждый PNG, сгенерированный PixelLab, имеет свой прозрачный отступ снизу
холста — обёртка стоит на правильной линии пола, но видимые "ноги" персонажа внутри неё не
доходят до её нижнего края. `spritePivot` у всех кадров — дефолтный `{x: 0.5, y: 0.5}` (проверено
в `.meta`), т.е. точка привязки "где ноги" нигде не размечена.

**Важное ограничение (поймано на этапе брейнсторминга):** отступ нельзя компенсировать
покадрово — тогда прыжковые/выпадовые кадры анимации (например, слайм на подскоке, гарпия в
верхней точке взлёта) будут силой прижаты к полу и потеряют вертикальное движение. Компенсация
должна быть ОДНА константа на весь персонажа/монстра (минимум отступа по всем его кадрам —
самый "приземлённый" кадр), а не пересчитываться на каждый кадр анимации.

## 2. Архитектура

Три части: офлайн-анализатор пикселей → сгенерированная таблица данных → рантайм-компенсация
поверх уже существующего `marginBottom`.

### 2.1 Editor-анализатор

`Assets/Editor/SpriteFloorAnalyzer.cs`, разовый инструмент, запускается через
`-executeMethod SpriteFloorAnalyzer.Run` (как и прошлые one-off скрипты в этом проекте — не
остаётся частью рантайм-кода, но, в отличие от них, **остаётся в репозитории**, т.к. таблицу нужно
перегенерировать при добавлении новых боевых спрайтов).

Для каждой текстуры — через `RenderTexture` + `Graphics.Blit` + `ReadPixels` (работает для ЛЮБОЙ
текстуры независимо от `Read/Write Enabled` в её импорте — не трогаем настройки импорта у 200+
существующих PNG):

```csharp
static float BottomTransparentFraction(Texture2D source, float alphaThreshold = 0.05f)
{
    var rt = RenderTexture.GetTemporary(source.width, source.height);
    Graphics.Blit(source, rt);
    var prev = RenderTexture.active;
    RenderTexture.active = rt;
    var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
    readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
    readable.Apply();
    RenderTexture.active = prev;
    RenderTexture.ReleaseTemporary(rt);

    var pixels = readable.GetPixels32();
    int width = source.width, height = source.height;
    for (int y = 0; y < height; y++) // снизу вверх — ищем первую строку с непрозрачным пикселем
    {
        for (int x = 0; x < width; x++)
        {
            if (pixels[y * width + x].a / 255f > alphaThreshold)
            {
                Object.DestroyImmediate(readable);
                return (float)y / height; // строк ниже этого ряда — y штук, это и есть отступ
            }
        }
    }
    Object.DestroyImmediate(readable);
    return 0f; // полностью прозрачная текстура — не должно происходить, безопасный дефолт
}
```

Источники сканирования:

1. **`Assets/Resources/CharacterAnimations/<Key>/**/*.png`** — для каждого `<Key>` (`Jennifer`,
   `Sasha`, `Violet`, `Monster_Bat`, `Monster_DarkKnight`, … — ровно те же имена папок, что уже
   использует `Resources.Load` в `JenniferAnimationFrames`/`SashaAnimationFrames`/
   `VioletAnimationFrames`/`MonsterAnimationFrames`) — берём **минимум** `BottomTransparentFraction`
   по ВСЕМ PNG под этой папкой (idle + атака + скилл-анимации вместе, не по отдельности — иначе
   персонаж дёргался бы по вертикали при переключении между idle/attack).
2. **`BossPhaseData.phaseSprite`** для каждого `BossKitData` под `Assets/ScriptableObjects/**`
   (боссы не лежат в `Resources/`, это обычные инспекторные ссылки) — по одному значению на
   каждую фазу (у босса пока нет нескольких кадров на фазу, только сам факт смены спрайта).

### 2.2 Данные

- **Анимированные (Resources) спрайты** → `Assets/Resources/CharacterAnimations/SpriteFloorOffsets.json`,
  простая карта `{ "Jennifer": 0.08, "Monster_Bat": 0.14, ... }`. Формат — обычный JSON, тот же
  принцип, что уже использует `SaveManager` для сейва; читаемо и диффится в git-истории при
  перегенерации.
- **Боссы** → отступ пишется НЕПОСРЕДСТВЕННО в новое поле на самом `BossPhaseData`:
  ```csharp
  public float floorPaddingFraction; // 0 = стоит вплотную к низу канваса, стандартный дефолт для старых фаз
  ```
  (тот же паттерн, что и добавление `icon` на `ActiveSkillData` в прошлой задаче — поле на
  существующем дата-ассете, без отдельной таблицы, т.к. боссов и так немного и они уже
  ScriptableObject).

### 2.3 Рантайм

Новый статический класс `Assets/Scripts/UI/SpriteFloorOffsets.cs`:

```csharp
public static class SpriteFloorOffsets
{
    static Dictionary<string, float> table; // ленивая загрузка + парсинг JSON, один раз за сессию

    public static float GetOffsetFraction(string animationFolderKey)
    {
        table ??= Load();
        return animationFolderKey != null && table.TryGetValue(animationFolderKey, out var v) ? v : 0f;
    }
}
```

Ключевое упрощение по сравнению с первоначальным вариантом дизайна: поскольку компенсация — ОДНА
константа на персонажа/монстра (не на конкретный спрайт), не нужен `Dictionary<Sprite,float>` и
регистрация каждого загруженного кадра. Нужен только **ключ папки аниации того, кто сейчас в
бою** — а он уже вычисляется существующим кодом:

- Игрок: `PlayableCharacterAnimations` получает маленький новый метод `FolderKey(displayName)`
  ("Дженифер"→"Jennifer" и т.д.), зеркалящий уже существующие `Idle`/`Attack`/`FastAttackLoop`.
- Монстр: `MonsterAnimations` уже возвращает `FolderKey` через внутренний `Lookup` — используем
  его напрямую (с префиксом `"Monster_"`, как в `MonsterAnimationFrames.Load`).
- Босс: не через `SpriteFloorOffsets` вообще — `RunFlowController` читает
  `enemy.BossEncounter`'s текущей фазы `floorPaddingFraction` напрямую.

`RunFlowController.Combat.cs`, там же где сейчас выставляется `marginBottom` (в `UpdateCombatUI`,
рядом с `stageFloorGap`):

```csharp
float stageFloorGap = GetStageFloorGapFromBottom();
float playerSpriteOffset = SpriteFloorOffsets.GetOffsetFraction(PlayableCharacterAnimations.FolderKey(player.DisplayName)) * playerStageSprite.resolvedStyle.height;
playerStageWrapper.style.marginBottom = stageFloorGap + playerSpriteOffset;
```

и аналогично для каждой `EnemyStageEntry` (либо через `MonsterAnimations`-ключ, либо через
`BossPhaseData.floorPaddingFraction`, если это босс). Компенсация вычисляется каждый кадр (дёшево —
один `Dictionary`-лукап по строке), но её ЗНАЧЕНИЕ на всё время боя с этим противником не меняется
(потому что ключ — персонаж/монстр целиком, не текущий кадр анимации), поэтому прыжки/выпады внутри
анимации визуально сохраняются: кадр с бОльшим собственным отступом просто окажется выше
константной линии, а не будет к ней прижат.

## 3. Затронутые файлы

- `Assets/Editor/SpriteFloorAnalyzer.cs` (новый, остаётся в репо для перегенерации при добавлении арта).
- `Assets/Resources/CharacterAnimations/SpriteFloorOffsets.json` (новый, генерируется анализатором).
- `Assets/Scripts/Data/BossPhaseData.cs` — новое поле `floorPaddingFraction`.
- `Assets/Scripts/UI/SpriteFloorOffsets.cs` (новый) — рантайм-загрузка таблицы + лукап.
- `Assets/Scripts/UI/PlayableCharacterAnimations.cs` — новый метод `FolderKey(displayName)`.
- `Assets/Scripts/UI/MonsterAnimations.cs` — используем уже существующий `FolderKey` из `Lookup`
  (возможно, потребуется сделать его публично доступным, если сейчас он приватный/внутренний).
- `Assets/Scripts/UI/RunFlowController.Combat.cs` — `UpdateCombatUI`: компенсация добавляется к
  уже существующему `marginBottom` для игрока и каждого врага.
- Существующие `BossPhaseData`-ассеты (сейчас 2 фазы Стража) получают заполненное
  `floorPaddingFraction` от анализатора.

## 4. Тестирование

- EditMode-тест на `BottomTransparentFraction`-подобную чистую функцию: даём ей маленькую
  in-memory `Texture2D` (нарисованную вручную пикселями в тесте — без реального PNG-файла),
  проверяем разные варианты (пусто снизу, вплотную к низу, полностью прозрачно).
- EditMode-тест на `SpriteFloorOffsets.GetOffsetFraction` с подменённой/тестовой JSON-таблицей —
  известный ключ возвращает записанное значение, неизвестный — `0f` (безопасный дефолт, не ломает
  поведение для персонажей без записи в таблице).
- Ручной прогон анализатора на реальных ассетах (нужен Unity Editor — доступен в этой песочнице
  по опыту прошлой задачи) + визуальная проверка в Play Mode, что персонажи/монстры стоят на полу,
  а прыжковые кадры (слайм, гарпия) по-прежнему визуально двигаются вверх-вниз, а не прилипают.

## 5. Открытые вопросы / решения, принятые в ходе брейнсторминга

- Компенсация — константа на персонажа/монстра (минимум по всем его кадрам), не покадровая — иначе
  ломает вертикальное движение прыжковых/выпадовых анимаций (поймано пользователем на этапе
  дизайна).
- Боссы — отдельный путь (поле на `BossPhaseData`), не через JSON-таблицу, т.к. их спрайты не в
  `Resources/` и их мало.
- Персонажи/монстры без записи в таблице (например, будущий контент до перегенерации таблицы)
  получают компенсацию `0f` — то есть текущее поведение, без регрессии.
