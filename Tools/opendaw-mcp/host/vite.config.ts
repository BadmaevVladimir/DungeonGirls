import {defineConfig} from "vite"
import {createReadStream, existsSync, readdirSync, readFileSync} from "node:fs"
import {resolve, dirname} from "node:path"
import {fileURLToPath} from "node:url"

const __dirname = dirname(fileURLToPath(import.meta.url))

// Обе строчки обязательны. Без exclude+dedupe vite держит две копии studio-boxes и
// Project.new падает на "AudioUnitBox is not instance of AudioUnitBox".
const OPENDAW = [
    "@opendaw/studio-core", "@opendaw/studio-core-wasm", "@opendaw/studio-adapters",
    "@opendaw/studio-boxes", "@opendaw/studio-enums",
    "@opendaw/lib-std", "@opendaw/lib-dsp", "@opendaw/lib-box", "@opendaw/lib-dom",
    "@opendaw/lib-runtime", "@opendaw/lib-jsx", "@opendaw/lib-midi", "@opendaw/lib-fusion",
    "@opendaw/lib-xml", "@opendaw/lib-dawproject"
]

const COI = {
    "Cross-Origin-Opener-Policy": "same-origin",
    "Cross-Origin-Embedder-Policy": "require-corp",
    "Cross-Origin-Resource-Policy": "cross-origin"
}

// Движок просит ${wasmUrl}/wasm/engine.wasm, поэтому корень отдачи — dist/, не dist/wasm/.
const wasmDir = resolve(__dirname, "../node_modules/@opendaw/studio-core-wasm/dist")

export default defineConfig({
    root: __dirname,
    base: "./",
    build: {outDir: resolve(__dirname, "dist"), emptyOutDir: true},
    server: {port: 5199, headers: COI},
    preview: {headers: COI},
    worker: {format: "es"},
    optimizeDeps: {exclude: OPENDAW},
    resolve: {dedupe: OPENDAW},
    plugins: [{
        name: "wasm-engine-assets",
        configureServer(server) {
            server.middlewares.use("/wasm-engine", (req, res, next) => {
                const rel = decodeURIComponent((req.url ?? "/").split("?")[0]!).replace(/^\/+/, "")
                const file = resolve(wasmDir, rel)
                if (!file.startsWith(wasmDir) || !existsSync(file)) {return next()}
                res.setHeader("Content-Type", "application/wasm")
                res.setHeader("Cross-Origin-Resource-Policy", "cross-origin")
                createReadStream(file).pipe(res)
            })
        },
        generateBundle() {
            const walk = (dir: string): string[] =>
                readdirSync(resolve(wasmDir, dir), {withFileTypes: true}).flatMap(entry =>
                    entry.isDirectory() ? walk(`${dir}/${entry.name}`) : [`${dir}/${entry.name}`])
            for (const name of walk("wasm").filter(name => name.endsWith(".wasm"))) {
                this.emitFile({type: "asset", fileName: `wasm-engine/${name}`,
                    source: readFileSync(resolve(wasmDir, name))})
            }
        }
    }]
})
