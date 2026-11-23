import { describe, expect, it } from 'vitest'
import { decodeTechnicalOptions } from './technicalContract'

const id = (n: number) => `c0990000-0000-4000-8000-${String(n).padStart(12, '0')}`
// SENTETIK: the technical part of ProductReferenceOptions (templates, dictionaries, familyFacts, units).
const options = () => ({
  templates: [{ id: id(1), version: '1', displayName: 'SENTETIK şablon', fields: [{ key: 'thickness', kind: 'measureMillimetre', role: null,
    required: true, dictionary: null, allowsCandidate: false, appliesWhenField: null, appliesWhenCode: null, parentField: null }] }],
  dictionaries: [{ name: 'coating', entries: [{ code: 'Z100', displayName: 'SENTETIK kaplama', status: 'approved', parentCode: null,
    isExplicitNone: false, massGm2: '100' }] }],
  familyFacts: [{ id: id(2), code: 'SENTETIK', displayName: 'SENTETIK aile', cins: 'SENTETIK cins', tur: 'SENTETIK tür', tip: 'SENTETIK tip',
    templateId: id(1), templateVersion: '1' }],
  units: [{ id: id(3), displayName: 'Kilogram', symbol: 'kg', isKilogram: true }],
})
type Json = Record<string, unknown>
const LEVELS: readonly (readonly [(value: ReturnType<typeof options>) => Json, string])[] = [
  [v => v.templates[0], 'displayName'], [v => v.templates[0].fields[0], 'parentField'], [v => v.dictionaries[0], 'name'],
  [v => v.dictionaries[0].entries[0], 'massGm2'], [v => v.familyFacts[0], 'templateVersion'], [v => v.units[0], 'symbol'],
]

describe('technical options exact key sets', () => {
  it('decodes the valid synthetic options and a template without the optional fields list', () => {
    expect(decodeTechnicalOptions(options())).toEqual(options())
    const value = options(); delete (value.templates[0] as Json).fields
    expect(decodeTechnicalOptions(value)?.templates[0].fields).toEqual([])
  })
  it.each(LEVELS.map((level, index) => [index, ...level] as const))('rejects an unknown key at level %i', (_index, pick) => {
    const value = options(); pick(value).sentetikExtraKey = null
    expect(decodeTechnicalOptions(value)).toBeNull()
  })
  it.each(LEVELS.map((level, index) => [index, ...level] as const))('rejects a missing required key at level %i', (_index, pick, key) => {
    const value = options(); const target = pick(value)
    expect(Object.hasOwn(target, key)).toBe(true)
    delete target[key]
    expect(decodeTechnicalOptions(value)).toBeNull()
  })
})
