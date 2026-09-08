import {describe, expect, it} from "vitest"
import {decodeChannel} from "../src/pcm"

// Mirrors the browser-side encoding shape (base64 over a Float32Array's raw bytes) without
// depending on a browser: Buffer.from(...).toString("base64") produces the same bytes btoa would.
const encode = (samples: ReadonlyArray<number>): string =>
    Buffer.from(new Float32Array(samples).buffer).toString("base64")

describe("decodeChannel", () => {
    it("round-trips a known Float32Array byte-exactly", () => {
        const samples = [0, 1, -1, 0.5, -0.75, 0.000123, -0.999999]
        const decoded = decodeChannel(encode(samples))
        expect(decoded.length).toBe(samples.length)
        samples.forEach((value, index) => expect(decoded[index]).toBe(Math.fround(value)))
    })

    it("decodes an empty channel to length 0", () => {
        const decoded = decodeChannel(encode([]))
        expect(decoded.length).toBe(0)
    })

    it("round-trips a few thousand samples across the chunking boundary", () => {
        const count = 5000
        const samples = Array.from({length: count}, (_, i) => Math.sin(i * 0.01) * 0.5)
        const decoded = decodeChannel(encode(samples))
        expect(decoded.length).toBe(count)
        for (let i = 0; i < count; i++) {
            expect(decoded[i]).toBe(Math.fround(samples[i]!))
        }
    })
})
