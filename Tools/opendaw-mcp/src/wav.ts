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
