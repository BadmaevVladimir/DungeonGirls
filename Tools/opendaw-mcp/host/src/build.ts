import {EffectFactories, Project} from "@opendaw/studio-core"
import {AudioBusFactory, ColorCodes, Devices, InstrumentFactories} from "@opendaw/studio-adapters"
import {AudioFileBox, AudioUnitBox, SoundfontDeviceBox, SoundfontFileBox} from "@opendaw/studio-boxes"
import {AudioUnitType, IconSymbol} from "@opendaw/studio-enums"
import {asInstanceOf, UUID} from "@opendaw/lib-std"
import {bootOpenDAW} from "./boot"
import {lookupAsset} from "./assets"
import {barTicks, ticksToSeconds} from "../../src/time"
import type {FlatArrangement} from "../../src/expand"
import {toMidi} from "../../src/pitch"

type FlatTrack = FlatArrangement["tracks"][number]

export type BuildSummary = {
    tracks: number, buses: number, regions: number, notes: number,
    bars: number, seconds: number, warnings: ReadonlyArray<string>,
    stems: ReadonlyArray<{unit: string, fileName: string}>,
    playfieldNotes: ReadonlyArray<number>
}

let project: Project | undefined
let summary: BuildSummary | undefined
let stems: Array<{uuid: string, fileName: string}> = []
let documentName: string | undefined

export const currentProject = (): Project => {
    if (project === undefined) {throw new Error("проект не собран: сначала вызовите build_arrangement")}
    return project
}
export const currentSummary = (): BuildSummary | null => summary ?? null
export const stemUnits = (): ReadonlyArray<{uuid: string, fileName: string}> => stems
// Имя последнего собранного документа — дефолт для export_bundle, чтобы бандл не назывался
// вечным "openDAW MCP", если вызывающий не передал своё имя явно.
export const currentDocumentName = (): string | undefined => documentName

export const resetProject = (): void => {
    project?.terminate()
    project = undefined
    summary = undefined
    stems = []
    documentName = undefined
}

// Метка перечисления ищется перебором по целочисленному диапазону параметра: у StringMapping
// нет свойства values, только x(value) -> {value, unit}. Молчаливого пропуска нет: не нашли — бросаем.
const setParameter = (path: string, parameter: any, value: number | string | boolean): void => {
    if (typeof value !== "string") {parameter.setValue(value); return}
    const {valueMapping, stringMapping} = parameter
    const min = valueMapping.y(0)
    const max = valueMapping.y(1)
    for (let candidate = min; candidate <= max; candidate++) {
        if (String(stringMapping.x(candidate)?.value) === value) {parameter.setValue(candidate); return}
    }
    throw new Error(`параметр "${path}" не принимает значение "${value}"`)
}

const applyParams = (adapter: Record<string, unknown>,
                     params: Record<string, number | string | boolean> | undefined): void => {
    for (const [path, value] of Object.entries(params ?? {})) {
        let node: unknown = adapter["namedParameter"]
        for (const segment of path.split(/[.[\]]+/).filter(part => part.length > 0)) {
            node = (node as Record<string, unknown>)[segment]
        }
        setParameter(path, node, value)
    }
}

export const buildProject = async (flat: FlatArrangement): Promise<BuildSummary> => {
    const {env} = await bootOpenDAW()
    // Старый project остаётся живым и подключённым, пока новый не соберётся целиком:
    // упавшая на середине сборка (неизвестный ассет, битый слот Playfield) не должна
    // стереть последний рабочий проект — resetProject() зовём только после успеха.
    const created = Project.new(env as never)
    const nextStems: Array<{uuid: string, fileName: string}> = []
    const playfieldNotes: number[] = []
    try {
        buildInto(created, flat, nextStems, playfieldNotes)
    } catch (error) {
        created.terminate()
        throw error
    }
    resetProject()
    project = created
    stems = nextStems
    documentName = flat.name
    summary = {
        tracks: flat.tracks.length,
        buses: flat.buses.length,
        regions: flat.regions.length,
        notes: flat.regions.reduce((total, region) => total + region.notes.length, 0),
        bars: Math.ceil(flat.end / barTicks(flat.signature)),
        seconds: ticksToSeconds(flat.end, flat.tempo),
        warnings: flat.warnings,
        stems: nextStems.map(stem => ({unit: stem.uuid, fileName: stem.fileName})),
        playfieldNotes
    }
    return summary
}

const buildInto = (created: Project, flat: FlatArrangement,
                   nextStems: Array<{uuid: string, fileName: string}>, playfieldNotes: number[]): void => {
    created.editing.modify(() => {
        created.api.setBpm(flat.tempo)
        // Шины сначала: дорожки при маршрутизации ссылаются на их AudioBusBox.
        const busBoxes = new Map<string, ReturnType<typeof AudioBusFactory.create>>()
        for (const bus of flat.buses) {
            const busBox = AudioBusFactory.create(
                created.skeleton, bus.name, IconSymbol.AudioBus,
                AudioUnitType.Bus, ColorCodes.forAudioType(AudioUnitType.Bus))
            const busUnit = asInstanceOf(busBox.output.targetVertex.unwrap("шина без юнита").box, AudioUnitBox)
            busUnit.volume.setValue(bus.mix.volume)
            busUnit.panning.setValue(bus.mix.pan)
            // AudioUnitFactory уже маршрутизирует новый юнит в primaryAudioBusBox по умолчанию;
            // ссылка ниже делает это явным и переживёт смену дефолта фабрики.
            busUnit.output.refer(created.primaryAudioBusBox.input)
            for (const effect of bus.effects) {
                const box = created.api.insertEffect(
                    busUnit.audioEffects, (EffectFactories.MergedNamed as never)[effect.device])
                applyParams(created.boxAdapters.adapterFor(box, Devices.isAny) as never, effect.params)
            }
            busBoxes.set(bus.name, busBox)
            if (bus.stem !== undefined) {
                nextStems.push({uuid: UUID.toString(busUnit.address.uuid), fileName: bus.stem})
            }
        }
        // Nano ждёт AudioFileBox, Soundfont — SoundfontFileBox, Playfield — массив слотов
        // с уже привязанными ассетами (сам заводит под них AudioFileBox через фабрику).
        // File-box адресуется UUID самого ассета, поэтому на один ассет в графе может
        // существовать ровно один box. Две дорожки на общий сэмпл или банк — обычный
        // случай (струнные и медь из одного soundfont), и второй create ронял сборку:
        // падал уже откат транзакции, пряча настоящую причину за "has outgoing edges".
        const reuseOrCreate = <B>(uuid: Uint8Array, create: () => B): B =>
            created.boxGraph.findBox(uuid).unwrapOrElse(create as never) as B
        const attachmentFor = (instrument: FlatTrack["instrument"]): unknown => {
            if (instrument.device === "Nano") {
                const entry = lookupAsset(instrument.sample!, "sample")
                return reuseOrCreate(entry.uuid, () =>
                    AudioFileBox.create(created.boxGraph, entry.uuid, box => {
                        box.fileName.setValue(instrument.sample!)
                        if (entry.seconds !== undefined) {box.endInSeconds.setValue(entry.seconds)}
                    }))
            }
            if (instrument.device === "Soundfont") {
                const entry = lookupAsset(instrument.soundfont!, "soundfont")
                return reuseOrCreate(entry.uuid, () =>
                    SoundfontFileBox.create(created.boxGraph, entry.uuid,
                        box => box.fileName.setValue(instrument.soundfont!)))
            }
            if (instrument.device === "Playfield") {
                return Object.entries(instrument.slots ?? {}).map(([pitch, sample]) => {
                    const entry = lookupAsset(sample, "sample")
                    const note = toMidi(pitch)
                    playfieldNotes.push(note)
                    return {
                        note,
                        uuid: entry.uuid,
                        name: sample,
                        durationInSeconds: entry.seconds ?? 0,
                        exclude: false
                    }
                })
            }
            return undefined
        }
        const trackBoxes = new Map<string, ReturnType<typeof created.api.createInstrument>["trackBox"]>()
        for (const track of flat.tracks) {
            const factory = (InstrumentFactories.Named as never)[track.instrument.device]
            const {audioUnitBox, instrumentBox, trackBox} =
                created.api.createInstrument(factory, {name: track.name, attachment: attachmentFor(track.instrument)})
            if (track.instrument.device === "Soundfont") {
                asInstanceOf(instrumentBox, SoundfontDeviceBox).presetIndex.setValue(track.instrument.preset ?? 0)
            }
            audioUnitBox.volume.setValue(track.mix.volume)
            audioUnitBox.panning.setValue(track.mix.pan)
            applyParams(created.boxAdapters.adapterFor(instrumentBox, Devices.isAny) as never, track.instrument.params)
            for (const effect of track.effects) {
                const box = created.api.insertEffect(
                    audioUnitBox.audioEffects, (EffectFactories.MergedNamed as never)[effect.device])
                applyParams(created.boxAdapters.adapterFor(box, Devices.isAny) as never, effect.params)
            }
            if (track.out !== undefined) {
                // Ссылка идёт на AudioBusBox шины, не на её AudioUnitBox напрямую.
                audioUnitBox.output.refer(busBoxes.get(track.out)!.input)
            }
            if (track.stem !== undefined) {
                nextStems.push({uuid: UUID.toString(audioUnitBox.address.uuid), fileName: track.stem})
            }
            trackBoxes.set(track.name, trackBox)
        }
        for (const region of flat.regions) {
            const noteRegion = created.api.createNoteRegion({
                trackBox: trackBoxes.get(region.track)!,
                position: region.position,
                duration: region.duration,
                name: region.pattern
            })
            for (const note of region.notes) {
                created.api.createNoteEvent({
                    owner: noteRegion,
                    position: note.position,
                    duration: note.duration,
                    pitch: note.pitch,
                    velocity: note.velocity
                })
            }
        }
    })
}
