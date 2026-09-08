import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {chromium, type Browser, type Page} from "playwright"
import {createServer, type ViteDevServer} from "vite"

let server: ViteDevServer
let browser: Browser
let page: Page

beforeAll(async () => {
    server = await createServer({configFile: "host/vite.config.ts"})
    await server.listen(5199)
    browser = await chromium.launch({args: ["--autoplay-policy=no-user-gesture-required"]})
    page = await browser.newPage()
    await page.goto("http://localhost:5199/", {waitUntil: "load"})
    await page.waitForFunction(() => typeof window.__odaw?.describe === "function", null, {timeout: 30_000})
})

afterAll(async () => {
    await browser?.close()
    await server?.close()
})

describe("каталог устройств", () => {
    it("содержит инструменты, выбранные для проекта", async () => {
        const catalog = await page.evaluate(() => window.__odaw.describe())
        const names = catalog.map(device => device.name)
        expect(names).toEqual(expect.arrayContaining(
            ["Vaporisateur", "Neon", "Nano", "Playfield", "Soundfont"]))
    })

    it("отдаёт диапазоны числовых параметров", async () => {
        const catalog = await page.evaluate(() => window.__odaw.describe())
        const vaporisateur = catalog.find(device => device.name === "Vaporisateur")!
        const cutoff = vaporisateur.params.find(param => param.name === "cutoff")!
        expect(cutoff.min).toBeTypeOf("number")
        expect(cutoff.max).toBeGreaterThan(cutoff.min!)
    })

    it("отдаёт метки дискретных параметров", async () => {
        const catalog = await page.evaluate(() => window.__odaw.describe())
        const vaporisateur = catalog.find(device => device.name === "Vaporisateur")!
        const waveform = vaporisateur.params.find(param => param.name.endsWith("waveform"))!
        expect(waveform.values).toEqual(["Sine", "Triangle", "Sawtooth", "Square"])
    })

    it("содержит эффекты", async () => {
        const catalog = await page.evaluate(() => window.__odaw.describe())
        expect(catalog.filter(device => device.kind === "effect").length).toBeGreaterThan(5)
    })
})
