import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {HostBridge} from "../../src/bridge"
import {parseDocument} from "../../src/schema"
import {expand} from "../../src/expand"
import {decodeChannel} from "../../src/pcm"

let bridge: HostBridge

const document = {
    name: "Sum", tempo: 120, end: "3.1",
    buses: [{name: "Low", stem: "low"}],
    patterns: {
        a: {length: "1b", notes: [{p: "C4", at: "1.1", d: "1/4"}]},
        b: {length: "1b", notes: [{p: "C2", at: "1.1", d: "1/2"}]}
    },
    tracks: [
        {name: "Lead", instrument: {device: "Vaporisateur"}, stem: "lead",
         place: [{pattern: "a", at: "1.1", repeat: 2}]},
        {name: "Bass", instrument: {device: "Neon"}, out: "Low",
         place: [{pattern: "b", at: "1.1", repeat: 2}]}
    ]
}

beforeAll(async () => {bridge = await HostBridge.create("host/dist")}, 120_000)
afterAll(async () => {await bridge?.close()})

// Отдельный документ для проверки идентичности стемов: источники разнесены по тактам,
// так что стем "lead" звучит только в такте 1, а "bass" (через шину) только в такте 2.
const identityDocument = {
    name: "Identity", tempo: 120, end: "3.1",
    buses: [{name: "Low", stem: "bass"}],
    patterns: {
        a: {length: "1b", notes: [{p: "C4", at: "1.1", d: "1/4"}]},
        b: {length: "1b", notes: [{p: "C2", at: "1.1", d: "1/2"}]}
    },
    tracks: [
        {name: "Lead", instrument: {device: "Vaporisateur"}, stem: "lead",
         place: [{pattern: "a", at: "1.1", repeat: 1}]},
        {name: "Bass", instrument: {device: "Neon"}, out: "Low",
         place: [{pattern: "b", at: "2.1", repeat: 1}]}
    ]
}

// RMS по обоим каналам стема на отрезке [fromSeconds, toSeconds).
const rmsEnergy = (left: Float32Array, right: Float32Array, sampleRate: number,
                    fromSeconds: number, toSeconds: number): number => {
    const from = Math.round(fromSeconds * sampleRate)
    const to = Math.round(toSeconds * sampleRate)
    let sumSquares = 0
    let count = 0
    for (let frame = from; frame < to; frame++) {
        const l = left[frame] ?? 0
        const r = right[frame] ?? 0
        sumSquares += l * l + r * r
        count += 2
    }
    return Math.sqrt(sumSquares / count)
}

describe("инвариант стемов", () => {
    it("сумма стемов совпадает с миксом", async () => {
        await bridge.call("build", expand(parseDocument(document)))
        const mix = await bridge.call<{channels: string[]}>("render", {target: "mix"})
        const stems = await bridge.call<{channels: string[], names: string[]}>("render", {target: "stems"})
        expect(stems.names.length).toBe(2)
        const mixChannels = mix.channels.map(decodeChannel)
        const stemChannels = stems.channels.map(decodeChannel)
        const frames = mixChannels[0]!.length
        for (let channel = 0; channel < 2; channel++) {
            let worst = 0
            for (let frame = 0; frame < frames; frame++) {
                let sum = 0
                for (let stem = 0; stem < stems.names.length; stem++) {
                    sum += stemChannels[stem * 2 + channel]![frame] ?? 0
                }
                worst = Math.max(worst, Math.abs(sum - mixChannels[channel]![frame]!))
            }
            expect(worst).toBeLessThan(1e-3)
        }
    }, 120_000)

    it("стем закреплён за своим источником, а не просто суммируется", async () => {
        await bridge.call("build", expand(parseDocument(identityDocument)))
        const stems = await bridge.call<{channels: string[], names: string[], sampleRate: number}>(
            "render", {target: "stems"})
        expect(stems.names.length).toBe(2)
        const leadIndex = stems.names.indexOf("lead")
        const bassIndex = stems.names.indexOf("bass")
        expect(leadIndex).toBeGreaterThanOrEqual(0)
        expect(bassIndex).toBeGreaterThanOrEqual(0)
        const channels = stems.channels.map(decodeChannel)
        const sampleRate = stems.sampleRate
        // Такт 2с при 120 BPM: окна взяты с отступом от границ такта и от атаки/хвоста ноты.
        const bar1 = {from: 0.3, to: 1.5}
        const bar2 = {from: 2.3, to: 3.5}
        const energy = (index: number, window: {from: number, to: number}) => rmsEnergy(
            channels[index * 2]!, channels[index * 2 + 1]!, sampleRate, window.from, window.to)
        const leadBar1 = energy(leadIndex, bar1)
        const leadBar2 = energy(leadIndex, bar2)
        const bassBar1 = energy(bassIndex, bar1)
        const bassBar2 = energy(bassIndex, bar2)
        const leadRatio = leadBar1 / leadBar2
        const bassRatio = bassBar2 / bassBar1
        console.log(`lead: bar1=${leadBar1} bar2=${leadBar2} ratio=${leadRatio}; ` +
            `bass: bar1=${bassBar1} bar2=${bassBar2} ratio=${bassRatio}`)
        expect(leadRatio).toBeGreaterThan(3)
        expect(bassRatio).toBeGreaterThan(3)
    }, 120_000)
})
