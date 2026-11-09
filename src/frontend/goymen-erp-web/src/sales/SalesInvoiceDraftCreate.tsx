/**
 * UI-62 (API-32): "Fatura taslağı hazırla" on one S-08 candidate group. The person keeps the whole group or splits it by
 * choosing some of its shares (Sinan S5, 2026-10-28); a partial quantity is not chosen here. The body names the customer
 * account and the chosen shipments only — every amount, the rate, the due date and the tax are the server's, and a share set
 * that cannot be invoiced now comes back as the server's reasons, listed verbatim (the screen evaluates none of them).
 *
 * One Idempotency-Key per submitted selection: an unknown outcome keeps the key and freezes the selection (it survives the
 * screen, ./pendingPoolAttempts.ts) until the same request is retried or left. 401/403 go to the auth boundary.
 */
import { useId, useState } from 'react'
import type { InvoiceCandidateGroup } from './dispatchContract'
import { MAX_INVOICE_DRAFT_SHARES, SALES_INVOICES_PATH, type CreateSalesInvoicePayload } from './invoiceDraftApi'
import {
  INVOICE_NOT_READY_CODE,
  invoiceDraftCreateNotice,
  invoiceDraftCreateScope,
  invoiceShareLabel,
  useInvoiceDraftCreate,
} from './invoiceDraft'
import { INVOICE_DRAFT_CREATE_TEXT as CT } from './invoiceDraftText'
import type { SalesInvoiceDetail } from './invoiceFinalizationContract'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { orderAttemptBinding } from './orderAttempt'
import { recallPoolAttempt } from './pendingPoolAttempts'
import { usePoolDirty } from './poolAttempt'
import { useShellState } from '../shell/shellContext'
import { Button } from '../ui/Button'
import styles from './SalesPool.module.css'

const isStringList = (value: unknown): value is readonly string[] =>
  Array.isArray(value) && value.every((item) => typeof item === 'string')

export function SalesInvoiceDraftCreate({
  group,
  groupIdentity,
  index,
  onCreated,
  onConflict,
}: {
  readonly group: InvoiceCandidateGroup
  /** The server's group key as one string: the attempt is held per group. */
  readonly groupIdentity: string
  readonly index: number
  readonly onCreated: (detail: SalesInvoiceDetail) => void
  /** A conflict other than `invoice_not_ready`: the candidates are read again. */
  readonly onConflict: () => void
}) {
  const shell = useShellState()
  const headingId = useId()
  const scope = invoiceDraftCreateScope(groupIdentity)
  const shareIds = group.shares.map((share) => share.shipmentId)
  const [selected, setSelected] = useState<readonly string[]>(() => {
    // An unresolved attempt from an earlier visit brings back the selection it was sent from.
    const recalled = recallPoolAttempt<CreateSalesInvoicePayload, SalesInvoiceDetail, unknown>(orderAttemptBinding(shell, scope))?.values
    return isStringList(recalled) ? shareIds.filter((id) => recalled.includes(id)) : shareIds
  })
  const [localError, setLocalError] = useState<string | null>(null)
  const attempt = useInvoiceDraftCreate(groupIdentity, selected, (next) => {
    if (next.phase === 'completed') onCreated(next.result)
    else if (next.phase === 'failed' && next.failure === 'conflict' && next.problem?.code !== INVOICE_NOT_READY_CODE) onConflict()
  })
  usePoolDirty(scope, attempt.frozen)

  const labelOf = (shipmentId: string) => {
    const share = group.shares.find((candidate) => candidate.shipmentId.toLowerCase() === shipmentId.toLowerCase())
    return share === undefined ? null : invoiceShareLabel(share)
  }
  const notice = invoiceDraftCreateNotice(attempt.state, attempt.blockedChange, labelOf)
  const locked = attempt.frozen || attempt.state.phase === 'completed'

  function toggle(shipmentId: string, checked: boolean) {
    setLocalError(null)
    setSelected((current) => shareIds.filter((id) => (id === shipmentId ? checked : current.includes(id))))
  }

  function choose(next: readonly string[]) {
    setLocalError(null)
    setSelected(next)
  }

  function create() {
    if (selected.length === 0) {
      setLocalError(CT.noneSelected)
      return
    }
    if (selected.length > MAX_INVOICE_DRAFT_SHARES) {
      setLocalError(CT.tooMany(MAX_INVOICE_DRAFT_SHARES))
      return
    }
    setLocalError(null)
    // A finished refusal is closed first, so the same or a changed selection is asked afresh.
    if (attempt.state.phase === 'failed' && attempt.state.unresolvedSince === undefined) attempt.reset()
    attempt.submit(SALES_INVOICES_PATH, {
      companyAccountId: group.key.companyAccountId,
      shipmentIds: shareIds.filter((id) => selected.includes(id)),
    })
  }

  return (
    <div className={styles.section} role="group" aria-labelledby={headingId} data-testid={`sales-invoice-group-${index}-draft`}>
      <h3 id={headingId} className={styles.subtitle}>{CT.heading}</h3>
      <p className={styles.muted}>{CT.intro}</p>
      <fieldset disabled={locked}>
        <legend>{CT.selectedCount(selected.length, shareIds.length)}</legend>
        <div className={styles.actions}>
          <Button type="button" variant="ghost" onClick={() => choose(shareIds)} data-testid={`sales-invoice-group-${index}-select-all`}>
            {CT.selectAll}
          </Button>
          <Button type="button" variant="ghost" onClick={() => choose([])} data-testid={`sales-invoice-group-${index}-select-none`}>
            {CT.selectNone}
          </Button>
        </div>
        <ul>
          {group.shares.map((share) => (
            <li key={share.shipmentId}>
              <label>
                <input
                  type="checkbox"
                  checked={selected.includes(share.shipmentId)}
                  onChange={(event) => toggle(share.shipmentId, event.target.checked)}
                  data-testid={`sales-invoice-share-${share.shipmentId}-select`}
                />{' '}
                {CT.selectShare(invoiceShareLabel(share))}
              </label>
            </li>
          ))}
        </ul>
      </fieldset>
      {localError === null ? null : (
        <p role="alert" className={styles.notice} data-testid={`sales-invoice-group-${index}-draft-error`}>{localError}</p>
      )}
      <div className={styles.actions}>
        <Button type="button" variant="primary" disabled={locked} onClick={create} data-testid={`sales-invoice-group-${index}-draft-create`}>
          {CT.create}
        </Button>
      </div>
      {notice === null ? null : (
        <div role={notice.role} className={styles.notice} data-testid={`sales-invoice-group-${index}-draft-notice`}>
          <p>{notice.message}</p>
          {notice.reasons.length > 0 ? (
            <ul aria-label={CT.notReady}>
              {notice.reasons.map((reason, position) => (
                <li key={`${position}:${reason}`}>{reason}</li>
              ))}
            </ul>
          ) : null}
        </div>
      )}
      {attempt.frozen && attempt.state.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={attempt.retry} data-testid={`sales-invoice-group-${index}-draft-retry`}>
            {T.retry}
          </Button>
          <Button type="button" variant="ghost" onClick={attempt.leave} data-testid={`sales-invoice-group-${index}-draft-abandon`}>
            {T.abandon}
          </Button>
        </div>
      ) : null}
    </div>
  )
}
