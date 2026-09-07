import {z} from "zod"
import {toMidi} from "./pitch"
import {parseDuration, parsePosition, parseSignature, type Signature} from "./time"

export type DocumentIssue = {path: string, message: string}

export class DocumentError extends Error {
    readonly issues: ReadonlyArray<DocumentIssue>

    constructor(issues: ReadonlyArray<DocumentIssue>) {
        super(`документ не прошёл проверку:\n${issues.map(i => `  ${i.path}: ${i.message}`).join("\n")}`)
        this.issues = issues
    }
}

const ParamValue = z.union([z.number(), z.string(), z.boolean()])

const Device = z.object({
    device: z.string().min(1),
    params: z.record(z.string(), ParamValue).optional()
}).strict()

const Instrument = z.object({
    device: z.string().min(1),
    params: z.record(z.string(), ParamValue).optional(),
    sample: z.string().optional(),
    soundfont: z.string().optional(),
    preset: z.number().int().min(0).optional(),
    slots: z.record(z.string(), z.string()).optional()
}).strict()

const Mix = z.object({
    volume: z.number().min(-96).max(6).default(0),
    pan: z.number().min(-1).max(1).default(0)
}).strict()

const Note = z.object({
    p: z.union([z.string(), z.number()]),
    at: z.string(),
    d: z.string(),
    v: z.number().min(0).max(1).default(0.8)
}).strict()

const Pattern = z.object({
    length: z.string(),
    notes: z.array(Note).min(1)
}).strict()

const Placement = z.object({
    pattern: z.string().min(1),
    at: z.string(),
    repeat: z.number().int().min(1).default(1)
}).strict()

const Bus = z.object({
    name: z.string().min(1),
    stem: z.string().min(1).optional(),
    effects: z.array(Device).default([]),
    mix: Mix.default({volume: 0, pan: 0})
}).strict()

const Track = z.object({
    name: z.string().min(1),
    instrument: Instrument,
    effects: z.array(Device).default([]),
    mix: Mix.default({volume: 0, pan: 0}),
    out: z.string().min(1).optional(),
    stem: z.string().min(1).optional(),
    place: z.array(Placement).min(1)
}).strict()

export const ArrangementDocument = z.object({
    name: z.string().min(1),
    tempo: z.number().min(20).max(400),
    signature: z.string().default("4/4"),
    end: z.string(),
    loop: z.object({start: z.string(), end: z.string()}).strict().optional(),
    buses: z.array(Bus).default([]),
    patterns: z.record(z.string(), Pattern),
    tracks: z.array(Track).min(1)
}).strict()

export type Arrangement = z.infer<typeof ArrangementDocument>

const collect = (issues: DocumentIssue[], path: string, action: () => void): void => {
    try {
        action()
    } catch (error) {
        issues.push({path, message: error instanceof Error ? error.message : String(error)})
    }
}

// Второй проход: всё, что zod не выражает — разбор строк времени и высот, ссылочная
// целостность, уникальность имён.
const crossCheck = (doc: Arrangement): ReadonlyArray<DocumentIssue> => {
    const issues: DocumentIssue[] = []
    let signature: Signature = {nominator: 4, denominator: 4}
    collect(issues, "signature", () => {signature = parseSignature(doc.signature)})

    let end = 0
    collect(issues, "end", () => {end = parsePosition(doc.end, signature)})

    const patternLengths = new Map<string, number>()
    for (const [name, pattern] of Object.entries(doc.patterns)) {
        let length = 0
        collect(issues, `patterns.${name}.length`, () => {
            length = parseDuration(pattern.length, signature)
            patternLengths.set(name, length)
        })
        pattern.notes.forEach((note, index) => {
            const base = `patterns.${name}.notes[${index}]`
            collect(issues, `${base}.p`, () => {toMidi(note.p)})
            let position = 0
            let duration = 0
            collect(issues, `${base}.at`, () => {position = parsePosition(note.at, signature)})
            collect(issues, `${base}.d`, () => {duration = parseDuration(note.d, signature)})
            if (length > 0 && duration > 0 && position + duration > length) {
                issues.push({
                    path: base,
                    message: `нота выходит за длину паттерна "${name}" (${position + duration} > ${length} тиков)`
                })
            }
        })
    }

    const busNames = new Set<string>()
    doc.buses.forEach((bus, index) => {
        if (busNames.has(bus.name)) {
            issues.push({path: `buses[${index}].name`, message: `шина "${bus.name}" объявлена дважды`})
        }
        busNames.add(bus.name)
    })

    const trackNames = new Set<string>()
    doc.tracks.forEach((track, index) => {
        if (trackNames.has(track.name)) {
            issues.push({path: `tracks[${index}].name`, message: `дорожка "${track.name}" объявлена дважды`})
        }
        trackNames.add(track.name)
        if (track.out !== undefined && !busNames.has(track.out)) {
            issues.push({path: `tracks[${index}].out`, message: `шина "${track.out}" не объявлена`})
        }
        track.place.forEach((placement, placeIndex) => {
            const base = `tracks[${index}].place[${placeIndex}]`
            if (!patternLengths.has(placement.pattern)) {
                issues.push({path: `${base}.pattern`, message: `паттерн "${placement.pattern}" не объявлен`})
            }
            collect(issues, `${base}.at`, () => {
                const at = parsePosition(placement.at, signature)
                const length = patternLengths.get(placement.pattern) ?? 0
                if (end > 0 && length > 0 && at + length * placement.repeat > end) {
                    issues.push({path: base, message: `расстановка выходит за end документа`})
                }
            })
        })
    })

    if (doc.loop !== undefined) {
        let start = 0
        let loopEnd = 0
        collect(issues, "loop.start", () => {start = parsePosition(doc.loop!.start, signature)})
        collect(issues, "loop.end", () => {loopEnd = parsePosition(doc.loop!.end, signature)})
        if (loopEnd <= start) {
            issues.push({path: "loop.end", message: "конец лупа должен быть строго позже начала"})
        }
    }
    return issues
}

export const parseDocument = (input: unknown): Arrangement => {
    const result = ArrangementDocument.safeParse(input)
    if (!result.success) {
        throw new DocumentError(result.error.issues.map(issue => ({
            path: issue.path.length === 0 ? "<корень>" : issue.path
                .map((segment, index) => typeof segment === "number"
                    ? `[${segment}]`
                    : index === 0 ? String(segment) : `.${String(segment)}`)
                .join(""),
            message: issue.message
        })))
    }
    const issues = crossCheck(result.data)
    if (issues.length > 0) {throw new DocumentError(issues)}
    return result.data
}
