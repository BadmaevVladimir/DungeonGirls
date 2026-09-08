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
    page.on("pageerror", error => console.error("[page]", error.message))
    await page.goto("http://localhost:5199/", {waitUntil: "load"})
    await page.waitForFunction(() => typeof window.__odaw?.status === "function", null, {timeout: 30_000})
})

afterAll(async () => {
    await browser?.close()
    await server?.close()
})

describe("загрузка openDAW в headless-Chromium", () => {
    it("страница cross-origin isolated и движок поднялся", async () => {
        const status = await page.evaluate(() => window.__odaw.status())
        expect(status.crossOriginIsolated).toBe(true)
        expect(status.wasmReady).toBe(true)
        expect(status.sampleRate).toBe(48000)
    })
})
