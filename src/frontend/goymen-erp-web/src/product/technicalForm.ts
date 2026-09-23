import type { ProductReferenceWrite } from './api'
import type { ProductReferenceOptions, ProductReferences } from './contract'
import type { TemplateField } from './technicalContract'

export const TECHNICAL_TEXT = {
  heading: 'Teknik özellikler', unavailable: 'Bu aile için yayımlanmış teknik şablon okunamıyor.',
  choose: 'Seçiniz', candidate: 'Aday', stock: 'Stok birimi (kg)', purchase: 'Alış birimi', sales: 'Satış birimi',
  unitUnavailable: 'Birim kataloğu okunamıyor.', invalidMeasure: 'Sıfırdan büyük, en fazla üç ondalıklı mm değeri giriniz.',
  invalidChoice: 'Yayımlanmış listeden geçerli bir değer seçiniz.', candidateRequired: 'Aday değer zorunlu alanı karşılamaz.',
  kilogram: 'Stok birimi kilogram olmalıdır.',
} as const

export interface TechnicalDraft {
  readonly sourceTemplateId: string | null
  readonly sourceTemplateVersion: string | null
  readonly attributes: Readonly<Record<string, unknown>>
  readonly stock: string
  readonly purchase: string
  readonly sales: string
  readonly attributesChanged: boolean
  readonly stockChanged: boolean
  readonly purchaseChanged: boolean
  readonly salesChanged: boolean
}
export function technicalDraft(references?: ProductReferences): TechnicalDraft {
  return { attributes: references?.attributes ?? {}, stock: references?.units.stock ?? '',
    sourceTemplateId: references?.templateId ?? null, sourceTemplateVersion: references?.templateVersion ?? null,
    purchase: references?.units.purchase ?? '', sales: references?.units.sales ?? '',
    attributesChanged: false, stockChanged: false, purchaseChanged: false, salesChanged: false }
}
export const technicalDirty = (draft: TechnicalDraft) => draft.attributesChanged || draft.stockChanged || draft.purchaseChanged || draft.salesChanged
export function familyTemplate(options: ProductReferenceOptions | null, familyId: string) {
  if (options?.unavailableCatalogs.includes('templates') || options?.unavailableCatalogs.includes('families')) return undefined
  const family = options?.technical?.familyFacts.find(f => f.id === familyId)
  return options?.technical?.templates.find(t => t.id === family?.templateId && t.version === family?.templateVersion)
}
export function fieldApplies(field: TemplateField, attributes: Readonly<Record<string, unknown>>) {
  return field.appliesWhenField === null || attributes[field.appliesWhenField] === field.appliesWhenCode
}
export function editableTemplate(options: ProductReferenceOptions | null, familyId: string, draft: TechnicalDraft) {
  const template = familyTemplate(options, familyId)
  return draft.sourceTemplateId !== null && (template?.id !== draft.sourceTemplateId || template?.version !== draft.sourceTemplateVersion)
    ? undefined : template
}

/** Mirrors released field constraints. Draft omission stays omission, never an invented default. */
export function technicalWrite(draft: TechnicalDraft, options: ProductReferenceOptions | null, familyId: string):
  { readonly ok: true; readonly references: ProductReferenceWrite } | { readonly ok: false; readonly errors: Readonly<Record<string, string>> } {
  const errors: Record<string, string> = {}
  const references: { templateId?: string | null; templateVersion?: string | null;
    attributes?: Record<string, unknown>; units?: { stock?: string | null; purchase?: string | null; sales?: string | null } } = {}
  if (draft.attributesChanged) {
    const template = editableTemplate(options, familyId, draft)
    if (familyId === '' && Object.keys(draft.attributes).length === 0) {
      references.templateId = null
      references.templateVersion = null
      references.attributes = {}
    } else if (!template) errors['references.attributes'] = TECHNICAL_TEXT.unavailable
    else {
      references.templateId = template.id
      references.templateVersion = template.version
      references.attributes = {}
      if (Object.keys(draft.attributes).some(key => !template.fields.some(f => f.key === key)))
        errors['references.attributes'] = TECHNICAL_TEXT.unavailable
      for (const field of template.fields) {
        if (!fieldApplies(field, draft.attributes)) continue
        const raw = draft.attributes[field.key]
        if (raw === undefined || raw === '') continue
        const path = `references.attributes.${field.key}`
        if (field.kind === 'measureMillimetre') {
          const value = String(raw).trim().replace(',', '.')
          if (!/^\d+(\.\d{1,3})?$/.test(value) || !Number.isFinite(Number(value)) || Number(value) <= 0
            || !Number.isSafeInteger(Math.round(Number(value) * 1000))) errors[path] = TECHNICAL_TEXT.invalidMeasure
          else references.attributes[field.key] = Number(value)
        } else {
          const entry = options?.technical?.dictionaries.find(d => d.name === field.dictionary)?.entries.find(e => e.code === raw)
          if (!entry || (field.parentField !== null && entry.parentCode !== null && entry.parentCode !== draft.attributes[field.parentField])
            || (entry.status === 'candidate' && !field.allowsCandidate)) errors[path] = TECHNICAL_TEXT.invalidChoice
          else if (field.required && entry.status === 'candidate') errors[path] = TECHNICAL_TEXT.candidateRequired
          else references.attributes[field.key] = raw
        }
      }
    }
  }
  for (const role of ['stock', 'purchase', 'sales'] as const) {
    if (!draft[`${role}Changed`]) continue
    const value = draft[role]
    const unit = options?.technical?.units.find(u => u.id === value)
    if (value !== '' && (!unit || options?.unavailableCatalogs.includes('units')))
      errors[`references.units.${role}`] = TECHNICAL_TEXT.invalidChoice
    else if (role === 'stock' && unit && !unit.isKilogram) errors['references.units.stock'] = TECHNICAL_TEXT.kilogram
    else references.units = { ...references.units, [role]: value === '' ? null : value }
  }
  return Object.keys(errors).length ? { ok: false, errors } : { ok: true, references }
}
