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
