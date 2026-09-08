import {describe, expect, it} from "vitest"
import {DocumentError, parseDocument} from "../src/schema"

const minimal = () => ({
    name: "Test",
    tempo: 120,
    end: "3.1",
    patterns: {riff: {length: "1b", notes: [{p: "C3", at: "1.1", d: "1/4"}]}},
    tracks: [{name: "Lead", instrument: {device: "Vaporisateur"}, place: [{pattern: "riff", at: "1.1"}]}]
})

describe("parseDocument", () => {
    it("принимает минимальный документ и подставляет умолчания", () => {
        const doc = parseDocument(minimal())
        expect(doc.signature).toBe("4/4")
        expect(doc.tracks[0]!.place[0]!.repeat).toBe(1)
        expect(doc.tracks[0]!.mix).toEqual({volume: 0, pan: 0})
        expect(doc.patterns.riff!.notes[0]!.v).toBe(0.8)
        expect(doc.buses).toEqual([])
    })

    it("отвергает ссылку на необъявленный паттерн с путём до поля", () => {
        const input = minimal()
        input.tracks[0]!.place[0]!.pattern = "missing"
        try {
            parseDocument(input)
            expect.unreachable("должно было бросить")
        } catch (error) {
            expect(error).toBeInstanceOf(DocumentError)
            const {issues} = error as DocumentError
            expect(issues).toContainEqual({
                path: "tracks[0].place[0].pattern",
                message: 'паттерн "missing" не объявлен'
            })
        }
    })

    it("отвергает ссылку на необъявленную шину", () => {
        const input = {...minimal(), tracks: [{...minimal().tracks[0]!, out: "Nope"}]}
        expect(() => parseDocument(input)).toThrow(DocumentError)
    })

    it("отвергает повторяющиеся имена дорожек", () => {
        const track = minimal().tracks[0]!
        expect(() => parseDocument({...minimal(), tracks: [track, {...track}]})).toThrow(DocumentError)
    })

    it("отвергает неизвестные поля", () => {
        expect(() => parseDocument({...minimal(), tempoo: 120})).toThrow(DocumentError)
    })

    it("отвергает непригодные позиции, длительности и высоты", () => {
        const badPosition = minimal()
        badPosition.tracks[0]!.place[0]!.at = "1.9"
        expect(() => parseDocument(badPosition)).toThrow(DocumentError)

        const badDuration = minimal()
        badDuration.patterns.riff!.notes[0]!.d = "1/5"
        expect(() => parseDocument(badDuration)).toThrow(DocumentError)

        const badPitch = minimal()
        badPitch.patterns.riff!.notes[0]!.p = "H3"
        expect(() => parseDocument(badPitch)).toThrow(DocumentError)
    })

    it("отвергает ноту, выходящую за длину паттерна", () => {
        const input = minimal()
        input.patterns.riff!.notes[0]! = {p: "C3", at: "1.4", d: "1/1"}
        expect(() => parseDocument(input)).toThrow(DocumentError)
    })

    it("отвергает конец лупа не позже начала", () => {
        const input = {...minimal(), loop: {start: "2.1", end: "2.1"}}
        expect(() => parseDocument(input)).toThrow(DocumentError)
    })

    it("отвергает паттерн с неправильной длиной, не путая с необъявленным паттерном", () => {
        const input = minimal()
        input.patterns.riff!.length = "1/5"
        try {
            parseDocument(input)
            expect.unreachable("должно было бросить")
        } catch (error) {
            expect(error).toBeInstanceOf(DocumentError)
            const {issues} = error as DocumentError
            expect(issues).toContainEqual({
                path: "patterns.riff.length",
                message: expect.stringContaining("")
            })
            const notDeclaredIssue = issues.find(i => i.message.includes("не объявлен"))
            expect(notDeclaredIssue).toBeUndefined()
        }
    })
})
