using System.Collections.Generic;

public readonly struct TutorialEntry
{
    public readonly string Id;
    public readonly string Section;
    public readonly string Title;
    public readonly string Body;

    public TutorialEntry(string id, string section, string title, string body)
    {
        Id = id;
        Section = section;
        Title = title;
        Body = body;
    }
}

// Весь текст обучения хранится в одном месте. Триггеры используют стабильные ID, а справочник
// использует те же формулировки, поэтому контекстные подсказки и постоянная помощь не расходятся.
//
// Разделение каналов (важно при добавлении нового текста):
//   * оверлей (Entries)   — механика, без которой первое решение принимается вслепую. Один экран,
//                           3-4 строки, останавливает игру, показывается ОДИН раз за сохранение.
//   * тултип (Tooltip*)   — расшифровка того, что уже видно на экране. Одна-две фразы. Нет
//                           элемента на экране — нет тултипа.
//   * справка (HelpEntries) — те же оверлеи, сгруппированные по разделам, доступна всегда.
// Полные формулы, таблицы по уровням и граничные случаи не место ни в оверлее, ни в тултипе.
public static class TutorialContent
{
    public const string Intro = "intro";
    public const string CharacterSelection = "character_selection";
    public const string MentorSelection = "mentor_selection";
    public const string RunStart = "run_start";
    public const string Map = "map";
    public const string CombatBasics = "combat_basics";
    public const string Defenses = "defenses";
    public const string JenniferActive = "active_jennifer";
    public const string VioletActive = "active_violet";
    public const string SashaActive = "active_sasha";
    public const string Reward = "reward";
    public const string Equipment = "equipment";
    public const string LevelUp = "level_up";
    public const string Camp = "camp";
    public const string RiskRoom = "risk_room";
    public const string EventRoom = "event_room";
    public const string Merchant = "merchant";
    public const string Boss = "boss";
    public const string Pause = "pause";
    public const string Results = "results";
    public const string VeteranCreated = "veteran_created";
    public const string Buildings = "buildings";
    public const string Gacha = "gacha";
    public const string Characters = "characters";
    public const string Veterans = "veterans";
    public const string Relationships = "relationships";
    public const string HotSprings = "hot_springs";

    const string SectionRun = "Забег";
    const string SectionCombat = "Бой";
    const string SectionProgress = "Развитие героини";
    const string SectionVillage = "Деревня";

    static readonly Dictionary<string, TutorialEntry> Entries = new Dictionary<string, TutorialEntry>
    {
        [Intro] = new TutorialEntry(Intro, SectionRun, "Добро пожаловать в Dungeon Girls",
            "Впереди подземелье из десяти этажей. На каждом этаже — десять комнат и босс в конце.\n\n" +
            "Забег закончится победой или смертью, но награду ты получишь в любом случае: чем дальше зайдёшь, тем больше. На эти награды растёт деревня, а вместе с ней — все будущие забеги.\n\n" +
            "Нажми «Начать забег»."),

        [CharacterSelection] = new TutorialEntry(CharacterSelection, SectionProgress, "Выбор героини",
            "У каждой героини свой стиль боя. Её класс решает, какое оружие ей подходит и какие навыки будут попадаться при повышении уровня.\n\n" +
            "Наведи курсор на навык в карточке, чтобы прочитать, что он делает.\n\n" +
            "Дженифер доступна всегда. Вайолет и Саша открываются, когда впервые выпадут в гаче."),

        [MentorSelection] = new TutorialEntry(MentorSelection, SectionProgress, "Наставник",
            "Наставник — ветеран прошлого забега, обязательно другой героини.\n\n" +
            "Его уникальный пассивный навык ты получаешь сразу и бесплатно: он не занимает ни одного из пяти слотов обычных навыков.\n\n" +
            "Остальные его навыки просто начнут чаще попадаться при повышении уровня — их всё равно придётся выбирать самой. Наставника можно не брать вовсе."),

        [RunStart] = new TutorialEntry(RunStart, SectionRun, "Начало забега",
            "Сверху видно этаж, пройденные комнаты и оставшиеся рационы. Чем глубже этаж, тем опаснее враги.\n\n" +
            "Уровень, навыки и снаряжение живут только внутри забега — в следующий раз всё начнётся заново. С собой остаётся только то, что начислят на экране итогов."),

        [Map] = new TutorialEntry(Map, SectionRun, "Карта этажа",
            "Этаж — это сеть комнат. До босса ты пройдёшь десять из них.\n\n" +
            "Идти можно только туда, куда ведёт стрелка от твоей комнаты: иногда путь один, иногда есть выбор. Соседние дорожки местами пересекаются, так что с одной можно перейти на другую.\n\n" +
            "Иконка показывает тип комнаты, но что именно внутри — узнаешь только войдя. Все дороги сходятся у босса."),

        [CombatBasics] = new TutorialEntry(CombatBasics, SectionCombat, "Основы боя",
            "Бой идёт сам: героиня и враги бьют без твоих команд.\n\n" +
            "Нажми на врага, чтобы сделать его главной целью — выбранная цель подсвечена. Без выбора героиня бьёт первого живого.\n\n" +
            "Активный навык применяй вручную или включи автоматический режим — тогда он будет применяться после каждой перезарядки."),

        [Defenses] = new TutorialEntry(Defenses, SectionCombat, "Броня и щит",
            "Физическая броня гасит удары и от них же изнашивается. Сама между боями она не чинится — только навыком, предметом или прокачанной Кузницей.\n\n" +
            "Магический щит принимает на себя магический урон и полностью восстанавливается после каждого боя.\n\n" +
            "Здоровье переносится из комнаты в комнату. Дойдёт до нуля — забег окончен."),

        [JenniferActive] = new TutorialEntry(JenniferActive, SectionCombat, "Дженифер: «3 быстрые атаки»",
            "Навык бьёт выбранную цель три раза подряд.\n\n" +
            "Применяй его вручную или включи автоматический режим. Навык повышается автоматически на 5-м и 10-м уровнях героини — выбирать его при повышении уровня не нужно."),

        [VioletActive] = new TutorialEntry(VioletActive, SectionCombat, "Вайолет: Скрытность",
            "«Дымовая граната» даёт Вайолет Скрытность на 3 секунды. Пока она действует, первые обычные атаки гарантированно становятся критическими, а связанные со Скрытностью навыки и предметы начинают действовать.\n\n" +
            "Сама граната урона не наносит — это подготовка к удару.\n\n" +
            "Навык становится сильнее на 5-м и 10-м уровнях героини."),

        [SashaActive] = new TutorialEntry(SashaActive, SectionCombat, "Саша: Ярость и Берсерк",
            "Ярость растёт по мере того, как Саша теряет здоровье, и усиливает навыки, которые от неё зависят.\n\n" +
            "«Берсерк» — переключатель без перезарядки: на 1-м уровне он повышает физическое сопротивление на 20%, но каждую секунду отнимает 1% текущего здоровья, не менее 1. Выключай вовремя — он может её убить.\n\n" +
            "Навык становится сильнее на 5-м и 10-м уровнях героини."),

        [Reward] = new TutorialEntry(Reward, SectionProgress, "Награда за комнату",
            "За победу дают валюту забега и предмет.\n\n" +
            "Валюта забега тратится только у торговца и только в этом забеге — на экране итогов она сгорит. Постоянные валюты начисляются отдельно, в самом конце."),

        [Equipment] = new TutorialEntry(Equipment, SectionProgress, "Снаряжение",
            "Выбери, в какой слот положить предмет. Карточка каждого слота показывает то, что надето сейчас, — сравнивай перед тем, как решить.\n\n" +
            "Старый предмет из выбранного слота пропадёт. Новый можно и просто выбросить.\n\n" +
            "Двуручное оружие занимает обе руки и наносит на 30% больше урона."),

        [LevelUp] = new TutorialEntry(LevelUp, SectionProgress, "Новый уровень",
            "Возьми один из навыков: новый начнётся с 1-го уровня, уже знакомый станет сильнее.\n\n" +
            "Обычных навыков помещается пять. Когда все пять заняты, останутся только улучшения уже взятых.\n\n" +
            "Уникальный пассивный навык героини не занимает один из этих пяти слотов, а уникальный активный навык повышается автоматически и в выборе не появляется."),

        [Camp] = new TutorialEntry(Camp, SectionRun, "Привал",
            "Привал тратит один рацион и восстанавливает указанное на кнопке количество здоровья. От привала можно отказаться — тогда рацион останется.\n\n" +
            "Предлагают его после обычной комнаты и после босса.\n\n" +
            "Броня на привале сама не чинится: для этого нужен подходящий навык, предмет или прокачанная Кузница."),

        [RiskRoom] = new TutorialEntry(RiskRoom, SectionRun, "Ловушка",
            "Ловушку можно попытаться пройти или обойти стороной.\n\n" +
            "Показанный шанс уже учитывает уровень и навыки героини. Обход ничего не стоит, но и награды не принесёт."),

        [EventRoom] = new TutorialEntry(EventRoom, SectionRun, "Событие",
            "Особая комната: находка, встреча или испытание с выбором. Награда бывает щедрой, но и цена ошибки заметна.\n\n" +
            "Некоторые последствия срабатывают не сразу, а в следующем бою, сундуке или на привале — если не уверена, что произошло, загляни в журнал забега."),

        [Merchant] = new TutorialEntry(Merchant, SectionRun, "Торговец",
            "Торговец берёт только валюту текущего забега. Покупку нужно сразу надеть или выбросить.\n\n" +
            "Товар подобран под класс героини. Торговец есть на каждом этаже, но на одном пути он встречается не больше раза — иногда ради него стоит свернуть на соседнюю дорожку.\n\n" +
            "Заход к торговцу не тратит рацион."),

        [Boss] = new TutorialEntry(Boss, SectionCombat, "Босс этажа",
            "Босс крепче обычных врагов и по ходу боя меняет повадки — чем сильнее он ранен, тем опаснее становится.\n\n" +
            "Перед особой атакой над ним загорается предупреждение с полосой: успей применить активный навык, пока полоса не заполнилась.\n\n" +
            "Иногда босс закрывается барьером — пока барьер держится, весь урон уходит в него.\n\n" +
            "Победа закрывает этаж. В зачёт ветерану идут только закрытые этажи."),

        [Pause] = new TutorialEntry(Pause, SectionRun, "Пауза",
            "Esc останавливает игру и показывает текущие характеристики, навыки и снаряжение.\n\n" +
            "«Покинуть забег» засчитывает поражение, но награды за пройденное ты всё равно получишь. «Выйти из игры» закрывает игру, и незаконченный забег не сохраняется."),

        [Results] = new TutorialEntry(Results, SectionRun, "Итоги забега",
            "Мета-валюта идёт на деревню, гача-валюта — на призывы. Обеих тем больше, чем дальше ты прошла.\n\n" +
            "За полностью зачищенное подземелье начисляют ещё четверть сверху."),

        [VeteranCreated] = new TutorialEntry(VeteranCreated, SectionProgress, "Создан ветеран",
            "После победы над финальным боссом героиня попадает в колоду ветеранов вместе со своими навыками и снаряжением.\n\n" +
            "Ранг C–S+ определяется скрытой серией детерминированных боевых испытаний."),

        [Buildings] = new TutorialEntry(Buildings, SectionVillage, "Здания деревни",
            "Здания улучшаются за мета-валюту и усиливают все будущие забеги.\n\n" +
            "Кузница отвечает за стартовое снаряжение и броню. Храм — за магический щит и перебросы навыков. Таверна — за рационы, урон оружия и лечение на привале.\n\n" +
            "Наведи курсор на бонусы здания, чтобы увидеть, что оно даёт сейчас и что добавит следующий уровень."),

        [Gacha] = new TutorialEntry(Gacha, SectionVillage, "Гача и копии",
            "Призыв стоит 50 гача-валюты. Примерно в одном случае из семи выпадает героиня, иначе — мета-валюта: чаще всего немного, изредка — крупная сумма.\n\n" +
            "Первая копия Вайолет или Саши открывает её для забегов; Дженифер доступна и так.\n\n" +
            "Следующие копии дают бонусы по кругу: снаряжение, уникальный пассивный навык, снаряжение, уникальный активный навык."),

        [Characters] = new TutorialEntry(Characters, SectionVillage, "Экран героинь",
            "Здесь видно, кто уже открыт, сколько копий выпало, сколько забегов пройдено и как растут отношения.\n\n" +
            "Дженифер доступна для забега всегда, даже пока её нет в этом списке."),

        [Veterans] = new TutorialEntry(Veterans, SectionProgress, "Колода ветеранов",
            "Ветеран — снимок героини на момент конца забега: оценка, навыки и снаряжение.\n\n" +
            "Взять его в наставники может любая другая героиня, класс при этом не важен.\n\n" +
            "Ранг ветерана не меняет наследование: передаются 2–5 навыков, а уникальный пассивный навык передаётся всегда."),

        [Relationships] = new TutorialEntry(Relationships, SectionVillage, "Отношения",
            "Отношения растут за каждый пройденный целиком этаж и за впервые увиденную сцену.\n\n" +
            "С новым уровнем отношений открываются новые сцены."),

        [HotSprings] = new TutorialEntry(HotSprings, SectionRun, "Комната отдыха",
            "Редкая комната, своя у каждой героини: Дженифер найдёт горячие источники, Вайолет — комнату ловушек, Саша — пивной погреб.\n\n" +
            "Отдых не тратит рацион и полностью восстанавливает здоровье, но не чинит броню. За забег такая комната встречается только один раз и никогда не бывает самой первой."),
    };

    public static bool TryGet(string id, out TutorialEntry entry) => Entries.TryGetValue(id, out entry);

    // Порядок Справки: сначала то, что игрок видит в забеге, потом бой, потом развитие героини,
    // потом деревня. Внутри раздела — в том порядке, в котором механика встречается впервые.
    public static IReadOnlyList<TutorialEntry> HelpEntries { get; } = new List<TutorialEntry>
    {
        Entries[Intro], Entries[RunStart], Entries[Map], Entries[RiskRoom], Entries[EventRoom],
        Entries[Merchant], Entries[Camp], Entries[HotSprings], Entries[Pause], Entries[Results],

        Entries[CombatBasics], Entries[Defenses], Entries[Boss],
        Entries[JenniferActive], Entries[VioletActive], Entries[SashaActive],

        Entries[CharacterSelection], Entries[MentorSelection], Entries[Reward], Entries[Equipment],
        Entries[LevelUp], Entries[VeteranCreated], Entries[Veterans],

        Entries[Buildings], Entries[Gacha], Entries[Characters], Entries[Relationships]
    };

    // Справка показывает активный навык только выбранной героини — остальные две для игрока шум.
    public static IReadOnlyList<TutorialEntry> HelpEntriesFor(string activeCharacterId)
    {
        string keep = ActiveSkillHintId(activeCharacterId);
        var result = new List<TutorialEntry>();
        foreach (var entry in HelpEntries)
        {
            bool isActiveSkillEntry = entry.Id == JenniferActive || entry.Id == VioletActive || entry.Id == SashaActive;
            if (isActiveSkillEntry && keep != null && entry.Id != keep) continue;
            result.Add(entry);
        }
        return result;
    }

    public static string ActiveSkillHintId(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId)) return null;
        switch (characterId.ToLowerInvariant())
        {
            case "jennifer": return JenniferActive;
            case "violet": return VioletActive;
            case "sasha": return SashaActive;
            default: return null;
        }
    }

    // ==================== Тултипы ====================
    // Правило: одна-две фразы, только про тот элемент, на котором висит тултип. Всё, что длиннее,
    // относится к Справке.

    public const string TooltipFloor = "Этаж из десяти и комнаты, пройденные на нём. До босса нужно пройти десять комнат.";
    public const string TooltipRations = "Рацион нужен, чтобы встать на привал. Отказ от привала рацион не тратит.";
    public const string TooltipHp = "Здоровье переносится между комнатами. Дойдёт до нуля — забег окончен.";
    public const string TooltipArmor = "Гасит физический урон и от ударов изнашивается. Сама между боями не восстанавливается.";
    public const string TooltipShield = "Принимает на себя магический урон. Полностью восстанавливается после каждого боя.";
    public const string TooltipAuto = "Активный навык будет применяться автоматически после каждой перезарядки.";
    public const string TooltipReroll = "Заменяет предложенные навыки на другие. Запас перебросов общий на весь забег.";
    public const string TooltipRunCurrency = "Валюта только этого забега. Тратится у торговца, в конце сгорает.";
    public const string TooltipMetaCurrency = "Постоянная валюта. Идёт на улучшение зданий деревни.";
    public const string TooltipGachaCurrency = "Постоянная валюта. Идёт на призывы, один призыв стоит 50.";
    public const string TooltipGrade = "Оценка за глубину забега: C− за 1–2 завершённых этажа, C за 3–4, B за 5–6, A за 7–8, S за 9, S+ за все десять.";
    public const string TooltipRelationships = "+10 за каждый пройденный целиком этаж и +10 за впервые увиденную сцену. 100 очков — второй уровень, 300 — третий.";
    public const string TooltipRunLog = "Важные события забега: исходы комнат, награды, срабатывания навыков.";
    public const string TooltipTrapChance = "Шанс уже учитывает уровень и навыки героини. Обойти ловушку можно без риска.";
    public const string TooltipGachaPull = "Один призыв за 50 гача-валюты. Примерно в одном случае из семи выпадает героиня, иначе — мета-валюта.";

    // Динамические тултипы: текст зависит от того, кем играет игрок и что у неё прокачано, поэтому
    // собирается на лету (см. TutorialManager.BindTooltip с Func<string>).
    public static string ActiveSkillTooltip(string characterId)
    {
        switch (characterId != null ? characterId.ToLowerInvariant() : string.Empty)
        {
            case "jennifer":
                return "«3 быстрые атаки»: три удара подряд по выбранной цели. В автоматическом режиме навык применяется после завершения перезарядки.";
            case "violet":
                return "«Дымовая граната»: даёт Вайолет Скрытность; выбирать цель не требуется. В автоматическом режиме навык применяется после завершения перезарядки.";
            case "sasha":
                return "«Берсерк»: переключатель без перезарядки. Пока включён — Саша крепче, но теряет здоровье.";
            default:
                return "Применяется после завершения перезарядки. В автоматическом режиме не требует нажатия.";
        }
    }

    public static string RageTooltip(float ragePercent) =>
        $"Текущая Ярость: {SkillDescriptionFormatter.Value($"{ragePercent:F0}%")}. Равна 1% плюс процент потерянного здоровья и плоские бонусы предметов; максимум — {SkillDescriptionFormatter.Value("100%")}.";

    public static string BerserkTooltip(float resistancePercent) =>
        resistancePercent > 0f
            ? $"Включён: физическое сопротивление повышено на {SkillDescriptionFormatter.Value($"{resistancePercent:F0}%")}. Каждую секунду Саша теряет {SkillDescriptionFormatter.Value("1% текущего здоровья")}, но не менее {SkillDescriptionFormatter.Value("1")}. Навык может привести к гибели."
            : $"Пока навык включён, физическое сопротивление повышено на {SkillDescriptionFormatter.Value("20%")} на текущем уровне. Каждую секунду Саша теряет {SkillDescriptionFormatter.Value("1% текущего здоровья")}, но не менее {SkillDescriptionFormatter.Value("1")}. Навык может привести к гибели.";

    public static string StealthTooltip(float remainingSeconds, int guaranteedCrits)
    {
        string criticalAttacks = guaranteedCrits > 0
            ? $" Гарантированных критических атак осталось: {SkillDescriptionFormatter.Value(guaranteedCrits.ToString())}."
            : string.Empty;
        return $"Скрытность действует ещё {SkillDescriptionFormatter.Value($"{remainingSeconds:F1} секунды")}.{criticalAttacks} Пока она действует, работают связанные с ней навыки и предметы.";
    }

    // ==================== Подписи на карте ====================

    public static string RoomTypeHint(RoomType type)
    {
        switch (type)
        {
            case RoomType.Combat: return "Бой. Победа даёт валюту забега и предмет.";
            case RoomType.Merchant: return "Торговец. Продаёт снаряжение за валюту забега.";
            case RoomType.Trap: return "Ловушка. Можно рискнуть ради награды или обойти без последствий.";
            case RoomType.Special: return "Событие. Находка, встреча или испытание с выбором.";
            case RoomType.Boss: return "Босс. Победа над ним закрывает этаж.";
            default: return "Комната этажа.";
        }
    }

    // ==================== Статус-эффекты боя ====================
    // Ключ — начало подписи бейджа из CombatantStatusEffects.GetActiveEffects (подписи бывают с
    // числом: «Заморозка ×7», «Барьер 40/40»), поэтому сравнение идёт по префиксу.
    static readonly (string prefix, string text)[] StatusTooltips =
    {
        ("Заморожен", "Не может атаковать и применять навыки в течение 5 секунд. Физический удар разбивает лёд и наносит дополнительный магический урон, равный урону этого удара."),
        ("Заморозка", "Каждый заряд снижает скорость атаки на 5% и действует 3 секунды. На десятом заряде цель замерзает на 5 секунд."),
        ("Иммунитет к заморозке", "Заморозка не действует на эту цель в течение 5 секунд."),
        ("Яд Плута", "Каждый заряд наносит 1 урон в секунду и действует 3 секунды."),
        ("Яд", "Каждый заряд наносит 4 урона в секунду и действует 3 секунды; максимум — 3 заряда."),
        ("Кровотечение", "Каждую секунду наносит указанный в навыке урон здоровью."),
        ("Оглушающий крик", "Снижает шанс критического удара на 20% на 4 секунды."),
        ("Скрытность", "Пока держится тень, работают навыки и предметы, завязанные на скрытность."),
        ("Берсерк", "На 1-м уровне повышает физическое сопротивление на 20%. Каждую секунду отнимает 1% текущего здоровья, но не менее 1."),
        ("Гарантированные критические атаки", "Указанное количество ближайших обычных атак гарантированно станет критическим."),
        ("Рипост готов", "Следующая атака после уклонения будет усилена."),
        ("Барьер", "Поглощает весь входящий урон, пока не будет пробит или не спадёт."),
        ("Физ. сопротивление", "Физический урон по этой цели снижен."),
        ("Маг. сопротивление", "Магический урон по этой цели снижен."),
        ("Упёртость", "На 1-м уровне новые отрицательные эффекты не действуют, пока Ярость не ниже 80%."),
        ("Проклятие замедления", "Снижает скорость атаки на 30% на 3 секунды."),
        ("На волоске", "На 1-м уровне после уклонения повышает скорость атаки на 3% на 3 секунды."),
        ("Запугивание", "Критический удар на 3 секунды снижает скорость атаки цели на величину, равную 70% текущей Ярости."),
        ("Урон снижен", "Снижает наносимый урон на 10% до конца следующего боя."),
        ("Скорость атаки снижена", "Снижает скорость атаки на 10% до конца следующего боя."),
        ("Урон усилен", "Повышает наносимый урон на 10% до конца боя."),
    };

    public static string StatusTooltip(string badgeLabel)
    {
        if (string.IsNullOrWhiteSpace(badgeLabel)) return null;
        foreach (var (prefix, text) in StatusTooltips)
        {
            if (badgeLabel.StartsWith(prefix, System.StringComparison.Ordinal)) return text;
        }
        return null;
    }

    // ==================== Модификаторы монстров ====================
    // Модификатор не хранится на CombatantRuntime — он вплавляется в DisplayName как прилагательное
    // (см. CombatantFactory / MonsterModifierCatalog.AdjectiveFor). Поэтому тултип собирается по
    // основам прилагательных: одна основа покрывает все три рода сразу.
    static readonly (string stem, string text)[] ModifierTooltips =
    {
        ("Быстр", "Быстрый: скорость атаки повышена на 25%."),
        ("Больш", "Большой: максимальное здоровье повышено на 50%."),
        ("Бронированн", "Бронированный: физическая защита повышена на 5."),
        ("Свиреп", "Свирепый: урон повышен на 25%."),
        ("Бронебойн", "Бронебойный: каждая неуклонённая атака дополнительно снижает физическую защиту, даже если она полностью остановила урон."),
    };

    public static string ModifierTooltip(string displayName, float guaranteedArmorDamage = 0f)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        List<string> lines = null;
        foreach (var (stem, text) in ModifierTooltips)
        {
            if (displayName.IndexOf(stem, System.StringComparison.Ordinal) < 0) continue;
            lines ??= new List<string>();
            lines.Add(stem == "Бронебойн" && guaranteedArmorDamage > 0f
                ? $"Бронебойный: каждая неуклонённая атака дополнительно снижает физическую защиту на {SkillDescriptionFormatter.Value($"{guaranteedArmorDamage:F0}")}, даже если она полностью остановила урон."
                : text);
        }
        return lines != null ? string.Join("\n", lines) : null;
    }
}
