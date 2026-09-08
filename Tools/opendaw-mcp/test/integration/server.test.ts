import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {mkdtemp, readFile} from "node:fs/promises"
import {tmpdir} from "node:os"
import {join} from "node:path"
import {Client} from "@modelcontextprotocol/sdk/client/index.js"
import {InMemoryTransport} from "@modelcontextprotocol/sdk/inMemory.js"
import {createServer} from "../../src/server"

let client: Client
let outputDir: string
const call = async (name: string, args: Record<string, unknown> = {}) => {
    const result = await client.callTool({name, arguments: args})
    if (result.isError === true) {throw new Error(JSON.stringify(result.content))}
    return JSON.parse((result.content as Array<{text: string}>)[0]!.text)
}

const document = {
    name: "SmokeTheme", tempo: 120, end: "5.1",
    patterns: {riff: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{name: "Lead", instrument: {device: "Vaporisateur"}, stem: "lead",
              place: [{pattern: "riff", at: "1.1", repeat: 4}]}]
}

beforeAll(async () => {
    outputDir = await mkdtemp(join(tmpdir(), "odaw-"))
    const server = createServer({hostDir: "host/dist", outputDir})
    const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair()
    client = new Client({name: "test", version: "0"})
    await Promise.all([server.connect(serverTransport), client.connect(clientTransport)])
}, 180_000)

afterAll(async () => {await client?.close()})

describe("MCP-сервер", () => {
    it("объявляет ровно восемь инструментов", async () => {
        const {tools} = await client.listTools()
        expect(tools.map(tool => tool.name).sort()).toEqual([
            "build_arrangement", "describe_devices", "export_bundle", "import_asset",
            "inspect_project", "list_assets", "render", "reset_project"
        ])
    })

    it("отклоняет негодный документ до касания браузера, с путями до полей", async () => {
        await expect(call("build_arrangement", {document: {...document, tracks: []}}))
            .rejects.toThrow(/tracks/)
    })

    it("отклоняет неизвестный параметр устройства", async () => {
        const broken = structuredClone(document)
        broken.tracks[0]!.instrument = {device: "Vaporisateur", params: {cutof: 1}} as never
        await expect(call("build_arrangement", {document: broken})).rejects.toThrow(/cutof/)
    })

    it("строит и рендерит микс в WAV", async () => {
        const summary = await call("build_arrangement", {document})
        expect(summary.regions).toBe(4)
        expect(summary.warnings).toEqual([])
        const result = await call("render", {target: "mix", name: "smoke"})
        expect(result.files).toHaveLength(1)
        expect(result.files[0]!.peak).toBeGreaterThan(0.001)
        const bytes = await readFile(result.files[0]!.path)
        expect(bytes.subarray(0, 4).toString()).toBe("RIFF")
    })

    it("рендерит стемы отдельными файлами", async () => {
        await call("build_arrangement", {document})
        const result = await call("render", {target: "stems", name: "smoke"})
        expect(result.files.map((file: {path: string}) => file.path.endsWith("lead.wav"))).toContain(true)
    })

    it("рендерит луп ровно нужной длины", async () => {
        await call("build_arrangement", {document: {...document, loop: {start: "1.1", end: "5.1"}}})
        const result = await call("render", {target: "mix", loop: true, name: "loop"})
        // 4 такта при 120 bpm = 8 с
        expect(result.files[0]!.seconds).toBeCloseTo(8, 2)
    })

    it("отказывается от лупа, если его конец не совпадает с концом документа", async () => {
        // end документа 6.1 (5 тактов), но луп заканчивается на 5.1: после лупа ещё есть
        // реальный материал, а не только хвост затухания — engine.ExportRange.end его не отрежет.
        await call("build_arrangement", {document: {...document, end: "6.1", loop: {start: "1.1", end: "5.1"}}})
        await expect(call("render", {target: "mix", loop: true, name: "bad-loop"}))
            .rejects.toThrow(/(конец|end)/i)
    })

    it("отказывается от лупа без range и без поля loop в документе", async () => {
        await call("build_arrangement", {document})
        await expect(call("render", {target: "mix", loop: true, name: "no-loop"}))
            .rejects.toThrow()
    })
})
