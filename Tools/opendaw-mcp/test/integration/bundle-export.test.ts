import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {mkdtemp, readFile, rm, stat, unlink, writeFile} from "node:fs/promises"
import {tmpdir} from "node:os"
import {join} from "node:path"
import {Client} from "@modelcontextprotocol/sdk/client/index.js"
import {InMemoryTransport} from "@modelcontextprotocol/sdk/inMemory.js"
import {createServer} from "../../src/server"
import {encodeWav} from "../../src/wav"

let client: Client
let outputDir: string
let scratchDir: string
const call = async (name: string, args: Record<string, unknown> = {}) => {
    const result = await client.callTool({name, arguments: args})
    if (result.isError === true) {throw new Error(JSON.stringify(result.content))}
    return JSON.parse((result.content as Array<{text: string}>)[0]!.text)
}

const document = {
    name: "BundleTest", tempo: 120, end: "2.1",
    patterns: {r: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{name: "Lead", instrument: {device: "Nano", sample: "big_noise"},
              place: [{pattern: "r", at: "1.1"}]}]
}

beforeAll(async () => {
    outputDir = await mkdtemp(join(tmpdir(), "odaw-out-"))
    scratchDir = await mkdtemp(join(tmpdir(), "odaw-bundle-"))
    const server = createServer({hostDir: "host/dist", outputDir})
    const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair()
    client = new Client({name: "test", version: "0"})
    await Promise.all([server.connect(serverTransport), client.connect(clientTransport)])
}, 180_000)

afterAll(async () => {
    await client?.close()
    await rm(scratchDir, {recursive: true, force: true})
})

describe("экспорт большого бандла", () => {
    it("экспортирует .odb, содержащий большой импортированный сэмпл, не вешая Node",
        async () => {
            // Шум, а не тишина: DEFLATE внутри ProjectBundle.encode схлопывает тишину до килобайт,
            // так что старый баг (Array.from(bytes) через page.evaluate) на ней бы не воспроизвёлся.
            const seconds = 140
            const sampleRate = 48000
            const frames = seconds * sampleRate
            const noise = (seed: number) => {
                const out = new Float32Array(frames)
                let state = seed
                for (let i = 0; i < frames; i++) {
                    state = (state * 1103515245 + 12345) & 0x7fffffff
                    out[i] = (state / 0x7fffffff) * 2 - 1
                }
                return out
            }
            const wav = encodeWav([noise(1), noise(2)], sampleRate, "int16")
            expect(wav.length).toBeGreaterThan(20 * 1024 * 1024)
            const inputPath = join(scratchDir, "big-noise.wav")
            await writeFile(inputPath, wav)

            await call("import_asset", {path: inputPath, name: "big_noise"})
            await unlink(inputPath)

            const summary = await call("build_arrangement", {document})
            expect(summary.regions).toBe(1)

            const target = join(outputDir, "big.odb")
            const result = await call("export_bundle", {path: target})
            expect(result.path).toBe(target)

            const info = await stat(target)
            expect(info.size).toBeGreaterThan(15 * 1024 * 1024)
            expect(info.size).toBeLessThan(40 * 1024 * 1024)

            const bytes = await readFile(target)
            expect(bytes.subarray(0, 4).toString("latin1")).toBe("PK\x03\x04")
        }, 120_000)
})
