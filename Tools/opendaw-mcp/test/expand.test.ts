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
