import {EffectFactories, Project} from "@opendaw/studio-core"
import {Devices, InstrumentFactories} from "@opendaw/studio-adapters"
import {bootOpenDAW} from "./boot"

export type ParamSpec = {
    name: string
    label: string
    unit: string
    min?: number
    max?: number
    values?: ReadonlyArray<string>
    default: number | string | boolean
}
export type DeviceSpec = {name: string, kind: "instrument" | "effect", params: ReadonlyArray<ParamSpec>}
export type Catalog = ReadonlyArray<DeviceSpec>

// Выше этого числа перебор меток бессмыслен: столько дискретных состояний не бывает у перечисления.
const MAX_DISCRETE = 64

const describeParameter = (path: string, adapter: any): ParamSpec => {
    const {valueMapping, stringMapping} = adapter
    const min = valueMapping.y(0)
    const max = valueMapping.y(1)
    const unit = String(stringMapping.x(min)?.unit ?? "")
    const base: ParamSpec = {name: path, label: String(adapter.name), unit, default: adapter.getValue()}
    if (typeof min !== "number" || typeof max !== "number") {return base}
    if (valueMapping.floating() || max - min > MAX_DISCRETE) {return {...base, min, max}}
    const labels: string[] = []
    for (let value = min; value <= max; value++) {labels.push(String(stringMapping.x(value)?.value ?? value))}
    const isEnum = labels.some(label => !Number.isFinite(Number(label)))
    if (!isEnum) {return {...base, min, max}}
    return {...base, min, max, values: labels, default: labels[Number(adapter.getValue()) - min] ?? labels[0]!}
}

// Параметр узнаётся по наличию valueMapping и stringMapping; всё остальное — вложенные группы.
const walk = (node: unknown, prefix: string, out: ParamSpec[]): void => {
    if (node === null || typeof node !== "object") {return}
    const candidate = node as Record<string, unknown>
    if (candidate["valueMapping"] !== undefined && candidate["stringMapping"] !== undefined) {
        out.push(describeParameter(prefix, candidate))
        return
    }
    if (Array.isArray(node)) {
        node.forEach((entry, index) => walk(entry, `${prefix}[${index}]`, out))
        return
    }
    for (const [key, value] of Object.entries(candidate)) {
        walk(value, prefix === "" ? key : `${prefix}.${key}`, out)
    }
}

const paramsOf = (project: Project, box: unknown): ParamSpec[] => {
    const adapter = project.boxAdapters.adapterFor(box as never, Devices.isAny) as unknown as Record<string, unknown>
    const params: ParamSpec[] = []
    walk(adapter["namedParameter"], "", params)
    return params
}

export const describeDevices = async (): Promise<Catalog> => {
    const {env} = await bootOpenDAW()
    const catalog: DeviceSpec[] = []
    for (const [name, factory] of Object.entries(InstrumentFactories.Named)) {
        const probe = Project.new(env as never)
        probe.editing.modify(() => {
            const {instrumentBox} = probe.api.createInstrument(factory as never)
            catalog.push({name, kind: "instrument", params: paramsOf(probe, instrumentBox)})
        })
        probe.terminate()
    }
    // MIDI-эффекты живут в midiEffects, аудио — в audioEffects; поле выбирается по карте.
    const effects: ReadonlyArray<readonly [string, unknown, "midi" | "audio"]> = [
        ...Object.entries(EffectFactories.MidiNamed).map(([key, value]) => [key, value, "midi"] as const),
        ...Object.entries(EffectFactories.AudioNamed).map(([key, value]) => [key, value, "audio"] as const)
    ]
    for (const [name, factory, slot] of effects) {
        const probe = Project.new(env as never)
        probe.editing.modify(() => {
            const {audioUnitBox} = probe.api.createInstrument(InstrumentFactories.Vaporisateur)
            const field = slot === "midi" ? audioUnitBox.midiEffects : audioUnitBox.audioEffects
            const effectBox = probe.api.insertEffect(field, factory as never)
            catalog.push({name, kind: "effect", params: paramsOf(probe, effectBox)})
        })
        probe.terminate()
    }
    return catalog
}
