import {describe, expect, it} from "vitest"
import {spawn} from "node:child_process"
import {mkdtemp} from "node:fs/promises"
import {tmpdir} from "node:os"
import {join} from "node:path"

// Ловит регрессии scripts/fix-dist-esm.mjs: если оно пропустит расширение-less импорт,
// это всплывёт только при живом запуске dist/, а не на tsc или vitest. Требует, чтобы
// npm run build уже отработал — иначе dist/index.js не существует.
describe("собранный сервер (dist/index.js)", () => {
    it("стартует и не падает на ESM-резолве импортов", async () => {
        const outputDir = await mkdtemp(join(tmpdir(), "odaw-dist-"))
        const child = spawn(process.execPath, ["dist/index.js"], {
            stdio: ["pipe", "pipe", "pipe"],
            env: {...process.env, OPENDAW_OUTPUT_DIR: outputDir}
        })
        let stderr = ""
        child.stderr.on("data", chunk => {stderr += String(chunk)})
        const exitedEarly = await new Promise<boolean>(resolveExit => {
            const timer = setTimeout(() => resolveExit(false), 2_000)
            child.once("exit", () => {clearTimeout(timer); resolveExit(true)})
        })
        expect(exitedEarly, `процесс завершился раньше времени, stderr:\n${stderr}`).toBe(false)
        expect(child.exitCode).toBeNull()
        child.kill()
    }, 20_000)
})
