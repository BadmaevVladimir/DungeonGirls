# openDAW MCP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Дать Claude Code возможность собирать аранжировку в openDAW из декларативного документа и рендерить микс, стемы и бесшовный луп в WAV.

**Architecture:** Три части. `host/` — статическая страница на vite, единственное место, касающееся API openDAW; поднимает движок в headless-Chromium и выставляет `window.__odaw`. `src/` — MCP-сервер на stdio: чистые модули разбора документа плюс восемь инструментов. `src/bridge.ts` — Playwright и статика с COOP/COEP. Поток односторонний: документ → проект в памяти страницы → PCM → WAV на диск.

**Tech Stack:** TypeScript 5.9.3, vite 7.3.6, vitest 4.1.11, zod 4.5.4, `@modelcontextprotocol/sdk` 1.30.0, playwright 1.63.0, `@opendaw/studio-core` 0.2.4, `@opendaw/studio-core-wasm` 0.0.15.

**Spec:** `Docs/superpowers/specs/2026-09-08-opendaw-mcp-design.md`

## Global Constraints

- Каталог работ: `Tools/opendaw-mcp/` в репозитории DungeonGirls. В `C:\Tools\openDAW` не писать ничего.
- Зависимости openDAW берутся с npm по **точным** версиям без диапазона: `@opendaw/studio-core@0.2.4`, `@opendaw/studio-core-wasm@0.0.15`. Локальная сборка в `C:\Tools\openDAW` — только справочник.
- `PPQN.Quarter = 960`, `PPQN.Bar = 3840`.
- MIDI 60 = **C3** (`MidiKeys.toFullString`: `NAMES[note % 12] + (floor(note / 12) - 2)`). Не C4.
- Позиции 1-based в нотации `такт.доля.шестнадцатая`. Длительности — дробями `1/4`, `1/8.`, `1/8t`, `2b`.
- Рендер всегда на `project.copy()`.
- Имена стем-файлов брать **только** из `ExportConfiguration.stemFileNames(config)`, никогда не собирать обходом `stems`.
- Движок запрашивает `${wasmUrl}/wasm/engine.wasm` — отдавать корень `dist/` пакета `studio-core-wasm`.
- Vite обязан иметь `optimizeDeps.exclude` **и** `resolve.dedupe` по всему списку `@opendaw/*`, иначе `Project.new` падает на `AudioUnitBox is not instance of AudioUnitBox`.
- Ассеты адресуются из документа **по имени**, не по UUID: UUID меняется от прогона к прогону и ломает воспроизводимость.
- Умолчания: `v` = 0.8, `mix.volume` = 0 dB, `mix.pan` = 0.
- Комментарии редкие и однострочные, по стилю окружающего кода.

---

### Task 1: Каркас пакета и `pitch.ts`

Первая задача несёт всю конфигурацию, чтобы дальше её не трогать, и первый чистый модуль, чтобы конфигурация сразу доказала работоспособность.

**Files:**
- Create: `Tools/opendaw-mcp/package.json`
- Create: `Tools/opendaw-mcp/tsconfig.json`
- Create: `Tools/opendaw-mcp/vitest.config.ts`
- Create: `Tools/opendaw-mcp/.gitignore`
- Create: `Tools/opendaw-mcp/src/pitch.ts`
- Test: `Tools/opendaw-mcp/test/pitch.test.ts`

**Interfaces:**
- Consumes: ничего.
- Produces: `toMidi(value: string | number): number`, `toName(midi: number): string`, `class PitchError extends Error`.

- [ ] **Step 1: Создать конфигурацию пакета**

`Tools/opendaw-mcp/package.json`:

```json
{
  "name": "opendaw-mcp",
  "private": true,
  "type": "module",
  "version": "0.1.0",
  "bin": {"opendaw-mcp": "./dist/index.js"},
  "scripts": {
    "test": "vitest run",
    "test:watch": "vitest",
    "typecheck": "tsc --noEmit",
    "build:host": "vite build --config host/vite.config.ts",
    "build": "npm run build:host && tsc -p tsconfig.build.json"
  },
  "dependencies": {
    "@modelcontextprotocol/sdk": "1.30.0",
    "playwright": "1.63.0",
    "zod": "4.5.4"
  },
  "devDependencies": {
    "@opendaw/studio-core": "0.2.4",
    "@opendaw/studio-core-wasm": "0.0.15",
    "@opendaw/studio-adapters": "0.3.2",
    "@opendaw/lib-std": "0.0.84",
    "@opendaw/lib-dsp": "0.0.92",
    "@types/node": "24.10.1",
    "typescript": "5.9.3",
    "vite": "7.3.6",
    "vitest": "4.1.11"
  }
}
```

`Tools/opendaw-mcp/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2023",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "skipLibCheck": true,
    "noEmit": true,
    "types": ["node"]
  },
  "include": ["src", "host/src", "test"]
}
```

`Tools/opendaw-mcp/vitest.config.ts`:

```ts
import {defineConfig} from "vitest/config"

export default defineConfig({
    test: {
        include: ["test/**/*.test.ts"],
        exclude: ["test/integration/**"],
        testTimeout: 10_000
    }
})
```

`Tools/opendaw-mcp/.gitignore`:

```
node_modules/
dist/
host/dist/
```

- [ ] **Step 2: Установить зависимости**

Run:
```bash
cd "C:/Unity Projects/DungeonGirls/Tools/opendaw-mcp" && npm install
```
Expected: успех. Если npm сообщит `allow-scripts` про `esbuild`, выполнить `npm approve-scripts esbuild && npm install` — без этого vite не соберёт страницу.

- [ ] **Step 3: Написать падающий тест**

`Tools/opendaw-mcp/test/pitch.test.ts`:

```ts
import {describe, expect, it} from "vitest"
import {PitchError, toMidi, toName} from "../src/pitch"

describe("toMidi", () => {
    it("следует соглашению openDAW: 60 = C3", () => {
        expect(toMidi("C3")).toBe(60)
        expect(toMidi("C4")).toBe(72)
        expect(toMidi("C-2")).toBe(0)
    })
    it("понимает диезы и бемоли", () => {
        expect(toMidi("C#3")).toBe(61)
        expect(toMidi("Db3")).toBe(61)
        expect(toMidi("B2")).toBe(59)
    })
    it("пропускает числа без изменений", () => {
        expect(toMidi(60)).toBe(60)
    })
    it("не различает регистр", () => {
        expect(toMidi("c3")).toBe(60)
    })
    it("отвергает мусор", () => {
        expect(() => toMidi("H3")).toThrow(PitchError)
        expect(() => toMidi("C")).toThrow(PitchError)
    })
    it("отвергает выход за диапазон MIDI", () => {
        expect(() => toMidi("C9")).toThrow(PitchError)
        expect(() => toMidi(-1)).toThrow(PitchError)
        expect(() => toMidi(128)).toThrow(PitchError)
    })
})

describe("toName", () => {
    it("обратен toMidi", () => {
        expect(toName(60)).toBe("C3")
        expect(toName(61)).toBe("C#3")
        expect(toName(0)).toBe("C-2")
        for (let midi = 0; midi <= 127; midi++) {
            expect(toMidi(toName(midi))).toBe(midi)
        }
    })
})
```

- [ ] **Step 4: Запустить тест и убедиться, что он падает**

Run: `npm test -- pitch`
Expected: FAIL, `Failed to resolve import "../src/pitch"`.

- [ ] **Step 5: Реализовать**

`Tools/opendaw-mcp/src/pitch.ts`:

```ts
// Соглашение openDAW (MidiKeys.toFullString): 60 = C3, а не C4.
const NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"] as const
const SEMITONES: Record<string, number> = {c: 0, d: 2, e: 4, f: 5, g: 7, a: 9, b: 11}
const PATTERN = /^([A-Ga-g])([#b]?)(-?\d+)$/

export class PitchError extends Error {}

export const toMidi = (value: string | number): number => {
    if (typeof value === "number") {
        if (!Number.isInteger(value) || value < 0 || value > 127) {
            throw new PitchError(`высота ${value} вне диапазона MIDI 0..127`)
        }
        return value
    }
    const match = PATTERN.exec(value.trim())
    if (match === null) {
        throw new PitchError(`не разобрать высоту "${value}", ожидается вид C3, F#2, Db4`)
    }
    const [, letter, accidental, octave] = match
    const base = SEMITONES[letter!.toLowerCase()]!
    const shift = accidental === "#" ? 1 : accidental === "b" ? -1 : 0
    const midi = (Number(octave) + 2) * 12 + base + shift
    if (midi < 0 || midi > 127) {
        throw new PitchError(`высота "${value}" даёт ${midi}, вне диапазона MIDI 0..127`)
    }
    return midi
}

export const toName = (midi: number): string => `${NAMES[midi % 12]}${Math.floor(midi / 12) - 2}`
```

- [ ] **Step 6: Запустить тест и убедиться, что он проходит**

Run: `npm test -- pitch`
Expected: PASS, 6 тестов.

- [ ] **Step 7: Коммит**

```bash
cd "C:/Unity Projects/DungeonGirls"
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): каркас пакета и разбор высот (60 = C3)"
```

---

### Task 2: `time.ts` — позиции и длительности

**Files:**
- Create: `Tools/opendaw-mcp/src/time.ts`
- Test: `Tools/opendaw-mcp/test/time.test.ts`

**Interfaces:**
- Consumes: ничего.
- Produces: `type Signature = {nominator: number, denominator: number}`, `parseSignature(text: string): Signature`, `parsePosition(text: string, signature: Signature): number`, `parseDuration(text: string, signature: Signature): number`, `ticksToSeconds(ticks: number, tempo: number): number`, `class TimeError extends Error`. Все длительности и позиции — в тиках PPQN.

- [ ] **Step 1: Написать падающий тест**

`Tools/opendaw-mcp/test/time.test.ts`:

```ts
import {describe, expect, it} from "vitest"
import {parseDuration, parsePosition, parseSignature, ticksToSeconds, TimeError} from "../src/time"

const FOUR_FOUR = {nominator: 4, denominator: 4}

describe("parsePosition", () => {
    it("считает от единицы: 1.1 — начало", () => {
        expect(parsePosition("1.1", FOUR_FOUR)).toBe(0)
    })
    it("складывает такты, доли и шестнадцатые", () => {
        expect(parsePosition("2.1", FOUR_FOUR)).toBe(3840)
        expect(parsePosition("1.2", FOUR_FOUR)).toBe(960)
        expect(parsePosition("1.1.2", FOUR_FOUR)).toBe(240)
        expect(parsePosition("3.2.3", FOUR_FOUR)).toBe(9120)
    })
    it("учитывает размер такта", () => {
        expect(parsePosition("2.1", {nominator: 3, denominator: 4})).toBe(2880)
    })
    it("отвергает нулевые и отрицательные индексы", () => {
        expect(() => parsePosition("0.1", FOUR_FOUR)).toThrow(TimeError)
        expect(() => parsePosition("1.0", FOUR_FOUR)).toThrow(TimeError)
    })
    it("отвергает долю за пределами такта", () => {
        expect(() => parsePosition("1.5", FOUR_FOUR)).toThrow(TimeError)
    })
    it("отвергает мусор", () => {
        expect(() => parsePosition("1", FOUR_FOUR)).toThrow(TimeError)
        expect(() => parsePosition("1.1.1.1", FOUR_FOUR)).toThrow(TimeError)
    })
})

describe("parseDuration", () => {
    it("разбирает простые дроби", () => {
        expect(parseDuration("1/4", FOUR_FOUR)).toBe(960)
        expect(parseDuration("1/8", FOUR_FOUR)).toBe(480)
        expect(parseDuration("1/16", FOUR_FOUR)).toBe(240)
        expect(parseDuration("1/1", FOUR_FOUR)).toBe(3840)
    })
    it("разбирает пунктир и триоль", () => {
        expect(parseDuration("1/8.", FOUR_FOUR)).toBe(720)
        expect(parseDuration("1/8t", FOUR_FOUR)).toBe(320)
    })
    it("разбирает такты", () => {
        expect(parseDuration("1b", FOUR_FOUR)).toBe(3840)
        expect(parseDuration("2b", FOUR_FOUR)).toBe(7680)
        expect(parseDuration("2b", {nominator: 3, denominator: 4})).toBe(5760)
    })
    it("отвергает нулевую длительность и мусор", () => {
        expect(() => parseDuration("0b", FOUR_FOUR)).toThrow(TimeError)
        expect(() => parseDuration("1/0", FOUR_FOUR)).toThrow(TimeError)
        expect(() => parseDuration("1/5", FOUR_FOUR)).toThrow(TimeError)
        expect(() => parseDuration("четверть", FOUR_FOUR)).toThrow(TimeError)
    })
})

describe("parseSignature", () => {
    it("разбирает размер", () => {
        expect(parseSignature("3/4")).toEqual({nominator: 3, denominator: 4})
    })
    it("отвергает недопустимый знаменатель", () => {
        expect(() => parseSignature("4/5")).toThrow(TimeError)
    })
})

describe("ticksToSeconds", () => {
    it("переводит тики в секунды по темпу", () => {
        expect(ticksToSeconds(3840, 120)).toBeCloseTo(2.0, 6)
        expect(ticksToSeconds(960, 60)).toBeCloseTo(1.0, 6)
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm test -- time`
Expected: FAIL, `Failed to resolve import "../src/time"`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/src/time.ts`:

```ts
export const QUARTER = 960
export const BAR = QUARTER * 4

export type Signature = {nominator: number, denominator: number}

export class TimeError extends Error {}

const DENOMINATORS = [1, 2, 4, 8, 16, 32]

export const parseSignature = (text: string): Signature => {
    const match = /^(\d+)\/(\d+)$/.exec(text.trim())
    if (match === null) {throw new TimeError(`не разобрать размер "${text}", ожидается вид 4/4`)}
    const nominator = Number(match[1])
    const denominator = Number(match[2])
    if (nominator < 1 || nominator > 32) {throw new TimeError(`числитель размера вне 1..32: ${nominator}`)}
    if (!DENOMINATORS.includes(denominator)) {
        throw new TimeError(`знаменатель размера должен быть одним из ${DENOMINATORS.join(", ")}, получен ${denominator}`)
    }
    return {nominator, denominator}
}

// Такт и доля в тиках, по формуле PPQN.fromSignature из lib-dsp.
const beatTicks = ({denominator}: Signature): number => Math.floor(BAR / denominator)
export const barTicks = (signature: Signature): number => beatTicks(signature) * signature.nominator

export const parsePosition = (text: string, signature: Signature): number => {
    const match = /^(\d+)\.(\d+)(?:\.(\d+))?$/.exec(text.trim())
    if (match === null) {
        throw new TimeError(`не разобрать позицию "${text}", ожидается вид 3.2 или 3.2.4`)
    }
    const bar = Number(match[1])
    const beat = Number(match[2])
    const sixteenth = match[3] === undefined ? 1 : Number(match[3])
    if (bar < 1 || beat < 1 || sixteenth < 1) {
        throw new TimeError(`позиция "${text}" считается от единицы, ноль недопустим`)
    }
    if (beat > signature.nominator) {
        throw new TimeError(`доля ${beat} за пределами такта ${signature.nominator}/${signature.denominator}`)
    }
    const perBeat = beatTicks(signature)
    const sixteenthTicks = QUARTER / 4
    if ((sixteenth - 1) * sixteenthTicks >= perBeat) {
        throw new TimeError(`шестнадцатая ${sixteenth} за пределами доли`)
    }
    return (bar - 1) * barTicks(signature) + (beat - 1) * perBeat + (sixteenth - 1) * sixteenthTicks
}

export const parseDuration = (text: string, signature: Signature): number => {
    const trimmed = text.trim()
    const bars = /^(\d+)b$/.exec(trimmed)
    if (bars !== null) {
        const count = Number(bars[1])
        if (count < 1) {throw new TimeError(`длительность "${text}" должна быть больше нуля`)}
        return count * barTicks(signature)
    }
    const fraction = /^1\/(\d+)([.t]?)$/.exec(trimmed)
    if (fraction === null) {
        throw new TimeError(`не разобрать длительность "${text}", ожидается 1/4, 1/8., 1/8t или 2b`)
    }
    const denominator = Number(fraction[1])
    if (!DENOMINATORS.includes(denominator)) {
        throw new TimeError(`знаменатель длительности должен быть одним из ${DENOMINATORS.join(", ")}, получен ${denominator}`)
    }
    const base = (QUARTER * 4) / denominator
    const modifier = fraction[2]
    return modifier === "." ? base * 1.5 : modifier === "t" ? (base * 2) / 3 : base
}

export const ticksToSeconds = (ticks: number, tempo: number): number => (ticks * 60) / QUARTER / tempo
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm test -- time`
Expected: PASS.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): разбор позиций и длительностей в тиках PPQN"
```

---

### Task 3: `schema.ts` — структурная схема документа

Проверяет форму и внутренние ссылки. Имена устройств и параметров проверяются позже, по сгенерированному каталогу (Task 9).

**Files:**
- Create: `Tools/opendaw-mcp/src/schema.ts`
- Test: `Tools/opendaw-mcp/test/schema.test.ts`

**Interfaces:**
- Consumes: `parseSignature`, `parsePosition`, `parseDuration` из `src/time.ts`; `toMidi` из `src/pitch.ts`.
- Produces: `const ArrangementDocument: z.ZodType`, `type Arrangement = z.infer<typeof ArrangementDocument>`, `parseDocument(input: unknown): Arrangement` (бросает `DocumentError` со списком проблем), `class DocumentError extends Error {readonly issues: ReadonlyArray<{path: string, message: string}>}`.

- [ ] **Step 1: Написать падающий тест**

`Tools/opendaw-mcp/test/schema.test.ts`:

```ts
import {describe, expect, it} from "vitest"
import {DocumentError, parseDocument} from "../src/schema"

const minimal = () => ({
    name: "Test",
    tempo: 120,
    end: "3.1",
    patterns: {riff: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{name: "Lead", instrument: {device: "Vaporisateur"}, place: [{pattern: "riff", at: "1.1"}]}]
})

describe("parseDocument", () => {
    it("принимает минимальный документ и подставляет умолчания", () => {
        const doc = parseDocument(minimal())
        expect(doc.signature).toBe("4/4")
        expect(doc.tracks[0]!.place[0]!.repeat).toBe(1)
        expect(doc.tracks[0]!.mix).toEqual({volume: 0, pan: 0})
        expect(doc.patterns.riff!.notes[0]!.v).toBe(0.8)
        expect(doc.buses).toEqual([])
    })

    it("отвергает ссылку на необъявленный паттерн с путём до поля", () => {
        const input = minimal()
        input.tracks[0]!.place[0]!.pattern = "missing"
        try {
            parseDocument(input)
            expect.unreachable("должно было бросить")
        } catch (error) {
            expect(error).toBeInstanceOf(DocumentError)
            const {issues} = error as DocumentError
            expect(issues).toContainEqual({
                path: "tracks[0].place[0].pattern",
                message: 'паттерн "missing" не объявлен'
            })
        }
    })

    it("отвергает ссылку на необъявленную шину", () => {
        const input = {...minimal(), tracks: [{...minimal().tracks[0]!, out: "Nope"}]}
        expect(() => parseDocument(input)).toThrow(DocumentError)
    })

    it("отвергает повторяющиеся имена дорожек", () => {
        const track = minimal().tracks[0]!
        expect(() => parseDocument({...minimal(), tracks: [track, {...track}]})).toThrow(DocumentError)
    })

    it("отвергает неизвестные поля", () => {
        expect(() => parseDocument({...minimal(), tempoo: 120})).toThrow(DocumentError)
    })

    it("отвергает непригодные позиции, длительности и высоты", () => {
        const badPosition = minimal()
        badPosition.tracks[0]!.place[0]!.at = "1.9"
        expect(() => parseDocument(badPosition)).toThrow(DocumentError)

        const badDuration = minimal()
        badDuration.patterns.riff!.notes[0]!.d = "1/5"
        expect(() => parseDocument(badDuration)).toThrow(DocumentError)

        const badPitch = minimal()
        badPitch.patterns.riff!.notes[0]!.p = "H3"
        expect(() => parseDocument(badPitch)).toThrow(DocumentError)
    })

    it("отвергает ноту, выходящую за длину паттерна", () => {
        const input = minimal()
        input.patterns.riff!.notes[0]! = {p: "C3", at: "1.4", d: "1/1"}
        expect(() => parseDocument(input)).toThrow(DocumentError)
    })

    it("отвергает конец лупа не позже начала", () => {
        const input = {...minimal(), loop: {start: "2.1", end: "2.1"}}
        expect(() => parseDocument(input)).toThrow(DocumentError)
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm test -- schema`
Expected: FAIL, `Failed to resolve import "../src/schema"`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/src/schema.ts`:

```ts
import {z} from "zod"
import {toMidi} from "./pitch"
import {parseDuration, parsePosition, parseSignature, type Signature} from "./time"

export type DocumentIssue = {path: string, message: string}

export class DocumentError extends Error {
    readonly issues: ReadonlyArray<DocumentIssue>

    constructor(issues: ReadonlyArray<DocumentIssue>) {
        super(`документ не прошёл проверку:\n${issues.map(i => `  ${i.path}: ${i.message}`).join("\n")}`)
        this.issues = issues
    }
}

const ParamValue = z.union([z.number(), z.string(), z.boolean()])

const Device = z.object({
    device: z.string().min(1),
    params: z.record(z.string(), ParamValue).optional()
}).strict()

const Instrument = z.object({
    device: z.string().min(1),
    params: z.record(z.string(), ParamValue).optional(),
    sample: z.string().optional(),
    soundfont: z.string().optional(),
    preset: z.number().int().min(0).optional(),
    slots: z.record(z.string(), z.string()).optional()
}).strict()

const Mix = z.object({
    volume: z.number().min(-96).max(6).default(0),
    pan: z.number().min(-1).max(1).default(0)
}).strict()

const Note = z.object({
    p: z.union([z.string(), z.number()]),
    at: z.string(),
    d: z.string(),
    v: z.number().min(0).max(1).default(0.8)
}).strict()

const Pattern = z.object({
    length: z.string(),
    notes: z.array(Note).min(1)
}).strict()

const Placement = z.object({
    pattern: z.string().min(1),
    at: z.string(),
    repeat: z.number().int().min(1).default(1)
}).strict()

const Bus = z.object({
    name: z.string().min(1),
    stem: z.string().min(1).optional(),
    effects: z.array(Device).default([]),
    mix: Mix.default({volume: 0, pan: 0})
}).strict()

const Track = z.object({
    name: z.string().min(1),
    instrument: Instrument,
    effects: z.array(Device).default([]),
    mix: Mix.default({volume: 0, pan: 0}),
    out: z.string().min(1).optional(),
    stem: z.string().min(1).optional(),
    place: z.array(Placement).min(1)
}).strict()

export const ArrangementDocument = z.object({
    name: z.string().min(1),
    tempo: z.number().min(20).max(400),
    signature: z.string().default("4/4"),
    end: z.string(),
    loop: z.object({start: z.string(), end: z.string()}).strict().optional(),
    buses: z.array(Bus).default([]),
    patterns: z.record(z.string(), Pattern),
    tracks: z.array(Track).min(1)
}).strict()

export type Arrangement = z.infer<typeof ArrangementDocument>

const collect = (issues: DocumentIssue[], path: string, action: () => void): void => {
    try {
        action()
    } catch (error) {
        issues.push({path, message: error instanceof Error ? error.message : String(error)})
    }
}

// Второй проход: всё, что zod не выражает — разбор строк времени и высот, ссылочная
// целостность, уникальность имён.
const crossCheck = (doc: Arrangement): ReadonlyArray<DocumentIssue> => {
    const issues: DocumentIssue[] = []
    let signature: Signature = {nominator: 4, denominator: 4}
    collect(issues, "signature", () => {signature = parseSignature(doc.signature)})

    let end = 0
    collect(issues, "end", () => {end = parsePosition(doc.end, signature)})

    const patternLengths = new Map<string, number>()
    for (const [name, pattern] of Object.entries(doc.patterns)) {
        let length = 0
        collect(issues, `patterns.${name}.length`, () => {
            length = parseDuration(pattern.length, signature)
            patternLengths.set(name, length)
        })
        pattern.notes.forEach((note, index) => {
            const base = `patterns.${name}.notes[${index}]`
            collect(issues, `${base}.p`, () => {toMidi(note.p)})
            let position = 0
            let duration = 0
            collect(issues, `${base}.at`, () => {position = parsePosition(note.at, signature)})
            collect(issues, `${base}.d`, () => {duration = parseDuration(note.d, signature)})
            if (length > 0 && duration > 0 && position + duration > length) {
                issues.push({
                    path: base,
                    message: `нота выходит за длину паттерна "${name}" (${position + duration} > ${length} тиков)`
                })
            }
        })
    }

    const busNames = new Set<string>()
    doc.buses.forEach((bus, index) => {
        if (busNames.has(bus.name)) {
            issues.push({path: `buses[${index}].name`, message: `шина "${bus.name}" объявлена дважды`})
        }
        busNames.add(bus.name)
    })

    const trackNames = new Set<string>()
    doc.tracks.forEach((track, index) => {
        if (trackNames.has(track.name)) {
            issues.push({path: `tracks[${index}].name`, message: `дорожка "${track.name}" объявлена дважды`})
        }
        trackNames.add(track.name)
        if (track.out !== undefined && !busNames.has(track.out)) {
            issues.push({path: `tracks[${index}].out`, message: `шина "${track.out}" не объявлена`})
        }
        track.place.forEach((placement, placeIndex) => {
            const base = `tracks[${index}].place[${placeIndex}]`
            if (!patternLengths.has(placement.pattern)) {
                issues.push({path: `${base}.pattern`, message: `паттерн "${placement.pattern}" не объявлен`})
            }
            collect(issues, `${base}.at`, () => {
                const at = parsePosition(placement.at, signature)
                const length = patternLengths.get(placement.pattern) ?? 0
                if (end > 0 && length > 0 && at + length * placement.repeat > end) {
                    issues.push({path: base, message: `расстановка выходит за end документа`})
                }
            })
        })
    })

    if (doc.loop !== undefined) {
        let start = 0
        let loopEnd = 0
        collect(issues, "loop.start", () => {start = parsePosition(doc.loop!.start, signature)})
        collect(issues, "loop.end", () => {loopEnd = parsePosition(doc.loop!.end, signature)})
        if (loopEnd <= start) {
            issues.push({path: "loop.end", message: "конец лупа должен быть строго позже начала"})
        }
    }
    return issues
}

export const parseDocument = (input: unknown): Arrangement => {
    const result = ArrangementDocument.safeParse(input)
    if (!result.success) {
        throw new DocumentError(result.error.issues.map(issue => ({
            path: issue.path.length === 0 ? "<корень>" : issue.path
                .map((segment, index) => typeof segment === "number"
                    ? `[${segment}]`
                    : index === 0 ? String(segment) : `.${String(segment)}`)
                .join(""),
            message: issue.message
        })))
    }
    const issues = crossCheck(result.data)
    if (issues.length > 0) {throw new DocumentError(issues)}
    return result.data
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm test -- schema`
Expected: PASS. Если путь в сообщении о необъявленном паттерне не совпал дословно, поправить формирование `path` в `parseDocument`, а не ослаблять тест: путь до поля — часть контракта с вызывающим.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): структурная схема документа аранжировки"
```

---

### Task 4: `expand.ts` — паттерны в плоские регионы

**Files:**
- Create: `Tools/opendaw-mcp/src/expand.ts`
- Test: `Tools/opendaw-mcp/test/expand.test.ts`

**Interfaces:**
- Consumes: `Arrangement` из `src/schema.ts`; `parsePosition`, `parseDuration`, `parseSignature`, `ticksToSeconds` из `src/time.ts`; `toMidi` из `src/pitch.ts`.
- Produces:
```ts
export type FlatNote = {pitch: number, position: number, duration: number, velocity: number}
export type FlatRegion = {track: string, pattern: string, position: number, duration: number, notes: FlatNote[]}
export type FlatArrangement = {
    name: string, tempo: number, signature: Signature,
    end: number, loop?: {start: number, end: number},
    buses: ReadonlyArray<{name: string, stem?: string, effects: ..., mix: {volume: number, pan: number}}>,
    tracks: ReadonlyArray<{name: string, instrument: ..., effects: ..., mix: ..., out?: string, stem?: string}>,
    regions: ReadonlyArray<FlatRegion>,
    warnings: ReadonlyArray<string>
}
export const expand = (doc: Arrangement): FlatArrangement
```

- [ ] **Step 1: Написать падающий тест**

`Tools/opendaw-mcp/test/expand.test.ts`:

```ts
import {describe, expect, it} from "vitest"
import {parseDocument} from "../src/schema"
import {expand} from "../src/expand"

const doc = (overrides: Record<string, unknown> = {}) => parseDocument({
    name: "Test",
    tempo: 120,
    end: "5.1",
    patterns: {
        riff: {
            length: "1b",
            notes: [
                {p: "C3", at: "1.1", d: "1/4"},
                {p: "G3", at: "1.3", d: "1/8", v: 0.5}
            ]
        }
    },
    tracks: [{name: "Lead", instrument: {device: "Vaporisateur"}, place: [{pattern: "riff", at: "1.1", repeat: 4}]}],
    ...overrides
})

describe("expand", () => {
    it("разворачивает repeat в отдельные регионы", () => {
        const flat = expand(doc())
        expect(flat.regions).toHaveLength(4)
        expect(flat.regions.map(region => region.position)).toEqual([0, 3840, 7680, 11520])
        expect(flat.regions.every(region => region.duration === 3840)).toBe(true)
    })

    it("переводит ноты в тики и MIDI, позиции — от начала паттерна", () => {
        const [first] = expand(doc()).regions
        expect(first!.notes).toEqual([
            {pitch: 60, position: 0, duration: 960, velocity: 0.8},
            {pitch: 67, position: 1920, duration: 480, velocity: 0.5}
        ])
    })

    it("детерминирован: один документ дважды даёт одно и то же", () => {
        expect(expand(doc())).toEqual(expand(doc()))
    })

    it("предупреждает о регионе, пересекающем начало лупа", () => {
        const flat = expand(doc({loop: {start: "2.1", end: "5.1"}, patterns: {
            riff: {length: "2b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}
        }, tracks: [{name: "Lead", instrument: {device: "Vaporisateur"},
            place: [{pattern: "riff", at: "1.1", repeat: 2}]}]}))
        expect(flat.warnings.join(" ")).toContain("пересекает начало лупа")
    })

    it("не предупреждает, когда регионы выровнены по началу лупа", () => {
        const flat = expand(doc({loop: {start: "2.1", end: "5.1"}}))
        expect(flat.warnings).toEqual([])
    })

    it("считает конец в тиках", () => {
        expect(expand(doc()).end).toBe(15360)
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm test -- expand`
Expected: FAIL, `Failed to resolve import "../src/expand"`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/src/expand.ts`:

```ts
import type {Arrangement} from "./schema"
import {toMidi} from "./pitch"
import {parseDuration, parsePosition, parseSignature, type Signature} from "./time"

export type FlatNote = {pitch: number, position: number, duration: number, velocity: number}
export type FlatRegion = {
    track: string, pattern: string, position: number, duration: number, notes: ReadonlyArray<FlatNote>
}
export type FlatArrangement = {
    name: string
    tempo: number
    signature: Signature
    end: number
    loop?: {start: number, end: number}
    buses: Arrangement["buses"]
    tracks: Arrangement["tracks"]
    regions: ReadonlyArray<FlatRegion>
    warnings: ReadonlyArray<string>
}

export const expand = (doc: Arrangement): FlatArrangement => {
    const signature = parseSignature(doc.signature)
    const regions: FlatRegion[] = []
    const warnings: string[] = []
    for (const track of doc.tracks) {
        for (const placement of track.place) {
            const pattern = doc.patterns[placement.pattern]!
            const length = parseDuration(pattern.length, signature)
            const notes = pattern.notes.map(note => ({
                pitch: toMidi(note.p),
                position: parsePosition(note.at, signature),
                duration: parseDuration(note.d, signature),
                velocity: note.v
            }))
            const start = parsePosition(placement.at, signature)
            for (let repeat = 0; repeat < placement.repeat; repeat++) {
                regions.push({
                    track: track.name,
                    pattern: placement.pattern,
                    position: start + repeat * length,
                    duration: length,
                    notes
                })
            }
        }
    }
    const loop = doc.loop === undefined ? undefined : {
        start: parsePosition(doc.loop.start, signature),
        end: parsePosition(doc.loop.end, signature)
    }
    if (loop !== undefined) {
        // Свёртка хвоста чинит затухания, но не ноту, тянущуюся через точку лупа.
        for (const region of regions) {
            if (region.position < loop.start && region.position + region.duration > loop.start) {
                warnings.push(
                    `регион паттерна "${region.pattern}" на дорожке "${region.track}" пересекает начало лупа: ` +
                    `звучащая в этот момент нота не завернётся`)
            }
        }
    }
    return {
        name: doc.name,
        tempo: doc.tempo,
        signature,
        end: parsePosition(doc.end, signature),
        loop,
        buses: doc.buses,
        tracks: doc.tracks,
        regions,
        warnings
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm test -- expand`
Expected: PASS.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): разворачивание паттернов в плоские регионы"
```

---

### Task 5: `loop.ts` — свёртка хвоста

**Files:**
- Create: `Tools/opendaw-mcp/src/loop.ts`
- Test: `Tools/opendaw-mcp/test/loop.test.ts`

**Interfaces:**
- Consumes: ничего.
- Produces: `foldTail(channels: ReadonlyArray<Float32Array>, loopLength: number): Float32Array[]`.

- [ ] **Step 1: Написать падающий тест**

`Tools/opendaw-mcp/test/loop.test.ts`:

```ts
import {describe, expect, it} from "vitest"
import {foldTail} from "../src/loop"

describe("foldTail", () => {
    it("складывает хвост на голову и обрезает до длины лупа", () => {
        const input = Float32Array.from([1, 2, 3, 4, 10, 20])
        const [out] = foldTail([input], 4)
        expect(Array.from(out!)).toEqual([11, 22, 3, 4])
        expect(out!.length).toBe(4)
    })

    it("сохраняет сигнал, когда хвоста нет", () => {
        const input = Float32Array.from([1, 2, 3, 4])
        const [out] = foldTail([input], 4)
        expect(Array.from(out!)).toEqual([1, 2, 3, 4])
    })

    it("сворачивает многократно, когда хвост длиннее лупа", () => {
        const input = Float32Array.from([1, 1, 2, 2, 3, 3, 4])
        const [out] = foldTail([input], 2)
        expect(Array.from(out!)).toEqual([1 + 2 + 3 + 4, 1 + 2 + 3])
    })

    it("дополняет нулями сигнал короче лупа", () => {
        const [out] = foldTail([Float32Array.from([5])], 3)
        expect(Array.from(out!)).toEqual([5, 0, 0])
    })

    it("обрабатывает каналы независимо", () => {
        const left = Float32Array.from([1, 0, 7, 0])
        const right = Float32Array.from([0, 1, 0, 9])
        const [outLeft, outRight] = foldTail([left, right], 2)
        expect(Array.from(outLeft!)).toEqual([8, 0])
        expect(Array.from(outRight!)).toEqual([0, 10])
    })

    it("не трогает исходные массивы", () => {
        const input = Float32Array.from([1, 2, 3, 4, 10, 20])
        foldTail([input], 4)
        expect(Array.from(input)).toEqual([1, 2, 3, 4, 10, 20])
    })

    it("отвергает неположительную длину лупа", () => {
        expect(() => foldTail([Float32Array.from([1])], 0)).toThrow()
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm test -- loop`
Expected: FAIL, `Failed to resolve import "../src/loop"`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/src/loop.ts`:

```ts
// Рендер диапазона отдаёт L + T сэмплов: хвост за границей лупа — это то, что в
// зацикленном воспроизведении звучит поверх начала. Складываем его туда, откуда он слышен.
export const foldTail = (channels: ReadonlyArray<Float32Array>, loopLength: number): Float32Array[] => {
    if (!Number.isInteger(loopLength) || loopLength <= 0) {
        throw new Error(`длина лупа должна быть положительным целым, получено ${loopLength}`)
    }
    return channels.map(input => {
        const out = new Float32Array(loopLength)
        out.set(input.subarray(0, Math.min(loopLength, input.length)))
        for (let offset = loopLength; offset < input.length; offset += loopLength) {
            const count = Math.min(loopLength, input.length - offset)
            for (let index = 0; index < count; index++) {
                out[index] += input[offset + index]!
            }
        }
        return out
    })
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm test -- loop`
Expected: PASS, 7 тестов.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): свёртка хвоста для бесшовного лупа"
```

---

### Task 6: `wav.ts` — запись WAV и пик

Свой писатель, а не `WavFile` из `@opendaw/lib-dsp`: кодирование выполняется в Node на голых `Float32Array`, и тянуть ради сорока строк RIFF-заголовка браузерный пакет через границу сервера невыгодно.

**Files:**
- Create: `Tools/opendaw-mcp/src/wav.ts`
- Test: `Tools/opendaw-mcp/test/wav.test.ts`

**Interfaces:**
- Consumes: ничего.
- Produces: `type WavFormat = "int16" | "float32"`, `peakOf(channels: ReadonlyArray<Float32Array>): number`, `encodeWav(channels: ReadonlyArray<Float32Array>, sampleRate: number, format: WavFormat): Uint8Array`, `writeWav(path: string, channels, sampleRate, format): Promise<{path: string, peak: number, seconds: number, clipped: boolean}>`.

- [ ] **Step 1: Написать падающий тест**

`Tools/opendaw-mcp/test/wav.test.ts`:

```ts
import {describe, expect, it} from "vitest"
import {encodeWav, peakOf} from "../src/wav"

const readHeader = (bytes: Uint8Array) => {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
    const text = (offset: number) => String.fromCharCode(...bytes.subarray(offset, offset + 4))
    return {
        riff: text(0),
        wave: text(8),
        fmt: text(12),
        audioFormat: view.getUint16(20, true),
        channels: view.getUint16(22, true),
        sampleRate: view.getUint32(24, true),
        bitsPerSample: view.getUint16(34, true),
        dataTag: text(36),
        dataSize: view.getUint32(40, true)
    }
}

describe("peakOf", () => {
    it("берёт максимум модуля по всем каналам", () => {
        expect(peakOf([Float32Array.from([0.1, -0.7]), Float32Array.from([0.3])])).toBeCloseTo(0.7, 6)
    })
    it("на тишине даёт ноль", () => {
        expect(peakOf([new Float32Array(8)])).toBe(0)
    })
})

describe("encodeWav", () => {
    it("пишет корректный заголовок 16-бит PCM", () => {
        const bytes = encodeWav([Float32Array.from([0, 1]), Float32Array.from([0, -1])], 48000, "int16")
        const header = readHeader(bytes)
        expect(header).toMatchObject({
            riff: "RIFF", wave: "WAVE", fmt: "fmt ", dataTag: "data",
            audioFormat: 1, channels: 2, sampleRate: 48000, bitsPerSample: 16, dataSize: 8
        })
        expect(bytes.length).toBe(44 + 8)
    })

    it("пишет корректный заголовок 32-бит float", () => {
        const bytes = encodeWav([Float32Array.from([0.5])], 48000, "float32")
        expect(readHeader(bytes)).toMatchObject({audioFormat: 3, channels: 1, bitsPerSample: 32, dataSize: 4})
    })

    it("перемежает каналы", () => {
        const bytes = encodeWav([Float32Array.from([1, 0]), Float32Array.from([0, 1])], 48000, "float32")
        const samples = new Float32Array(bytes.buffer.slice(bytes.byteOffset + 44))
        expect(Array.from(samples)).toEqual([1, 0, 0, 1])
    })

    it("ограничивает перегруз при 16 битах вместо переполнения", () => {
        const bytes = encodeWav([Float32Array.from([2.0, -2.0])], 48000, "int16")
        const view = new DataView(bytes.buffer, bytes.byteOffset + 44)
        expect(view.getInt16(0, true)).toBe(32767)
        expect(view.getInt16(2, true)).toBe(-32768)
    })

    it("отвергает каналы разной длины", () => {
        expect(() => encodeWav([Float32Array.from([1]), Float32Array.from([1, 2])], 48000, "int16")).toThrow()
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm test -- wav`
Expected: FAIL, `Failed to resolve import "../src/wav"`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/src/wav.ts`:

```ts
import {mkdir, writeFile} from "node:fs/promises"
import {dirname} from "node:path"

export type WavFormat = "int16" | "float32"

export const peakOf = (channels: ReadonlyArray<Float32Array>): number => {
    let peak = 0
    for (const channel of channels) {
        for (const sample of channel) {
            const magnitude = Math.abs(sample)
            if (magnitude > peak) {peak = magnitude}
        }
    }
    return peak
}

export const encodeWav = (channels: ReadonlyArray<Float32Array>,
                          sampleRate: number,
                          format: WavFormat): Uint8Array => {
    if (channels.length === 0) {throw new Error("нечего кодировать: нет каналов")}
    const frames = channels[0]!.length
    if (channels.some(channel => channel.length !== frames)) {
        throw new Error("каналы разной длины")
    }
    const bytesPerSample = format === "int16" ? 2 : 4
    const dataSize = frames * channels.length * bytesPerSample
    const bytes = new Uint8Array(44 + dataSize)
    const view = new DataView(bytes.buffer)
    const tag = (offset: number, text: string) => {
        for (let index = 0; index < text.length; index++) {bytes[offset + index] = text.charCodeAt(index)}
    }
    tag(0, "RIFF")
    view.setUint32(4, 36 + dataSize, true)
    tag(8, "WAVE")
    tag(12, "fmt ")
    view.setUint32(16, 16, true)
    view.setUint16(20, format === "int16" ? 1 : 3, true)
    view.setUint16(22, channels.length, true)
    view.setUint32(24, sampleRate, true)
    view.setUint32(28, sampleRate * channels.length * bytesPerSample, true)
    view.setUint16(32, channels.length * bytesPerSample, true)
    view.setUint16(34, bytesPerSample * 8, true)
    tag(36, "data")
    view.setUint32(40, dataSize, true)
    let offset = 44
    for (let frame = 0; frame < frames; frame++) {
        for (const channel of channels) {
            const sample = channel[frame]!
            if (format === "int16") {
                const clamped = Math.max(-1, Math.min(1, sample))
                view.setInt16(offset, Math.round(clamped * (clamped < 0 ? 32768 : 32767)), true)
            } else {
                view.setFloat32(offset, sample, true)
            }
            offset += bytesPerSample
        }
    }
    return bytes
}

export const writeWav = async (path: string,
                               channels: ReadonlyArray<Float32Array>,
                               sampleRate: number,
                               format: WavFormat) => {
    const peak = peakOf(channels)
    await mkdir(dirname(path), {recursive: true})
    await writeFile(path, encodeWav(channels, sampleRate, format))
    return {
        path,
        peak,
        seconds: (channels[0]?.length ?? 0) / sampleRate,
        // encodeInts16 обрезает молча, поэтому перегруз должен доехать до вызывающего.
        clipped: format === "int16" && peak > 1
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm test -- wav`
Expected: PASS.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): запись WAV в 16 бит и float32 с учётом перегруза"
```

---

### Task 7: Страница-хост — загрузка openDAW

Первая задача с браузером. Забирает оба обхода из спайка. Результат — страница, которая поднимает движок и отвечает на `window.__odaw.status()`.

**Files:**
- Create: `Tools/opendaw-mcp/host/index.html`
- Create: `Tools/opendaw-mcp/host/vite.config.ts`
- Create: `Tools/opendaw-mcp/host/src/boot.ts`
- Create: `Tools/opendaw-mcp/host/src/main.ts`
- Test: `Tools/opendaw-mcp/test/integration/boot.test.ts`
- Create: `Tools/opendaw-mcp/vitest.integration.config.ts`
- Modify: `Tools/opendaw-mcp/package.json` (скрипт `test:integration`)

**Interfaces:**
- Consumes: ничего из предыдущих задач.
- Produces: `host/src/boot.ts` экспортирует `bootOpenDAW(): Promise<BootResult>`, где `BootResult = {context: AudioContext, env: ProjectEnv}`; `window.__odaw.status(): Promise<{crossOriginIsolated: boolean, wasmReady: boolean, sampleRate: number}>`.

- [ ] **Step 1: Написать конфигурацию vite**

`Tools/opendaw-mcp/host/vite.config.ts`:

```ts
import {defineConfig} from "vite"
import {createReadStream, existsSync} from "node:fs"
import {resolve} from "node:path"

// Обе строчки обязательны. Без exclude+dedupe vite держит две копии studio-boxes и
// Project.new падает на "AudioUnitBox is not instance of AudioUnitBox".
const OPENDAW = [
    "@opendaw/studio-core", "@opendaw/studio-core-wasm", "@opendaw/studio-adapters",
    "@opendaw/studio-boxes", "@opendaw/studio-enums",
    "@opendaw/lib-std", "@opendaw/lib-dsp", "@opendaw/lib-box", "@opendaw/lib-dom",
    "@opendaw/lib-runtime", "@opendaw/lib-jsx", "@opendaw/lib-midi", "@opendaw/lib-fusion",
    "@opendaw/lib-xml", "@opendaw/lib-dawproject"
]

const COI = {
    "Cross-Origin-Opener-Policy": "same-origin",
    "Cross-Origin-Embedder-Policy": "require-corp",
    "Cross-Origin-Resource-Policy": "cross-origin"
}

// Движок просит ${wasmUrl}/wasm/engine.wasm, поэтому корень отдачи — dist/, не dist/wasm/.
const wasmDir = resolve(__dirname, "../node_modules/@opendaw/studio-core-wasm/dist")

export default defineConfig({
    root: __dirname,
    base: "./",
    build: {outDir: resolve(__dirname, "dist"), emptyOutDir: true},
    server: {port: 5199, headers: COI},
    preview: {headers: COI},
    worker: {format: "es"},
    optimizeDeps: {exclude: OPENDAW},
    resolve: {dedupe: OPENDAW},
    plugins: [{
        name: "wasm-engine-assets",
        configureServer(server) {
            server.middlewares.use("/wasm-engine", (req, res, next) => {
                const rel = decodeURIComponent((req.url ?? "/").split("?")[0]!).replace(/^\/+/, "")
                const file = resolve(wasmDir, rel)
                if (!file.startsWith(wasmDir) || !existsSync(file)) {return next()}
                res.setHeader("Content-Type", "application/wasm")
                res.setHeader("Cross-Origin-Resource-Policy", "cross-origin")
                createReadStream(file).pipe(res)
            })
        },
        generateBundle() {
            const walk = (dir: string): string[] => {
                const {readdirSync} = require("node:fs") as typeof import("node:fs")
                return readdirSync(resolve(wasmDir, dir), {withFileTypes: true}).flatMap(entry =>
                    entry.isDirectory() ? walk(`${dir}/${entry.name}`) : [`${dir}/${entry.name}`])
            }
            const {readFileSync} = require("node:fs") as typeof import("node:fs")
            for (const name of walk("wasm").filter(name => name.endsWith(".wasm"))) {
                this.emitFile({type: "asset", fileName: `wasm-engine/${name}`,
                    source: readFileSync(resolve(wasmDir, name))})
            }
        }
    }]
})
```

`Tools/opendaw-mcp/host/index.html`:

```html
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>openDAW MCP host</title></head>
<body><script type="module" src="/src/main.ts"></script></body>
</html>
```

- [ ] **Step 2: Написать загрузку**

`Tools/opendaw-mcp/host/src/boot.ts`:

```ts
import workersUrl from "@opendaw/studio-core/workers-main.js?worker&url"
import workletsUrl from "@opendaw/studio-core/processors.js?url"
import wasmProcessorUrl from "@opendaw/studio-core-wasm/wasm-processor.js?url"
import wasmOfflineWorkerUrl from "@opendaw/studio-core-wasm/wasm-offline-worker.js?worker&url"
import {
    AudioWorklets, GlobalSampleLoaderManager, GlobalSoundfontLoaderManager, Workers
} from "@opendaw/studio-core"
import {WasmEngine} from "@opendaw/studio-core-wasm"

export type BootResult = {
    context: AudioContext
    env: unknown
    wasmReady: boolean
    sampleManager: GlobalSampleLoaderManager
    soundfontManager: GlobalSoundfontLoaderManager
}

let booted: Promise<BootResult> | undefined

// Провайдер, отдающий только то, что импортировали через import_asset; сеть не трогаем.
const makeProvider = (store: Map<string, {uuid: Uint8Array, data: unknown}>) => ({
    fetch: (uuid: Uint8Array) => {
        for (const entry of store.values()) {
            if (entry.uuid.every((byte, index) => byte === uuid[index])) {
                return Promise.resolve(entry.data as never)
            }
        }
        return Promise.reject(new Error("ассет не импортирован"))
    },
    invalidate: () => {}
})

export const assetStore = new Map<string, {uuid: Uint8Array, data: unknown, kind: "sample" | "soundfont"}>()

export const bootOpenDAW = (): Promise<BootResult> => booted ??= (async () => {
    await Workers.install(workersUrl)
    AudioWorklets.install(workletsUrl)
    const context = new AudioContext({sampleRate: 48000, latencyHint: 0})
    const audioWorklets = await AudioWorklets.createFor(context)
    WasmEngine.install({
        processorUrl: wasmProcessorUrl,
        offlineWorkerUrl: wasmOfflineWorkerUrl,
        wasmUrl: "/wasm-engine"
    })
    const wasmReady = await WasmEngine.ensureReady(context)
    if (!wasmReady) {throw new Error("WASM-движок openDAW не поднялся")}
    const provider = makeProvider(assetStore as never)
    const sampleManager = new GlobalSampleLoaderManager(provider as never)
    const soundfontManager = new GlobalSoundfontLoaderManager(provider as never)
    const env = {
        audioContext: context, audioWorklets, sampleManager, soundfontManager,
        sampleService: provider, soundfontService: provider
    }
    return {context, env, wasmReady, sampleManager, soundfontManager}
})()
```

`Tools/opendaw-mcp/host/src/main.ts`:

```ts
import {bootOpenDAW} from "./boot"

const api = {
    status: async () => {
        const {context, wasmReady} = await bootOpenDAW()
        return {crossOriginIsolated: self.crossOriginIsolated, wasmReady, sampleRate: context.sampleRate}
    }
}

declare global {
    interface Window {__odaw: typeof api}
}

window.__odaw = api
```

- [ ] **Step 3: Написать интеграционный тест**

`Tools/opendaw-mcp/vitest.integration.config.ts`:

```ts
import {defineConfig} from "vitest/config"

export default defineConfig({
    test: {include: ["test/integration/**/*.test.ts"], testTimeout: 120_000, hookTimeout: 120_000, fileParallelism: false}
})
```

Добавить в `package.json` скрипт: `"test:integration": "vitest run --config vitest.integration.config.ts"`.

`Tools/opendaw-mcp/test/integration/boot.test.ts`:

```ts
import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {chromium, type Browser, type Page} from "playwright"
import {createServer, type ViteDevServer} from "vite"

let server: ViteDevServer
let browser: Browser
let page: Page

beforeAll(async () => {
    server = await createServer({configFile: "host/vite.config.ts"})
    await server.listen(5199)
    browser = await chromium.launch({args: ["--autoplay-policy=no-user-gesture-required"]})
    page = await browser.newPage()
    page.on("pageerror", error => console.error("[page]", error.message))
    await page.goto("http://localhost:5199/", {waitUntil: "load"})
    await page.waitForFunction(() => typeof window.__odaw?.status === "function", null, {timeout: 30_000})
})

afterAll(async () => {
    await browser?.close()
    await server?.close()
})

describe("загрузка openDAW в headless-Chromium", () => {
    it("страница cross-origin isolated и движок поднялся", async () => {
        const status = await page.evaluate(() => window.__odaw.status())
        expect(status.crossOriginIsolated).toBe(true)
        expect(status.wasmReady).toBe(true)
        expect(status.sampleRate).toBe(48000)
    })
})
```

- [ ] **Step 4: Установить Chromium для Playwright**

Run: `npx playwright install chromium`
Expected: скачивание ~150 МБ, затем `downloaded to ...`.

- [ ] **Step 5: Запустить интеграционный тест и убедиться, что он проходит**

Run: `npm run test:integration -- boot`
Expected: PASS.

Если падает `WebAssembly.compile(): expected magic word` — плагин отдаёт `index.html` вместо `.wasm`: проверить, что `wasmDir` указывает на `dist`, а не `dist/wasm`. Если падает `AudioUnitBox is not instance of AudioUnitBox` — в `OPENDAW` не хватает пакета; добавить и перезапустить, предварительно удалив `node_modules/.vite`.

- [ ] **Step 6: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): страница-хост поднимает движок openDAW в headless"
```

---

### Task 8: `describe` — каталог устройств из адаптеров

**Files:**
- Create: `Tools/opendaw-mcp/host/src/describe.ts`
- Modify: `Tools/opendaw-mcp/host/src/main.ts` (добавить `describe` в `api`)
- Test: `Tools/opendaw-mcp/test/integration/describe.test.ts`

**Interfaces:**
- Consumes: `bootOpenDAW` из `host/src/boot.ts`.
- Produces:
```ts
export type ParamSpec = {
    name: string, label: string, unit: string,
    min?: number, max?: number, values?: ReadonlyArray<string>, default: number | string | boolean
}
export type DeviceSpec = {name: string, kind: "instrument" | "effect", params: ReadonlyArray<ParamSpec>}
export type Catalog = ReadonlyArray<DeviceSpec>
export const describeDevices = (): Promise<Catalog>
```
`window.__odaw.describe(): Promise<Catalog>`.

- [ ] **Step 1: Написать интеграционный тест**

`Tools/opendaw-mcp/test/integration/describe.test.ts` (тот же каркас `beforeAll`/`afterAll`, что в Task 7 — скопировать целиком, вынос общего каркаса делается в Task 14):

```ts
import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {chromium, type Browser, type Page} from "playwright"
import {createServer, type ViteDevServer} from "vite"

let server: ViteDevServer
let browser: Browser
let page: Page

beforeAll(async () => {
    server = await createServer({configFile: "host/vite.config.ts"})
    await server.listen(5199)
    browser = await chromium.launch({args: ["--autoplay-policy=no-user-gesture-required"]})
    page = await browser.newPage()
    await page.goto("http://localhost:5199/", {waitUntil: "load"})
    await page.waitForFunction(() => typeof window.__odaw?.describe === "function", null, {timeout: 30_000})
})

afterAll(async () => {
    await browser?.close()
    await server?.close()
})

describe("каталог устройств", () => {
    it("содержит инструменты, выбранные для проекта", async () => {
        const catalog = await page.evaluate(() => window.__odaw.describe())
        const names = catalog.map(device => device.name)
        expect(names).toEqual(expect.arrayContaining(
            ["Vaporisateur", "Neon", "Nano", "Playfield", "Soundfont"]))
    })

    it("отдаёт диапазоны числовых параметров", async () => {
        const catalog = await page.evaluate(() => window.__odaw.describe())
        const vaporisateur = catalog.find(device => device.name === "Vaporisateur")!
        const cutoff = vaporisateur.params.find(param => param.name === "cutoff")!
        expect(cutoff.min).toBeTypeOf("number")
        expect(cutoff.max).toBeGreaterThan(cutoff.min!)
    })

    it("отдаёт метки дискретных параметров", async () => {
        const catalog = await page.evaluate(() => window.__odaw.describe())
        const vaporisateur = catalog.find(device => device.name === "Vaporisateur")!
        const waveform = vaporisateur.params.find(param => param.name.endsWith("waveform"))!
        expect(waveform.values).toEqual(["Sine", "Triangle", "Sawtooth", "Square"])
    })

    it("содержит эффекты", async () => {
        const catalog = await page.evaluate(() => window.__odaw.describe())
        expect(catalog.filter(device => device.kind === "effect").length).toBeGreaterThan(5)
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm run test:integration -- describe`
Expected: FAIL, `window.__odaw.describe is not a function`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/host/src/describe.ts`:

```ts
import {Project} from "@opendaw/studio-core"
import {EffectFactories, InstrumentFactories} from "@opendaw/studio-adapters"
import {bootOpenDAW} from "./boot"

export type ParamSpec = {
    name: string
    label: string
    unit: string
    min?: number
    max?: number
    values?: ReadonlyArray<string>
    default: number | string | boolean
}
export type DeviceSpec = {name: string, kind: "instrument" | "effect", params: ReadonlyArray<ParamSpec>}
export type Catalog = ReadonlyArray<DeviceSpec>

// Каталог не пишется руками: каждое устройство поднимается один раз и опрашивается
// через namedParameter, поэтому он не может разойтись с адаптерами openDAW.
const walk = (node: unknown, prefix: string, out: ParamSpec[]): void => {
    if (node === null || typeof node !== "object") {return}
    const candidate = node as Record<string, unknown>
    if (typeof candidate["valueMapping"] === "object" && candidate["valueMapping"] !== null) {
        const mapping = candidate["valueMapping"] as Record<string, unknown>
        const stringMapping = candidate["stringMapping"] as Record<string, unknown> | undefined
        out.push({
            name: prefix,
            label: String(candidate["name"] ?? prefix),
            unit: String((stringMapping?.["unit"] as string | undefined) ?? ""),
            min: typeof mapping["min"] === "number" ? mapping["min"] : undefined,
            max: typeof mapping["max"] === "number" ? mapping["max"] : undefined,
            values: Array.isArray(stringMapping?.["values"])
                ? (stringMapping["values"] as string[]) : undefined,
            default: (candidate["getValue"] as (() => number) | undefined)?.() ?? 0
        })
        return
    }
    if (Array.isArray(node)) {
        node.forEach((entry, index) => walk(entry, `${prefix}[${index}]`, out))
        return
    }
    for (const [key, value] of Object.entries(candidate)) {
        walk(value, prefix === "" ? key : `${prefix}.${key}`, out)
    }
}

export const describeDevices = async (): Promise<Catalog> => {
    const {env} = await bootOpenDAW()
    const catalog: DeviceSpec[] = []
    for (const [name, factory] of Object.entries(InstrumentFactories.Named)) {
        const probe = Project.new(env as never)
        probe.editing.modify(() => {
            const {instrumentBox} = probe.api.createAnyInstrument(factory as never)
            const adapter = probe.boxAdapters.adapterFor(instrumentBox) as Record<string, unknown>
            const params: ParamSpec[] = []
            walk(adapter["namedParameter"], "", params)
            catalog.push({name, kind: "instrument", params})
        })
        probe.terminate()
    }
    for (const [name, factory] of Object.entries(EffectFactories.Named ?? {})) {
        const probe = Project.new(env as never)
        probe.editing.modify(() => {
            const {audioUnitBox} = probe.api.createInstrument(InstrumentFactories.Vaporisateur)
            const effectBox = probe.api.insertEffect(audioUnitBox.audioEffects, factory as never)
            const adapter = probe.boxAdapters.adapterFor(effectBox) as Record<string, unknown>
            const params: ParamSpec[] = []
            walk(adapter["namedParameter"], "", params)
            catalog.push({name, kind: "effect", params})
        })
        probe.terminate()
    }
    return catalog
}
```

`Tools/opendaw-mcp/host/src/main.ts` — заменить объект `api` на:

```ts
import {bootOpenDAW} from "./boot"
import {describeDevices} from "./describe"

const api = {
    status: async () => {
        const {context, wasmReady} = await bootOpenDAW()
        return {crossOriginIsolated: self.crossOriginIsolated, wasmReady, sampleRate: context.sampleRate}
    },
    describe: () => describeDevices()
}

declare global {
    interface Window {__odaw: typeof api}
}

window.__odaw = api
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm run test:integration -- describe`
Expected: PASS.

Точные имена `EffectFactories.Named` и поля `valueMapping`/`stringMapping` проверить по `C:\Tools\openDAW\packages\studio\adapters\src\factories\` и `ParameterAdapterSet.ts`; если структура отличается — поправить `walk`, но каталог по-прежнему **генерировать**, а не выписывать руками.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): каталог устройств генерируется из адаптеров openDAW"
```

---

### Task 9: `validate-devices.ts` — проверка документа по каталогу

**Files:**
- Create: `Tools/opendaw-mcp/src/validate-devices.ts`
- Test: `Tools/opendaw-mcp/test/validate-devices.test.ts`

**Interfaces:**
- Consumes: `Arrangement` из `src/schema.ts`, `DocumentIssue` из `src/schema.ts`, `Catalog`/`DeviceSpec`/`ParamSpec` (объявить локальный структурный тип, дублирующий `host/src/describe.ts`, чтобы сервер не импортировал браузерный модуль).
- Produces: `validateDevices(doc: Arrangement, catalog: Catalog): ReadonlyArray<DocumentIssue>`.

- [ ] **Step 1: Написать падающий тест**

`Tools/opendaw-mcp/test/validate-devices.test.ts`:

```ts
import {describe, expect, it} from "vitest"
import {parseDocument} from "../src/schema"
import {validateDevices, type Catalog} from "../src/validate-devices"

const catalog: Catalog = [
    {
        name: "Vaporisateur", kind: "instrument", params: [
            {name: "cutoff", label: "Flt. Cutoff", unit: "hz", min: 20, max: 20000, default: 1000},
            {name: "oscillators[0].waveform", label: "Waveform", unit: "",
             values: ["Sine", "Triangle", "Sawtooth", "Square"], default: "Sine"}
        ]
    },
    {name: "Delay", kind: "effect", params: [{name: "wet", label: "Wet", unit: "", min: 0, max: 1, default: 0.5}]}
]

const doc = (instrument: unknown, effects: unknown[] = []) => parseDocument({
    name: "T", tempo: 120, end: "2.1",
    patterns: {r: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{name: "L", instrument, effects, place: [{pattern: "r", at: "1.1"}]}]
})

describe("validateDevices", () => {
    it("пропускает корректный документ", () => {
        expect(validateDevices(doc({device: "Vaporisateur", params: {cutoff: 800}}), catalog)).toEqual([])
    })

    it("ловит неизвестное устройство и подсказывает доступные", () => {
        const [issue] = validateDevices(doc({device: "Vaporizer"}), catalog)
        expect(issue!.path).toBe("tracks[0].instrument.device")
        expect(issue!.message).toContain("Vaporisateur")
    })

    it("ловит неизвестный параметр", () => {
        const [issue] = validateDevices(doc({device: "Vaporisateur", params: {cutof: 800}}), catalog)
        expect(issue!.path).toBe("tracks[0].instrument.params.cutof")
    })

    it("ловит число вне диапазона", () => {
        const [issue] = validateDevices(doc({device: "Vaporisateur", params: {cutoff: 99999}}), catalog)
        expect(issue!.message).toContain("20..20000")
    })

    it("ловит недопустимую метку дискретного параметра", () => {
        const [issue] = validateDevices(
            doc({device: "Vaporisateur", params: {"oscillators[0].waveform": "Noise"}}), catalog)
        expect(issue!.message).toContain("Sine")
    })

    it("проверяет эффекты и требует, чтобы устройство было эффектом", () => {
        expect(validateDevices(doc({device: "Vaporisateur"}, [{device: "Delay"}]), catalog)).toEqual([])
        const [issue] = validateDevices(doc({device: "Vaporisateur"}, [{device: "Vaporisateur"}]), catalog)
        expect(issue!.message).toContain("не эффект")
    })

    it("требует, чтобы инструмент был инструментом", () => {
        const [issue] = validateDevices(doc({device: "Delay"}), catalog)
        expect(issue!.message).toContain("не инструмент")
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm test -- validate-devices`
Expected: FAIL, `Failed to resolve import "../src/validate-devices"`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/src/validate-devices.ts`:

```ts
import type {Arrangement, DocumentIssue} from "./schema"

export type ParamSpec = {
    name: string, label: string, unit: string,
    min?: number, max?: number, values?: ReadonlyArray<string>, default: number | string | boolean
}
export type DeviceSpec = {name: string, kind: "instrument" | "effect", params: ReadonlyArray<ParamSpec>}
export type Catalog = ReadonlyArray<DeviceSpec>

const checkDevice = (catalog: Catalog,
                     path: string,
                     kind: "instrument" | "effect",
                     device: {device: string, params?: Record<string, number | string | boolean>},
                     issues: DocumentIssue[]): void => {
    const spec = catalog.find(entry => entry.name === device.device)
    if (spec === undefined) {
        const available = catalog.filter(entry => entry.kind === kind).map(entry => entry.name).join(", ")
        issues.push({path: `${path}.device`, message: `устройство "${device.device}" неизвестно; доступны: ${available}`})
        return
    }
    if (spec.kind !== kind) {
        issues.push({
            path: `${path}.device`,
            message: `"${device.device}" — ${spec.kind === "instrument" ? "не эффект" : "не инструмент"}`
        })
        return
    }
    for (const [name, value] of Object.entries(device.params ?? {})) {
        const param = spec.params.find(entry => entry.name === name)
        if (param === undefined) {
            const available = spec.params.map(entry => entry.name).slice(0, 12).join(", ")
            issues.push({
                path: `${path}.params.${name}`,
                message: `у "${device.device}" нет параметра "${name}"; есть: ${available}`
            })
            continue
        }
        if (param.values !== undefined) {
            if (typeof value !== "string" || !param.values.includes(value)) {
                issues.push({
                    path: `${path}.params.${name}`,
                    message: `значение должно быть одним из: ${param.values.join(", ")}`
                })
            }
            continue
        }
        if (typeof value !== "number") {
            issues.push({path: `${path}.params.${name}`, message: "значение должно быть числом"})
            continue
        }
        if ((param.min !== undefined && value < param.min) || (param.max !== undefined && value > param.max)) {
            issues.push({
                path: `${path}.params.${name}`,
                message: `значение ${value} вне диапазона ${param.min}..${param.max}`
            })
        }
    }
}

export const validateDevices = (doc: Arrangement, catalog: Catalog): ReadonlyArray<DocumentIssue> => {
    const issues: DocumentIssue[] = []
    doc.tracks.forEach((track, index) => {
        checkDevice(catalog, `tracks[${index}].instrument`, "instrument", track.instrument, issues)
        track.effects.forEach((effect, effectIndex) =>
            checkDevice(catalog, `tracks[${index}].effects[${effectIndex}]`, "effect", effect, issues))
    })
    doc.buses.forEach((bus, index) =>
        bus.effects.forEach((effect, effectIndex) =>
            checkDevice(catalog, `buses[${index}].effects[${effectIndex}]`, "effect", effect, issues)))
    return issues
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm test -- validate-devices`
Expected: PASS.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): строгая проверка устройств и параметров по каталогу"
```

---

### Task 10: `build` — плоская аранжировка в Project

**Files:**
- Create: `Tools/opendaw-mcp/host/src/build.ts`
- Modify: `Tools/opendaw-mcp/host/src/main.ts`
- Test: `Tools/opendaw-mcp/test/integration/build.test.ts`

**Interfaces:**
- Consumes: `bootOpenDAW`; `FlatArrangement` из `src/expand.ts` (в браузере — как структурный тип, объявленный локально: страница получает уже плоскую структуру по проводу).
- Produces: `buildProject(flat: FlatArrangement): BuildSummary`, где
```ts
export type BuildSummary = {
    tracks: number, buses: number, regions: number, notes: number,
    bars: number, seconds: number, warnings: ReadonlyArray<string>,
    stems: ReadonlyArray<{unit: string, fileName: string}>
}
```
Модуль хранит текущий проект в модульной переменной и экспортирует `currentProject(): Project`, `currentSummary(): BuildSummary | undefined`, `resetProject(): void`, `stemUnits(): ReadonlyArray<{uuid: string, fileName: string}>`.
`window.__odaw.build(flat)`, `window.__odaw.inspect()`, `window.__odaw.reset()`.

- [ ] **Step 1: Написать интеграционный тест**

`Tools/opendaw-mcp/test/integration/build.test.ts` (тот же каркас `beforeAll`/`afterAll`):

```ts
import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {chromium, type Browser, type Page} from "playwright"
import {createServer, type ViteDevServer} from "vite"
import {parseDocument} from "../../src/schema"
import {expand} from "../../src/expand"

let server: ViteDevServer
let browser: Browser
let page: Page

const flat = () => expand(parseDocument({
    name: "Test", tempo: 120, end: "5.1",
    buses: [{name: "Harmony", stem: "harmony"}],
    patterns: {riff: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}, {p: "G3", at: "1.3", d: "1/8"}]}},
    tracks: [
        {name: "Lead", instrument: {device: "Vaporisateur"}, stem: "lead",
         place: [{pattern: "riff", at: "1.1", repeat: 4}]},
        {name: "Bass", instrument: {device: "Neon"}, out: "Harmony",
         place: [{pattern: "riff", at: "1.1", repeat: 4}]}
    ]
}))

beforeAll(async () => {
    server = await createServer({configFile: "host/vite.config.ts"})
    await server.listen(5199)
    browser = await chromium.launch({args: ["--autoplay-policy=no-user-gesture-required"]})
    page = await browser.newPage()
    await page.goto("http://localhost:5199/", {waitUntil: "load"})
    await page.waitForFunction(() => typeof window.__odaw?.build === "function", null, {timeout: 30_000})
})

afterAll(async () => {
    await browser?.close()
    await server?.close()
})

describe("сборка проекта", () => {
    it("создаёт дорожки, шины, регионы и ноты", async () => {
        const summary = await page.evaluate(input => window.__odaw.build(input), flat())
        expect(summary.tracks).toBe(2)
        expect(summary.buses).toBe(1)
        expect(summary.regions).toBe(8)
        expect(summary.notes).toBe(16)
        expect(summary.seconds).toBeCloseTo(8, 1)
    })

    it("объявляет стемы для помеченных дорожек и шин", async () => {
        const summary = await page.evaluate(input => window.__odaw.build(input), flat())
        expect(summary.stems.map(stem => stem.fileName).sort()).toEqual(["harmony", "lead"])
    })

    it("inspect отдаёт последнюю сводку", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        expect(await page.evaluate(() => window.__odaw.inspect())).toMatchObject({tracks: 2, regions: 8})
    })

    it("reset очищает проект", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        await page.evaluate(() => window.__odaw.reset())
        expect(await page.evaluate(() => window.__odaw.inspect())).toBeNull()
    })

    it("повторная сборка не накапливает дорожки", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const second = await page.evaluate(input => window.__odaw.build(input), flat())
        expect(second.tracks).toBe(2)
        expect(second.regions).toBe(8)
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm run test:integration -- build`
Expected: FAIL, `window.__odaw.build is not a function`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/host/src/build.ts`:

```ts
import {Project} from "@opendaw/studio-core"
import {EffectFactories, InstrumentFactories} from "@opendaw/studio-adapters"
import {AudioUnitType} from "@opendaw/studio-enums"
import {UUID} from "@opendaw/lib-std"
import {bootOpenDAW} from "./boot"

export type FlatNote = {pitch: number, position: number, duration: number, velocity: number}
export type FlatRegion = {
    track: string, pattern: string, position: number, duration: number, notes: ReadonlyArray<FlatNote>
}
export type FlatTrack = {
    name: string
    instrument: {device: string, params?: Record<string, number | string | boolean>,
                 sample?: string, soundfont?: string, preset?: number, slots?: Record<string, string>}
    effects: ReadonlyArray<{device: string, params?: Record<string, number | string | boolean>}>
    mix: {volume: number, pan: number}
    out?: string
    stem?: string
}
export type FlatBus = {
    name: string, stem?: string
    effects: ReadonlyArray<{device: string, params?: Record<string, number | string | boolean>}>
    mix: {volume: number, pan: number}
}
export type FlatArrangement = {
    name: string, tempo: number, signature: {nominator: number, denominator: number}
    end: number, loop?: {start: number, end: number}
    buses: ReadonlyArray<FlatBus>, tracks: ReadonlyArray<FlatTrack>
    regions: ReadonlyArray<FlatRegion>, warnings: ReadonlyArray<string>
}
export type BuildSummary = {
    tracks: number, buses: number, regions: number, notes: number,
    bars: number, seconds: number, warnings: ReadonlyArray<string>,
    stems: ReadonlyArray<{unit: string, fileName: string}>
}

let project: Project | undefined
let summary: BuildSummary | undefined
let stems: Array<{uuid: string, fileName: string}> = []

export const currentProject = (): Project => {
    if (project === undefined) {throw new Error("проект не собран: сначала вызовите build_arrangement")}
    return project
}
export const currentSummary = (): BuildSummary | null => summary ?? null
export const stemUnits = (): ReadonlyArray<{uuid: string, fileName: string}> => stems

export const resetProject = (): void => {
    project?.terminate()
    project = undefined
    summary = undefined
    stems = []
}

const applyParams = (adapter: Record<string, unknown>,
                     params: Record<string, number | string | boolean> | undefined): void => {
    for (const [path, value] of Object.entries(params ?? {})) {
        let node: unknown = adapter["namedParameter"]
        for (const segment of path.split(/[.[\]]+/).filter(part => part.length > 0)) {
            node = (node as Record<string, unknown>)[segment]
        }
        const parameter = node as {setValue: (value: unknown) => void, stringMapping?: {values?: string[]}}
        const labels = parameter.stringMapping?.values
        parameter.setValue(typeof value === "string" && labels !== undefined ? labels.indexOf(value) : value)
    }
}

export const buildProject = async (flat: FlatArrangement): Promise<BuildSummary> => {
    const {env} = await bootOpenDAW()
    resetProject()
    const created = Project.new(env as never)
    const nextStems: Array<{uuid: string, fileName: string}> = []
    created.editing.modify(() => {
        created.api.setBpm(flat.tempo)
        const busUnits = new Map<string, ReturnType<typeof created.api.createInstrument>["audioUnitBox"]>()
        for (const bus of flat.buses) {
            const {audioUnitBox} = created.api.createInstrument(InstrumentFactories.Tape, {name: bus.name})
            audioUnitBox.type.setValue(AudioUnitType.Bus)
            audioUnitBox.volume.setValue(bus.mix.volume)
            audioUnitBox.panning.setValue(bus.mix.pan)
            for (const effect of bus.effects) {
                const box = created.api.insertEffect(
                    audioUnitBox.audioEffects, (EffectFactories.Named as never)[effect.device])
                applyParams(created.boxAdapters.adapterFor(box) as never, effect.params)
            }
            busUnits.set(bus.name, audioUnitBox)
            if (bus.stem !== undefined) {
                nextStems.push({uuid: UUID.toString(audioUnitBox.address.uuid), fileName: bus.stem})
            }
        }
        const trackBoxes = new Map<string, ReturnType<typeof created.api.createInstrument>["trackBox"]>()
        for (const track of flat.tracks) {
            const factory = (InstrumentFactories.Named as never)[track.instrument.device]
            const {audioUnitBox, instrumentBox, trackBox} =
                created.api.createInstrument(factory, {name: track.name})
            audioUnitBox.volume.setValue(track.mix.volume)
            audioUnitBox.panning.setValue(track.mix.pan)
            applyParams(created.boxAdapters.adapterFor(instrumentBox) as never, track.instrument.params)
            for (const effect of track.effects) {
                const box = created.api.insertEffect(
                    audioUnitBox.audioEffects, (EffectFactories.Named as never)[effect.device])
                applyParams(created.boxAdapters.adapterFor(box) as never, effect.params)
            }
            if (track.out !== undefined) {
                audioUnitBox.output.refer(busUnits.get(track.out)!.input)
            }
            if (track.stem !== undefined) {
                nextStems.push({uuid: UUID.toString(audioUnitBox.address.uuid), fileName: track.stem})
            }
            trackBoxes.set(track.name, trackBox)
        }
        for (const region of flat.regions) {
            const noteRegion = created.api.createNoteRegion({
                trackBox: trackBoxes.get(region.track)!,
                position: region.position,
                duration: region.duration,
                name: region.pattern
            })
            for (const note of region.notes) {
                created.api.createNoteEvent({
                    owner: noteRegion,
                    position: note.position,
                    duration: note.duration,
                    pitch: note.pitch,
                    velocity: note.velocity
                })
            }
        }
    })
    project = created
    stems = nextStems
    const barTicks = Math.floor(3840 / flat.signature.denominator) * flat.signature.nominator
    summary = {
        tracks: flat.tracks.length,
        buses: flat.buses.length,
        regions: flat.regions.length,
        notes: flat.regions.reduce((total, region) => total + region.notes.length, 0),
        bars: Math.ceil(flat.end / barTicks),
        seconds: (flat.end * 60) / 960 / flat.tempo,
        warnings: flat.warnings,
        stems: nextStems.map(stem => ({unit: stem.uuid, fileName: stem.fileName}))
    }
    return summary
}
```

Дополнить `host/src/main.ts`, добавив в `api`: `build: (flat) => buildProject(flat)`, `inspect: () => currentSummary()`, `reset: () => {resetProject()}`.

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm run test:integration -- build`
Expected: PASS.

Маршрутизация в шину (`audioUnitBox.output.refer`) и переключение типа юнита на `Bus` — самые вероятные места расхождения с реальным API. Сверить с `createAudioBusUnit` в `C:\Tools\openDAW\packages\studio\core\src\dawproject\DawProjectImporter.ts` (около строки 301) и поправить по образцу оттуда.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): сборка проекта openDAW из плоской аранжировки"
```

---

### Task 11: `render` — микс, стемы, диапазон

**Files:**
- Create: `Tools/opendaw-mcp/host/src/render.ts`
- Modify: `Tools/opendaw-mcp/host/src/main.ts`
- Test: `Tools/opendaw-mcp/test/integration/render.test.ts`

**Interfaces:**
- Consumes: `currentProject`, `stemUnits` из `host/src/build.ts`.
- Produces:
```ts
export type RenderRequest = {target: "mix" | "stems", range?: {start: number, end: number}}
export type RenderResult = {
    sampleRate: number
    channels: ReadonlyArray<ReadonlyArray<number>>   // по каналам, обычные массивы: через page.evaluate Float32Array не переживает сериализацию
    names: ReadonlyArray<string>                      // одно имя на стерео-пару; для микса — ["mix"]
}
export const renderProject = (request: RenderRequest): Promise<RenderResult>
```
`window.__odaw.render(request)`.

- [ ] **Step 1: Написать интеграционный тест**

`Tools/opendaw-mcp/test/integration/render.test.ts` (тот же каркас, что в Task 10, с тем же `flat()`):

```ts
describe("рендер", () => {
    it("микс даёт две дорожки каналов с ненулевым сигналом", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const result = await page.evaluate(() => window.__odaw.render({target: "mix"}))
        expect(result.names).toEqual(["mix"])
        expect(result.channels).toHaveLength(2)
        expect(Math.max(...result.channels[0]!.map(Math.abs))).toBeGreaterThan(0.001)
    })

    it("стемы дают по паре каналов на стем, имена в порядке каналов", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const result = await page.evaluate(() => window.__odaw.render({target: "stems"}))
        expect(result.channels).toHaveLength(result.names.length * 2)
        expect(result.names.sort()).toEqual(["harmony", "lead"])
    })

    it("рендер диапазона короче полного и отдаёт хвост за границей", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const full = await page.evaluate(() => window.__odaw.render({target: "mix"}))
        const ranged = await page.evaluate(() =>
            window.__odaw.render({target: "mix", range: {start: 0, end: 3840}}))
        expect(ranged.channels[0]!.length).toBeLessThan(full.channels[0]!.length)
        // 3840 тиков при 120 bpm = 2 с = 96000 сэмплов; всё сверх — хвост затухания
        expect(ranged.channels[0]!.length).toBeGreaterThan(96000)
    })

    it("отказывается рендерить несобранный проект", async () => {
        await page.evaluate(() => window.__odaw.reset())
        await expect(page.evaluate(() => window.__odaw.render({target: "mix"})))
            .rejects.toThrow(/не собран/)
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm run test:integration -- render`
Expected: FAIL, `window.__odaw.render is not a function`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/host/src/render.ts`:

```ts
import {OfflineEngineRenderer} from "@opendaw/studio-core"
import {ExportConfiguration} from "@opendaw/studio-adapters"
import {DefaultObservableValue, Option} from "@opendaw/lib-std"
import {currentProject, stemUnits} from "./build"

export type RenderRequest = {target: "mix" | "stems", range?: {start: number, end: number}}
export type RenderResult = {
    sampleRate: number
    channels: ReadonlyArray<ReadonlyArray<number>>
    names: ReadonlyArray<string>
}

export const renderProject = async ({target, range}: RenderRequest): Promise<RenderResult> => {
    const project = currentProject()
    const units = stemUnits()
    if (target === "stems" && units.length === 0) {
        throw new Error("ни одна дорожка или шина не помечена полем stem")
    }
    const config: Record<string, unknown> = {}
    if (range !== undefined) {config["range"] = {start: range.start, end: range.end}}
    if (target === "stems") {
        config["stems"] = Object.fromEntries(units.map(unit => [unit.uuid, {
            includeAudioEffects: true, includeSends: false, fileName: unit.fileName
        }]))
    }
    const optConfig = Object.keys(config).length === 0
        ? Option.None
        : Option.wrap(config as ExportConfiguration)
    const progress = new DefaultObservableValue(0.0)
    // copy(): start() временно гасит зону лупа, трогать живой проект нельзя.
    const audio = await OfflineEngineRenderer.start(project.copy(), optConfig, progress, undefined, 48_000)
    // Имена берутся только отсюда: метроном добавляется последней парой внутри движка,
    // самостоятельный обход stems даёт на одно имя меньше.
    const names = target === "stems"
        ? ExportConfiguration.stemFileNames(config as ExportConfiguration)
        : ["mix"]
    return {
        sampleRate: audio.sampleRate ?? 48_000,
        channels: audio.frames.map((frames: Float32Array) => Array.from(frames)),
        names
    }
}
```

Дополнить `host/src/main.ts`: `render: (request) => renderProject(request)`.

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm run test:integration -- render`
Expected: PASS.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): рендер микса, стемов и диапазона"
```

---

### Task 12: Ассеты и экспорт `.odb`

**Files:**
- Create: `Tools/opendaw-mcp/host/src/assets.ts`
- Modify: `Tools/opendaw-mcp/host/src/main.ts`
- Modify: `Tools/opendaw-mcp/host/src/build.ts` (привязка `sample`/`soundfont`/`slots`/`preset` к инструменту)
- Test: `Tools/opendaw-mcp/test/integration/assets.test.ts`

**Interfaces:**
- Consumes: `bootOpenDAW`, `assetStore` из `host/src/boot.ts`; `currentProject` из `host/src/build.ts`.
- Produces: `importAsset(name: string, kind: "sample" | "soundfont", bytes: number[]): Promise<AssetInfo>`, `listAssets(): ReadonlyArray<AssetInfo>`, `exportBundle(): Promise<number[]>`, где `AssetInfo = {name: string, kind: "sample" | "soundfont", uuid: string, seconds?: number, presets?: ReadonlyArray<{index: number, name: string}>}`.
`window.__odaw.importAsset`, `window.__odaw.listAssets`, `window.__odaw.bundle`.

- [ ] **Step 1: Написать интеграционный тест**

`Tools/opendaw-mcp/test/integration/assets.test.ts` (тот же каркас; для сэмпла сгенерировать WAV через `encodeWav` из `src/wav.ts`):

```ts
import {encodeWav} from "../../src/wav"

const sampleBytes = () => {
    const tone = Float32Array.from({length: 4800}, (_, index) => Math.sin(index * 0.1) * 0.5)
    return Array.from(encodeWav([tone, tone], 48000, "int16"))
}

describe("ассеты", () => {
    it("импортирует сэмпл и возвращает его в списке по имени", async () => {
        await page.evaluate(bytes => window.__odaw.importAsset("test_tone", "sample", bytes), sampleBytes())
        const assets = await page.evaluate(() => window.__odaw.listAssets())
        const entry = assets.find(asset => asset.name === "test_tone")!
        expect(entry.kind).toBe("sample")
        expect(entry.seconds).toBeCloseTo(0.1, 2)
    })

    it("собирает проект с инструментом Nano, ссылающимся на сэмпл по имени", async () => {
        await page.evaluate(bytes => window.__odaw.importAsset("test_tone", "sample", bytes), sampleBytes())
        const summary = await page.evaluate(input => window.__odaw.build(input), flatWithNano())
        expect(summary.tracks).toBe(1)
    })

    it("отказывает на ссылке в несуществующий ассет", async () => {
        await page.evaluate(() => window.__odaw.reset())
        await expect(page.evaluate(input => window.__odaw.build(input), flatWithMissingSample()))
            .rejects.toThrow(/не импортирован/)
    })

    it("экспортирует непустой .odb", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const bytes = await page.evaluate(() => window.__odaw.bundle())
        expect(bytes.length).toBeGreaterThan(100)
    })
})
```

`flatWithNano()` и `flatWithMissingSample()` строятся так же, как `flat()`, но с `instrument: {device: "Nano", sample: "test_tone"}` и `sample: "nope"` соответственно.

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm run test:integration -- assets`
Expected: FAIL, `window.__odaw.importAsset is not a function`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/host/src/assets.ts`:

```ts
import {UUID} from "@opendaw/lib-std"
import {ProjectBundle, ProjectMeta, ProjectProfile} from "@opendaw/studio-core"
import {SoundFont2} from "soundfont2"
import {Option} from "@opendaw/lib-std"
import {assetStore, bootOpenDAW} from "./boot"
import {currentProject} from "./build"

export type AssetInfo = {
    name: string
    kind: "sample" | "soundfont"
    uuid: string
    seconds?: number
    presets?: ReadonlyArray<{index: number, name: string}>
}

const infos = new Map<string, AssetInfo>()

export const importAsset = async (name: string,
                                  kind: "sample" | "soundfont",
                                  bytes: ReadonlyArray<number>): Promise<AssetInfo> => {
    const {context} = await bootOpenDAW()
    const buffer = Uint8Array.from(bytes)
    const uuid = UUID.generate()
    if (kind === "sample") {
        const decoded = await context.decodeAudioData(buffer.slice().buffer)
        const frames = Array.from({length: decoded.numberOfChannels},
            (_, channel) => decoded.getChannelData(channel))
        const data = {
            frames,
            numberOfFrames: decoded.length,
            numberOfChannels: decoded.numberOfChannels,
            sampleRate: decoded.sampleRate
        }
        assetStore.set(name, {uuid, data, kind})
        const info: AssetInfo = {
            name, kind, uuid: UUID.toString(uuid), seconds: decoded.length / decoded.sampleRate
        }
        infos.set(name, info)
        return info
    }
    const soundfont = new SoundFont2(buffer)
    assetStore.set(name, {uuid, data: soundfont, kind})
    const info: AssetInfo = {
        name, kind, uuid: UUID.toString(uuid),
        presets: soundfont.presets.map((preset, index) => ({index, name: preset.header.name}))
    }
    infos.set(name, info)
    return info
}

export const listAssets = (): ReadonlyArray<AssetInfo> => Array.from(infos.values())

export const lookupAsset = (name: string, kind: "sample" | "soundfont") => {
    const entry = assetStore.get(name)
    if (entry === undefined) {throw new Error(`ассет "${name}" не импортирован`)}
    if (entry.kind !== kind) {throw new Error(`ассет "${name}" — ${entry.kind}, а нужен ${kind}`)}
    return entry
}

export const exportBundle = async (name: string): Promise<number[]> => {
    const profile = new ProjectProfile(UUID.generate(), currentProject(), ProjectMeta.init(name), Option.None)
    const encoded = await ProjectBundle.encode(profile)
    return Array.from(new Uint8Array(encoded))
}
```

В `host/src/build.ts` заменить создание инструмента на вариант с привязкой ассета. Добавить импорты
`import {AudioFileBox, SoundfontFileBox} from "@opendaw/studio-boxes"` и `import {lookupAsset} from "./assets"`,
затем перед `created.api.createInstrument(...)`:

```ts
const attachmentFor = (instrument: FlatTrack["instrument"]) => {
    if (instrument.device === "Nano") {
        const entry = lookupAsset(instrument.sample!, "sample")
        return AudioFileBox.create(created.boxGraph, entry.uuid, box => box.fileName.setValue(instrument.sample!))
    }
    if (instrument.device === "Soundfont") {
        const entry = lookupAsset(instrument.soundfont!, "soundfont")
        return SoundfontFileBox.create(created.boxGraph, entry.uuid,
            box => box.fileName.setValue(instrument.soundfont!))
    }
    if (instrument.device === "Playfield") {
        return {
            slots: Object.entries(instrument.slots ?? {}).map(([pitch, sample]) => ({
                pitch: Number(pitch),
                file: AudioFileBox.create(created.boxGraph, lookupAsset(sample, "sample").uuid,
                    box => box.fileName.setValue(sample))
            }))
        }
    }
    return undefined
}
```

и передавать результат в `createInstrument(factory, {name: track.name, attachment: attachmentFor(track.instrument)})`.
После создания инструмента `Soundfont` выставить пресет: `created.boxAdapters.adapterFor(instrumentBox).namedParameter.preset.setValue(track.instrument.preset ?? 0)`.

Точные формы `attachment` (`Nano` → `AudioFileBox`, `Playfield` → `PlayfieldAttachment`, `Soundfont` →
`SoundfontFileBox`) сверить с `C:\Tools\openDAW\packages\studio\adapters\src\factories\InstrumentFactories.ts`,
строки 47, 74 и 211. Если поле `PlayfieldAttachment` называется иначе — привести код к нему; выдумывать
своё имя нельзя, тест на сборку с Nano упадёт первым.

В `host/src/main.ts` добавить в `api`: `importAsset: ({name, kind, bytes}) => importAsset(name, kind, bytes)`,
`listAssets: () => listAssets()`, `bundle: (name?: string) => exportBundle(name ?? "openDAW MCP")`.

В `package.json` добавить зависимость `"soundfont2": "0.5.0"` (её же использует openDAW).

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm run test:integration -- assets`
Expected: PASS.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): импорт сэмплов и soundfont, экспорт .odb"
```

---

### Task 13: `bridge.ts` — Playwright и статика

**Files:**
- Create: `Tools/opendaw-mcp/src/bridge.ts`
- Test: `Tools/opendaw-mcp/test/integration/bridge.test.ts`

**Interfaces:**
- Consumes: собранная страница в `host/dist`.
- Produces:
```ts
export class HostBridge {
    static create(hostDir: string): Promise<HostBridge>
    call<T>(name: string, argument?: unknown): Promise<T>
    close(): Promise<void>
}
export class HostLostError extends Error {}
```
`call` пробрасывает исключения страницы наружу как `Error` с сохранённым сообщением.
Если страница умерла, `call` поднимает её заново и бросает `HostLostError`: состояние
потеряно, и вызывающий обязан узнать об этом, а не получить тихо пустой проект.

- [ ] **Step 1: Написать интеграционный тест**

`Tools/opendaw-mcp/test/integration/bridge.test.ts`:

```ts
import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {HostBridge, HostLostError} from "../../src/bridge"

let bridge: HostBridge

beforeAll(async () => {bridge = await HostBridge.create("host/dist")}, 120_000)
afterAll(async () => {await bridge?.close()})

describe("HostBridge", () => {
    it("поднимает страницу и вызывает примитивы", async () => {
        const status = await bridge.call<{wasmReady: boolean}>("status")
        expect(status.wasmReady).toBe(true)
    })

    it("пробрасывает ошибку страницы с сообщением", async () => {
        await expect(bridge.call("render", {target: "mix"})).rejects.toThrow(/не собран/)
    })

    it("переживает смерть страницы и честно сообщает о потере состояния", async () => {
        await bridge.call("status")
        await bridge.killPageForTest()
        await expect(bridge.call("inspect")).rejects.toThrow(HostLostError)
        // после подъёма мост снова работает, но проект пуст
        expect(await bridge.call("inspect")).toBeNull()
        expect((await bridge.call<{wasmReady: boolean}>("status")).wasmReady).toBe(true)
    })
})
```

Перед запуском собрать страницу: `npm run build:host`.

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm run build:host && npm run test:integration -- bridge`
Expected: FAIL, `Failed to resolve import "../../src/bridge"`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/src/bridge.ts`:

```ts
import {createServer, type Server} from "node:http"
import {createReadStream} from "node:fs"
import {stat} from "node:fs/promises"
import {extname, join, normalize, resolve} from "node:path"
import {chromium, type Browser, type Page} from "playwright"

export class HostLostError extends Error {}

const TYPES: Record<string, string> = {
    ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript",
    ".css": "text/css", ".json": "application/json", ".wasm": "application/wasm"
}

// COOP/COEP обязательны: движку нужен SharedArrayBuffer, а он есть только на
// cross-origin isolated странице.
const serveStatic = (root: string): Promise<{server: Server, port: number}> => new Promise(done => {
    const server = createServer(async (req, res) => {
        const requested = decodeURIComponent((req.url ?? "/").split("?")[0]!)
        const relative = normalize(requested === "/" ? "/index.html" : requested).replace(/^([/\\])+/, "")
        const file = resolve(root, relative)
        res.setHeader("Cross-Origin-Opener-Policy", "same-origin")
        res.setHeader("Cross-Origin-Embedder-Policy", "require-corp")
        res.setHeader("Cross-Origin-Resource-Policy", "cross-origin")
        if (!file.startsWith(resolve(root))) {res.writeHead(403).end(); return}
        const info = await stat(file).catch(() => null)
        if (info === null || !info.isFile()) {res.writeHead(404).end(); return}
        res.setHeader("Content-Type", TYPES[extname(file)] ?? "application/octet-stream")
        createReadStream(file).pipe(res)
    })
    server.listen(0, "127.0.0.1", () => done({server, port: (server.address() as {port: number}).port}))
})

const openPage = async (browser: Browser, port: number): Promise<Page> => {
    const page = await browser.newPage()
    await page.goto(`http://127.0.0.1:${port}/`, {waitUntil: "load"})
    await page.waitForFunction(() => typeof (window as never as {__odaw?: unknown}).__odaw === "object",
        null, {timeout: 60_000})
    return page
}

export class HostBridge {
    static async create(hostDir: string): Promise<HostBridge> {
        const root = resolve(hostDir)
        await stat(join(root, "index.html")).catch(() => {
            throw new Error(`страница не собрана: нет ${join(root, "index.html")}, выполните npm run build:host`)
        })
        const {server, port} = await serveStatic(root)
        const browser = await chromium.launch({args: ["--autoplay-policy=no-user-gesture-required"]})
        return new HostBridge(server, browser, await openPage(browser, port), port)
    }

    readonly #server: Server
    readonly #browser: Browser
    readonly #port: number
    #page: Page

    private constructor(server: Server, browser: Browser, page: Page, port: number) {
        this.#server = server
        this.#browser = browser
        this.#page = page
        this.#port = port
    }

    async call<T>(name: string, argument?: unknown): Promise<T> {
        if (this.#page.isClosed()) {await this.#revive()}
        try {
            return await this.#page.evaluate(([method, arg]) => {
                const api = (window as never as {__odaw: Record<string, (value?: unknown) => unknown>}).__odaw
                const fn = api[method as string]
                if (fn === undefined) {throw new Error(`нет примитива "${method}"`)}
                return Promise.resolve(fn(arg)) as Promise<unknown>
            }, [name, argument] as const) as T
        } catch (error) {
            // Упавшую страницу поднимаем, но молчать нельзя: проект жил в её памяти.
            if (this.#page.isClosed() || !this.#browser.isConnected()) {
                await this.#revive()
                throw new HostLostError(
                    "страница openDAW перезапущена, состояние проекта потеряно: вызовите build_arrangement заново")
            }
            throw error
        }
    }

    async #revive(): Promise<void> {
        if (!this.#browser.isConnected()) {
            throw new HostLostError("браузер закрыт и не может быть поднят в этой сессии")
        }
        this.#page = await openPage(this.#browser, this.#port)
    }

    // Только для тестов: смоделировать смерть страницы.
    async killPageForTest(): Promise<void> {await this.#page.close()}

    async close(): Promise<void> {
        await this.#browser.close()
        await new Promise<void>(done => this.#server.close(() => done()))
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm run build:host && npm run test:integration -- bridge`
Expected: PASS.

- [ ] **Step 5: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): мост Playwright со статикой COOP/COEP"
```

---

### Task 14: `server.ts` — восемь инструментов MCP

**Files:**
- Create: `Tools/opendaw-mcp/src/server.ts`
- Create: `Tools/opendaw-mcp/src/index.ts`
- Create: `Tools/opendaw-mcp/tsconfig.build.json`
- Create: `Tools/opendaw-mcp/README.md`
- Test: `Tools/opendaw-mcp/test/integration/server.test.ts`

**Interfaces:**
- Consumes: `HostBridge`; `parseDocument`, `DocumentError`; `expand`; `validateDevices`; `foldTail`; `writeWav`; `ticksToSeconds`.
- Produces: `createServer(options: {hostDir: string, outputDir: string}): McpServer`; исполняемый вход `src/index.ts`.

- [ ] **Step 1: Написать интеграционный тест**

`Tools/opendaw-mcp/test/integration/server.test.ts`: поднять `createServer` с `InMemoryTransport` из `@modelcontextprotocol/sdk`, вызвать инструменты клиентом.

```ts
import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {mkdtemp, readFile} from "node:fs/promises"
import {tmpdir} from "node:os"
import {join} from "node:path"
import {Client} from "@modelcontextprotocol/sdk/client/index.js"
import {InMemoryTransport} from "@modelcontextprotocol/sdk/inMemory.js"
import {createServer} from "../../src/server"

let client: Client
let outputDir: string
const call = async (name: string, args: Record<string, unknown> = {}) => {
    const result = await client.callTool({name, arguments: args})
    if (result.isError === true) {throw new Error(JSON.stringify(result.content))}
    return JSON.parse((result.content as Array<{text: string}>)[0]!.text)
}

const document = {
    name: "SmokeTheme", tempo: 120, end: "5.1",
    patterns: {riff: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{name: "Lead", instrument: {device: "Vaporisateur"}, stem: "lead",
              place: [{pattern: "riff", at: "1.1", repeat: 4}]}]
}

beforeAll(async () => {
    outputDir = await mkdtemp(join(tmpdir(), "odaw-"))
    const server = createServer({hostDir: "host/dist", outputDir})
    const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair()
    client = new Client({name: "test", version: "0"})
    await Promise.all([server.connect(serverTransport), client.connect(clientTransport)])
}, 180_000)

afterAll(async () => {await client?.close()})

describe("MCP-сервер", () => {
    it("объявляет ровно восемь инструментов", async () => {
        const {tools} = await client.listTools()
        expect(tools.map(tool => tool.name).sort()).toEqual([
            "build_arrangement", "describe_devices", "export_bundle", "import_asset",
            "inspect_project", "list_assets", "render", "reset_project"
        ])
    })

    it("отклоняет негодный документ до касания браузера, с путями до полей", async () => {
        await expect(call("build_arrangement", {document: {...document, tracks: []}}))
            .rejects.toThrow(/tracks/)
    })

    it("отклоняет неизвестный параметр устройства", async () => {
        const broken = structuredClone(document)
        broken.tracks[0]!.instrument = {device: "Vaporisateur", params: {cutof: 1}} as never
        await expect(call("build_arrangement", {document: broken})).rejects.toThrow(/cutof/)
    })

    it("строит и рендерит микс в WAV", async () => {
        const summary = await call("build_arrangement", {document})
        expect(summary.regions).toBe(4)
        const result = await call("render", {target: "mix", name: "smoke"})
        expect(result.files).toHaveLength(1)
        expect(result.files[0]!.peak).toBeGreaterThan(0.001)
        const bytes = await readFile(result.files[0]!.path)
        expect(bytes.subarray(0, 4).toString()).toBe("RIFF")
    })

    it("рендерит стемы отдельными файлами", async () => {
        await call("build_arrangement", {document})
        const result = await call("render", {target: "stems", name: "smoke"})
        expect(result.files.map((file: {path: string}) => file.path.endsWith("lead.wav"))).toContain(true)
    })

    it("рендерит луп ровно нужной длины", async () => {
        await call("build_arrangement", {document: {...document, loop: {start: "1.1", end: "5.1"}}})
        const result = await call("render", {target: "mix", loop: true, name: "loop"})
        // 4 такта при 120 bpm = 8 с
        expect(result.files[0]!.seconds).toBeCloseTo(8, 2)
    })
})
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `npm run build:host && npm run test:integration -- server`
Expected: FAIL, `Failed to resolve import "../../src/server"`.

- [ ] **Step 3: Реализовать**

`Tools/opendaw-mcp/src/server.ts`:

```ts
import {readFile} from "node:fs/promises"
import {basename, extname, join, resolve} from "node:path"
import {McpServer} from "@modelcontextprotocol/sdk/server/mcp.js"
import {z} from "zod"
import {HostBridge, HostLostError} from "./bridge"
import {DocumentError, parseDocument} from "./schema"
import {validateDevices, type Catalog} from "./validate-devices"
import {expand} from "./expand"
import {foldTail} from "./loop"
import {writeWav, type WavFormat} from "./wav"
import {parsePosition, parseSignature, ticksToSeconds} from "./time"

type Options = {hostDir: string, outputDir: string}
type RenderReply = {sampleRate: number, channels: number[][], names: string[]}

const ok = (value: unknown) => ({content: [{type: "text" as const, text: JSON.stringify(value, null, 2)}]})
const fail = (text: string) => ({isError: true, content: [{type: "text" as const, text}]})

// Каждый инструмент оборачивается этим: DocumentError и HostLostError — ожидаемые исходы,
// а не аварии, и должны доезжать до вызывающего читаемым текстом.
const guard = async (action: () => Promise<unknown>) => {
    try {
        return ok(await action())
    } catch (error) {
        if (error instanceof DocumentError) {
            return fail(error.issues.map(issue => `${issue.path}: ${issue.message}`).join("\n"))
        }
        if (error instanceof HostLostError) {return fail(error.message)}
        return fail(error instanceof Error ? `${error.message}\n${error.stack ?? ""}` : String(error))
    }
}

export const createServer = (options: Options): McpServer => {
    const server = new McpServer({name: "opendaw", version: "0.1.0"})
    // Браузер поднимается лениво на первом инструменте, которому он нужен, и живёт до конца сессии.
    let bridge: HostBridge | undefined
    const host = async (): Promise<HostBridge> => bridge ??= await HostBridge.create(options.hostDir)
    let tempo = 120
    let signature = "4/4"
    // Луп документа запоминается при сборке: render {loop: true} без range берёт его.
    let documentLoop: {start: number, end: number} | undefined

    server.registerTool("describe_devices",
        {description: "Каталог инструментов и эффектов openDAW с параметрами, диапазонами и единицами.",
         inputSchema: {}},
        () => guard(async () => (await host()).call<Catalog>("describe")))

    server.registerTool("build_arrangement",
        {description: "Собрать проект из документа аранжировки. Заменяет предыдущий проект целиком.",
         inputSchema: {document: z.unknown()}},
        ({document}) => guard(async () => {
            const doc = parseDocument(document)
            const catalog = await (await host()).call<Catalog>("describe")
            const issues = validateDevices(doc, catalog)
            // Отказ до касания проекта: наполовину построенная аранжировка звучит молча неправильно.
            if (issues.length > 0) {throw new DocumentError(issues)}
            tempo = doc.tempo
            signature = doc.signature
            const flat = expand(doc)
            documentLoop = flat.loop
            return (await host()).call("build", flat)
        }))

    server.registerTool("inspect_project",
        {description: "Сводка текущего проекта или null, если он не собран.", inputSchema: {}},
        () => guard(async () => (await host()).call("inspect")))

    server.registerTool("reset_project",
        {description: "Очистить проект без перезапуска браузера.", inputSchema: {}},
        () => guard(async () => {
            await (await host()).call("reset")
            return {reset: true}
        }))

    server.registerTool("render",
        {description: "Отрендерить микс или стемы в WAV. loop сворачивает хвост затухания на начало.",
         inputSchema: {
             target: z.enum(["mix", "stems"]).default("mix"),
             name: z.string().min(1),
             loop: z.boolean().default(false),
             range: z.object({start: z.string(), end: z.string()}).optional(),
             format: z.enum(["int16", "float32"]).default("int16")
         }},
        ({target, name, loop, range, format}) => guard(async () => {
            const parsed = parseSignature(signature)
            const explicit = range === undefined ? undefined : {
                start: parsePosition(range.start, parsed), end: parsePosition(range.end, parsed)
            }
            const bounds = explicit ?? (loop ? documentLoop : undefined)
            if (loop && bounds === undefined) {
                throw new DocumentError([{
                    path: "range",
                    message: "для loop нужен либо range, либо поле loop в документе"
                }])
            }
            const reply = await (await host()).call<RenderReply>("render", {target, range: bounds})
            const channels = reply.channels.map(values => Float32Array.from(values))
            const prepared = loop
                ? foldTail(channels, Math.round(
                    ticksToSeconds(bounds!.end - bounds!.start, tempo) * reply.sampleRate))
                : channels
            // Имена приходят по одному на стерео-пару, каналы — парами в том же порядке.
            const files = await Promise.all(reply.names.map((fileName, index) => writeWav(
                join(options.outputDir, `${name}_${fileName}.wav`),
                [prepared[index * 2]!, prepared[index * 2 + 1]!],
                reply.sampleRate, format as WavFormat)))
            const clipped = files.filter(file => file.clipped).map(file => file.path)
            return {
                files,
                warnings: clipped.length === 0 ? [] : [
                    `перегруз (пик выше 1.0) обрезан при записи 16 бит: ${clipped.join(", ")}`]
            }
        }))

    server.registerTool("import_asset",
        {description: "Импортировать сэмпл (wav/mp3/flac) или soundfont (sf2) с диска.",
         inputSchema: {path: z.string().min(1), name: z.string().min(1).optional()}},
        ({path, name}) => guard(async () => {
            const file = resolve(path)
            const extension = extname(file).toLowerCase()
            const kind = extension === ".sf2" ? "soundfont" as const : "sample" as const
            if (![".wav", ".mp3", ".flac", ".sf2"].includes(extension)) {
                throw new DocumentError([{path: "path", message: `неподдерживаемое расширение "${extension}"`}])
            }
            const bytes = Array.from(await readFile(file))
            return (await host()).call("importAsset",
                {name: name ?? basename(file, extension), kind, bytes})
        }))

    server.registerTool("list_assets",
        {description: "Импортированные сэмплы и soundfont с именами для ссылок из документа.",
         inputSchema: {}},
        () => guard(async () => (await host()).call("listAssets")))

    server.registerTool("export_bundle",
        {description: "Сохранить .odb для ручных правок в UI openDAW.",
         inputSchema: {path: z.string().min(1)}},
        ({path}) => guard(async () => {
            const bytes = await (await host()).call<number[]>("bundle")
            const {writeFile, mkdir} = await import("node:fs/promises")
            const target = resolve(path)
            await mkdir(join(target, ".."), {recursive: true})
            await writeFile(target, Uint8Array.from(bytes))
            return {path: target, bytes: bytes.length}
        }))

    return server
}
```

`src/index.ts`:

```ts
#!/usr/bin/env node
import {StdioServerTransport} from "@modelcontextprotocol/sdk/server/stdio.js"
import {fileURLToPath} from "node:url"
import {dirname, resolve} from "node:path"
import {createServer} from "./server"

const here = dirname(fileURLToPath(import.meta.url))
const server = createServer({
    hostDir: resolve(here, "../host/dist"),
    outputDir: process.env["OPENDAW_OUTPUT_DIR"] ?? resolve(process.cwd(), "Audio/Music")
})
await server.connect(new StdioServerTransport())
```

`tsconfig.build.json`:

```json
{
  "extends": "./tsconfig.json",
  "compilerOptions": {"noEmit": false, "outDir": "dist", "declaration": false},
  "include": ["src"]
}
```

`README.md` — назначение, `npm install && npx playwright install chromium && npm run build`, описание восьми инструментов, пример документа, напоминание, что версии openDAW пинятся точно и обновляются осознанно.

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `npm run build:host && npm run test:integration -- server`
Expected: PASS.

- [ ] **Step 5: Проверить типы и весь набор тестов**

Run: `npm run typecheck && npm test && npm run test:integration`
Expected: без ошибок типов, все тесты зелёные.

- [ ] **Step 6: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "feat(opendaw-mcp): MCP-сервер с восемью инструментами и записью WAV"
```

---

### Task 15: Инвариант — сумма стемов равна миксу

Один тест, проверяющий всю цепочку разом: маршрутизацию по шинам, порядок каналов и соответствие имён.

**Files:**
- Create: `Tools/opendaw-mcp/test/integration/stems-sum.test.ts`

**Interfaces:**
- Consumes: `HostBridge`, `parseDocument`, `expand`.
- Produces: ничего.

- [ ] **Step 1: Написать тест**

```ts
import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {HostBridge} from "../../src/bridge"
import {parseDocument} from "../../src/schema"
import {expand} from "../../src/expand"

let bridge: HostBridge

const document = {
    name: "Sum", tempo: 120, end: "3.1",
    buses: [{name: "Low", stem: "low"}],
    patterns: {
        a: {length: "1b", notes: [{p: "C4", at: "1.1", d: "1/4"}]},
        b: {length: "1b", notes: [{p: "C2", at: "1.1", d: "1/2"}]}
    },
    tracks: [
        {name: "Lead", instrument: {device: "Vaporisateur"}, stem: "lead",
         place: [{pattern: "a", at: "1.1", repeat: 2}]},
        {name: "Bass", instrument: {device: "Neon"}, out: "Low",
         place: [{pattern: "b", at: "1.1", repeat: 2}]}
    ]
}

beforeAll(async () => {bridge = await HostBridge.create("host/dist")}, 120_000)
afterAll(async () => {await bridge?.close()})

describe("инвариант стемов", () => {
    it("сумма стемов совпадает с миксом", async () => {
        await bridge.call("build", expand(parseDocument(document)))
        const mix = await bridge.call<{channels: number[][]}>("render", {target: "mix"})
        const stems = await bridge.call<{channels: number[][], names: string[]}>("render", {target: "stems"})
        expect(stems.names.length).toBe(2)
        const frames = mix.channels[0]!.length
        for (let channel = 0; channel < 2; channel++) {
            let worst = 0
            for (let frame = 0; frame < frames; frame++) {
                let sum = 0
                for (let stem = 0; stem < stems.names.length; stem++) {
                    sum += stems.channels[stem * 2 + channel]![frame] ?? 0
                }
                worst = Math.max(worst, Math.abs(sum - mix.channels[channel]![frame]!))
            }
            expect(worst).toBeLessThan(1e-3)
        }
    })
})
```

- [ ] **Step 2: Запустить тест**

Run: `npm run build:host && npm run test:integration -- stems-sum`
Expected: PASS.

Если расхождение велико — почти наверняка перепутан порядок пар каналов либо `includeSends` вносит сигнал дважды. Проверять порядок против `ExportConfiguration.stemFileNames`, не подгонять допуск.

- [ ] **Step 3: Коммит**

```bash
git add Tools/opendaw-mcp
git commit -m "test(opendaw-mcp): инвариант — сумма стемов равна миксу"
```

---

### Task 16: Регистрация сервера и ручная проверка

**Files:**
- Modify: `.mcp.json` (в корне репозитория; создать, если отсутствует)
- Modify: `Tools/opendaw-mcp/README.md`

**Interfaces:**
- Consumes: собранный `Tools/opendaw-mcp/dist/index.js`.
- Produces: ничего.

- [ ] **Step 1: Собрать сервер**

Run: `cd Tools/opendaw-mcp && npm run build`
Expected: появляются `host/dist/index.html` и `dist/index.js`.

- [ ] **Step 2: Зарегистрировать сервер**

Добавить в `.mcp.json` корня репозитория:

```json
{
  "mcpServers": {
    "opendaw": {
      "command": "node",
      "args": ["Tools/opendaw-mcp/dist/index.js"],
      "env": {"OPENDAW_OUTPUT_DIR": "Audio/Music"}
    }
  }
}
```

Если файл уже есть — добавить только ключ `opendaw`, не переписывая остальные.

- [ ] **Step 3: Ручная проверка**

Перезапустить сессию Claude Code, затем:
1. `describe_devices` — убедиться, что в каталоге есть Vaporisateur, Neon, Nano, Playfield, Soundfont.
2. `build_arrangement` с восьмитактовой темой на двух дорожках и шиной.
3. `render` с `target: "stems"` и `loop: true`.
4. Открыть получившиеся WAV и **послушать**: стыка на петле быть не должно.
5. `export_bundle`, затем импортировать `.odb` в openDAW на https://localhost:8080 и убедиться, что регионы и ноты на месте.

Пункты 4 и 5 автоматизации не поддаются: щелчок на стыке и «звучит не так» — это то, что проверяется ушами.

- [ ] **Step 4: Коммит**

```bash
git add .mcp.json Tools/opendaw-mcp/README.md
git commit -m "chore(opendaw-mcp): регистрация MCP-сервера в проекте"
```

---

## Порядок и зависимости

Задачи 1–6 — чистые модули, идут строго по порядку, браузер не нужен, обратная связь секундная.
Задача 7 открывает браузерную часть; 8–12 опираются на неё и друг на друга по порядку.
Задача 13 требует собранной страницы (7–12). Задача 14 требует всего предыдущего.
Задачи 15–16 — проверка и подключение.

Задачи 2–6 независимы друг от друга и могут выполняться параллельно, если исполнителей несколько; всё остальное последовательно.
