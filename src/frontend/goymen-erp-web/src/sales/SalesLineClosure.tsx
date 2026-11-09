/**
 * SAT-D17 / SAT-D115 (LC-3) / SAT-D216 / SAT-D222 — "Satırı eksik kapat" on S-04, inside the status card, behind the
 * `sales.orders.lines.close` control the shell publishes only to an actor holding that name beside the read.
 *
 *  - Only the lines the SERVER answers as under-delivered (`direction` Under, a shipped total, no closed remainder yet) on an
 *    `Active` order are offered. The remainder shown is the server's deviation; the screen judges no band, tolerance, shipper
 *    or commitment — the server measures the band at closure and refuses the block band (422), a moved shipped total, an
 *    unverified or self shipper, open commitments and a revision that awaits customer acceptance (409), all said in Turkish.
 *  - The confirmation shows the line, the shipped total the command will name (SAT-D213 pattern), the server's remainder and
 *    the tolerance profile the status read returned — information only.
 *  - One idempotent attempt per line snapshot (`orderAttempt`): an unknown outcome keeps its key and a retry resends the same
 *    snapshot; a recorded closure, or a conflict, re-reads the detail and the status.
 *  - The attempt lives in the status container (`useLineClosure`), not in the card: a recorded closure moves the draft version
 *    and with it the status read, and the card remounts — the notice and the key must survive that.
 *  - 401/403 go to the shell's auth boundary; the screen does not change the session or the authority itself.
 */
import { useEffect, useId, useRef } from 'react'
import { isOutcomeStillUnknown } from './orderAttempt'
import { lineClosureNotice, isLineClosable, type LineClosureAttempt, type LineClosureController } from './lineClosure'
import { SALES_LINE_CLOSE_CAPABILITY } from './capabilities'
import { STATUS_AXIS_STATE, type LineRealization, type OrderStatus } from './orderStatus'
import type { DraftDetail } from './contract'
import { LINE_CLOSURE_TEXT as T, ORDER_STATUS_TEXT } from './salesText'
import { hasCapability, type ShellState } from '../shell/shellState'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

export function SalesLineClosure({
  shell,
  detail,
  status,
  companyId,
  controller,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly status: OrderStatus
  readonly companyId: string
  readonly controller: LineClosureController
}) {
  const headingId = useId()
  const { attempt, selectedLineId } = controller
  if (!hasCapability(shell, SALES_LINE_CLOSE_CAPABILITY) || companyId !== detail.draft.companyId) return null
  const closable = status.lifecycle === STATUS_AXIS_STATE.active ? status.realization.lines.filter(isLineClosable) : []
  if (closable.length === 0 && attempt.phase === 'idle') return null

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const notice = lineClosureNotice(attempt)
  const selected = closable.find((line) => line.lineId === selectedLineId) ?? null
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-line-closure">
      <h3 id={headingId}>{T.heading}</h3>
      <p className={styles.muted}>{T.description}</p>
      {notice === null ? null : (
        <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-line-closure-notice">
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={controller.retry} data-testid="sales-line-closure-retry">{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={controller.leave}>{T.abandon}</Button>
        </div>
      ) : null}
      {closable.length === 0 ? null : (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="sales-line-closure-lines">
            <caption className={styles.caption}>{T.caption}</caption>
            <thead><tr>
              <th scope="col">{T.lineNo}</th><th scope="col">{T.ordered}</th><th scope="col">{T.shipped}</th>
              <th scope="col">{T.remainder}</th><th scope="col">{T.state}</th><th scope="col">{T.action}</th>
            </tr></thead>
            <tbody>{closable.map((line) => (
              <tr key={line.lineId} data-testid={`sales-line-closure-line-${line.lineNo}`}>
                <th scope="row">{line.lineNo}</th>
                <td className={styles.numericValue}>{line.ordered}</td>
                <td className={styles.numericValue}>{line.shipped}</td>
                <td className={styles.numericValue}>{line.deviation}</td>
                <td>{ORDER_STATUS_TEXT.realization[line.state]}</td>
                <td>
                  <Button
                    type="button"
                    variant="secondary"
                    disabled={frozen}
                    aria-label={T.closeLabel(line.lineNo)}
                    onClick={() => controller.open(line.lineId)}
                    data-testid={`sales-line-closure-open-${line.lineNo}`}
                  >
                    {T.close}
                  </Button>
                </td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
      {selected === null ? null : (
        <LineClosureDialog line={selected} status={status} attempt={attempt} frozen={frozen} controller={controller} />
      )}
    </section>
  )
}

/** The inline confirmation: the server's facts for one line and the tolerance the status read returned (information only). */
function LineClosureDialog({
  line,
  status,
  attempt,
  frozen,
  controller,
}: {
  readonly line: LineRealization
  readonly status: OrderStatus
  readonly attempt: LineClosureAttempt
  readonly frozen: boolean
  readonly controller: LineClosureController
}) {
  const headingId = useId()
  const noteId = useId()
  const headingRef = useRef<HTMLHeadingElement>(null)
  useEffect(() => {
    headingRef.current?.focus()
  }, [line.lineId])
  const tolerance = status.realization.tolerance
  return (
    <div role="dialog" aria-labelledby={headingId} aria-describedby={noteId} className={styles.section} data-testid="sales-line-closure-dialog">
      <h4 id={headingId} ref={headingRef} tabIndex={-1}>{T.confirmHeading(line.lineNo)}</h4>
      <p id={noteId} className={styles.muted}>{T.confirmNote}</p>
      <dl className={styles.facts}>
        <div><dt>{T.ordered}</dt><dd data-testid="sales-line-closure-ordered">{line.ordered}</dd></div>
        <div><dt>{T.shipped}</dt><dd data-testid="sales-line-closure-shipped">{line.shipped}</dd></div>
        <div><dt>{T.remainder}</dt><dd data-testid="sales-line-closure-remainder">{line.deviation}</dd></div>
        <div>
          <dt>{T.currentBand}</dt>
          <dd data-testid="sales-line-closure-band">{line.band === null ? T.bandNotMeasured : ORDER_STATUS_TEXT.band[line.band]}</dd>
        </div>
        <div>
          <dt>{T.tolerance}</dt>
          <dd data-testid="sales-line-closure-tolerance">
            {tolerance === null
              ? T.toleranceMissing
              : T.toleranceBand(tolerance.warnPercent, tolerance.approvalPercent, tolerance.blockPercent)}
          </dd>
        </div>
      </dl>
      <p className={styles.muted}>{T.toleranceMeasured}</p>
      <div className={styles.actions}>
        <Button
          type="button"
          variant="primary"
          disabled={frozen}
          onClick={() => controller.confirm(line)}
          data-testid="sales-line-closure-confirm"
        >
          {attempt.phase === 'submitting' ? T.submitting : T.confirm}
        </Button>
        <Button
          type="button"
          variant="ghost"
          className={styles.linkButton}
          disabled={attempt.phase === 'submitting'}
          onClick={controller.cancel}
          data-testid="sales-line-closure-cancel"
        >
          {T.cancel}
        </Button>
      </div>
    </div>
  )
}
