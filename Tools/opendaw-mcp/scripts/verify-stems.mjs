// Приёмка набора файлов трека. Запуск из корня репозитория:
//   node Tools/opendaw-mcp/scripts/verify-stems.mjs Audio/Music Combat_jennifer drums harmony lead
import {readFile} from "node:fs/promises"
import {join} from "node:path"
import {checkStemSet, decodeWav, toleranceFor} from "../dist/verify-stems.js"

const [dir, name, ...layers] = process.argv.slice(2)
if (dir === undefined || name === undefined || layers.length === 0) {
    console.error("нужно: <каталог> <имя трека> <слой> [слой ...]")
    process.exit(2)
}

// Отсутствующий файл — самый частый сбой (микс приезжает как <name>_mix.wav и его
// переименовывают вручную). Стектрейс ENOENT про это ничего не объясняет.
const load = async path => {
    try {
        return decodeWav(new Uint8Array(await readFile(path)))
    } catch (error) {
        if (error?.code === "ENOENT") {
            console.error(`${name}: набор не принят`)
            console.error(`  - файл не найден: ${path}`)
            process.exit(1)
        }
        throw error
    }
}

const mix = await load(join(dir, `${name}.wav`))
const stems = []
for (const layer of layers) {
    stems.push({name: layer, wav: await load(join(dir, `${name}_${layer}.wav`))})
}

const tolerance = toleranceFor(mix.bitsPerSample, stems.length)
const problems = checkStemSet({mix, stems, tolerance})
if (problems.length === 0) {
    const seconds = (mix.channels[0]?.length ?? 0) / mix.sampleRate
    console.log(`${name}: набор корректен, ${layers.length} слоя, ${seconds.toFixed(2)} с, ` +
        `${mix.sampleRate} Гц, ${mix.bitsPerSample} бит, допуск ${tolerance.toExponential(2)}`)
    process.exit(0)
}
console.error(`${name}: набор не принят`)
for (const problem of problems) {console.error(`  - ${problem}`)}
process.exit(1)
