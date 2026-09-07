// Соглашение openDAW (MidiKeys.toFullString): 60 = C3, а не C4.
const NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"] as const
const SEMITONES: Record<string, number> = {c: 0, d: 2, e: 4, f: 5, g: 7, a: 9, b: 11}
const PATTERN = /^([A-Ga-g])([#b]?)(-?\d+)$/

export class PitchError extends Error {}

export const toMidi = (value: string | number): number => {
    if (typeof value === "number") {
        if (!Number.isInteger(value) || value < 0 || value > 127) {
            throw new PitchError(`высота ${value} вне диапазона MIDI 0..127`)
        }
        return value
    }
    const match = PATTERN.exec(value.trim())
    if (match === null) {
        throw new PitchError(`не разобрать высоту "${value}", ожидается вид C3, F#2, Db4`)
    }
    const [, letter, accidental, octave] = match
    const base = SEMITONES[letter!.toLowerCase()]!
    const shift = accidental === "#" ? 1 : accidental === "b" ? -1 : 0
    const midi = (Number(octave) + 2) * 12 + base + shift
    if (midi < 0 || midi > 127) {
        throw new PitchError(`высота "${value}" даёт ${midi}, вне диапазона MIDI 0..127`)
    }
    return midi
}

export const toName = (midi: number): string => `${NAMES[midi % 12]}${Math.floor(midi / 12) - 2}`
