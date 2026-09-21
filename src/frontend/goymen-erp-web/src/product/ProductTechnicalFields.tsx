import type { ProductReferenceOptions } from './contract'
import { editableTemplate, fieldApplies, TECHNICAL_TEXT, technicalDraft, type TechnicalDraft } from './technicalForm'
import styles from './ProductCreatePage.module.css'

export function ProductTechnicalFields({ familyId, options, value, frozen, onChange, errors = {}, fieldRef }: {
  readonly familyId: string; readonly options: ProductReferenceOptions | null
  readonly value: TechnicalDraft | undefined; readonly frozen: boolean
  readonly onChange: (value: TechnicalDraft) => void
  readonly errors?: Readonly<Record<string, string>>
  readonly fieldRef?: (element: HTMLFieldSetElement | null) => void
}) {
  const draft = value ?? technicalDraft()
  const template = editableTemplate(options, familyId, draft)
  function changeAttribute(key: string, value: string) {
    const attributes = { ...draft.attributes, [key]: value }
    // Remove children/conditional fields that no longer apply after this explicit choice.
    const removed = new Set([key])
    for (let pass = 0; pass < (template?.fields.length ?? 0); pass++) {
      for (const field of template?.fields ?? []) {
        if (field.key !== key && ((field.parentField !== null && removed.has(field.parentField))
          || !fieldApplies(field, attributes))) {
          delete attributes[field.key]
          removed.add(field.key)
        }
      }
    }
    onChange({ ...draft, attributes, attributesChanged: true })
  }
  return <fieldset id="references" ref={fieldRef} className={styles.roles} disabled={frozen} tabIndex={-1}>
    <legend>{TECHNICAL_TEXT.heading}</legend>
    {familyId !== '' && !template ? <p role="status">{TECHNICAL_TEXT.unavailable}</p> : null}
    {template ? <p>{template.displayName}</p> : null}
    {template?.fields.filter(field => fieldApplies(field, draft.attributes)).map(field => {
      const id = `references.attributes.${field.key}`
      const entries = options?.technical?.dictionaries.find(d => d.name === field.dictionary)?.entries ?? []
      return <div className={styles.field} key={field.key}>
        <label htmlFor={id}>{field.key}{field.kind === 'measureMillimetre' ? ' (mm)' : ''}{field.required ? ' *' : ''}</label>
        {field.kind === 'measureMillimetre'
          ? <input id={id} inputMode="decimal" value={String(draft.attributes[field.key] ?? '')}
            onChange={e => changeAttribute(field.key, e.target.value)} aria-invalid={Boolean(errors[id]) || undefined} />
          : <select id={id} value={String(draft.attributes[field.key] ?? '')}
            onChange={e => changeAttribute(field.key, e.target.value)} aria-invalid={Boolean(errors[id]) || undefined}>
            <option value="">{TECHNICAL_TEXT.choose}</option>
            {entries.filter(e => field.parentField === null || e.parentCode === null || e.parentCode === draft.attributes[field.parentField]).map(entry =>
              <option key={entry.code} value={entry.code}
                disabled={entry.status === 'candidate' && (field.required || !field.allowsCandidate)}>
                {entry.displayName}{entry.status === 'candidate' ? ` (${TECHNICAL_TEXT.candidate})` : ''}
              </option>)}
          </select>}
        {errors[id] ? <p role="alert">{errors[id]}</p> : null}
      </div>
    })}
    {(['stock', 'sales'] as const).map(role => <div className={styles.field} key={role}>
      <label htmlFor={`references.units.${role}`}>{TECHNICAL_TEXT[role]}</label>
      <select id={`references.units.${role}`} value={draft[role]}
        disabled={!options?.technical || options.unavailableCatalogs.includes('units')}
        onChange={e => onChange({ ...draft, [role]: e.target.value, [`${role}Changed`]: true })}>
        <option value="">{TECHNICAL_TEXT.choose}</option>
        {!options?.technical?.units.some(u => u.id === draft[role]) && draft[role] !== ''
          ? <option value={draft[role]} disabled>{TECHNICAL_TEXT.unitUnavailable}</option> : null}
        {options?.technical?.units.filter(u => role !== 'stock' || u.isKilogram).map(unit =>
          <option key={unit.id} value={unit.id}>{unit.displayName} ({unit.symbol})</option>)}
      </select>
      {errors[`references.units.${role}`] ? <p role="alert">{errors[`references.units.${role}`]}</p> : null}
    </div>)}
    {errors['references.attributes'] ? <p role="alert">{errors['references.attributes']}</p> : null}
  </fieldset>
}
