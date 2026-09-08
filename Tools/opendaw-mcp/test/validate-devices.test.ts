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
