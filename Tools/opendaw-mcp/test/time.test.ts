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
