// tsc (moduleResolution "bundler", to keep extension-less imports valid for typecheck) emits
// relative specifiers with no extension. Node's ESM loader requires one at runtime, so this
// rewrites the compiled output in place after tsc -p tsconfig.build.json.
import {readdir, readFile, writeFile} from "node:fs/promises"
import {join} from "node:path"

const dist = join(import.meta.dirname, "..", "dist")

const addExtension = (source) => source.replace(
    /(from\s+|import\s*\(\s*)(["'])(\.[^"']*)\2/g,
    (whole, keyword, quote, path) => /\.[a-zA-Z]+$/.test(path)
        ? whole
        : `${keyword}${quote}${path}.js${quote}`
)

const files = await readdir(dist)
for (const file of files) {
    if (!file.endsWith(".js")) {continue}
    const path = join(dist, file)
    const original = await readFile(path, "utf8")
    const fixed = addExtension(original)
    if (fixed !== original) {await writeFile(path, fixed)}
}
