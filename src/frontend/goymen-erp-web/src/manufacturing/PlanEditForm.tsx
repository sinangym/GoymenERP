/**
 * MFG-PLAN-EDIT: the Draft plan's edit form, inside the plan detail.
 *
 * It starts from the plan's CURRENT content — the last history entry, which carries the Inventory label version
 * accepted for each roll — and sends the new content as a whole. The work center, the planned inputs (keep, change kg,
 * remove, add from Inventory's candidate read) and the target may change; the process never does. The server decides
 * every rule again (`ProductionPlan.Revise`) and revalidates each roll's exact label version; this form only refuses
 * what it can already see (missing fields, a roll with no label version, a change of nothing) so nothing is sent for
 * it. Reads follow the create form's pattern: the auth boundary hears a 401/403 and a superseded read is dropped.
 *
 * M01.2: this form has no cut-line editor. A plan's stored cut lines (the current revision's) travel unchanged in every
 * revision it sends — same order, ids, sequences, lengths and counts — because the server would read an omitted
 * member as "no cut lines" and clear them.
 */

import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { fetchWorkCenters } from '../organization/api'
import type { WorkCenterSummary } from '../organization/contract'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { isKilogramText, type ProductionPlanView } from './contract'
import { PLAN_EDIT_TEXT } from './planEditText'
import type { CapturedPlanCommercialContext, PlanRevision } from './planRevisionContract'
import type { CutToLengthSpecRequest, PlanTargetBasis, RevisePlanRequest } from './planCommandApi'
import { fetchParentRollCandidates, type ParentRollCandidate, type ParentRollCandidates } from './planReferences'
import { PlanPreviewPanel } from './PlanPreviewPanel'
import { planPreviewInput, usePlanPreview } from './usePlanPreview'
import styles from './PlanPage.module.css'

type ReadState<T> =
  | { readonly type: 'idle' | 'loading' }
  | { readonly type: 'ready'; readonly data: T }
  | { readonly type: 'failed'; readonly message: string }

interface EditRow {
  readonly parentRollId: string
  /** Known only for a roll added from the candidate read in this form. */
  readonly labelCode: string | null
  readonly labelVersion: string | null
  /** Immutable commercial ownership/account provenance captured when this planned input was accepted. */
  readonly commercialContext: CapturedPlanCommercialContext | null
  readonly plannedKg: string
}

export interface PlanEditFormProps {
  readonly companyId: string
  readonly plan: ProductionPlanView
  readonly current: PlanRevision
  readonly busy: boolean
  readonly onSubmit: (request: RevisePlanRequest) => void
  readonly onCancel: () => void
}

function positiveKilograms(value: string): boolean {
  return isKilogramText(value) && /[1-9]/.test(value)
}

/** The server's lossless text: no insignificant trailing fractional zero. */
function canonicalKilograms(value: string): string {
  if (!value.includes('.')) return value
  return value.replace(/0+$/, '').replace(/\.$/, '')
}

function sameCommercialContext(
  current: CapturedPlanCommercialContext | null,
  sent: CapturedPlanCommercialContext | null,
): boolean {
  if (current === null || sent === null) return current === sent
  return (
    current.partyId.toLowerCase() === sent.partyId.toLowerCase() &&
    current.companyPartyAccountId.toLowerCase() === sent.companyPartyAccountId.toLowerCase() &&
    current.partyVersion === sent.partyVersion &&
    current.accountVersion === sent.accountVersion
  )
}

function readFailure(subject: string, outcome: { readonly type: string; readonly status?: number }): string {
  if (outcome.type === 'http') {
    if (outcome.status === 401 || outcome.status === 403) return `${subject} için yetkiniz artık geçerli değil.`
    return `${subject} kaynağı yanıt vermedi (${outcome.status}).`
  }
  if (outcome.type === 'offline') return 'Çevrimdışısınız.'
  if (outcome.type === 'fence-stale') return 'Şirket veya oturum değişti; eski sonuç kullanılmadı.'
  return outcome.type === 'malformed'
    ? `${subject} kaynağı beklenen sözleşmeyle yanıt vermedi.`
    : `${subject} kaynağına ulaşılamadı.`
}

/**
 * The current revision's stored cut lines as the one request member both the live preview and the revision send —
 * same order, ids, signed sequences, canonical lengths and counts — or null for a plan without lines.
 */
function storedCutToLengthSpec(current: PlanRevision): CutToLengthSpecRequest | null {
  if (current.cutToLengthSpec === null) return null
  return {
    lines: current.cutToLengthSpec.lines.map((line) => ({
      lineId: line.lineId,
      sequence: line.sequence,
      lengthMm: line.lengthMm,
      count: line.count,
    })),
  }
}

function sameContent(current: PlanRevision, request: RevisePlanRequest): boolean {
  const target = request.target
  const sameTarget = target === null
    ? current.target === null
    : current.target?.basis === target.basis && current.target.kg === canonicalKilograms(target.kg)
  return (
    current.workCenterId === request.workCenterId &&
    sameTarget &&
    current.inputs.length === request.inputs.length &&
    current.inputs.every((input, index) => {
      const sent = request.inputs[index]
      return (
        input.parentRollId === sent.parentRollId &&
        input.plannedKg === canonicalKilograms(sent.plannedKg) &&
        input.expectedLabelVersion === sent.expectedLabelVersion &&
        sameCommercialContext(input.commercialContext, sent.commercialContext ?? null)
      )
    })
  )
}

export function PlanEditForm({ companyId, plan, current, busy, onSubmit, onCancel }: PlanEditFormProps) {
  const authBoundary = useAuthBoundary()
  const { capture: captureSecurity, report: reportSecurity } = authBoundary
  const [workCenters, setWorkCenters] = useState<ReadState<readonly WorkCenterSummary[]>>({ type: 'loading' })
  const [workCenterId, setWorkCenterId] = useState(current.workCenterId)
  const [rows, setRows] = useState<readonly EditRow[]>(() =>
    current.inputs.map((input) => ({
      parentRollId: input.parentRollId,
      labelCode: null,
      labelVersion: input.expectedLabelVersion,
      commercialContext: input.commercialContext,
      plannedKg: input.plannedKg,
    })),
  )
  const [targetBasis, setTargetBasis] = useState<'' | PlanTargetBasis>(
    current.target?.basis === 'InputMass' || current.target?.basis === 'GoodOutputMass' ? current.target.basis : '',
  )
  const [targetKg, setTargetKg] = useState(current.target?.kg ?? '')
  const [reason, setReason] = useState('')
  const [search, setSearch] = useState('')
  const [candidates, setCandidates] = useState<ReadState<ParentRollCandidates>>({ type: 'idle' })
  const [formError, setFormError] = useState<string | null>(null)
  const candidateGeneration = useRef(0)

  useEffect(() => {
    const controller = new AbortController()
    async function read() {
      const security = captureSecurity()
      const outcome = await fetchWorkCenters(companyId, controller.signal)
      if (controller.signal.aborted) return
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        reportSecurity(outcome.status, security)
      }
      setWorkCenters(
        outcome.type === 'ok-single'
          ? { type: 'ready', data: outcome.data }
          : { type: 'failed', message: readFailure('İş merkezi', outcome) },
      )
    }
    void read()
    return () => {
      controller.abort()
      candidateGeneration.current += 1
    }
  }, [captureSecurity, companyId, reportSecurity])

  const readCandidates = useCallback(async () => {
    const generation = ++candidateGeneration.current
    setCandidates({ type: 'loading' })
    const security = captureSecurity()
    const outcome = await fetchParentRollCandidates(companyId, search)
    if (generation !== candidateGeneration.current) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      reportSecurity(outcome.status, security)
    }
    setCandidates(
      outcome.type === 'ok-single'
        ? { type: 'ready', data: outcome.data }
        : { type: 'failed', message: readFailure('Ana rulo', outcome) },
    )
  }, [captureSecurity, companyId, reportSecurity, search])

  // M01.2: the stored cut lines the revision carries unchanged; the preview answers for them too.
  const cutToLengthSpec = storedCutToLengthSpec(current)

  // AT-061 / AT-062: the same button-less backend preview as the create form, for the content being edited. It writes
  // nothing and never gates the revision below (the reason is not part of what is calculated).
  const preview = usePlanPreview(companyId, planPreviewInput(
    workCenterId,
    rows.map((row) => ({
      parentRollId: row.parentRollId,
      label: row.labelCode ?? row.parentRollId,
      labelVersion: row.labelVersion,
      commercialContext: row.commercialContext,
      plannedKg: row.plannedKg,
    })),
    targetBasis,
    targetKg,
    undefined,
    cutToLengthSpec,
  ), authBoundary)

  function labelOf(parentRollId: string): string {
    return rows.find((row) => row.parentRollId === parentRollId)?.labelCode ?? parentRollId
  }

  function workCenterLabel(id: string): string {
    const found = workCenters.type === 'ready' ? workCenters.data.find((workCenter) => workCenter.id === id) : undefined
    return found === undefined ? id : `${found.code} — ${found.name}`
  }

  function addCandidate(candidate: ParentRollCandidate) {
    if (!candidate.draftSelectable) return
    setRows((existing) => {
      const index = existing.findIndex((row) => row.parentRollId === candidate.labelId)
      if (index < 0) {
        return [...existing, {
          parentRollId: candidate.labelId,
          labelCode: candidate.labelCode,
          labelVersion: candidate.labelVersion,
          commercialContext: null,
          plannedKg: '',
        }]
      }
      // A kept roll re-read from Inventory: its current label version replaces the one the plan accepted.
      return existing.map((row, position) => position === index
        ? { ...row, labelCode: candidate.labelCode, labelVersion: candidate.labelVersion }
        : row)
    })
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    if (workCenterId === '' || rows.length === 0 || rows.some((row) => !positiveKilograms(row.plannedKg))) {
      setFormError(PLAN_EDIT_TEXT.formRequired)
      return
    }
    if (rows.some((row) => row.labelVersion === null)) {
      setFormError(PLAN_EDIT_TEXT.legacyRequired)
      return
    }
    if ((targetBasis === '') !== (targetKg === '') || (targetKg !== '' && !positiveKilograms(targetKg))) {
      setFormError(PLAN_EDIT_TEXT.targetPair)
      return
    }
    const trimmed = reason.trim()
    if (trimmed.length === 0 || trimmed.length > 256) {
      setFormError(PLAN_EDIT_TEXT.reasonRequired)
      return
    }
    const request: RevisePlanRequest = {
      expectedVersion: plan.version,
      workCenterId,
      inputs: rows.map((row) => ({
        parentRollId: row.parentRollId,
        plannedKg: row.plannedKg,
        expectedLabelVersion: row.labelVersion as string,
        commercialContext: row.commercialContext,
      })),
      target: targetBasis === '' ? null : { basis: targetBasis, kg: targetKg },
      reason: trimmed,
      ...(cutToLengthSpec === null ? {} : { cutToLengthSpec }),
    }
    if (sameContent(current, request)) {
      setFormError(PLAN_EDIT_TEXT.noChange)
      return
    }
    setFormError(null)
    onSubmit(request)
  }

  const knownWorkCenter = workCenters.type === 'ready'
    && workCenters.data.some((workCenter) => workCenter.id === current.workCenterId)

  return (
    <section className={styles.commandCard} aria-labelledby="manufacturing-plan-edit-heading">
      <h2 id="manufacturing-plan-edit-heading">{PLAN_EDIT_TEXT.formHeading}</h2>
      {formError === null ? null : <p className={styles.error} role="alert">{formError}</p>}
      <form className={styles.form} onSubmit={submit} noValidate>
        <fieldset disabled={busy}>
          <legend>İş merkezi</legend>
          <p className={styles.note}>{PLAN_EDIT_TEXT.processNote(plan.process)}</p>
          {workCenters.type === 'loading' ? <p role="status">İş merkezleri yükleniyor…</p> : null}
          {workCenters.type === 'failed' ? <p role="alert">{workCenters.message}</p> : null}
          <select aria-label="İş merkezi" value={workCenterId} onChange={(event) => setWorkCenterId(event.target.value)}>
            {knownWorkCenter ? null : <option value={current.workCenterId}>{current.workCenterId} (mevcut)</option>}
            {workCenters.type === 'ready'
              ? workCenters.data.map((workCenter) => (
                  <option key={workCenter.id} value={workCenter.id}>
                    {workCenter.code} — {workCenter.name}{workCenter.isActive ? '' : ' (pasif)'}
                    {workCenter.id === current.workCenterId ? ' (mevcut)' : ''}
                  </option>
                ))
              : null}
          </select>
        </fieldset>

        <fieldset disabled={busy}>
          <legend>Planlanan girdiler</legend>
          <p className={styles.note}>{PLAN_EDIT_TEXT.inputsNote}</p>
          {rows.length === 0 ? <p>Ana rulo kalmadı; en az bir ana rulo ekleyin.</p> : (
            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">Ana rulo</th>
                    <th scope="col">Etiket sürümü</th>
                    <th scope="col">Planlanan kg</th>
                    <th scope="col">İşlem</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => {
                    const name = row.labelCode ?? row.parentRollId
                    return (
                      <tr key={row.parentRollId}>
                        <th scope="row">
                          {row.labelCode === null ? null : <>{row.labelCode}<br /></>}
                          <span className={styles.mono}>{row.parentRollId}</span>
                        </th>
                        <td>{row.labelVersion ?? <span className={styles.error}>{PLAN_EDIT_TEXT.legacyLabel}</span>}</td>
                        <td>
                          <input
                            aria-label={`${name} planlanan kg`}
                            inputMode="decimal"
                            value={row.plannedKg}
                            onChange={(event) => {
                              const value = event.target.value
                              setRows((existing) => existing.map((candidate) =>
                                candidate.parentRollId === row.parentRollId ? { ...candidate, plannedKg: value } : candidate))
                            }}
                          />
                        </td>
                        <td>
                          <button
                            type="button"
                            aria-label={`${name} kaldır`}
                            onClick={() => setRows((existing) =>
                              existing.filter((candidate) => candidate.parentRollId !== row.parentRollId))}
                          >
                            Kaldır
                          </button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
          <div className={styles.searchRow}>
            <label htmlFor="plan-edit-roll-search">Eklenecek etiket, ürün veya lot ara</label>
            <input id="plan-edit-roll-search" maxLength={64} value={search} onChange={(event) => setSearch(event.target.value)} />
            <button type="button" onClick={() => void readCandidates()}>Ana rulo ara</button>
          </div>
          {candidates.type === 'loading' ? <p role="status">Ana rulolar yükleniyor…</p> : null}
          {candidates.type === 'failed' ? <p role="alert">{candidates.message}</p> : null}
          {candidates.type === 'ready' ? (
            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <thead>
                  <tr><th scope="col">Etiket</th><th scope="col">Sürüm</th><th scope="col">Fiziksel kg</th><th scope="col">Seçim</th></tr>
                </thead>
                <tbody>
                  {candidates.data.items.map((candidate) => {
                    const row = rows.find((existing) => existing.parentRollId === candidate.labelId)
                    const label = !candidate.draftSelectable
                      ? 'Seçilemez'
                      : row === undefined
                        ? 'Ekle'
                        : row.labelVersion === candidate.labelVersion
                          ? 'Planda'
                          : 'Sürümü güncelle'
                    return (
                      <tr key={candidate.labelId}>
                        <th scope="row">{candidate.labelCode}<div className={styles.mono}>{candidate.labelId}</div></th>
                        <td>{candidate.labelVersion}</td>
                        <td>{candidate.physicalKg} kg</td>
                        <td>
                          <button
                            type="button"
                            disabled={label === 'Seçilemez' || label === 'Planda'}
                            aria-label={`${candidate.labelCode} ${label}`}
                            onClick={() => addCandidate(candidate)}
                          >
                            {label}
                          </button>
                          {!candidate.draftSelectable ? <div className={styles.muted}>{candidate.draftRefusalCode}</div> : null}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          ) : null}
        </fieldset>

        <fieldset disabled={busy}>
          <legend>İsteğe bağlı hedef</legend>
          <div className={styles.formGrid}>
            <label htmlFor="plan-edit-target-basis">Hedef türü</label>
            <select
              id="plan-edit-target-basis"
              value={targetBasis}
              onChange={(event) => setTargetBasis(event.target.value as '' | PlanTargetBasis)}
            >
              <option value="">Hedef yok</option>
              <option value="InputMass">Girdi kütlesi</option>
              <option value="GoodOutputMass">İyi ürün kütlesi</option>
            </select>
            <label htmlFor="plan-edit-target-kg">Hedef kg</label>
            <input id="plan-edit-target-kg" inputMode="decimal" value={targetKg} onChange={(event) => setTargetKg(event.target.value)} />
          </div>
        </fieldset>

        <PlanPreviewPanel state={preview} labelOf={labelOf} workCenterLabel={workCenterLabel} />

        <fieldset disabled={busy}>
          <legend>{PLAN_EDIT_TEXT.reasonLabel}</legend>
          <label htmlFor="plan-edit-reason">{PLAN_EDIT_TEXT.reasonLabel}</label>
          <input
            id="plan-edit-reason"
            maxLength={256}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
          <p className={styles.muted}>{PLAN_EDIT_TEXT.reasonHelp}</p>
        </fieldset>

        <div className={styles.actions}>
          <button type="submit" disabled={busy} aria-busy={busy}>
            {busy ? PLAN_EDIT_TEXT.saving : PLAN_EDIT_TEXT.save}
          </button>
          <button type="button" disabled={busy} onClick={onCancel}>{PLAN_EDIT_TEXT.cancel}</button>
        </div>
      </form>
    </section>
  )
}
