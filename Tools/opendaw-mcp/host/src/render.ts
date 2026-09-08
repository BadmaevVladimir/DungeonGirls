import {OfflineEngineRenderer} from "@opendaw/studio-core"
import {ExportConfiguration} from "@opendaw/studio-adapters"
import {DefaultObservableValue, Option} from "@opendaw/lib-std"
import {currentProject, stemUnits} from "./build"
import {bytesToBase64} from "./base64"

export type RenderRequest = {target: "mix" | "stems", range?: {start: number, end: number}}
export type RenderResult = {
    sampleRate: number
    channels: ReadonlyArray<string>
    names: ReadonlyArray<string>
}

const toBase64 = (frames: Float32Array): string =>
    bytesToBase64(new Uint8Array(frames.buffer, frames.byteOffset, frames.byteLength))

export const renderProject = async ({target, range}: RenderRequest): Promise<RenderResult> => {
    const project = currentProject()
    const units = stemUnits()
    if (target === "stems" && units.length === 0) {
        throw new Error("ни одна дорожка или шина не помечена полем stem")
    }
    const config: Record<string, unknown> = {}
    if (range !== undefined) {config["range"] = {start: range.start, end: range.end}}
    if (target === "stems") {
        config["stems"] = Object.fromEntries(units.map(unit => [unit.uuid, {
            includeAudioEffects: true, includeSends: false, fileName: unit.fileName
        }]))
    }
    const optConfig = Object.keys(config).length === 0
        ? Option.None
        : Option.wrap(config as ExportConfiguration)
    const progress = new DefaultObservableValue(0.0)
    // copy(): start() временно гасит зону лупа, трогать живой проект нельзя.
    const audio = await OfflineEngineRenderer.start(project.copy(), optConfig, progress, undefined, 48_000)
    // Имена берутся только отсюда: метроном добавляется последней парой внутри движка,
    // самостоятельный обход stems даёт на одно имя меньше.
    const names = target === "stems"
        ? ExportConfiguration.stemFileNames(config as ExportConfiguration)
        : ["mix"]
    return {
        sampleRate: audio.sampleRate ?? 48_000,
        channels: audio.frames.map((frames: Float32Array) => toBase64(frames)),
        names
    }
}
