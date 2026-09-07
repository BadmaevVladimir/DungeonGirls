export const QUARTER = 960
export const BAR = QUARTER * 4

export type Signature = {nominator: number, denominator: number}

export class TimeError extends Error {}

const DENOMINATORS = [1, 2, 4, 8, 16, 32]

export const parseSignature = (text: string): Signature => {
    const match = /^(\d+)\/(\d+)$/.exec(text.trim())
    if (match === null) {throw new TimeError(`не разобрать размер "${text}", ожидается вид 4/4`)}
    const nominator = Number(match[1])
    const denominator = Number(match[2])
    if (nominator < 1 || nominator > 32) {throw new TimeError(`числитель размера вне 1..32: ${nominator}`)}
    if (!DENOMINATORS.includes(denominator)) {
        throw new TimeError(`знаменатель размера должен быть одним из ${DENOMINATORS.join(", ")}, получен ${denominator}`)
    }
    return {nominator, denominator}
}

// Такт и доля в тиках, по формуле PPQN.fromSignature из lib-dsp.
const beatTicks = ({denominator}: Signature): number => Math.floor(BAR / denominator)
export const barTicks = (signature: Signature): number => beatTicks(signature) * signature.nominator

export const parsePosition = (text: string, signature: Signature): number => {
    const match = /^(\d+)\.(\d+)(?:\.(\d+))?$/.exec(text.trim())
    if (match === null) {
        throw new TimeError(`не разобрать позицию "${text}", ожидается вид 3.2 или 3.2.4`)
    }
    const bar = Number(match[1])
    const beat = Number(match[2])
    const sixteenth = match[3] === undefined ? 1 : Number(match[3])
    if (bar < 1 || beat < 1 || sixteenth < 1) {
        throw new TimeError(`позиция "${text}" считается от единицы, ноль недопустим`)
    }
    if (beat > signature.nominator) {
        throw new TimeError(`доля ${beat} за пределами такта ${signature.nominator}/${signature.denominator}`)
    }
    const perBeat = beatTicks(signature)
    const sixteenthTicks = QUARTER / 4
    if ((sixteenth - 1) * sixteenthTicks >= perBeat) {
        throw new TimeError(`шестнадцатая ${sixteenth} за пределами доли`)
    }
    return (bar - 1) * barTicks(signature) + (beat - 1) * perBeat + (sixteenth - 1) * sixteenthTicks
}

export const parseDuration = (text: string, signature: Signature): number => {
    const trimmed = text.trim()
    const bars = /^(\d+)b$/.exec(trimmed)
    if (bars !== null) {
        const count = Number(bars[1])
        if (count < 1) {throw new TimeError(`длительность "${text}" должна быть больше нуля`)}
        return count * barTicks(signature)
    }
    const fraction = /^1\/(\d+)([.t]?)$/.exec(trimmed)
    if (fraction === null) {
        throw new TimeError(`не разобрать длительность "${text}", ожидается 1/4, 1/8., 1/8t или 2b`)
    }
    const denominator = Number(fraction[1])
    if (!DENOMINATORS.includes(denominator)) {
        throw new TimeError(`знаменатель длительности должен быть одним из ${DENOMINATORS.join(", ")}, получен ${denominator}`)
    }
    const base = (QUARTER * 4) / denominator
    const modifier = fraction[2]
    return modifier === "." ? base * 1.5 : modifier === "t" ? (base * 2) / 3 : base
}

export const ticksToSeconds = (ticks: number, tempo: number): number => (ticks * 60) / QUARTER / tempo
