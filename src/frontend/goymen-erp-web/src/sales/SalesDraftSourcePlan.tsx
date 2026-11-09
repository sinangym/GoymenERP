/**
 * SAT-D251..SAT-D255 (EK50) / API-11 — "Kaynak planı" on the order detail's allocation tab.
 *
 *  - Read: `GET /sales/drafts/{id}/source-plan` for exactly the detail on screen (strict decoder); every state of
 *    `RemoteViewState` through `StateBlock`; 401/403 go to the shell's auth boundary.
 *  - Every figure is the server's: the locked (delivered) part, the stock part the allocation owner answers ("okunamadı" when it
 *    cannot be read), the suggestion ("kullanılamıyor" while no free-stock source exists) and the warnings. The screen sums
 *    nothing: the SAT-D252 equality is judged by the server (422 `sales_source_plan_sum_mismatch`).
 *  - Write: behind `sales.orders.source-plan.write` (shell capability only, never a role). Only the lines the server answers as
 *    writable take input (production, purchasing, subcontract); only the lines the person touched are sent.
 *  - SAT-D254: an open linked production/purchasing document opens a confirmation first and the save names
 *    `confirmLinkedSupply: true`; an unreadable link blocks the save.
 *  - One idempotent attempt per plan snapshot (`useSourcePlanAttempt`): an unknown outcome keeps its key and "tekrar gönder"
 *    resends the same snapshot; a recorded plan or a conflict re-reads the detail and the plan.
 */
import { useId, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftSourcePlan } from './api'
import { userDisplayFor, type DraftDetail, type UserDisplay } from './contract'
import { formatInstant } from './format'
import { SALES_SOURCE_PLAN_WRITE_CAPABILITY } from './capabilities'
import { isOutcomeStillUnknown } from './orderAttempt'
import { SOURCE_PLAN_TEXT as T } from './salesText'
import {
  SOURCE_PLAN_LINK_STATUS,
  SOURCE_PLAN_STOCK_STATUS,
  SOURCE_PLAN_WARNING,
  type SourcePlan,
  type SourcePlanLine,
  type SourcePlanLineRequest,
  type SourcePlanRequest,
} from './sourcePlan'
import { sourcePlanFrozen, sourcePlanNotice, useSourcePlanAttempt, type SourcePlanController } from './sourcePlanAttempt'
import { orderUnitCodes } from './unitNames'
import { UnitName } from './UnitName'
import { parseDecimalInput } from './wirePrimitives'
import { StateBlock } from '../shell/StateBlock'
import { hasCapability, type ShellState } from '../shell/shellState'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

const PARTS = ['production', 'purchasing', 'subcontract'] as const
type Part = (typeof PARTS)[number]
type LineDraft = Partial<Record<Part, string>>

export function SalesDraftSourcePlan({
  shell,
  detail,
  companyId,
  namespace,
  routeEntryGeneration,
  onRecorded,
  userDisplays = [],
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly onRecorded: () => void
  /** MOD-08: the people the detail names; an id without an entry is shown as it is stored. */
  readonly userDisplays?: readonly UserDisplay[]
}) {
  const boundary = useAuthBoundary()
  const headingId = useId()
  const actorId = shell.activeContext?.userId ?? ''
  const [drafts, setDrafts] = useState<Record<string, LineDraft>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pendingConfirm, setPendingConfirm] = useState<SourcePlanRequest | null>(null)
  const controller = useSourcePlanAttempt({
    shell,
    draftId: detail.draft.id,
    companyId,
    onRecorded: (recorded) => {
      if (recorded) setDrafts({})
      setPendingConfirm(null)
      onRecorded()
      // A conflict may leave the version as it was (another line changed the plan, a link opened): read the plan again too.
      // A recorded plan moves the version, which re-keys the read by itself.
      if (!recorded) void query.refetch()
    },
  })
  const query = useQuery({
    queryKey: [namespace, actorId, routeEntryGeneration, 'sales-source-plan', companyId, detail.draft.id, detail.draft.version],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftSourcePlan(detail, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
    staleTime: 0, gcTime: 0, retry: false, refetchOnWindowFocus: false,
  })
  const unitCodes = useMemo(() => orderUnitCodes(detail), [detail])
  const plan = query.data?.type === 'ok-single' ? query.data.data : null
  const notice = sourcePlanNotice(controller.attempt)
  const frozen = sourcePlanFrozen(controller.attempt)
  const canWrite = hasCapability(shell, SALES_SOURCE_PLAN_WRITE_CAPABILITY)

  function edit(lineId: string, part: Part, value: string) {
    setFormError(null)
    setDrafts((current) => ({ ...current, [lineId]: { ...current[lineId], [part]: value } }))
  }

  function valueOf(line: SourcePlanLine, part: Part): string {
    return drafts[line.lineId]?.[part] ?? line.saved?.[part] ?? ''
  }

  function buildPayload(current: SourcePlan, confirmLinkedSupply: boolean): SourcePlanRequest | string {
    const lines: SourcePlanLineRequest[] = []
    for (const line of current.lines) {
      if (!line.writable || drafts[line.lineId] === undefined) continue
      const parsed = PARTS.map((part) => parseDecimalInput(valueOf(line, part)))
      if (parsed.some((entry) => !entry.ok)) return T.invalidInput(line.lineNo)
      const [production, purchasing, subcontract] = parsed.map((entry) => (entry.ok ? entry.value : ''))
      lines.push({ lineId: line.lineId, production, purchasing, subcontract })
    }
    if (lines.length === 0) return T.nothingToSave
    return { expectedVersion: current.version, confirmLinkedSupply, lines }
  }

  function submit(current: SourcePlan) {
    const linkedOpen = current.linkedSupply.some((entry) => entry.status === SOURCE_PLAN_LINK_STATUS.present)
      || current.lines.some((line) => line.warnings.includes(SOURCE_PLAN_WARNING.linkedSupplyOpen))
    const payload = buildPayload(current, linkedOpen)
    if (typeof payload === 'string') {
      setFormError(payload)
      return
    }
    setFormError(null)
    if (linkedOpen) {
      setPendingConfirm(payload)
      return
    }
    controller.save(payload)
  }

  function confirmLinked() {
    if (pendingConfirm === null) return
    if (controller.save(pendingConfirm)) setPendingConfirm(null)
  }

  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-source-plan">
      <h3 id={headingId}>{T.heading}</h3>
      <p className={styles.muted}>{T.description}</p>
      {notice === null ? null : (
        <p role={controller.attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-source-plan-notice">
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(controller.attempt) && controller.attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={controller.retry} data-testid="sales-source-plan-retry">{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={controller.leave} data-testid="sales-source-plan-abandon">
            {T.abandon}
          </Button>
        </div>
      ) : null}
      {plan === null ? (
        <StateBlock
          state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data)}
          subject={T.subject}
          onRetry={() => void query.refetch()}
        />
      ) : (
        <PlanView
          plan={plan}
          userDisplays={userDisplays}
          unitCodes={unitCodes}
          canWrite={canWrite}
          frozen={frozen}
          valueOf={valueOf}
          edit={edit}
          formError={formError}
          onSave={() => submit(plan)}
        />
      )}
      {pendingConfirm === null ? null : (
        <LinkedSupplyDialog
          controller={controller}
          onConfirm={confirmLinked}
          onCancel={() => setPendingConfirm(null)}
        />
      )}
    </section>
  )
}

function PlanView({
  plan,
  userDisplays,
  unitCodes,
  canWrite,
  frozen,
  valueOf,
  edit,
  formError,
  onSave,
}: {
  readonly plan: SourcePlan
  readonly userDisplays: readonly UserDisplay[]
  readonly unitCodes: ReadonlyMap<string, string>
  readonly canWrite: boolean
  readonly frozen: boolean
  readonly valueOf: (line: SourcePlanLine, part: Part) => string
  readonly edit: (lineId: string, part: Part, value: string) => void
  readonly formError: string | null
  readonly onSave: () => void
}) {
  const linkUnreadable = plan.linkedSupply.some((entry) => entry.status === SOURCE_PLAN_LINK_STATUS.unreadable)
  const linkPresent = plan.linkedSupply.some((entry) => entry.status === SOURCE_PLAN_LINK_STATUS.present)
  const anyWritable = plan.lines.some((line) => line.writable)
  const editable = canWrite && plan.writable && !linkUnreadable
  const personName = (id: string) => {
    const person = userDisplayFor(userDisplays, id)
    return person === null ? id : person.displayName
  }
  return (
    <>
      <p className={styles.muted}>{T.sumRule}</p>
      <h4>{T.linkedHeading}</h4>
      <dl className={styles.facts} data-testid="sales-source-plan-linked">
        {plan.linkedSupply.map((entry) => (
          <div key={entry.kind}>
            <dt>{T.linkedKind[entry.kind]}</dt>
            <dd data-testid={`sales-source-plan-linked-${entry.kind}`}>{T.linkedStatus[entry.status]}</dd>
          </div>
        ))}
      </dl>
      {linkUnreadable ? <p role="alert" className={styles.notice} data-testid="sales-source-plan-linked-unreadable">{T.linkedUnreadable}</p> : null}
      {linkPresent ? <p className={styles.notice} data-testid="sales-source-plan-linked-present">{T.linkedPresent}</p> : null}
      {!canWrite ? <p className={styles.muted} data-testid="sales-source-plan-read-only">{T.readOnly}</p> : null}
      {canWrite && !plan.writable ? <p className={styles.muted} data-testid="sales-source-plan-order-locked">{T.orderLocked}</p> : null}
      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="sales-source-plan-lines">
          <caption className={styles.caption}>{T.caption}</caption>
          <thead><tr>
            <th scope="col">{T.lineNo}</th><th scope="col">{T.quantity}</th><th scope="col">{T.locked}</th>
            <th scope="col">{T.remaining}</th><th scope="col">{T.stock}</th><th scope="col">{T.suggestion}</th>
            <th scope="col">{T.recorded}</th><th scope="col">{T.production}</th><th scope="col">{T.purchasing}</th>
            <th scope="col">{T.subcontract}</th><th scope="col">{T.warnings}</th>
          </tr></thead>
          <tbody>{plan.lines.map((line) => {
            const inputs = editable && line.writable
            return (
              <tr key={line.lineId} data-testid={`sales-source-plan-line-${line.lineNo}`}>
                <th scope="row">{line.lineNo}</th>
                <td className={styles.numericValue}>{line.quantity} <UnitName id={line.orderUnitId} unitCodes={unitCodes} /></td>
                <td className={styles.numericValue} data-testid={`sales-source-plan-locked-${line.lineNo}`}>{line.locked ?? T.unreadable}</td>
                <td className={styles.numericValue}>{line.remaining ?? T.unreadable}</td>
                <td className={styles.numericValue} data-testid={`sales-source-plan-stock-${line.lineNo}`}>
                  {line.stockStatus === SOURCE_PLAN_STOCK_STATUS.unreadable || line.stock === null ? T.unreadable : line.stock}
                </td>
                <td data-testid={`sales-source-plan-suggestion-${line.lineNo}`}>
                  {T.suggestionValue[line.suggestion]}
                  <br /><span className={styles.muted}>{T.suggestionReason[line.suggestionReason]}</span>
                </td>
                <td data-testid={`sales-source-plan-recorded-${line.lineNo}`}>
                  {line.saved === null ? T.noRecorded : (
                    <>
                      {T.recordedParts(line.saved.stock, line.saved.production, line.saved.purchasing, line.saved.subcontract)}
                      <br /><span className={styles.muted}>
                        {T.recordedBy(personName(line.saved.savedBy), formatInstant(line.saved.savedAtUtc), line.saved.draftVersion)}
                      </span>
                      {line.saved.linkedSupplyConfirmed ? <><br /><span className={styles.muted}>{T.recordedConfirmed}</span></> : null}
                    </>
                  )}
                </td>
                {inputs ? PARTS.map((part) => (
                  <td key={part}>
                    <input
                      inputMode="decimal"
                      aria-label={T.inputLabel(T[part], line.lineNo)}
                      value={valueOf(line, part)}
                      disabled={frozen}
                      onChange={(event) => edit(line.lineId, part, event.target.value)}
                      data-testid={`sales-source-plan-${part}-${line.lineNo}`}
                    />
                  </td>
                )) : (
                  <td colSpan={PARTS.length} data-testid={`sales-source-plan-lock-${line.lineNo}`}>
                    {!canWrite ? null : line.lockReason === null ? (line.writable ? null : T.lockedNoReason) : T.lockReason[line.lockReason]}
                  </td>
                )}
                <td data-testid={`sales-source-plan-warnings-${line.lineNo}`}>
                  {line.warnings.length === 0 ? null : (
                    <ul>{line.warnings.map((warning) => <li key={warning}>{T.warning[warning]}</li>)}</ul>
                  )}
                </td>
              </tr>
            )
          })}</tbody>
        </table>
      </div>
      {formError === null ? null : <p role="alert" className={styles.fieldError} data-testid="sales-source-plan-form-error">{formError}</p>}
      {editable && anyWritable ? (
        <div className={styles.actions}>
          <Button type="button" variant="primary" disabled={frozen} onClick={onSave} data-testid="sales-source-plan-save">{T.save}</Button>
        </div>
      ) : null}
    </>
  )
}

/** SAT-D254: the inline confirmation for an open linked production/purchasing document; the confirmation is audited. */
function LinkedSupplyDialog({
  controller,
  onConfirm,
  onCancel,
}: {
  readonly controller: SourcePlanController
  readonly onConfirm: () => void
  readonly onCancel: () => void
}) {
  const headingId = useId()
  const noteId = useId()
  const submitting = controller.attempt.phase === 'submitting'
  return (
    <div role="dialog" aria-labelledby={headingId} aria-describedby={noteId} className={styles.section} data-testid="sales-source-plan-confirm-dialog">
      <h4 id={headingId}>{T.confirmHeading}</h4>
      <p id={noteId} className={styles.muted}>{T.confirmNote}</p>
      <div className={styles.actions}>
        <Button type="button" variant="primary" disabled={sourcePlanFrozen(controller.attempt)} onClick={onConfirm} data-testid="sales-source-plan-confirm">
          {submitting ? T.submitting : T.confirm}
        </Button>
        <Button type="button" variant="ghost" className={styles.linkButton} disabled={submitting} onClick={onCancel} data-testid="sales-source-plan-cancel">
          {T.cancel}
        </Button>
      </div>
    </div>
  )
}
