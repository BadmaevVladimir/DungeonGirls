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

const flatWithPlayfield = () => expand(parseDocument({
    name: "Test", tempo: 120, end: "2.1",
    patterns: {r: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{
        name: "Drums", instrument: {device: "Playfield", slots: {C1: "test_tone"}},
        place: [{pattern: "r", at: "1.1"}]
    }]
}))

// Два инструмента на один и тот же ассет. File-box адресуется UUID ассета, поэтому
// второй create сталкивался с уже существующим box и ронял всю сборку — причём падал
// уже откат транзакции, маскируя настоящую причину.
const flatWithTwoNano = () => expand(parseDocument({
    name: "Test", tempo: 120, end: "2.1",
    patterns: {r: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [
        {name: "One", instrument: {device: "Nano", sample: "test_tone"},
         place: [{pattern: "r", at: "1.1"}]},
        {name: "Two", instrument: {device: "Nano", sample: "test_tone"},
         place: [{pattern: "r", at: "1.1"}]}
    ]
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
        await page.evaluate(bytes => {
            const url = URL.createObjectURL(new Blob([new Uint8Array(bytes)]))
            return window.__odaw.importAsset("test_tone", "sample", url)
        }, sampleBytes())
        const assets = await page.evaluate(() => window.__odaw.listAssets())
        const entry = assets.find(asset => asset.name === "test_tone")!
        expect(entry.kind).toBe("sample")
        expect(entry.seconds).toBeCloseTo(0.1, 2)
    })

    it("собирает проект с инструментом Nano, ссылающимся на сэмпл по имени", async () => {
        await page.evaluate(bytes => {
            const url = URL.createObjectURL(new Blob([new Uint8Array(bytes)]))
            return window.__odaw.importAsset("test_tone", "sample", url)
        }, sampleBytes())
        const summary = await page.evaluate(input => window.__odaw.build(input), flatWithNano())
        expect(summary.tracks).toBe(1)
    })

    it("собирает Playfield со слотом по имени ноты и кладёт его на правильный MIDI-номер", async () => {
        await page.evaluate(bytes => {
            const url = URL.createObjectURL(new Blob([new Uint8Array(bytes)]))
            return window.__odaw.importAsset("test_tone", "sample", url)
        }, sampleBytes())
        const summary = await page.evaluate(input => window.__odaw.build(input), flatWithPlayfield())
        expect(summary.tracks).toBe(1)
        // Соглашение openDAW: 60 = C3, значит C1 = 36. Number("C1") дал бы NaN.
        expect(summary.playfieldNotes).toEqual([36])
    })

    it("собирает две дорожки, ссылающиеся на один и тот же ассет", async () => {
        await page.evaluate(() => window.__odaw.reset())
        await page.evaluate(bytes => {
            const url = URL.createObjectURL(new Blob([new Uint8Array(bytes)]))
            return window.__odaw.importAsset("test_tone", "sample", url)
        }, sampleBytes())
        const summary = await page.evaluate(input => window.__odaw.build(input), flatWithTwoNano())
        expect(summary.tracks).toBe(2)
    })

    it("отказывает на ссылке в несуществующий ассет", async () => {
        await page.evaluate(() => window.__odaw.reset())
        await expect(page.evaluate(input => window.__odaw.build(input), flatWithMissingSample()))
            .rejects.toThrow(/не импортирован/)
    })

    it("упавшая на середине сборка не стирает предыдущий рабочий проект", async () => {
        // Хороший проект собран и виден в inspect.
        const good = await page.evaluate(input => window.__odaw.build(input), flat())
        expect(await page.evaluate(() => window.__odaw.inspect())).toMatchObject(good)
        // Сборка, падающая внутри браузера (ссылка на неимпортированный ассет) не должна
        // тронуть уже стоящий проект — раньше resetProject() рвал его раньше времени.
        await expect(page.evaluate(input => window.__odaw.build(input), flatWithMissingSample()))
            .rejects.toThrow(/не импортирован/)
        expect(await page.evaluate(() => window.__odaw.inspect())).toMatchObject(good)
    })

    it("экспортирует .odb с сигнатурой zip-архива (PK\\x03\\x04)", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const base64 = await page.evaluate(() => window.__odaw.bundle())
        const bytes = Buffer.from(base64, "base64")
        expect(bytes.length).toBeGreaterThan(100)
        expect(Array.from(bytes.subarray(0, 4))).toEqual([0x50, 0x4b, 0x03, 0x04])
    })

    it("bundle() реально дёргает провайдер сэмплов (Nano ссылается на AudioFileBox)", async () => {
        await page.evaluate(() => window.__odaw.reset())
        await page.evaluate(bytes => {
            const url = URL.createObjectURL(new Blob([new Uint8Array(bytes)]))
            return window.__odaw.importAsset("test_tone", "sample", url)
        }, sampleBytes())
        await page.evaluate(input => window.__odaw.build(input), flatWithNano())
        const base64 = await page.evaluate(() => window.__odaw.bundle())
        const bytes = Buffer.from(base64, "base64")
        expect(Array.from(bytes.subarray(0, 4))).toEqual([0x50, 0x4b, 0x03, 0x04])
    })
})
