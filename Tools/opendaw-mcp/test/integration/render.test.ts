import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {chromium, type Browser, type Page} from "playwright"
import {createServer, type ViteDevServer} from "vite"
import {parseDocument} from "../../src/schema"
import {expand} from "../../src/expand"
import {decodeChannel} from "../../src/pcm"

let server: ViteDevServer
let browser: Browser
let page: Page

const flat = () => expand(parseDocument({
    name: "Test", tempo: 120, end: "5.1",
    buses: [{name: "Harmony", stem: "harmony"}],
    patterns: {riff: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}, {p: "G3", at: "1.3", d: "1/8"}]}},
    tracks: [
        {name: "Lead", instrument: {device: "Vaporisateur"}, stem: "lead",
         place: [{pattern: "riff", at: "1.1", repeat: 4}]},
        {name: "Bass", instrument: {device: "Neon"}, out: "Harmony",
         place: [{pattern: "riff", at: "1.1", repeat: 4}]}
    ]
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

describe("рендер", () => {
    it("микс даёт две дорожки каналов с ненулевым сигналом", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const result = await page.evaluate(() => window.__odaw.render({target: "mix"}))
        expect(result.names).toEqual(["mix"])
        expect(result.channels).toHaveLength(2)
        const left = decodeChannel(result.channels[0]!)
        let peak = 0
        for (const sample of left) {peak = Math.max(peak, Math.abs(sample))}
        expect(peak).toBeGreaterThan(0.001)
    })

    it("стемы дают по паре каналов на стем, имена в порядке каналов", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const result = await page.evaluate(() => window.__odaw.render({target: "stems"}))
        expect(result.channels).toHaveLength(result.names.length * 2)
        expect(result.names.slice().sort()).toEqual(["harmony", "lead"])
    })

    it("рендер диапазона короче полного и отдаёт хвост за границей", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const full = await page.evaluate(() => window.__odaw.render({target: "mix"}))
        // Движок использует range.start как позицию затравки (engine.set_position), а range.end — только
        // как верхнюю границу maxDurationSeconds (не как точку обрыва воспроизведения): диапазон вида
        // {start: 0, end: X} с X меньше natural-длины проекта рендерит бит-в-бит то же самое, что и полный
        // микс. Последний такт (11520..15360 тиков = 2 с) реально укорачивает рендер, пропуская начало.
        const ranged = await page.evaluate(() =>
            window.__odaw.render({target: "mix", range: {start: 11520, end: 15360}}))
        const fullLength = decodeChannel(full.channels[0]!).length
        const rangedLength = decodeChannel(ranged.channels[0]!).length
        expect(rangedLength).toBeLessThan(fullLength)
        // 3840 тиков при 120 bpm = 2 с = 96000 сэмплов; всё сверх — хвост затухания
        expect(rangedLength).toBeGreaterThan(96000)
    })

    it("отказывается рендерить несобранный проект", async () => {
        await page.evaluate(() => window.__odaw.reset())
        await expect(page.evaluate(() => window.__odaw.render({target: "mix"})))
            .rejects.toThrow(/не собран/)
    })
})
