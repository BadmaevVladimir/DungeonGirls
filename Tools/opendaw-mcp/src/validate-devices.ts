import type {Arrangement, DocumentIssue} from "./schema"

export type ParamSpec = {
    name: string, label: string, unit: string,
    min?: number, max?: number, values?: ReadonlyArray<string>, default: number | string | boolean
}
export type DeviceSpec = {name: string, kind: "instrument" | "effect", params: ReadonlyArray<ParamSpec>}
export type Catalog = ReadonlyArray<DeviceSpec>

const checkDevice = (catalog: Catalog,
                     path: string,
                     kind: "instrument" | "effect",
                     device: {device: string, params?: Record<string, number | string | boolean>},
                     issues: DocumentIssue[]): void => {
    const spec = catalog.find(entry => entry.name === device.device)
    if (spec === undefined) {
        const available = catalog.filter(entry => entry.kind === kind).map(entry => entry.name).join(", ")
        issues.push({path: `${path}.device`, message: `устройство "${device.device}" неизвестно; доступны: ${available}`})
        return
    }
    if (spec.kind !== kind) {
        issues.push({
            path: `${path}.device`,
            message: `"${device.device}" — ${spec.kind === "instrument" ? "не эффект" : "не инструмент"}`
        })
        return
    }
    for (const [name, value] of Object.entries(device.params ?? {})) {
        const param = spec.params.find(entry => entry.name === name)
        if (param === undefined) {
            const available = spec.params.map(entry => entry.name).slice(0, 12).join(", ")
            issues.push({
                path: `${path}.params.${name}`,
                message: `у "${device.device}" нет параметра "${name}"; есть: ${available}`
            })
            continue
        }
        if (param.values !== undefined) {
            if (typeof value !== "string" || !param.values.includes(value)) {
                issues.push({
                    path: `${path}.params.${name}`,
                    message: `значение должно быть одним из: ${param.values.join(", ")}`
                })
            }
            continue
        }
        if (typeof value !== "number") {
            issues.push({path: `${path}.params.${name}`, message: "значение должно быть числом"})
            continue
        }
        if ((param.min !== undefined && value < param.min) || (param.max !== undefined && value > param.max)) {
            issues.push({
                path: `${path}.params.${name}`,
                message: `значение ${value} вне диапазона ${param.min}..${param.max}`
            })
        }
    }
}

export const validateDevices = (doc: Arrangement, catalog: Catalog): ReadonlyArray<DocumentIssue> => {
    const issues: DocumentIssue[] = []
    doc.tracks.forEach((track, index) => {
        checkDevice(catalog, `tracks[${index}].instrument`, "instrument", track.instrument, issues)
        track.effects.forEach((effect, effectIndex) =>
            checkDevice(catalog, `tracks[${index}].effects[${effectIndex}]`, "effect", effect, issues))
    })
    doc.buses.forEach((bus, index) =>
        bus.effects.forEach((effect, effectIndex) =>
            checkDevice(catalog, `buses[${index}].effects[${effectIndex}]`, "effect", effect, issues)))
    return issues
}
