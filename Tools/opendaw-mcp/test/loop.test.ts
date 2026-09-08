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
