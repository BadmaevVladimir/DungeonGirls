import {writeFile, mkdir} from "node:fs/promises"
import {basename, extname, join, resolve} from "node:path"
import {McpServer} from "@modelcontextprotocol/sdk/server/mcp.js"
import {z} from "zod"
import {HostBridge, HostLostError} from "./bridge"
import {DocumentError, parseDocument} from "./schema"
import {validateDevices, type Catalog} from "./validate-devices"
import {expand} from "./expand"
import {foldTail} from "./loop"
import {writeWav, type WavFormat} from "./wav"
import {decodeChannel} from "./pcm"
import {parsePosition, parseSignature, ticksToSeconds} from "./time"

type Options = {hostDir: string, outputDir: string}
// PCM едет по проводу base64: минуты стерео-микса в JSON-числах весили бы гигабайты.
type RenderReply = {sampleRate: number, channels: ReadonlyArray<string>, names: ReadonlyArray<string>}

const ok = (value: unknown) => ({content: [{type: "text" as const, text: JSON.stringify(value, null, 2)}]})
const fail = (text: string) => ({isError: true, content: [{type: "text" as const, text}]})

// Каждый инструмент оборачивается этим: DocumentError и HostLostError — ожидаемые исходы,
// а не аварии, и должны доезжать до вызывающего читаемым текстом.
const guard = async (action: () => Promise<unknown>) => {
    try {
        return ok(await action())
    } catch (error) {
        if (error instanceof DocumentError) {
            return fail(error.issues.map(issue => `${issue.path}: ${issue.message}`).join("\n"))
        }
        if (error instanceof HostLostError) {return fail(error.message)}
        return fail(error instanceof Error ? `${error.message}\n${error.stack ?? ""}` : String(error))
    }
}

export const createServer = (options: Options): McpServer => {
    const server = new McpServer({name: "opendaw", version: "0.1.0"})
    // Браузер поднимается лениво на первом инструменте, которому он нужен, и живёт до конца сессии.
    let bridge: HostBridge | undefined
    const host = async (): Promise<HostBridge> => bridge ??= await HostBridge.create(options.hostDir)
    let tempo = 120
    let signature = "4/4"
    // Луп и конец документа запоминаются при сборке: render {loop: true} без range берёт их.
    let documentLoop: {start: number, end: number} | undefined
    let documentEnd: number | undefined

    server.registerTool("describe_devices",
        {description: "Каталог инструментов и эффектов openDAW с параметрами, диапазонами и единицами.",
         inputSchema: {}},
        () => guard(async () => (await host()).call<Catalog>("describe")))

    server.registerTool("build_arrangement",
        {description: "Собрать проект из документа аранжировки. Заменяет предыдущий проект целиком.",
         inputSchema: {document: z.unknown()}},
        ({document}) => guard(async () => {
            const doc = parseDocument(document)
            const catalog = await (await host()).call<Catalog>("describe")
            const issues = validateDevices(doc, catalog)
            // Отказ до касания проекта: наполовину построенная аранжировка звучит молча неправильно.
            if (issues.length > 0) {throw new DocumentError(issues)}
            const flat = expand(doc)
            const result = await (await host()).call("build", flat)
            // Кэши обновляются только после успешной сборки: если build упал, документ не стал живым.
            tempo = doc.tempo
            signature = doc.signature
            documentLoop = flat.loop
            documentEnd = flat.end
            return result
        }))

    server.registerTool("inspect_project",
        {description: "Сводка текущего проекта или null, если он не собран.", inputSchema: {}},
        () => guard(async () => (await host()).call("inspect")))

    server.registerTool("reset_project",
        {description: "Очистить проект без перезапуска браузера.", inputSchema: {}},
        () => guard(async () => {
            await (await host()).call("reset")
            return {reset: true}
        }))

    server.registerTool("render",
        {description: "Отрендерить микс или стемы в WAV. loop сворачивает хвост затухания на начало.",
         inputSchema: {
             target: z.enum(["mix", "stems"]).default("mix"),
             name: z.string().min(1),
             loop: z.boolean().default(false),
             range: z.object({start: z.string(), end: z.string()}).optional(),
             format: z.enum(["int16", "float32"]).default("int16")
         }},
        ({target, name, loop, range, format}) => guard(async () => {
            const parsed = parseSignature(signature)
            const explicit = range === undefined ? undefined : {
                start: parsePosition(range.start, parsed), end: parsePosition(range.end, parsed)
            }
            const bounds = explicit ?? (loop ? documentLoop : undefined)
            if (loop && bounds === undefined) {
                throw new DocumentError([{
                    path: "range",
                    message: "для loop нужен либо range, либо поле loop в документе"
                }])
            }
            if (loop && bounds !== undefined) {
                // engine.ExportRange.end не обрезает рендер — это только потолок maxDurationSeconds,
                // движок останавливается по тишине. Реально урезает только range.start (затравка).
                // Значит рендер [bounds.start, bounds.end] на деле играет от bounds.start до КОНЦА
                // документа плюс хвост затухания. Если после bounds.end есть настоящий материал,
                // foldTail свернёт его на начало лупа как мусор — тихо и неслышимо для return value.
                if (documentEnd === undefined) {
                    throw new DocumentError([{
                        path: "range",
                        message: "нет собранного проекта: сначала вызовите build_arrangement"
                    }])
                }
                if (bounds.end !== documentEnd) {
                    throw new DocumentError([{
                        path: "range.end",
                        message: `луп должен заканчиваться там же, где документ, иначе после него звучит ` +
                            `настоящий материал, а не только хвост затухания: конец лупа ${bounds.end} тиков ` +
                            `(${ticksToSeconds(bounds.end, tempo).toFixed(2)} с), конец документа ` +
                            `${documentEnd} тиков (${ticksToSeconds(documentEnd, tempo).toFixed(2)} с)`
                    }])
                }
            }
            const reply = await (await host()).call<RenderReply>("render", {target, range: bounds})
            const channels = reply.channels.map(decodeChannel)
            const prepared = loop
                ? foldTail(channels, Math.round(
                    ticksToSeconds(bounds!.end - bounds!.start, tempo) * reply.sampleRate))
                : channels
            // Имена приходят по одному на стерео-пару, каналы — парами в том же порядке.
            const files = await Promise.all(reply.names.map((fileName, index) => writeWav(
                join(options.outputDir, `${name}_${fileName}.wav`),
                [prepared[index * 2]!, prepared[index * 2 + 1]!],
                reply.sampleRate, format as WavFormat)))
            const clipped = files.filter(file => file.clipped).map(file => file.path)
            return {
                files,
                warnings: clipped.length === 0 ? [] : [
                    `перегруз (пик выше 1.0) обрезан при записи 16 бит: ${clipped.join(", ")}`]
            }
        }))

    server.registerTool("import_asset",
        {description: "Импортировать сэмпл (wav/mp3/flac) или soundfont (sf2) с диска.",
         inputSchema: {path: z.string().min(1), name: z.string().min(1).optional()}},
        ({path, name}) => guard(async () => {
            const file = resolve(path)
            const extension = extname(file).toLowerCase()
            const kind = extension === ".sf2" ? "soundfont" as const : "sample" as const
            if (![".wav", ".mp3", ".flac", ".sf2"].includes(extension)) {
                throw new DocumentError([{path: "path", message: `неподдерживаемое расширение "${extension}"`}])
            }
            // Файл не читаем в память Node: страница сама фетчит его по HTTP через bridge.serveFile,
            // иначе десятки МБ soundfont'а валят Node ещё до того, как страница их увидит.
            const url = (await host()).serveFile(file)
            // __odaw.importAsset(name, kind, url) берёт три позиционных аргумента, не объект.
            return (await host()).callPositional("importAsset", [name ?? basename(file, extension), kind, url])
        }))

    server.registerTool("list_assets",
        {description: "Импортированные сэмплы и soundfont с именами для ссылок из документа.",
         inputSchema: {}},
        () => guard(async () => (await host()).call("listAssets")))

    server.registerTool("export_bundle",
        {description: "Сохранить .odb для ручных правок в UI openDAW.",
         inputSchema: {path: z.string().min(1)}},
        ({path}) => guard(async () => {
            const bytes = await (await host()).call<number[]>("bundle")
            const target = resolve(path)
            await mkdir(join(target, ".."), {recursive: true})
            await writeFile(target, Uint8Array.from(bytes))
            return {path: target, bytes: bytes.length}
        }))

    return server
}
