import {createServer, type Server} from "node:http"
import {createReadStream} from "node:fs"
import {stat} from "node:fs/promises"
import {extname, join, normalize, resolve} from "node:path"
import {chromium, type Browser, type Page} from "playwright"

export class HostLostError extends Error {}

const LOST_MESSAGE = "страница openDAW перезапущена, состояние проекта потеряно: вызовите build_arrangement заново"

const TYPES: Record<string, string> = {
    ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript",
    ".css": "text/css", ".json": "application/json", ".wasm": "application/wasm"
}

// COOP/COEP обязательны: движку нужен SharedArrayBuffer, а он есть только на
// cross-origin isolated странице.
const serveStatic = (root: string): Promise<{server: Server, port: number}> => new Promise(done => {
    const rootResolved = resolve(root)
    const server = createServer(async (req, res) => {
        const requested = decodeURIComponent((req.url ?? "/").split("?")[0]!)
        const relative = normalize(requested === "/" ? "/index.html" : requested).replace(/^([/\\])+/, "")
        const file = resolve(rootResolved, relative)
        res.setHeader("Cross-Origin-Opener-Policy", "same-origin")
        res.setHeader("Cross-Origin-Embedder-Policy", "require-corp")
        res.setHeader("Cross-Origin-Resource-Policy", "cross-origin")
        if (!file.startsWith(rootResolved)) {res.writeHead(403).end(); return}
        const info = await stat(file).catch(() => null)
        if (info === null || !info.isFile()) {res.writeHead(404).end(); return}
        res.setHeader("Content-Type", TYPES[extname(file)] ?? "application/octet-stream")
        createReadStream(file).pipe(res)
    })
    server.listen(0, "127.0.0.1", () => done({server, port: (server.address() as {port: number}).port}))
})

const openPage = async (browser: Browser, port: number): Promise<Page> => {
    const page = await browser.newPage()
    await page.goto(`http://127.0.0.1:${port}/`, {waitUntil: "load"})
    await page.waitForFunction(() => typeof (window as never as {__odaw?: unknown}).__odaw === "object",
        null, {timeout: 60_000})
    return page
}

export class HostBridge {
    static async create(hostDir: string): Promise<HostBridge> {
        const root = resolve(hostDir)
        await stat(join(root, "index.html")).catch(() => {
            throw new Error(`страница не собрана: нет ${join(root, "index.html")}, выполните npm run build:host`)
        })
        const {server, port} = await serveStatic(root)
        const browser = await chromium.launch({args: ["--autoplay-policy=no-user-gesture-required"]})
        return new HostBridge(server, browser, await openPage(browser, port), port)
    }

    readonly #server: Server
    readonly #browser: Browser
    readonly #port: number
    #page: Page

    private constructor(server: Server, browser: Browser, page: Page, port: number) {
        this.#server = server
        this.#browser = browser
        this.#page = page
        this.#port = port
    }

    async call<T>(name: string, argument?: unknown): Promise<T> {
        if (this.#page.isClosed()) {
            // Страница уже умерла до этого вызова: поднимаем заново, но этот call обязан
            // сообщить о потере состояния, а не тихо выполниться на пустом проекте.
            await this.#revive()
            throw new HostLostError(LOST_MESSAGE)
        }
        try {
            return await this.#page.evaluate(([method, arg]) => {
                const api = (window as never as {__odaw: Record<string, (value?: unknown) => unknown>}).__odaw
                const fn = api[method as string]
                if (fn === undefined) {throw new Error(`нет примитива "${method}"`)}
                return Promise.resolve(fn(arg)) as Promise<unknown>
            }, [name, argument] as const) as T
        } catch (error) {
            // Упавшую страницу поднимаем, но молчать нельзя: проект жил в её памяти.
            if (this.#page.isClosed() || !this.#browser.isConnected()) {
                await this.#revive()
                throw new HostLostError(LOST_MESSAGE)
            }
            throw error
        }
    }

    async #revive(): Promise<void> {
        if (!this.#browser.isConnected()) {
            throw new HostLostError("браузер закрыт и не может быть поднят в этой сессии")
        }
        this.#page = await openPage(this.#browser, this.#port)
    }

    // Только для тестов: смоделировать смерть страницы.
    async killPageForTest(): Promise<void> {await this.#page.close()}

    async close(): Promise<void> {
        await this.#browser.close()
        await new Promise<void>(done => this.#server.close(() => done()))
    }
}
