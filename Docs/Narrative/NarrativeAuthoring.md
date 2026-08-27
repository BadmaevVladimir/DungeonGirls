# Dungeon Girls — заполнение VN-сцен

Текстовые сцены хранятся по одной на файл в `Assets/StreamingAssets/Content/Scenes`. Для новой обычной реплики достаточно указать `speaker` и `text`. Поле `emotion` опционально: если оно пропущено, остаётся текущая эмоция персонажа.

Стабильные ID персонажей:

- `jennifer` — Дженифер, Воин;
- `sasha` — Саша, Варвар;
- `violet` — Вайолет, Плут;
- `player` — игрок, без спрайта;
- `narrator` — повествователь, без спрайта.

Зафиксированные сцены Дженифер:

- `jennifer_intro_tavern` — «Первая встреча»;
- `jennifer_campfire` — «У костра»;
- `jennifer_hot_springs` — «Горячие источники».

Триггеры этих сцен пока намеренно не подключены.

## Минимальный пример

```json
{
  "id": "jennifer_intro_tavern",
  "title": "Первая встреча",
  "characterId": "jennifer",
  "background": "tavern",
  "actors": [
    {
      "characterId": "jennifer",
      "displayName": "Дженифер",
      "slot": 2,
      "emotion": "neutral"
    }
  ],
  "lines": [
    {
      "speaker": "jennifer",
      "text": "Текст реплики.",
      "emotion": "neutral"
    },
    {
      "speaker": "player",
      "speakerName": "Игрок",
      "text": "Ответ игрока. Для него спрайт не нужен."
    }
  ]
}
```

## Позиции персонажей

Одновременно видны максимум пять персонажей. Поле `slot` принимает значения `0`–`4` слева направо. Говорящий автоматически немного увеличивается, остальные персонажи затемняются. Имя берётся из `displayName`; `speakerName` в конкретной реплике позволяет его переопределить.

## Эмоции

```json
{
  "speaker": "jennifer",
  "emotion": "smile",
  "text": "Эта реплика переключит её на спрайт smile."
}
```

Если указанной эмоции ещё нет в библиотеке визуалов, плеер использует `neutral`, затем первый доступный спрайт.

## Изменения состава сцены

Массив `stage` нужен только в репликах, где меняется постановка:

```json
{
  "speaker": "sasha",
  "text": "Саша входит в кадр.",
  "stage": [
    { "action": "show", "characterId": "sasha", "slot": 4, "emotion": "neutral" },
    { "action": "move", "characterId": "jennifer", "slot": 1 },
    { "action": "emotion", "characterId": "violet", "emotion": "surprised" }
  ]
}
```

Доступные действия: `show`, `hide`, `move`, `emotion`.

## Фоны и CG

- `background` в корне задаёт начальный фон;
- `background` в реплике меняет фон перед её показом;
- `cg` в реплике включает полноэкранный CG;
- `returnToStage: true` скрывает CG и возвращает фон со спрайтами.

```json
{
  "speaker": "narrator",
  "speakerName": "Рассказчик",
  "text": "Кадр сменяется иллюстрацией.",
  "cg": "jennifer_tavern_cg"
},
{
  "speaker": "jennifer",
  "text": "Возвращение к обычной постановке.",
  "returnToStage": true
}
```

Текстовые JSON можно менять без пересборки. Новые портреты эмоций, фоны и CG нужно один раз добавить в `NarrativeVisualLibrary` в Unity, используя те же строковые ID.
