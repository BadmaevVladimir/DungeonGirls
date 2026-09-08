import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {chromium, type Browser, type Page} from "playwright"
import {createServer, type ViteDevServer} from "vite"
import {parseDocument} from "../../src/schema"
import {expand} from "../../src/expand"
import {encodeWav} from "../../src/wav"

let server: ViteDevServer
let browser: Browser
let page: Page

const sampleBytes = () => {
    const tone = Float32Array.from({length: 4800}, (_, index) => Math.sin(index * 0.1) * 0.5)
    return Array.from(encodeWav([tone, tone], 48000, "int16"))
}

const flat = () => expand(parseDocument({
    name: "Test", tempo: 120, end: "2.1",
    patterns: {r: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{name: "Lead", instrument: {device: "Vaporisateur"}, place: [{pattern: "r", at: "1.1"}]}]
}))

const flatWithNano = () => expand(parseDocument({
    name: "Test", tempo: 120, end: "2.1",
    patterns: {r: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{
        name: "Lead", instrument: {device: "Nano", sample: "test_tone"},
        place: [{pattern: "r", at: "1.1"}]
    }]
}))

const flatWithMissingSample = () => expand(parseDocument({
    name: "Test", tempo: 120, end: "2.1",
    patterns: {r: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{
        name: "Lead", instrument: {device: "Nano", sample: "nope"},
        place: [{pattern: "r", at: "1.1"}]
    }]
}))

beforeAll(async () => {
    server = await createServer({configFile: "host/vite.config.ts"})
    await server.listen(5199)
    browser = await chromium.launch({args: ["--autoplay-policy=no-user-gesture-required"]})
    page = await browser.newPage()
    page.on("pageerror", error => console.error("[page]", error.message))
    await page.goto("http://localhost:5199/", {waitUntil: "load"})
    await page.waitForFunction(() => typeof window.__odaw?.build === "function", null, {timeout: 30_000})
})

afterAll(async () => {
    await browser?.close()
    await server?.close()
})

describe("ассеты", () => {
    it("импортирует сэмпл и возвращает его в списке по имени", async () => {
        await page.evaluate(bytes => window.__odaw.importAsset("test_tone", "sample", bytes), sampleBytes())
        const assets = await page.evaluate(() => window.__odaw.listAssets())
        const entry = assets.find(asset => asset.name === "test_tone")!
        expect(entry.kind).toBe("sample")
        expect(entry.seconds).toBeCloseTo(0.1, 2)
    })

    it("собирает проект с инструментом Nano, ссылающимся на сэмпл по имени", async () => {
        await page.evaluate(bytes => window.__odaw.importAsset("test_tone", "sample", bytes), sampleBytes())
        const summary = await page.evaluate(input => window.__odaw.build(input), flatWithNano())
        expect(summary.tracks).toBe(1)
    })

    it("отказывает на ссылке в несуществующий ассет", async () => {
        await page.evaluate(() => window.__odaw.reset())
        await expect(page.evaluate(input => window.__odaw.build(input), flatWithMissingSample()))
            .rejects.toThrow(/не импортирован/)
    })

    it("экспортирует непустой .odb", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const bytes = await page.evaluate(() => window.__odaw.bundle())
        expect(bytes.length).toBeGreaterThan(100)
    })
})
