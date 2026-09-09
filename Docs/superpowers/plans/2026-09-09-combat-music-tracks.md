# Боевые темы героинь и тема деревни — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Написать в openDAW три боевые темы тремя синхронными слоями каждая и одиночную тему деревни, довести их до `Assets/Resources/Music/` и доказать, что слои сходятся в микс.

**Architecture:** Каждый трек — декларативный JSON-документ в `Audio/Music/arrangements/`. `build_arrangement` собирает проект, два вызова `render` по одному и тому же построенному проекту дают стемы и контрольный микс. Новый скрипт `verify-stems` читает получившиеся wav и машинно доказывает инвариант «сумма стемов = микс». Сначала строится технический скелет, доказывающий рельсы, и только потом пишется музыка.

**Tech Stack:** openDAW MCP (`Tools/opendaw-mcp`), Node 20+ ESM, TypeScript, vitest. Инструменты openDAW: Playfield, Soundfont (GeneralUser GS), Vaporisateur, Neon, Cubed. Никаких VST — в браузере они невозможны.

**Spec:** `Docs/superpowers/specs/2026-09-09-combat-music-tracks-design.md`

## Global Constraints

- Ветка работы: `feat/combat-music-tracks` (уже создана, спек в ней закоммичен).
- Соглашение высот openDAW: **60 = C3**, не C4. `toMidi` считает `(октава + 2) × 12 + полутон`.
- Позиции — `такт.доля.шестнадцатая`, счёт **с единицы**, ноль недопустим. Длительности — только `1/N`, `1/N.`, `1/Nt` или `Nb`. Перепутать синтаксис нельзя, парсер разный.
- Нота не должна выходить за длину своего паттерна: `позиция + длительность > length` — отказ сборки.
- Все объекты документа `strict`: лишнее поле — отказ. `patterns[*].notes` и `tracks[*].place` требуют минимум один элемент.
- `mix.volume` в диапазоне −96…+6 dB, `mix.pan` −1…+1.
- **`loop.end` обязан равняться `end` документа.** Ничего в аранжировке после конца лупа.
- **Мастер только линейный.** Никакой нелинейной обработки на мастере: она разрушает равенство суммы стемов и микса. Компрессия, `Fold`, `Waveshaper`, `Crusher`, `Maximizer` — только на шинах слоёв.
- **Никаких общих send-реверберов.** Реверб и дилей — инсерты на шине конкретного слоя.
- openDAW рендерит на **48000 Гц**, стерео.
- `render` пишет в `Audio/Music/` (переменная `OPENDAW_OUTPUT_DIR` в `.mcp.json`) и складывает имя как `<name>_<стем>.wav`. Микс приезжает отдельным файлом с суффиксом имени стема микса — точное имя фиксируется в Task 2 и дальше используется как известное.
- `list_assets` в новой сессии пуст: импорт сэмплов не переживает перезапуск сервера и входит в процедуру каждого сеанса.
- Аудиоформаты в `/Audio` не версионируются (`.gitignore`), JSON — версионируется.
- Комментарии и сообщения коммитов — по-русски, как во всём репозитории.

---

### Task 1: Скрипт проверки набора стемов

Машинная приёмка из спека («сумма стемов совпадает с миксом», «одинаковая длина, частота, каналы», «не клипует») сейчас существует только как integration-тест сервера на синтетическом проекте. Нужен инструмент, проверяющий **реальные файлы на диске**, иначе каждый трек будет приниматься на глазок.

**Files:**
- Create: `Tools/opendaw-mcp/src/verify-stems.ts`
- Create: `Tools/opendaw-mcp/scripts/verify-stems.mjs`
- Test: `Tools/opendaw-mcp/test/verify-stems.test.ts`

**Interfaces:**
- Consumes: `encodeWav` из `src/wav.ts` (только в тестах, для сборки образцов).
- Produces:
  - `decodeWav(bytes: Uint8Array): {sampleRate: number, channels: Float32Array[]}` — понимает int16, int24 и float32 PCM, идёт по чанкам, а не полагается на 44-байтовый заголовок.
  - `checkStemSet(input: {mix: Decoded, stems: {name: string, wav: Decoded}[], tolerance: number}): string[]` — список найденных проблем, пустой список означает «набор корректен».
  - `type Decoded = {sampleRate: number, channels: Float32Array[]}`

- [ ] **Step 1: Написать падающие тесты**

Создать `Tools/opendaw-mcp/test/verify-stems.test.ts`:

```typescript
import {describe, expect, it} from "vitest"
import {encodeWav} from "../src/wav"
import {checkStemSet, decodeWav, type Decoded} from "../src/verify-stems"

const tone = (frames: number, frequency: number, gain = 0.2): Float32Array => {
    const data = new Float32Array(frames)
    for (let frame = 0; frame < frames; frame++) {
        data[frame] = gain * Math.sin(2 * Math.PI * frequency * frame / 48000)
    }
    return data
}

const stereo = (mono: Float32Array): Float32Array[] => [mono, mono.slice()]

const sum = (parts: Float32Array[][]): Float32Array[] => {
    const frames = parts[0]![0]!.length
    return [0, 1].map(channel => {
        const out = new Float32Array(frames)
        for (const part of parts) {
            for (let frame = 0; frame < frames; frame++) {out[frame]! += part[channel]![frame]!}
        }
        return out
    })
}

const decoded = (channels: Float32Array[]): Decoded => ({sampleRate: 48000, channels})

describe("decodeWav", () => {
    it("читает float32 и возвращает те же отсчёты", () => {
        const channels = stereo(tone(1000, 440))
        const result = decodeWav(encodeWav(channels, 48000, "float32"))
        expect(result.sampleRate).toBe(48000)
        expect(result.channels.length).toBe(2)
        expect(result.channels[0]![123]).toBeCloseTo(channels[0]![123]!, 6)
    })

    it("читает int16 с точностью квантования", () => {
        const channels = stereo(tone(1000, 440))
        const result = decodeWav(encodeWav(channels, 48000, "int16"))
        expect(result.channels[0]![123]).toBeCloseTo(channels[0]![123]!, 3)
    })

    it("отвергает не-WAV", () => {
        expect(() => decodeWav(new Uint8Array([1, 2, 3, 4]))).toThrow()
    })
})

describe("checkStemSet", () => {
    const drums = stereo(tone(4800, 110))
    const harmony = stereo(tone(4800, 220))
    const lead = stereo(tone(4800, 330))
    const good = {
        mix: decoded(sum([drums, harmony, lead])),
        stems: [
            {name: "drums", wav: decoded(drums)},
            {name: "harmony", wav: decoded(harmony)},
            {name: "lead", wav: decoded(lead)}
        ],
        tolerance: 1e-6
    }

    it("принимает корректный набор", () => {
        expect(checkStemSet(good)).toEqual([])
    })

    it("ловит расхождение суммы с миксом", () => {
        const broken = {...good, mix: decoded(sum([drums, harmony]))}
        expect(checkStemSet(broken).join(" ")).toContain("сумма стемов")
    })

    it("ловит разную длину", () => {
        const short = stereo(tone(2400, 330))
        const broken = {...good, stems: [
            good.stems[0]!, good.stems[1]!, {name: "lead", wav: decoded(short)}
        ]}
        expect(checkStemSet(broken).join(" ")).toContain("длина")
    })

    it("ловит разную частоту дискретизации", () => {
        const broken = {...good, stems: [
            good.stems[0]!, good.stems[1]!,
            {name: "lead", wav: {sampleRate: 44100, channels: lead}}
        ]}
        expect(checkStemSet(broken).join(" ")).toContain("частота")
    })

    it("ловит перегруз", () => {
        const loud = stereo(tone(4800, 110, 0.9))
        const parts = [loud, harmony, lead]
        const broken = {
            mix: decoded(sum(parts)),
            stems: [
                {name: "drums", wav: decoded(loud)},
                {name: "harmony", wav: decoded(harmony)},
                {name: "lead", wav: decoded(lead)}
            ],
            tolerance: 1e-6
        }
        expect(checkStemSet(broken).join(" ")).toContain("перегруз")
    })

    it("ловит тишину в слое", () => {
        const silence = stereo(new Float32Array(4800))
        const parts = [drums, harmony, silence]
        const broken = {
            mix: decoded(sum(parts)),
            stems: [
                {name: "drums", wav: decoded(drums)},
                {name: "harmony", wav: decoded(harmony)},
                {name: "lead", wav: decoded(silence)}
            ],
            tolerance: 1e-6
        }
        expect(checkStemSet(broken).join(" ")).toContain("тишина")
    })

    it("ловит два одинаковых слоя", () => {
        const parts = [drums, harmony, harmony]
        const broken = {
            mix: decoded(sum(parts)),
            stems: [
                {name: "drums", wav: decoded(drums)},
                {name: "harmony", wav: decoded(harmony)},
                {name: "lead", wav: decoded(harmony.map(c => c.slice()))}
            ],
            tolerance: 1e-6
        }
        expect(checkStemSet(broken).join(" ")).toContain("совпадают")
    })

    it("требует хотя бы один стем", () => {
        expect(checkStemSet({...good, stems: []}).join(" ")).toContain("нет стемов")
    })
})
```

- [ ] **Step 2: Прогнать тесты и убедиться, что они падают**

Run: `cd Tools/opendaw-mcp && npx vitest run test/verify-stems.test.ts`
Expected: FAIL — `Cannot find module '../src/verify-stems'`.

- [ ] **Step 3: Написать реализацию**

Создать `Tools/opendaw-mcp/src/verify-stems.ts`:

```typescript
// Приёмка музыкального набора: читает готовые wav с диска и проверяет инвариант слоёв.
// Отличается от test/integration/stems-sum.test.ts тем, что смотрит на реальные файлы
// трека, а не на синтетический проект внутри движка.

export type Decoded = {sampleRate: number, channels: Float32Array[]}

export type StemSet = {
    mix: Decoded
    stems: ReadonlyArray<{name: string, wav: Decoded}>
    tolerance: number
}

export class WavError extends Error {}

const ascii = (bytes: Uint8Array, offset: number, length: number): string => {
    let text = ""
    for (let index = 0; index < length; index++) {text += String.fromCharCode(bytes[offset + index]!)}
    return text
}

// Чанки идут в произвольном порядке и между fmt и data может стоять что угодно
// (LIST, cue, fact), поэтому заголовок разбирается обходом, а не по фиксированным смещениям.
export const decodeWav = (bytes: Uint8Array): Decoded => {
    if (bytes.length < 12 || ascii(bytes, 0, 4) !== "RIFF" || ascii(bytes, 8, 4) !== "WAVE") {
        throw new WavError("не WAV: отсутствует заголовок RIFF/WAVE")
    }
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
    let format = 0
    let channelCount = 0
    let sampleRate = 0
    let bitsPerSample = 0
    let dataOffset = -1
    let dataSize = 0
    let offset = 12
    while (offset + 8 <= bytes.length) {
        const id = ascii(bytes, offset, 4)
        const size = view.getUint32(offset + 4, true)
        const body = offset + 8
        if (id === "fmt ") {
            format = view.getUint16(body, true)
            channelCount = view.getUint16(body + 2, true)
            sampleRate = view.getUint32(body + 4, true)
            bitsPerSample = view.getUint16(body + 14, true)
        } else if (id === "data") {
            dataOffset = body
            dataSize = Math.min(size, bytes.length - body)
        }
        offset = body + size + (size % 2)
    }
    if (channelCount === 0 || dataOffset < 0) {throw new WavError("в WAV нет чанка fmt или data")}
    const bytesPerSample = bitsPerSample / 8
    const frames = Math.floor(dataSize / (bytesPerSample * channelCount))
    const channels = Array.from({length: channelCount}, () => new Float32Array(frames))
    for (let frame = 0; frame < frames; frame++) {
        for (let channel = 0; channel < channelCount; channel++) {
            const at = dataOffset + (frame * channelCount + channel) * bytesPerSample
            let value: number
            if (format === 3 && bitsPerSample === 32) {
                value = view.getFloat32(at, true)
            } else if (format === 1 && bitsPerSample === 16) {
                value = view.getInt16(at, true) / 32768
            } else if (format === 1 && bitsPerSample === 24) {
                const raw = bytes[at]! | (bytes[at + 1]! << 8) | (bytes[at + 2]! << 16)
                value = ((raw & 0x800000) === 0 ? raw : raw - 0x1000000) / 8388608
            } else {
                throw new WavError(`формат ${format} c ${bitsPerSample} бит не поддержан`)
            }
            channels[channel]![frame] = value
        }
    }
    return {sampleRate, channels}
}

const peakOf = (wav: Decoded): number => {
    let peak = 0
    for (const channel of wav.channels) {
        for (const sample of channel) {
            const magnitude = Math.abs(sample)
            if (magnitude > peak) {peak = magnitude}
        }
    }
    return peak
}

const rmsOf = (wav: Decoded): number => {
    let squares = 0
    let count = 0
    for (const channel of wav.channels) {
        for (const sample of channel) {squares += sample * sample; count++}
    }
    return count === 0 ? 0 : Math.sqrt(squares / count)
}

const identical = (left: Decoded, right: Decoded): boolean => {
    if (left.channels.length !== right.channels.length) {return false}
    for (let channel = 0; channel < left.channels.length; channel++) {
        const a = left.channels[channel]!
        const b = right.channels[channel]!
        if (a.length !== b.length) {return false}
        for (let frame = 0; frame < a.length; frame++) {
            if (Math.abs(a[frame]! - b[frame]!) > 1e-9) {return false}
        }
    }
    return true
}

// Порог «слой не пустой». −80 dBFS: ниже этого слой неотличим от тишины даже
// на полной громкости, и его вклад в микс не услышать.
const SILENCE_RMS = 1e-4

export const checkStemSet = ({mix, stems, tolerance}: StemSet): string[] => {
    const problems: string[] = []
    if (stems.length === 0) {
        problems.push("нет стемов: проверять нечего")
        return problems
    }
    const frames = mix.channels[0]?.length ?? 0
    for (const {name, wav} of stems) {
        if (wav.sampleRate !== mix.sampleRate) {
            problems.push(`частота стема "${name}" (${wav.sampleRate}) не равна частоте микса (${mix.sampleRate})`)
        }
        if (wav.channels.length !== mix.channels.length) {
            problems.push(`число каналов стема "${name}" (${wav.channels.length}) не равно миксу (${mix.channels.length})`)
        }
        if ((wav.channels[0]?.length ?? 0) !== frames) {
            problems.push(`длина стема "${name}" (${wav.channels[0]?.length ?? 0}) не равна длине микса (${frames})`)
        }
        if (peakOf(wav) > 1) {problems.push(`перегруз в стеме "${name}": пик выше 1.0`)}
        if (rmsOf(wav) < SILENCE_RMS) {problems.push(`тишина в стеме "${name}": он ничего не вносит в микс`)}
    }
    if (peakOf(mix) > 1) {problems.push("перегруз в контрольном миксе: пик выше 1.0")}
    for (let left = 0; left < stems.length; left++) {
        for (let right = left + 1; right < stems.length; right++) {
            if (identical(stems[left]!.wav, stems[right]!.wav)) {
                problems.push(`стемы "${stems[left]!.name}" и "${stems[right]!.name}" совпадают отсчёт в отсчёт`)
            }
        }
    }
    // Сумму считаем только когда формы сошлись: иначе получим лавину ложных расхождений.
    if (problems.length === 0) {
        let worst = 0
        for (let channel = 0; channel < mix.channels.length; channel++) {
            for (let frame = 0; frame < frames; frame++) {
                let total = 0
                for (const {wav} of stems) {total += wav.channels[channel]![frame]!}
                worst = Math.max(worst, Math.abs(total - mix.channels[channel]![frame]!))
            }
        }
        if (worst > tolerance) {
            problems.push(`сумма стемов расходится с миксом: худшее отклонение ${worst.toExponential(2)} при допуске ${tolerance.toExponential(2)}`)
        }
    }
    return problems
}
```

- [ ] **Step 4: Прогнать тесты и убедиться, что они проходят**

Run: `cd Tools/opendaw-mcp && npx vitest run test/verify-stems.test.ts`
Expected: PASS, 11 тестов.

- [ ] **Step 5: Написать CLI-обёртку**

Создать `Tools/opendaw-mcp/scripts/verify-stems.mjs`:

```javascript
// Приёмка набора файлов трека. Запуск из корня репозитория:
//   node Tools/opendaw-mcp/scripts/verify-stems.mjs Audio/Music Combat_jennifer drums harmony lead
// Допуск подбирается по разрядности файлов: у int16 квантование даёт отклонение
// заметно выше float32-округления, и единый жёсткий порог давал бы ложные отказы.
import {readFile} from "node:fs/promises"
import {join} from "node:path"
import {checkStemSet, decodeWav} from "../dist/verify-stems.js"

const [dir, name, ...layers] = process.argv.slice(2)
if (dir === undefined || name === undefined || layers.length === 0) {
    console.error("нужно: <каталог> <имя трека> <слой> [слой ...]")
    process.exit(2)
}

const load = async path => decodeWav(new Uint8Array(await readFile(path)))

const mixPath = join(dir, `${name}.wav`)
const mix = await load(mixPath)
const stems = []
for (const layer of layers) {
    stems.push({name: layer, wav: await load(join(dir, `${name}_${layer}.wav`))})
}

// 3 × шаг квантования int16 покрывает округление микса и каждого из трёх слоёв.
const tolerance = 1e-4
const problems = checkStemSet({mix, stems, tolerance})
if (problems.length === 0) {
    const seconds = (mix.channels[0]?.length ?? 0) / mix.sampleRate
    console.log(`${name}: набор корректен, ${layers.length} слоя, ${seconds.toFixed(2)} с, ${mix.sampleRate} Гц`)
    process.exit(0)
}
console.error(`${name}: набор не принят`)
for (const problem of problems) {console.error(`  - ${problem}`)}
process.exit(1)
```

- [ ] **Step 6: Собрать пакет и убедиться, что CLI запускается**

Run: `cd Tools/opendaw-mcp && npm run build && cd ../.. && node Tools/opendaw-mcp/scripts/verify-stems.mjs Audio/Music e2e drums harmony lead`

Expected: скрипт отработает на уже лежащих в `Audio/Music/` файлах `e2e_*.wav`. Ожидаемый результат — либо «набор корректен», либо внятный список проблем. Оба исхода приемлемы: это файлы старого e2e-прогона, а не приёмка трека; проверяется, что CLI читает реальные wav и печатает осмысленный отчёт, а не падает стектрейсом. Если `Audio/Music/e2e_mix.wav` отсутствует, взять любой другой набор или пропустить шаг, отметив это.

- [ ] **Step 7: Прогнать весь модульный набор пакета**

Run: `cd Tools/opendaw-mcp && npm test`
Expected: PASS, прежние 70 тестов плюс 11 новых.

- [ ] **Step 8: Коммит**

```bash
git add Tools/opendaw-mcp/src/verify-stems.ts Tools/opendaw-mcp/scripts/verify-stems.mjs Tools/opendaw-mcp/test/verify-stems.test.ts
git commit -m "feat: приёмка набора стемов по готовым wav

Проверяет реальные файлы трека, а не синтетический проект: равенство
суммы слоёв и микса, совпадение длины, частоты и каналов, отсутствие
перегруза, пустых и продублированных слоёв.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Технический скелет на Дженифер

Примитивный документ, прогнанный через весь конвейер до `Assets/Resources/Music/`. Задача шага — доказать рельсы **до** того, как в них вложена музыка. Ошибка в контракте, найденная на третьем треке, стоила бы переписывания первых двух.

**Files:**
- Create: `Audio/Music/arrangements/Combat_jennifer.json`
- Modify: `Assets/Resources/Music/Combat_jennifer.wav` (перезапись)

**Interfaces:**
- Consumes: `verify-stems.mjs` из Task 1.
- Produces: структура документа (шины, стемы, порядок дорожек), которую Tasks 3–5 повторяют без изменений; подтверждённое имя файла контрольного микса.

- [ ] **Step 1: Импортировать ассеты в сессию**

`list_assets` в новой сессии пуст. Импортировать пять файлов:

```
import_asset {path: "Audio/Samples/Drums/kick.wav",  name: "kick"}
import_asset {path: "Audio/Samples/Drums/snare.wav", name: "snare"}
import_asset {path: "Audio/Samples/Drums/hihat.wav", name: "hihat"}
import_asset {path: "Audio/Samples/Drums/tom.wav",   name: "tom"}
import_asset {path: "Audio/Samples/Soundfonts/GeneralUser.sf2", name: "GeneralUser"}
```

Затем `list_assets` — убедиться, что все пять на месте и у `GeneralUser` виден список пресетов.

- [ ] **Step 2: Записать скелетный документ**

Создать `Audio/Music/arrangements/Combat_jennifer.json`. Два такта, по одному звуку на слой — этого достаточно, чтобы все три стема были непустыми и различными:

```json
{
  "name": "Combat_jennifer",
  "tempo": 132,
  "signature": "4/4",
  "end": "3.1",
  "loop": {"start": "1.1", "end": "3.1"},
  "buses": [
    {"name": "Drums", "stem": "drums", "mix": {"volume": -6}},
    {"name": "Harmony", "stem": "harmony", "mix": {"volume": -8}},
    {"name": "Lead", "stem": "lead", "mix": {"volume": -10}}
  ],
  "patterns": {
    "beat": {
      "length": "1b",
      "notes": [
        {"p": "C1", "at": "1.1", "d": "1/8"},
        {"p": "C1", "at": "1.3", "d": "1/8"},
        {"p": "D1", "at": "1.2", "d": "1/8"},
        {"p": "D1", "at": "1.4", "d": "1/8"}
      ]
    },
    "bass": {
      "length": "1b",
      "notes": [
        {"p": "D2", "at": "1.1", "d": "1/2"},
        {"p": "A2", "at": "1.3", "d": "1/2"}
      ]
    },
    "melody": {
      "length": "1b",
      "notes": [
        {"p": "D4", "at": "1.1", "d": "1/4"},
        {"p": "F4", "at": "1.3", "d": "1/4"}
      ]
    }
  },
  "tracks": [
    {
      "name": "Kit",
      "instrument": {"device": "Playfield", "slots": {"C1": "kick", "D1": "snare", "F#1": "hihat", "A1": "tom"}},
      "out": "Drums",
      "place": [{"pattern": "beat", "at": "1.1", "repeat": 2}]
    },
    {
      "name": "Bass",
      "instrument": {"device": "Vaporisateur", "params": {"voicingMode": "mono", "cutoff": 1200}},
      "mix": {"volume": -4},
      "out": "Harmony",
      "place": [{"pattern": "bass", "at": "1.1", "repeat": 2}]
    },
    {
      "name": "Melody",
      "instrument": {"device": "Vaporisateur"},
      "mix": {"volume": -6},
      "out": "Lead",
      "place": [{"pattern": "melody", "at": "1.1", "repeat": 2}]
    }
  ]
}
```

- [ ] **Step 3: Собрать и осмотреть проект**

```
build_arrangement {document: <содержимое файла>}
inspect_project
```

Expected: сборка без ошибок, в сводке три дорожки и три шины со стемами `drums`, `harmony`, `lead`. Если `build_arrangement` отказывает — читать путь до поля в сообщении, это строгая валидация, а не сбой.

- [ ] **Step 4: Отрендерить стемы и микс**

Два вызова по **одному и тому же** построенному проекту, без пересборки между ними:

```
render {target: "stems", name: "Combat_jennifer", loop: true, format: "int16"}
render {target: "mix",   name: "Combat_jennifer", loop: true, format: "int16"}
```

Expected: в `Audio/Music/` появились `Combat_jennifer_drums.wav`, `Combat_jennifer_harmony.wav`, `Combat_jennifer_lead.wav` и файл микса. **Записать в заметки задачи фактическое имя файла микса** — `render` складывает имя как `<name>_<стем>.wav`, и для микса суффикс определяется движком. Tasks 3–6 опираются на это имя.

Run: `ls -la Audio/Music/Combat_jennifer*`

- [ ] **Step 5: Привести микс к игровому имени и проверить набор**

```bash
mv "Audio/Music/Combat_jennifer_mix.wav" "Audio/Music/Combat_jennifer.wav"
node Tools/opendaw-mcp/scripts/verify-stems.mjs Audio/Music Combat_jennifer drums harmony lead
```

(Если имя файла микса на шаге 4 оказалось другим — подставить фактическое.)

Expected: `Combat_jennifer: набор корректен, 3 слоя, ~3.64 с, 48000 Гц`.
Два такта при 132 BPM — 3,64 секунды. Если скрипт отказывает, это отказ шага: разбирать причину, а не идти дальше.

- [ ] **Step 6: Установить в игру и проверить, что Unity видит файл**

```bash
cp Audio/Music/Combat_jennifer.wav Assets/Resources/Music/Combat_jennifer.wav
```

Стемы в `Assets/Resources/Music/` на этом шаге **не** копируются: многослойного проигрывателя в Unity нет, и лишние файлы в `Resources` только раздули бы сборку. Они попадут туда на шаге установки в Task 7, когда станут настоящей музыкой.

Проверка: запустить бой за Дженифер и услышать вместо прежней темы двухтактный скелет. Если ручной запуск Unity в этой сессии невозможен — отметить проверку как отложенную до Task 7 и идти дальше; замена файла по имени кодом не затрагивается, риск низкий.

- [ ] **Step 7: Прослушать луп**

Зациклить `Audio/Music/Combat_jennifer.wav` в любом плеере на 5–6 проходов. Стык не должен щёлкать. Это первая и главная проверка свёртки хвоста: если щелчок слышен на скелете, дальше писать музыку бессмысленно.

- [ ] **Step 8: Коммит**

```bash
git add Audio/Music/arrangements/Combat_jennifer.json
git commit -m "feat: технический скелет боевой темы Дженифер

Двухтактный документ с тремя слоями: доказывает конвейер до
Assets/Resources/Music до того, как в него вложена музыка. Сумма
стемов сходится с миксом, стык лупа не слышен.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Общая процедура сочинения (Tasks 3–6)

Композиция — не детерминированный шаг: она требует цикла «набросок → рендер → прослушивание → правка». План задаёт весь технический каркас дословно (устройства, параметры, шины, гармонию, ритмические рисунки) и критерии приёмки, но конкретные ноты мелодии рождаются в этом цикле. Это осознанное отступление от обычного правила «каждый шаг полностью определён»: ноты, выписанные вслепую, всё равно были бы переписаны после первого прослушивания.

Цикл на каждый трек:

1. Расширить документ до полной длины по каркасу задачи.
2. `build_arrangement` → `render` стемы → `render` микс (тот же проект, без пересборки).
3. `verify-stems.mjs` — машинная приёмка.
4. Прослушать в трёх состояниях: только `drums`; `drums + harmony`; всё вместе.
5. Если состояние не проходит критерий задачи — править документ и с шага 2.
6. `export_bundle` для возможной ручной доводки в интерфейсе openDAW.
7. Коммит документа.

Проверять слои по отдельности проще всего, сложив их в любом плеере с регулировкой громкости дорожек, либо временно отрендерив нужное подмножество через `range` и слушая отдельные wav из `Audio/Music/`.

---

### Task 3: Боевая тема Дженифер

Дженифер — Воин: прямая героика, марш, честная сила. Её документ становится утверждённым шаблоном структуры для Вайолет и Саши.

**Files:**
- Modify: `Audio/Music/arrangements/Combat_jennifer.json` (скелет заменяется полной темой)

**Interfaces:**
- Consumes: структура шин из Task 2, имя файла микса, подтверждённое в Task 2 шаг 4.
- Produces: `Combat_jennifer{,_drums,_harmony,_lead}.wav` в `Audio/Music/`; порядок ключей и структура документа, повторяемые в Tasks 4–6.

- [ ] **Step 1: Задать каркас документа**

Ре минор натуральный, 132 BPM, 4/4, 16 тактов.

- `end`: `"17.1"`, `loop`: `{"start": "1.1", "end": "17.1"}`. Такт 17 — первая доля после шестнадцатого такта; ничего в аранжировке после этой точки быть не должно.
- Длина лупа: 16 × 4 × 60 / 132 = **29,09 с**.
- Шины ровно как в Task 2: `Drums`/`drums`, `Harmony`/`harmony`, `Lead`/`lead`.

Эффекты на шинах (нелинейное — только здесь, мастер остаётся чистым):

```json
"buses": [
  {"name": "Drums", "stem": "drums", "mix": {"volume": -5}},
  {"name": "Harmony", "stem": "harmony", "mix": {"volume": -7},
   "effects": [{"device": "DattorroReverb",
                "params": {"decay": 0.45, "damping": 0.35, "wet": -14, "dry": 0}}]},
  {"name": "Lead", "stem": "lead", "mix": {"volume": -8},
   "effects": [{"device": "DattorroReverb",
                "params": {"decay": 0.3, "damping": 0.5, "wet": -22, "dry": 0}}]}
]
```

`lead` намеренно почти сухой: длинный реверб на слое, который в игре вводится подъёмом громкости, слышится как «включили комнату», а не как вступление партии.

- [ ] **Step 2: Написать слой drums**

`Playfield` со слотами `{"C1": "kick", "D1": "snare", "F#1": "hihat", "A1": "tom"}`, дорожка `out: "Drums"`.

Маршевый рисунок: бочка на долях 1 и 3, снейр на 2 и 4, хэт восьмыми. Базовый однотактовый паттерн:

```json
"beat": {
  "length": "1b",
  "notes": [
    {"p": "C1", "at": "1.1", "d": "1/8", "v": 0.9},
    {"p": "C1", "at": "1.3", "d": "1/8", "v": 0.85},
    {"p": "D1", "at": "1.2", "d": "1/8", "v": 0.8},
    {"p": "D1", "at": "1.4", "d": "1/8", "v": 0.8},
    {"p": "F#1", "at": "1.1", "d": "1/16", "v": 0.5},
    {"p": "F#1", "at": "1.1.3", "d": "1/16", "v": 0.35},
    {"p": "F#1", "at": "1.2", "d": "1/16", "v": 0.5},
    {"p": "F#1", "at": "1.2.3", "d": "1/16", "v": 0.35},
    {"p": "F#1", "at": "1.3", "d": "1/16", "v": 0.5},
    {"p": "F#1", "at": "1.3.3", "d": "1/16", "v": 0.35},
    {"p": "F#1", "at": "1.4", "d": "1/16", "v": 0.5},
    {"p": "F#1", "at": "1.4.3", "d": "1/16", "v": 0.35}
  ]
}
```

Плюс отдельный паттерн `fill` на один такт с томом (`A1`) шестнадцатыми на четвёртой доле — ставится в тактах 8 и 16 вместо `beat`, чтобы шестнадцатитактовый круг не звучал как восемь одинаковых пар. Расстановка: `beat` ×7, `fill` в такте 8, `beat` ×7, `fill` в такте 16.

- [ ] **Step 3: Написать слой harmony**

Две дорожки в шину `Harmony`.

Гармония, по два такта на аккорд, круг из восьми тактов повторяется дважды:

| Такты | Аккорд | Бас |
|---|---|---|
| 1–2 | Dm (i) | D2 |
| 3–4 | B♭ (VI) | Bb1 |
| 5–6 | F (III) | F2 |
| 7–8 | C (VII) | C2 |
| 9–10 | Dm | D2 |
| 11–12 | B♭ | Bb1 |
| 13–14 | Gm (iv) | G1 |
| 15–16 | A (V) | A1 |

Смена iv–V во второй половине даёт кругу движение к возвращению и не даёт шестнадцати тактам распасться на два одинаковых восьмитактия.

Дорожка «Strings» — `{"device": "Soundfont", "soundfont": "GeneralUser", "preset": 48}` (String Ensemble). Выдержанные трезвучия в диапазоне D3–A4, целыми на такт, `v` около 0,6.

Дорожка «Bass» — `{"device": "Vaporisateur", "params": {"voicingMode": "mono", "oscillators[0].waveform": "Sawtooth", "cutoff": 900, "attack": 0.005, "release": 0.15}}`. Ход четвертями с движением по квартам к следующему аккорду на четвёртой доле каждого второго такта.

Критерий слоя: `drums + harmony` без `lead` должны звучать как законченная тема, в которой слышны и тональность, и все смены аккордов.

- [ ] **Step 4: Написать слой lead**

Две дорожки в шину `Lead`.

Дорожка «Arp» — арпеджио, как задано ГДД:

```json
{
  "name": "Arp",
  "instrument": {"device": "Vaporisateur",
                 "params": {"oscillators[0].waveform": "Sawtooth", "unisonCount": 2,
                            "unisonDetune": 12, "cutoff": 4500, "attack": 0.002,
                            "decay": 0.2, "sustain": 0.3, "release": 0.12}},
  "effects": [{"device": "Arpeggio",
               "params": {"modeIndex": "Up", "numOctaves": 2, "rate": "1/16", "gate": 0.7}}],
  "mix": {"volume": -9},
  "out": "Lead",
  "place": [...]
}
```

В паттерн кладутся выдержанные аккордовые тоны на такт — `Arpeggio` сам разворачивает их шестнадцатыми на две октавы. Аккорды те же, что в harmony.

Дорожка «Horn» — `{"device": "Soundfont", "soundfont": "GeneralUser", "preset": 61}` (Brass Section). Геройский акцент: короткая восходящая фигура на границах фраз, то есть в тактах 8 и 16, и выдержанная квинта в тактах 1 и 9. Всего 6–8 нот на весь круг — акцент, а не вторая мелодия.

Критерий слоя: убрать `lead` — тональность и форма читаются полностью. Если без него тема разваливается, гармоническая функция утекла в `lead` и слой переписывается.

- [ ] **Step 5: Собрать, отрендерить, проверить**

```
build_arrangement {document: <документ>}
render {target: "stems", name: "Combat_jennifer", loop: true, format: "int16"}
render {target: "mix",   name: "Combat_jennifer", loop: true, format: "int16"}
```

```bash
mv "Audio/Music/Combat_jennifer_mix.wav" "Audio/Music/Combat_jennifer.wav"
node Tools/opendaw-mcp/scripts/verify-stems.mjs Audio/Music Combat_jennifer drums harmony lead
```

Expected: `набор корректен, 3 слоя, ~29.09 с, 48000 Гц`. При отказе — править и повторять с `build_arrangement`.

- [ ] **Step 6: Прослушать в трёх состояниях**

- `drums` в одиночку — внятный самостоятельный пульс, а не остаток от музыки.
- `drums + harmony` — законченная тема.
- всё вместе — пик, в котором `lead` добавляет энергию, а не тональность.
- Луп на 5–6 проходов — стык не слышен, и он должен быть не слышен **в каждом сочетании слоёв**, а не только в полном миксе.

Не проходит — возвращаться к шагам 2–4.

- [ ] **Step 7: Сохранить бандл и закоммитить документ**

```
export_bundle {path: "Audio/Music/Combat_jennifer.odb"}
```

```bash
git add Audio/Music/arrangements/Combat_jennifer.json
git commit -m "feat: боевая тема Дженифер

Ре минор, 132 BPM, шестнадцатитактовый луп тремя слоями. Гармония
целиком в harmony, lead только украшает: тема читается без ведущей
партии.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Боевая тема Вайолет

Вайолет — Плут: вёрткость, синкопа, недосказанность. Структура документа повторяет Task 3, музыкальное содержание — нет.

**Files:**
- Create: `Audio/Music/arrangements/Combat_violet.json`

**Interfaces:**
- Consumes: структура документа, утверждённая в Task 3.
- Produces: `Combat_violet{,_drums,_harmony,_lead}.wav` в `Audio/Music/`.

- [ ] **Step 1: Задать каркас**

Ля фригийский, 148 BPM, 4/4, 16 тактов. `end`: `"17.1"`, `loop`: `{"start": "1.1", "end": "17.1"}`. Длина лупа: 16 × 4 × 60 / 148 = **25,95 с**.

Шины те же три. Эффекты:

```json
"buses": [
  {"name": "Drums", "stem": "drums", "mix": {"volume": -6}},
  {"name": "Harmony", "stem": "harmony", "mix": {"volume": -8},
   "effects": [{"device": "DattorroReverb",
                "params": {"decay": 0.35, "damping": 0.5, "wet": -16, "dry": 0}}]},
  {"name": "Lead", "stem": "lead", "mix": {"volume": -9},
   "effects": [{"device": "Delay",
                "params": {"delay": "3/16", "feedback": 0.32, "cross": 1, "wet": -12, "dry": 0}}]}
]
```

Дилей на `3/16` против четырёхдольного размера — источник вёрткости: эхо всё время падает не туда, куда ждёшь.

- [ ] **Step 2: Написать слой drums**

Тот же `Playfield` с теми же слотами. Рисунок хэт-ведомый: непрерывные шестнадцатые на `F#1` с чередованием `v` 0,5 / 0,3 (даёт качание), бочка **смещена с сильных долей** — на `1.1`, `1.2.4`, `1.3.3`, снейр как рикошет на слабую восьмую (`1.2.3` и `1.4.3`), а не ровно на 2 и 4.

Два разных однотактовых паттерна, чередующихся через такт: это не даёт синкопе застыть в один жёсткий рисунок.

Критерий: рисунок должен читаться как «вёрткий», а не как «сбитый». Если на слух непонятно, где доля 1, — рисунок переписывается: слой `drums` в игре звучит всегда и обязан держать пульс.

- [ ] **Step 3: Написать слой harmony**

Фригийский лад: тоника Am, вторая ступень **B♭** — это фирменный цвет темы, и он должен быть слышен в первых же тактах.

| Такты | Аккорд | Бас |
|---|---|---|
| 1–2 | Am (i) | A1 |
| 3–4 | B♭ (♭II) | Bb1 |
| 5–6 | Am | A1 |
| 7–8 | G (♭VII) | G1 |
| 9–10 | Am | A1 |
| 11–12 | B♭ | Bb1 |
| 13–14 | F (♭VI) | F1 |
| 15–16 | E (V) | E1 |

Дорожка «Pizz» — `{"device": "Soundfont", "soundfont": "GeneralUser", "preset": 45}` (Pizzicato Strings), восьмыми, синкопированно, короткие ноты.

Дорожка «Upright» — `{"device": "Soundfont", "soundfont": "GeneralUser", "preset": 32}` (Acoustic Bass), ход четвертями с проходящими нотами лада, `v` около 0,7.

Pizzicato и контрабас вместо выдержанных струнных: у Вайолет гармония должна пульсировать, а не лежать ковром, иначе тема сольётся с темой Дженифер.

- [ ] **Step 4: Написать слой lead**

Одна дорожка «Blade» в шину `Lead`:

```json
{
  "name": "Blade",
  "instrument": {"device": "Neon",
                 "params": {"voicingMode": "mono", "lines[0].wave1": "Pulse",
                            "glideTime": 0.05, "octave": 1}},
  "mix": {"volume": -7},
  "out": "Lead",
  "place": [...]
}
```

Резкая синкопированная партия: короткие ноты на слабых долях и шестнадцатых, паузы длиннее фраз, ни одной ноты ровно на долю 1 в первых четырёх тактах. Фраза 2 такта, повторяется с вариацией.

Критерий слоя: панч без длинного хвоста; дилей должен читаться как ответ, а не как каша. Если `feedback` замыливает фразу — уменьшать до 0,2.

- [ ] **Step 5: Собрать, отрендерить, проверить**

```
build_arrangement {document: <документ>}
render {target: "stems", name: "Combat_violet", loop: true, format: "int16"}
render {target: "mix",   name: "Combat_violet", loop: true, format: "int16"}
```

```bash
mv "Audio/Music/Combat_violet_mix.wav" "Audio/Music/Combat_violet.wav"
node Tools/opendaw-mcp/scripts/verify-stems.mjs Audio/Music Combat_violet drums harmony lead
```

Expected: `набор корректен, 3 слоя, ~25.95 с, 48000 Гц`.

- [ ] **Step 6: Прослушать в трёх состояниях**

Те же три критерия, что в Task 3 шаг 6, плюс отдельно: тема Вайолет и тема Дженифер, поставленные подряд, должны различаться на слух в первые две секунды.

- [ ] **Step 7: Сохранить бандл и закоммитить**

```
export_bundle {path: "Audio/Music/Combat_violet.odb"}
```

```bash
git add Audio/Music/arrangements/Combat_violet.json
git commit -m "feat: боевая тема Вайолет

Ля фригийский, 148 BPM, синкопированный рисунок и дилей 3/16 против
четырёхдольного размера. Pizzicato и контрабас вместо выдержанных
струнных, чтобы гармония пульсировала, а не лежала ковром.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Боевая тема Саши

Саша — Варвар: вес, инерция, ярость. Самый медленный и самый плотный из трёх треков.

**Files:**
- Create: `Audio/Music/arrangements/Combat_sasha.json`

**Interfaces:**
- Consumes: структура документа, утверждённая в Task 3.
- Produces: `Combat_sasha{,_drums,_harmony,_lead}.wav` в `Audio/Music/`.

- [ ] **Step 1: Задать каркас**

До минор, 108 BPM, 4/4 с half-time ощущением, 16 тактов. `end`: `"17.1"`, `loop`: `{"start": "1.1", "end": "17.1"}`. Длина лупа: 16 × 4 × 60 / 108 = **35,56 с**.

```json
"buses": [
  {"name": "Drums", "stem": "drums", "mix": {"volume": -4}},
  {"name": "Harmony", "stem": "harmony", "mix": {"volume": -8},
   "effects": [
     {"device": "Fold", "params": {"drive": 6, "volume": -4}},
     {"device": "DattorroReverb",
      "params": {"decay": 0.5, "damping": 0.6, "wet": -18, "dry": 0}}
   ]},
  {"name": "Lead", "stem": "lead", "mix": {"volume": -8},
   "effects": [
     {"device": "Waveshaper", "params": {"inputGain": 8, "outputGain": -6, "mix": 0.7}},
     {"device": "Revamp",
      "params": {"highPass.enabled": true, "highPass.frequency": 160,
                 "midBell.enabled": true, "midBell.frequency": 900, "midBell.gain": 4}}
   ]}
]
```

`Fold` и `Waveshaper` живут на шинах слоёв, а не на мастере: там они попадают внутрь своего стема и равенство суммы сохраняют. Высокочастотный срез на 160 Гц в `lead` убирает низ риффа, чтобы он не дрался с басом.

- [ ] **Step 2: Написать слой drums**

Half-time: бочка на доле 1 и на «и» третьей доли, снейр **только на доле 3** и намеренно поздно — на `1.3.2`, том (`A1`) как вес в конце двухтактовой фразы. Хэт редкий, четвертями, `v` около 0,4 — плотность здесь набирается низом, а не верхом.

Критерий: рисунок должен ощущаться вдвое медленнее темпа, оставаясь при 108 BPM.

- [ ] **Step 3: Написать слой harmony**

Выдержанные квинты, без терций: неопределённость лада — часть тяжести.

| Такты | Основа | Бас |
|---|---|---|
| 1–4 | C5 (до–соль) | C1 |
| 5–6 | A♭5 | Ab1 |
| 7–8 | B♭5 | Bb1 |
| 9–12 | C5 | C1 |
| 13–14 | F5 | F1 |
| 15–16 | G5 | G1 |

Четырёхтактовые стояния на тонике — это и есть инерция: гармония движется реже, чем у двух других героинь.

Дорожка «Drone» — `{"device": "Vaporisateur", "params": {"oscillators[0].waveform": "Sawtooth", "unisonCount": 3, "unisonDetune": 18, "cutoff": 1400, "attack": 0.05, "release": 0.4}}`, выдержанные квинты целыми нотами.

Дорожка «Bass» — `{"device": "Cubed", "params": {"waveform": "Sawtooth", "cutoff": 0.35, "resonance": 0.5, "decay": 0.6, "accent": 0.8}}`. `Cubed` — машина с собственным паттерном, даёт плотный нижний гул с характерным движением фильтра; для веса Саши это точнее, чем ещё один `Vaporisateur`.

- [ ] **Step 4: Написать слой lead**

Одна дорожка «Riff»:

```json
{
  "name": "Riff",
  "instrument": {"device": "Vaporisateur",
                 "params": {"oscillators[0].waveform": "Sawtooth",
                            "oscillators[1].waveform": "Square", "oscillators[1].octave": -1,
                            "voicingMode": "mono", "unisonCount": 4, "unisonDetune": 25,
                            "cutoff": 3000, "attack": 0.003, "release": 0.1}},
  "mix": {"volume": -6},
  "out": "Lead",
  "place": [...]
}
```

Тяжёлый рифф: короткая фигура в один такт из 4–6 нот в диапазоне C2–C3, ритмически совпадающая с бочкой, повторяется всю тему с вариацией в тактах 13–16. Рифф работает вместе с ударными, а не поверх них — это и создаёт ощущение удара.

Критерий слоя: убрать `lead` — остаются квинты и бочка, тема опознаётся как тема Саши. Если без риффа тема становится безликой, рифф забрал слишком много и часть его фигуры переносится в `harmony`.

- [ ] **Step 5: Собрать, отрендерить, проверить**

```
build_arrangement {document: <документ>}
render {target: "stems", name: "Combat_sasha", loop: true, format: "int16"}
render {target: "mix",   name: "Combat_sasha", loop: true, format: "int16"}
```

```bash
mv "Audio/Music/Combat_sasha_mix.wav" "Audio/Music/Combat_sasha.wav"
node Tools/opendaw-mcp/scripts/verify-stems.mjs Audio/Music Combat_sasha drums harmony lead
```

Expected: `набор корректен, 3 слоя, ~35.56 с, 48000 Гц`.

Отдельный риск этого трека: `Fold` и `Waveshaper` легко загоняют шину в перегруз. Если скрипт сообщает «перегруз» — убавлять `mix.volume` шины, а **не** ставить лимитер на мастер.

- [ ] **Step 6: Прослушать в трёх состояниях**

Те же три критерия. Плюс: три темы подряд должны звучать как три разных персонажа.

- [ ] **Step 7: Сохранить бандл и закоммитить**

```
export_bundle {path: "Audio/Music/Combat_sasha.odb"}
```

```bash
git add Audio/Music/arrangements/Combat_sasha.json
git commit -m "feat: боевая тема Саши

До минор, 108 BPM, half-time. Выдержанные квинты без терций и рифф в
унисон с бочкой. Fold и Waveshaper только на шинах слоёв: на мастере
они разрушили бы равенство суммы стемов и микса.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Тема деревни Hub

`Hub` — единственный обязательный недостающий файл по таблице ГДД: без него деревня, Таверна, Кузница и призыв идут в тишине. Слоёв у него нет, требование одно — бесшовный луп.

**Files:**
- Create: `Audio/Music/arrangements/Hub.json`

**Interfaces:**
- Consumes: ничего от предыдущих задач, кроме процедуры рендера.
- Produces: `Hub.wav` в `Audio/Music/`.

- [ ] **Step 1: Написать документ**

Соль мажор, 92 BPM, 4/4, 16 тактов. `end`: `"17.1"`, `loop`: `{"start": "1.1", "end": "17.1"}`. Длина: 16 × 4 × 60 / 92 = **41,74 с**.

Шин со стемами **нет** — трек одиночный, стемы ему не нужны. Достаточно дорожек без `out` и `stem`; `render {target: "mix"}` соберёт их в один файл.

Три дорожки:

- «Harp» — `{"device": "Soundfont", "soundfont": "GeneralUser", "preset": 46}` (Orchestral Harp). Раскладка аккордов восьмыми, мягко, `v` около 0,5.
- «Lute» — `{"device": "Soundfont", "soundfont": "GeneralUser", "preset": 24}` (Acoustic Guitar nylon). Простая мелодия четвертями и половинными, диапазон G3–D5.
- «Strings» — `{"device": "Soundfont", "soundfont": "GeneralUser", "preset": 49}` (String Ensemble 2). Выдержанная подложка, `v` около 0,35.

Ударных нет.

Гармония: G – Em – C – D по два такта, круг повторяется дважды, во второй раз в тактах 13–14 вместо C ставится Am — иначе сорок секунд будут ощущаться как один и тот же восьмитакт дважды.

Реверб — `DattorroReverb` инсертом на дорожке «Strings» (`decay` 0,55, `wet` −12). Мастер остаётся чистым: у `Hub` нет стемов, но правило одно для всех документов, и нарушать его ради одного трека значит завести исключение, о котором потом забудут.

- [ ] **Step 2: Собрать и отрендерить**

```
build_arrangement {document: <документ>}
render {target: "mix", name: "Hub", loop: true, format: "int16"}
```

Микс приедет как `Audio/Music/Hub_<суффикс>.wav`:

```bash
mv "Audio/Music/Hub_mix.wav" "Audio/Music/Hub.wav"
ls -la Audio/Music/Hub*
```

- [ ] **Step 3: Прослушать луп**

Зациклить на 5–6 проходов. Стык не слышен, тема не надоедает за минуту, ощущение — тепло деревни, а не тревога подземелья.

`verify-stems.mjs` здесь неприменим: у трека нет стемов, проверять равенство суммы не с чем.

- [ ] **Step 4: Установить в игру**

```bash
cp Audio/Music/Hub.wav Assets/Resources/Music/Hub.wav
```

Проверка: зайти в деревню — играет `Hub`; войти в бой — тема героини; выйти обратно — снова `Hub`, без наложения. Подбор идёт по имени файла, кода это не касается.

- [ ] **Step 5: Коммит**

```bash
git add Audio/Music/arrangements/Hub.json
git commit -m "feat: тема деревни Hub

Соль мажор, 92 BPM, арфа, лютня и струнные без ударных. Закрывает
единственный обязательный недостающий трек по таблице ГДД.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Установка, приёмка и обновление ГДД

**Files:**
- Modify: `Assets/Resources/Music/` — 13 файлов
- Modify: `Docs/superpowers/specs/2026-09-09-combat-music-tracks-design.md` (статус реализации)

**Interfaces:**
- Consumes: все четыре набора файлов из `Audio/Music/`.
- Produces: готовая музыка в игре.

- [ ] **Step 1: Скопировать все файлы**

```bash
for hero in jennifer violet sasha; do
  for layer in drums harmony lead; do
    cp "Audio/Music/Combat_${hero}_${layer}.wav" "Assets/Resources/Music/Combat_${hero}_${layer}.wav"
  done
  cp "Audio/Music/Combat_${hero}.wav" "Assets/Resources/Music/Combat_${hero}.wav"
done
cp Audio/Music/Hub.wav Assets/Resources/Music/Hub.wav
ls -la Assets/Resources/Music/
```

Expected: 13 wav-файлов. Стемы кладутся в `Resources` сейчас, хотя проигрыватель слоёв ещё не написан: они и есть результат этой работы, и без них следующий шаг — рантайм-микшер — начинать не с чем.

- [ ] **Step 2: Машинная приёмка всех трёх наборов**

```bash
for hero in jennifer violet sasha; do
  node Tools/opendaw-mcp/scripts/verify-stems.mjs Assets/Resources/Music "Combat_${hero}" drums harmony lead
done
```

Expected: три строки «набор корректен». Проверяются **установленные** файлы, а не рабочие копии: копирование тоже может испортить набор.

- [ ] **Step 3: Проверить воспроизводимость документа**

Спек требует, чтобы закоммиченные документы давали те же файлы при повторном прогоне. Проверить на одном треке достаточно — конвейер общий:

```bash
md5sum Audio/Music/Combat_jennifer_drums.wav
```

Затем заново: `build_arrangement` из закоммиченного `Audio/Music/arrangements/Combat_jennifer.json` (не из рабочей копии в контексте), `render {target: "stems", name: "Combat_jennifer_repro", loop: true, format: "int16"}` и сравнить:

```bash
md5sum Audio/Music/Combat_jennifer_repro_drums.wav
rm Audio/Music/Combat_jennifer_repro_*.wav
```

Expected: суммы совпадают. Если нет — в конвейере есть недетерминированный шаг (например, рандомизация в `Velocity` или несохранённый импорт ассета), и это надо записать в открытые вопросы спека, а не замолчать.

- [ ] **Step 4: Прогнать тесты проекта**

Run: `cd Tools/opendaw-mcp && npm test`
Expected: PASS.

Unity-тесты музыки не касаются — привязка идёт по имени файла, кода изменений нет. Если EditMode-набор запускается легко, прогнать и его для страховки; помнить, что `-runTests` вместе с `-quit` молча ничего не делает, и всегда передавать `-testResults <путь>`.

- [ ] **Step 5: Ручная приёмка по чек-листу спека**

Пройти в игре бой за каждую героиню и деревню. Отметить по списку:

- [ ] Стык лупа не слышен ни у одного трека.
- [ ] Стык не слышен в каждом сочетании слоёв, а не только в полном миксе.
- [ ] `drums` в одиночку звучит как самостоятельный пульс.
- [ ] `drums + harmony` звучит как законченная тема.
- [ ] Три героини различимы на слух в первые две секунды.
- [ ] `lead` вводится как вступление партии, а не как включение реверберации.
- [ ] Прослушано в наушниках и на обычных динамиках.
- [ ] Смена контекста деревня → бой → деревня без наложения треков.

Незакрытый пункт — это незакрытая задача, а не примечание. Автотесты звучание не проверяют, и «тесты прошли» не заменяет «звучит хорошо».

- [ ] **Step 6: Обновить статус спека**

В `Docs/superpowers/specs/2026-09-09-combat-music-tracks-design.md` заменить строку `Статус реализации: не начата` на `Статус реализации: выполнено <дата>` и вписать под ней фактические результаты: длины лупов, худшее отклонение суммы от микса по каждому треку, что осталось открытым.

- [ ] **Step 7: Коммит и завершение ветки**

```bash
git add Assets/Resources/Music Docs/superpowers/specs/2026-09-09-combat-music-tracks-design.md
git commit -m "feat: боевые темы трёх героинь и тема деревни в игре

Тринадцать файлов в Assets/Resources/Music: три набора слоёв с
контрольными миксами и одиночный Hub. Машинная приёмка набора пройдена
на установленных файлах, ручное прослушивание проведено.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Затем воспользоваться навыком `superpowers:finishing-a-development-branch`, чтобы решить, как вливать ветку.

- [ ] **Step 8: Обновить ГДД в Notion**

На странице «06 — Интерфейс, обучение и звук» в таблице треков сменить статус `Hub` с «Нужно написать» на «Готов». На странице «Инструмент — MCP-сервер openDAW и слои музыки» в разделе «Чего пока нет» уточнить: авторский экспорт стемов не только готов, но и применён к трём боевым темам; нереализованной остаётся только сторона Unity — микшер слоёв и датчик напряжения.

---

## Что этот план не делает

- Не пишет `CombatMusicIntensity` и многослойный проигрыватель в Unity. После него игра по-прежнему играет цельные `Combat_<hero>.wav`; стемы лежат в `Resources` как материал для следующей работы.
- Не пишет `Combat_Boss`. Нужен ли Дженифер отдельный босс-трек — открытый вопрос спека, решается после того, как боевые темы зазвучат.
- Не трогает `.rpp`-исходники старых тем в `Audio/Music/`: они остаются справочным материалом.
