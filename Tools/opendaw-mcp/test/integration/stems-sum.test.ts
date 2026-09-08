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
})
