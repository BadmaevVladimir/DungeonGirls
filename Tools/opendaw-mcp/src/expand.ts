import type {Arrangement} from "./schema"
import {toMidi} from "./pitch"
import {parseDuration, parsePosition, parseSignature, type Signature} from "./time"

export type FlatNote = {pitch: number, position: number, duration: number, velocity: number}
export type FlatRegion = {
    track: string, pattern: string, position: number, duration: number, notes: ReadonlyArray<FlatNote>
}
export type FlatArrangement = {
    name: string
    tempo: number
    signature: Signature
    end: number
    loop?: {start: number, end: number}
    buses: Arrangement["buses"]
    tracks: Arrangement["tracks"]
    regions: ReadonlyArray<FlatRegion>
    warnings: ReadonlyArray<string>
}

export const expand = (doc: Arrangement): FlatArrangement => {
    const signature = parseSignature(doc.signature)
    const regions: FlatRegion[] = []
    const warnings: string[] = []
    for (const track of doc.tracks) {
        for (const placement of track.place) {
            const pattern = doc.patterns[placement.pattern]!
            const length = parseDuration(pattern.length, signature)
            const notes = pattern.notes.map(note => ({
                pitch: toMidi(note.p),
                position: parsePosition(note.at, signature),
                duration: parseDuration(note.d, signature),
                velocity: note.v
            }))
            const start = parsePosition(placement.at, signature)
            for (let repeat = 0; repeat < placement.repeat; repeat++) {
                regions.push({
                    track: track.name,
                    pattern: placement.pattern,
                    position: start + repeat * length,
                    duration: length,
                    notes
                })
            }
        }
    }
    const loop = doc.loop === undefined ? undefined : {
        start: parsePosition(doc.loop.start, signature),
        end: parsePosition(doc.loop.end, signature)
    }
    if (loop !== undefined) {
        // Свёртка хвоста чинит затухания, но не ноту, тянущуюся через точку лупа.
        for (const region of regions) {
            if (region.position < loop.start && region.position + region.duration > loop.start) {
                warnings.push(
                    `регион паттерна "${region.pattern}" на дорожке "${region.track}" пересекает начало лупа: ` +
                    `звучащая в этот момент нота не завернётся`)
            }
        }
    }
    return {
        name: doc.name,
        tempo: doc.tempo,
        signature,
        end: parsePosition(doc.end, signature),
        loop,
        buses: doc.buses,
        tracks: doc.tracks,
        regions,
        warnings
    }
}
