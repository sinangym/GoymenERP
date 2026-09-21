import { isBoundUuid, isNonEmptyString, isLosslessVersion } from './wireGuards'

export interface TemplateField {
  readonly key: string
  readonly kind: 'measureMillimetre' | 'dictionaryReference'
  readonly role: string | null
  readonly required: boolean
  readonly dictionary: string | null
  readonly allowsCandidate: boolean
  readonly appliesWhenField: string | null
  readonly appliesWhenCode: string | null
  readonly parentField: string | null
}
export interface TemplateOption {
  readonly id: string
  readonly version: string
  readonly displayName: string
  readonly fields: readonly TemplateField[]
}
export interface DictionaryEntry {
  readonly code: string
  readonly displayName: string
  readonly status: 'approved' | 'candidate'
  readonly parentCode: string | null
  readonly isExplicitNone: boolean
}
export interface TechnicalOptions {
  readonly templates: readonly TemplateOption[]
  readonly dictionaries: readonly { readonly name: string; readonly entries: readonly DictionaryEntry[] }[]
  readonly familyFacts: readonly {
    readonly id: string; readonly cins: string; readonly tur: string; readonly tip: string
    readonly templateId: string | null; readonly templateVersion: string | null
  }[]
  readonly units: readonly {
    readonly id: string; readonly displayName: string; readonly symbol: string; readonly isKilogram: boolean
  }[]
}

function object(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
const text = (value: unknown): value is string => isNonEmptyString(value, 250)
const nullableText = (value: unknown) => value === null || text(value)
function unique<T>(items: readonly T[], key: (item: T) => string): boolean {
  return new Set(items.map(key)).size === items.length
}

/** Optional legacy metadata is empty; malformed supplied metadata never becomes a choice. */
export function decodeTechnicalOptions(raw: Record<string, unknown>): TechnicalOptions | null {
  const templates: TemplateOption[] = []
  const dictionaries: TechnicalOptions['dictionaries'][number][] = []
  const familyFacts: TechnicalOptions['familyFacts'][number][] = []
  const units: TechnicalOptions['units'][number][] = []
  for (const key of ['templates', 'dictionaries', 'familyFacts', 'units']) {
    if (raw[key] != null && !Array.isArray(raw[key])) return null
  }
  for (const item of (raw.templates ?? []) as unknown[]) {
    if (!object(item) || !isBoundUuid(item.id) || !text(item.displayName)
      || !isLosslessVersion(item.version)
      || (item.fields != null && !Array.isArray(item.fields))) return null
    const fields: TemplateField[] = []
    for (const field of (item.fields ?? []) as unknown[]) {
      if (!object(field) || !text(field.key) || ['__proto__', 'constructor', 'prototype'].includes(field.key)
        || (field.kind !== 'measureMillimetre' && field.kind !== 'dictionaryReference')
        || typeof field.required !== 'boolean' || typeof field.allowsCandidate !== 'boolean'
        || !['role', 'dictionary', 'appliesWhenField', 'appliesWhenCode', 'parentField'].every(k => nullableText(field[k]))
        || (field.kind === 'dictionaryReference' && !text(field.dictionary))
        || ((field.appliesWhenField === null) !== (field.appliesWhenCode === null))) return null
      fields.push(field as unknown as TemplateField)
    }
    if (!unique(fields, f => f.key)) return null
    templates.push({ id: item.id, version: item.version, displayName: item.displayName, fields })
  }
  for (const item of (raw.dictionaries ?? []) as unknown[]) {
    if (!object(item) || !text(item.name) || !Array.isArray(item.entries)) return null
    const entries: DictionaryEntry[] = []
    for (const entry of item.entries) {
      if (!object(entry) || !text(entry.code) || !text(entry.displayName)
        || (entry.status !== 'approved' && entry.status !== 'candidate')
        || !nullableText(entry.parentCode) || typeof entry.isExplicitNone !== 'boolean') return null
      entries.push(entry as unknown as DictionaryEntry)
    }
    if (!unique(entries, e => e.code)) return null
    dictionaries.push({ name: item.name, entries })
  }
  for (const item of (raw.familyFacts ?? []) as unknown[]) {
    if (!object(item) || !isBoundUuid(item.id) || !text(item.cins) || !text(item.tur) || !text(item.tip)
      || (item.templateId !== null && !isBoundUuid(item.templateId))
      || !nullableText(item.templateVersion)
      || ((item.templateId === null) !== (item.templateVersion === null))) return null
    familyFacts.push(item as unknown as TechnicalOptions['familyFacts'][number])
  }
  for (const item of (raw.units ?? []) as unknown[]) {
    if (!object(item) || !isBoundUuid(item.id) || !text(item.displayName) || !text(item.symbol)
      || typeof item.isKilogram !== 'boolean') return null
    units.push(item as unknown as TechnicalOptions['units'][number])
  }
  if (!unique(templates, t => `${t.id}/${t.version}`) || !unique(dictionaries, d => d.name)
    || !unique(familyFacts, f => f.id) || !unique(units, u => u.id)) return null
  return { templates, dictionaries, familyFacts, units }
}
