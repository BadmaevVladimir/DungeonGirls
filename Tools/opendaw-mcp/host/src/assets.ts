import {Option, Progress, UUID} from "@opendaw/lib-std"
import {ProjectBundle, ProjectMeta, ProjectProfile} from "@opendaw/studio-core"
import {AudioData} from "@opendaw/lib-dsp"
import type {SampleMetaData, SoundfontMetaData} from "@opendaw/studio-adapters"
import {SoundFont2} from "soundfont2"
import {assetStore, bootOpenDAW, type AssetEntry} from "./boot"
import {currentProject} from "./build"

export type AssetInfo = {
    name: string
    kind: "sample" | "soundfont"
    uuid: string
    seconds?: number
    presets?: ReadonlyArray<{index: number, name: string}>
}

const infos = new Map<string, AssetInfo>()

export const importAsset = async (name: string,
                                  kind: "sample" | "soundfont",
                                  url: string): Promise<AssetInfo> => {
    const {context} = await bootOpenDAW()
    // Файл фетчится страницей напрямую с диска через bridge.serveFile — байты никогда
    // не проходят через page.evaluate как JSON-массив чисел (десятки МБ роняют Node).
    const response = await fetch(url)
    const buffer = new Uint8Array(await response.arrayBuffer())
    const uuid = UUID.generate()
    if (kind === "sample") {
        // decodeAudioData отсоединяет буфер, поэтому копируем перед вызовом.
        const decoded = await context.decodeAudioData(buffer.slice().buffer)
        // AudioData.frames живут в SharedArrayBuffer; decodeAudioData отдаёт обычный ArrayBuffer, копируем.
        const data = AudioData.create(decoded.sampleRate, decoded.length, decoded.numberOfChannels)
        for (let channel = 0; channel < decoded.numberOfChannels; channel++) {
            data.frames[channel]!.set(decoded.getChannelData(channel))
        }
        const seconds = decoded.length / decoded.sampleRate
        // SampleProvider.fetch обязан вернуть [AudioData, SampleMetaData] — GlobalSampleLoaderManager
        // деструктурирует результат именно так при первой загрузке ассета.
        const meta: SampleMetaData = {
            name, bpm: 0, duration: seconds, sample_rate: decoded.sampleRate, origin: "import"
        }
        const entry: AssetEntry = {uuid, data: [data, meta], kind, seconds}
        assetStore.set(name, entry)
        const info: AssetInfo = {name, kind, uuid: UUID.toString(uuid), seconds}
        infos.set(name, info)
        return info
    }
    const soundfont = new SoundFont2(buffer)
    // SoundfontProvider.fetch обязан вернуть [ArrayBuffer, SoundfontMetaData].
    const meta: SoundfontMetaData = {name, size: buffer.byteLength, url: "", license: "", origin: "import"}
    const entry: AssetEntry = {uuid, data: [buffer.buffer as ArrayBuffer, meta], kind}
    assetStore.set(name, entry)
    const info: AssetInfo = {
        name, kind, uuid: UUID.toString(uuid),
        presets: soundfont.presets.map((preset, index) => ({index, name: preset.header.name}))
    }
    infos.set(name, info)
    return info
}

export const listAssets = (): ReadonlyArray<AssetInfo> => Array.from(infos.values())

export const lookupAsset = (name: string, kind: "sample" | "soundfont"): AssetEntry => {
    const entry = assetStore.get(name)
    if (entry === undefined) {throw new Error(`ассет "${name}" не импортирован`)}
    if (entry.kind !== kind) {throw new Error(`ассет "${name}" — ${entry.kind}, а нужен ${kind}`)}
    return entry
}

export const exportBundle = async (name: string): Promise<number[]> => {
    const profile = new ProjectProfile(UUID.generate(), currentProject(), ProjectMeta.init(name), Option.None)
    const encoded = await ProjectBundle.encode(profile, Progress.Empty)
    return Array.from(new Uint8Array(encoded))
}
