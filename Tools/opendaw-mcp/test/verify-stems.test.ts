import {describe, expect, it} from "vitest"
import {encodeWav} from "../src/wav"
import {checkStemSet, decodeWav, toleranceFor, type Decoded} from "../src/verify-stems"

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

    it("сообщает разрядность исходного файла", () => {
        const channels = stereo(tone(1000, 440))
        expect(decodeWav(encodeWav(channels, 48000, "int16")).bitsPerSample).toBe(16)
        expect(decodeWav(encodeWav(channels, 48000, "float32")).bitsPerSample).toBe(32)
    })
})

describe("toleranceFor", () => {
    it("для float32 берёт порог округления", () => {
        expect(toleranceFor(32, 3)).toBe(1e-6)
    })

    it("для int16 берёт пять шагов квантования на три слоя", () => {
        expect(toleranceFor(16, 3)).toBeCloseTo(5 / 32768, 9)
    })

    it("для int24 порог на три порядка строже, чем для int16", () => {
        expect(toleranceFor(24, 3)).toBeLessThan(toleranceFor(16, 3) / 100)
    })

    it("допуск растёт с числом слоёв: каждый добавляет своё округление", () => {
        expect(toleranceFor(16, 5)).toBeGreaterThan(toleranceFor(16, 3))
    })

    it("реальное расхождение int16-набора укладывается в допуск", () => {
        // Наблюдённое на наборе e2e_*.wav расхождение — 1.22e-4. Порог обязан его
        // принимать: это квантование, а не расхождение слоёв.
        expect(toleranceFor(16, 3)).toBeGreaterThan(1.22e-4)
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
        // 1.2 берётся вместо суммы «громких, но не клиппующих» слоёв намеренно:
        // синусы на 110/220/330 Гц не совпадают по фазе и в сумме пика выше 1 не дают.
        const loud = stereo(tone(4800, 110, 1.2))
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
