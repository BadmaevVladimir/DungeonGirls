import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {chromium, type Browser, type Page} from "playwright"
import {createServer, type ViteDevServer} from "vite"
import {parseDocument} from "../../src/schema"
import {expand} from "../../src/expand"

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

describe("сборка проекта", () => {
    it("создаёт дорожки, шины, регионы и ноты", async () => {
        const summary = await page.evaluate(input => window.__odaw.build(input), flat())
        expect(summary.tracks).toBe(2)
        expect(summary.buses).toBe(1)
        expect(summary.regions).toBe(8)
        expect(summary.notes).toBe(16)
        expect(summary.seconds).toBeCloseTo(8, 1)
    })

    it("объявляет стемы для помеченных дорожек и шин", async () => {
        const summary = await page.evaluate(input => window.__odaw.build(input), flat())
        expect(summary.stems.map(stem => stem.fileName).sort()).toEqual(["harmony", "lead"])
    })

    it("inspect отдаёт последнюю сводку", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        expect(await page.evaluate(() => window.__odaw.inspect())).toMatchObject({tracks: 2, regions: 8})
    })

    it("reset очищает проект", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        await page.evaluate(() => window.__odaw.reset())
        expect(await page.evaluate(() => window.__odaw.inspect())).toBeNull()
    })

    it("повторная сборка не накапливает дорожки", async () => {
        await page.evaluate(input => window.__odaw.build(input), flat())
        const second = await page.evaluate(input => window.__odaw.build(input), flat())
        expect(second.tracks).toBe(2)
        expect(second.regions).toBe(8)
    })

    it("применяет перечисляемый параметр инструмента, а не молча игнорирует его", async () => {
        const withWaveform = (waveform: string) => expand(parseDocument({
            name: "Test", tempo: 120, end: "2.1",
            patterns: {r: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
            tracks: [{
                name: "Lead",
                instrument: {device: "Vaporisateur", params: {"oscillators[0].waveform": waveform}},
                place: [{pattern: "r", at: "1.1"}]
            }]
        }))
        // Допустимая метка должна пройти без ошибки.
        await expect(page.evaluate(input => window.__odaw.build(input), withWaveform("Sawtooth")))
            .resolves.toMatchObject({tracks: 1})
        // Заведомо недопустимая метка обязана бросить, а не тихо остаться дефолтом.
        await expect(page.evaluate(input => window.__odaw.build(input), withWaveform("Noise")))
            .rejects.toThrow()
    })
})
