import {mkdir, mkdtemp, rm, writeFile} from "node:fs/promises"
import {tmpdir} from "node:os"
import {join, resolve} from "node:path"
import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {isWithinRoot, serveStatic} from "../src/bridge"

describe("isWithinRoot: граница проверки корня", () => {
    it("пропускает сам корень и файлы внутри него", () => {
        expect(isWithinRoot(resolve("host/dist"), resolve("host/dist"))).toBe(true)
        expect(isWithinRoot(resolve("host/dist/index.html"), resolve("host/dist"))).toBe(true)
    })

    it("отклоняет соседнюю папку с общим префиксом имени (dist-evil vs dist)", () => {
        const root = resolve("host/dist")
        const sibling = resolve("host/dist-evil/secret")
        // Старая проверка file.startsWith(rootResolved) пропускала этот путь: строка
        // "...\\dist-evil\\secret" начинается с "...\\dist".
        expect(sibling.startsWith(root)).toBe(true)
        expect(isWithinRoot(sibling, root)).toBe(false)
    })

    it("отклоняет путь на один уровень выше корня", () => {
        const root = resolve("host/dist")
        expect(isWithinRoot(resolve(root, ".."), root)).toBe(false)
    })
})

// root/index.html — обычный файл внутри корня; root-evil/secret.txt — недостижимый снаружи файл,
// используется для проверки, что реальный HTTP-путь через сервер туда не попадает.
let root: string
let base: string
let close: () => Promise<void>

beforeAll(async () => {
    const parent = await mkdtemp(join(tmpdir(), "odaw-static-"))
    root = join(parent, "root")
    await mkdir(root, {recursive: true})
    await writeFile(join(root, "index.html"), "<html>ok</html>")
    const evilSibling = join(parent, "root-evil")
    await mkdir(evilSibling, {recursive: true})
    await writeFile(join(evilSibling, "secret.txt"), "секрет соседней папки")
    const {server, port} = await serveStatic(root)
    base = `http://127.0.0.1:${port}`
    close = () => new Promise<void>(done => server.close(() => done()))
}, 30_000)

afterAll(async () => {
    await close?.()
    await rm(join(root, ".."), {recursive: true, force: true})
})

describe("serveStatic: HTTP-запросы через guard", () => {
    it("отдаёт обычный файл внутри корня", async () => {
        const response = await fetch(`${base}/index.html`)
        expect(response.status).toBe(200)
        expect(await response.text()).toBe("<html>ok</html>")
    })

    it("не отдаёт содержимое соседней папки при ../-обходе", async () => {
        const response = await fetch(`${base}/../root-evil/secret.txt`)
        expect(response.status).not.toBe(200)
    })

    it("не отдаёт содержимое соседней папки при процентно-закодированном обходе (%2e%2e)", async () => {
        const response = await fetch(`${base}/%2e%2e/root-evil/secret.txt`)
        expect(response.status).not.toBe(200)
    })

    it("не отдаёт содержимое соседней папки при процентно-закодированном слэше (%2f)", async () => {
        const response = await fetch(`${base}/%2e%2e%2froot-evil%2fsecret.txt`)
        expect(response.status).not.toBe(200)
    })
})
