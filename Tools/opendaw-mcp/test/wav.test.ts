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
