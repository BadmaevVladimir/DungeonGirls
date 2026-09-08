import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {mkdtemp, rm, writeFile} from "node:fs/promises"
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

beforeAll(async () => {
    outputDir = await mkdtemp(join(tmpdir(), "odaw-out-"))
    scratchDir = await mkdtemp(join(tmpdir(), "odaw-large-"))
    const server = createServer({hostDir: "host/dist", outputDir})
    const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair()
    client = new Client({name: "test", version: "0"})
    await Promise.all([server.connect(serverTransport), client.connect(clientTransport)])
}, 180_000)

afterAll(async () => {
    await client?.close()
    await rm(scratchDir, {recursive: true, force: true})
})

describe("импорт большого файла", () => {
    it("импортирует ~25MB WAV через HTTP, не пронося байты через page.evaluate как JSON-массив",
        async () => {
            // Достаточно большой, чтобы старый путь (Array.from(bytes) через page.evaluate)
            // валил Node по памяти; тишина — самый дешёвый способ нагнать нужный размер.
            const seconds = 140
            const sampleRate = 48000
            const frames = seconds * sampleRate
            const silence = new Float32Array(frames)
            const wav = encodeWav([silence, silence], sampleRate, "int16")
            expect(wav.length).toBeGreaterThan(20 * 1024 * 1024)
            const path = join(scratchDir, "big-silence.wav")
            await writeFile(path, wav)

            const info = await call("import_asset", {path, name: "big_silence"})
            expect(info.kind).toBe("sample")
            expect(info.seconds).toBeCloseTo(seconds, 0)

            const assets = await call("list_assets")
            const entry = assets.find((asset: {name: string}) => asset.name === "big_silence")
            expect(entry).toBeDefined()
        }, 120_000)
})
