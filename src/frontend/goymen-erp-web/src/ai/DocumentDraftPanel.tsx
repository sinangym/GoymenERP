/**
 * WP-B3 — belgeden taslak (AI panelinde). Kullanıcı bir dosya seçer veya metin yapıştırır, taslak türünü seçer; sunucu
 * belgeyi kademeli okur ve alanları deterministik doğrular. Ekran yalnız sunucunun döndürdüğü taslağı gösterir: her
 * alan "doğrulandı / doğrulanmadı / hatalı" durumuyla ve nedeniyle. Taslak kayıt oluşturmaz; kullanıcı sahip modülün
 * oluşturma ekranına taslakla gider (yalnız o ekranın yetkisi varsa). Belge metni ekranda yalnız düz metin olarak
 * gösterilir; hiçbir içerik işaretleme/bağlantı olarak çizilmez.
 */

import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { createDocumentDraft } from './aiApi'
import { problemCode } from './aiContract'
import {
  AI_DOCUMENT_DRAFT_STATE_KEY,
  DRAFT_KINDS,
  DRAFT_PROBLEM_CODE,
  DRAFT_TEXT_SOURCES,
  draftInvalidFields,
  draftUnreadableReasons,
  mediaTypeOf,
  type DocumentDraft,
  type DraftField,
  type DraftInvalidField,
  type DraftKind,
  type DraftLine,
  type DraftRequest,
  type DraftScreenCode,
  type DraftTextSource,
} from './documentDraftContract'
import {
  DRAFT_FIELD_LABEL,
  DRAFT_FINDING_LABEL,
  DRAFT_KIND_LABEL,
  DRAFT_REASON_LABEL,
  DRAFT_STATUS_LABEL,
  DRAFT_TEXT,
  DRAFT_TEXT_SOURCE_LABEL,
  DRAFT_TIER_LABEL,
  DRAFT_UNREADABLE_LABEL,
} from './documentDraftText'
import styles from './AiPanel.module.css'

/** The shell capability (and so the route) each owner screen code continues on. */
const SCREEN_CAPABILITY: Readonly<Record<DraftScreenCode, keyof typeof CAPABILITY>> = {
  'sales.drafts.create': 'salesDraftsCreate',
  'purchasing.invoices.import': 'purchasingInvoicesList',
  'purchasing.orders.list': 'purchasingOrdersList',
}


type DraftView =
  | { readonly kind: 'idle' }
  | { readonly kind: 'pending' }
  | { readonly kind: 'drafted'; readonly draft: DocumentDraft }
  | { readonly kind: 'invalid'; readonly fields: readonly DraftInvalidField[] }
  | { readonly kind: 'notice'; readonly title: string; readonly body: string; readonly reasons?: readonly string[] }
  | { readonly kind: 'failed'; readonly outcome: ViewOutcome<DocumentDraft> }

function viewOfDraftOutcome(outcome: ViewOutcome<DocumentDraft>): DraftView {
  if (outcome.type === 'ok-single') return { kind: 'drafted', draft: outcome.data }
  if (outcome.type !== 'http') return { kind: 'failed', outcome }
  const code = problemCode(outcome.body)
  if (outcome.status === 400 && code === DRAFT_PROBLEM_CODE.invalid) return { kind: 'invalid', fields: draftInvalidFields(outcome.body) }
  if (outcome.status === 422 && code === DRAFT_PROBLEM_CODE.unreadable) {
    return { kind: 'notice', title: DRAFT_TEXT.unreadableTitle, body: DRAFT_TEXT.unreadableBody, reasons: draftUnreadableReasons(outcome.body) }
  }
  if (outcome.status === 503 && code === DRAFT_PROBLEM_CODE.disabled) {
    return { kind: 'notice', title: DRAFT_TEXT.disabledTitle, body: DRAFT_TEXT.disabledBody }
  }
  if (outcome.status === 403) return { kind: 'notice', title: DRAFT_TEXT.refusedTitle, body: DRAFT_TEXT.refusedBody }
  return { kind: 'failed', outcome }
}

async function toBase64(file: Blob): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer())
  let binary = ''
  const chunk = 0x8000
  for (let index = 0; index < bytes.length; index += chunk) {
    binary += String.fromCharCode(...bytes.subarray(index, index + chunk))
  }
  return btoa(binary)
}

export function DocumentDraftPanel({ companyId }: { readonly companyId: string }) {
  const boundary = useAuthBoundary()
  const [kind, setKind] = useState<DraftKind>('sales-order')
  const [mode, setMode] = useState<'file' | 'text'>('file')
  const [file, setFile] = useState<File | null>(null)
  const [text, setText] = useState('')
  const [textSource, setTextSource] = useState<DraftTextSource>('email')
  const [localInvalid, setLocalInvalid] = useState<readonly DraftInvalidField[]>([])
  const [view, setView] = useState<DraftView>({ kind: 'idle' })
  const inFlight = useRef<AbortController | null>(null)

  useEffect(() => () => inFlight.current?.abort(), [])

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (view.kind === 'pending') return
    const invalid: DraftInvalidField[] = []
    const mediaType = file === null ? null : mediaTypeOf(file)
    if (mode === 'file' && (file === null || mediaType === null)) invalid.push('content')
    if (mode === 'text' && text.trim() === '') invalid.push('text')
    setLocalInvalid(invalid)
    if (invalid.length > 0) return

    inFlight.current?.abort()
    const controller = new AbortController()
    inFlight.current = controller
    setView({ kind: 'pending' })
    const request: DraftRequest =
      mode === 'file' && file !== null && mediaType !== null
        ? { draftKind: kind, mediaType, contentBase64: await toBase64(file) }
        : { draftKind: kind, text, textSource }
    if (controller.signal.aborted || inFlight.current !== controller) return
    const snapshot = boundary.capture()
    const outcome = await createDocumentDraft(request, companyId, controller.signal)
    if (controller.signal.aborted || inFlight.current !== controller) return
    inFlight.current = null
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    setView(viewOfDraftOutcome(outcome))
  }

  const invalidFields = view.kind === 'invalid' ? [...localInvalid, ...view.fields] : localInvalid
  const isInvalid = (field: DraftInvalidField) => invalidFields.includes(field)
  const pending = view.kind === 'pending'

  return (
    <div className={styles.ask} data-ai-section="document-draft">
      <p className={styles.hint}>{DRAFT_TEXT.notice}</p>
      <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate aria-busy={pending || undefined}>
        <label className={styles.field}>
          {DRAFT_TEXT.kindLabel}
          <select value={kind} aria-invalid={isInvalid('draftKind') || undefined} onChange={(event) => setKind(event.target.value as DraftKind)}>
            {DRAFT_KINDS.map((option) => (
              <option key={option} value={option}>
                {DRAFT_KIND_LABEL[option]}
              </option>
            ))}
          </select>
        </label>
        {isInvalid('draftKind') ? <p className={styles.fieldError}>{DRAFT_TEXT.kindInvalid}</p> : null}
        <fieldset className={styles.field}>
          <legend>{DRAFT_TEXT.modeLabel}</legend>
          <label>
            <input type="radio" name="ai-draft-mode" checked={mode === 'file'} onChange={() => setMode('file')} /> {DRAFT_TEXT.modeFile}
          </label>
          <label>
            <input type="radio" name="ai-draft-mode" checked={mode === 'text'} onChange={() => setMode('text')} /> {DRAFT_TEXT.modeText}
          </label>
        </fieldset>
        {mode === 'file' ? (
          <>
            <label className={styles.field}>
              {DRAFT_TEXT.fileLabel}
              <input
                type="file"
                accept=".xml,.csv,.xlsx,.pdf,.png,.jpg,.jpeg,.tif,.tiff"
                aria-invalid={isInvalid('content') || isInvalid('mediaType') || undefined}
                onChange={(event) => setFile(event.target.files?.[0] ?? null)}
              />
            </label>
            {isInvalid('content') ? <p className={styles.fieldError}>{DRAFT_TEXT.fileInvalid}</p> : null}
            {isInvalid('mediaType') ? <p className={styles.fieldError}>{DRAFT_TEXT.mediaTypeInvalid}</p> : null}
          </>
        ) : (
          <>
            <label className={styles.field}>
              {DRAFT_TEXT.textLabel}
              <textarea value={text} rows={6} aria-invalid={isInvalid('text') || undefined} onChange={(event) => setText(event.target.value)} />
            </label>
            {isInvalid('text') ? <p className={styles.fieldError}>{DRAFT_TEXT.textInvalid}</p> : null}
            <label className={styles.field}>
              {DRAFT_TEXT.textSourceLabel}
              <select value={textSource} onChange={(event) => setTextSource(event.target.value as DraftTextSource)}>
                {DRAFT_TEXT_SOURCES.map((option) => (
                  <option key={option} value={option}>
                    {DRAFT_TEXT_SOURCE_LABEL[option]}
                  </option>
                ))}
              </select>
            </label>
          </>
        )}
        <button type="submit" className={styles.submitButton} disabled={pending}>
          {pending ? DRAFT_TEXT.submitting : DRAFT_TEXT.submit}
        </button>
      </form>
      <DraftResult view={view} />
    </div>
  )
}

function DraftResult({ view }: { readonly view: DraftView }) {
  switch (view.kind) {
    case 'pending':
      return <StateBlock state={{ kind: 'initial-loading' }} subject={DRAFT_TEXT.stateSubject} />
    case 'failed':
      return <StateBlock state={classifyOutcome(view.outcome)} subject={DRAFT_TEXT.stateSubject} />
    case 'notice':
      return (
        <div className={styles.state} role="alert" data-ai-draft-state="notice">
          <p className={styles.stateTitle}>{view.title}</p>
          <p>{view.body}</p>
          {view.reasons?.map((reason) =>
            DRAFT_UNREADABLE_LABEL[reason] ? <p key={reason} className={styles.hint}>{DRAFT_UNREADABLE_LABEL[reason]}</p> : null,
          )}
        </div>
      )
    case 'drafted':
      return <DraftView draft={view.draft} />
    default:
      return null
  }
}

function DraftView({ draft }: { readonly draft: DocumentDraft }) {
  const state = useShellState()
  const navigate = useNavigate()
  const capabilityKey = SCREEN_CAPABILITY[draft.openIn.screenCode]
  const capabilityId = CAPABILITY[capabilityKey]
  const canOpen = hasCapability(state, capabilityId)
  const header: readonly DraftField[] = [
    draft.documentNumber,
    draft.documentDate,
    draft.currency,
    draft.counterparty.taxId,
    draft.counterparty.name,
    draft.payableAmount,
  ]

  return (
    <section className={styles.answer} aria-labelledby="ai-draft-heading" data-ai-draft="drafted">
      <h3 id="ai-draft-heading" className={styles.answerHeading}>
        {`${DRAFT_TEXT.resultHeading}: ${DRAFT_KIND_LABEL[draft.draftKind] ?? draft.draftKind}`}
      </h3>
      <p className={styles.hint}>{`${DRAFT_TEXT.tierLabel}: ${DRAFT_TIER_LABEL[draft.readingTier] ?? draft.readingTier}`}</p>
      {draft.quarantineFindings.length > 0 ? (
        <div className={styles.state} role="alert" data-ai-draft-quarantine="true">
          <p className={styles.stateTitle}>{DRAFT_TEXT.quarantineTitle}</p>
          <p>{DRAFT_TEXT.quarantineBody}</p>
          <ul>
            {draft.quarantineFindings.map((finding) => (
              <li key={finding}>{DRAFT_FINDING_LABEL[finding] ?? finding}</li>
            ))}
          </ul>
        </div>
      ) : null}
      <h4>{DRAFT_TEXT.headerHeading}</h4>
      <FieldTable fields={header} caption={DRAFT_TEXT.headerHeading} />
      {draft.counterparty.matchedPartyId !== null ? <p className={styles.hint}>{DRAFT_TEXT.matchedParty}</p> : null}
      <h4>{DRAFT_TEXT.linesHeading}</h4>
      {draft.lines.length === 0 ? <p>{DRAFT_TEXT.linesEmpty}</p> : draft.lines.map((line) => <LineView key={line.lineNumber} line={line} />)}
      <p className={styles.hint}>{`${DRAFT_TEXT.sourceLabel}: ${draft.source.sha256.slice(0, 12)}`}</p>
      {canOpen ? (
        <button
          type="button"
          className={styles.submitButton}
          onClick={() => void navigate(CAPABILITY_ROUTE[capabilityId], { state: { [AI_DOCUMENT_DRAFT_STATE_KEY]: draft } })}
        >
          {DRAFT_TEXT.openIn}
        </button>
      ) : (
        <p className={styles.hint}>{DRAFT_TEXT.openInUnavailable}</p>
      )}
    </section>
  )
}

function LineView({ line }: { readonly line: DraftLine }) {
  const fields = [
    line.productCode, line.description, line.quantity, line.unit, line.unitPrice, line.lineAmount,
    line.section, line.wallThickness, line.length,
  ].filter((field) => field.value !== null || field.reasonCode !== null)
  const caption = `${DRAFT_TEXT.lineLabel} ${line.lineNumber}`
  return (
    <article className={styles.result} data-ai-draft-line={line.lineNumber}>
      <FieldTable fields={fields} caption={caption} />
      {line.matchedProductId !== null ? <p className={styles.hint}>{DRAFT_TEXT.matchedProduct}</p> : null}
    </article>
  )
}

function FieldTable({ fields, caption }: { readonly fields: readonly DraftField[]; readonly caption: string }) {
  return (
    <table className={styles.source}>
      <caption>{caption}</caption>
      <thead>
        <tr>
          <th scope="col">{DRAFT_TEXT.fieldColumn}</th>
          <th scope="col">{DRAFT_TEXT.valueColumn}</th>
          <th scope="col">{DRAFT_TEXT.statusColumn}</th>
          <th scope="col">{DRAFT_TEXT.reasonColumn}</th>
        </tr>
      </thead>
      <tbody>
        {fields.map((field) => (
          <tr key={field.code} data-ai-draft-field={field.code} data-ai-draft-status={field.status}>
            <th scope="row">{DRAFT_FIELD_LABEL[field.code] ?? field.code}</th>
            {/* Document content is plain text, never markup. */}
            <td>{field.value ?? DRAFT_TEXT.emptyValue}</td>
            <td>{DRAFT_STATUS_LABEL[field.status]}</td>
            <td>{field.reasonCode === null ? '' : (DRAFT_REASON_LABEL[field.reasonCode] ?? field.reasonCode)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
