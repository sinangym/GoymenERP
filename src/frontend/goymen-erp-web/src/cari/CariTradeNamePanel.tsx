/**
 * F-027 / C-02 — the Cari card's names, edited in place.
 *
 * What the user can do here, and nothing more:
 *  - READ the hukuki unvan (legal name), read-only: it is changed on its own panel under a separate permission
 *    (ALS-D111/ALS-D113 maker-checker, ./CariLegalNamePanel.tsx), so this panel points there instead of offering a field.
 *  - CHANGE or CLEAR the ticari unvan (trade name), fenced on the party version the form opened on. A save of the stored
 *    value is the server's no-op; the client does not send an unchanged value at all.
 *
 * What is offered comes from the read's `viewer` block, measured by the server for this actor and company; the server
 * re-authorizes every command. The read is a side read of the card and never moves its command-readiness slot; the
 * command uses the card's own contract context.
 */

import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContractContext } from './clientContract'
import { TRADE_NAME_MAX, TRADE_NAME_TEXT as T, checkTradeName } from './tradeName'
import { fetchTradeName, updateTradeName } from './tradeNameApi'
import { useTradeNameCommand } from './useTradeNameCommand'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariTradeName.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
  /** Called after an applied change so the card's own read (which shows the names too) is refreshed. */
  readonly onChanged?: () => void
}

export function CariTradeNamePanel({ partyId, companyId, namespace, contractContext, onChanged }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const inputId = useId()
  const hintId = useId()
  const errorId = useId()
  const queryKey = useMemo(() => [namespace, 'counterparty-trade-name', partyId, companyId], [
    namespace,
    partyId,
    companyId,
  ])

  const read = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchTradeName(partyId, companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })

  const refresh = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey })
    onChanged?.()
  }, [onChanged, queryClient, queryKey])
  const command = useTradeNameCommand(companyId, contractContext, `cari.trade-name:${partyId}`, refresh)
  /** `null` = the user has not typed; the field then shows the stored value of the latest read. */
  const [draft, setDraft] = useState<string | null>(null)
  const [fieldError, setFieldError] = useState<string | null>(null)

  if (read.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-trade-name" data-state="loading">
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.muted} role="status">
          {T.loading}
        </p>
      </div>
    )
  }

  const outcome = read.data
  if (outcome === undefined || outcome.type !== 'ok') {
    const forbidden = outcome?.type === 'http' && outcome.status === 403
    const notFound = outcome?.type === 'http' && outcome.status === 404
    const state = forbidden ? 'forbidden' : notFound ? 'not-found' : 'error'
    return (
      <div className={styles.panel} data-testid="cari-trade-name" data-state={state}>
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.error} role="alert">
          {forbidden ? T.forbidden : notFound ? T.notFound : T.loadError}
        </p>
        {forbidden || notFound ? null : (
          <div className={styles.actions}>
            <button type="button" className={styles.button} onClick={() => void read.refetch()}>
              {T.readAgain}
            </button>
          </div>
        )}
      </div>
    )
  }

  const view = outcome.data
  const stored = view.tradeName ?? ''
  const shown = draft ?? stored
  const check = checkTradeName(shown)
  const changed = check.ok ? check.value !== view.tradeName : true
  const locked = command.busy || command.unresolved

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (locked) return
    if (!check.ok) {
      setFieldError(check.message)
      return
    }
    setFieldError(null)
    if (!changed) return
    const target = check.value
    const ok = await command.run(
      `update:${partyId}`,
      (context) => updateTradeName(context, partyId, view.version, target),
      target === null ? T.cleared : T.saved,
    )
    if (ok) setDraft(null)
  }

  return (
    <div className={styles.panel} data-testid="cari-trade-name" data-state="ready">
      <h3 className={styles.title}>{T.heading}</h3>
      <dl className={styles.facts}>
        <dt>{T.legalNameLabel}</dt>
        <dd data-legal-name>{view.legalName}</dd>
        {view.viewer.canUpdateTradeName ? null : (
          <>
            <dt>{T.tradeNameLabel}</dt>
            <dd data-trade-name={view.tradeName === null ? 'none' : 'set'}>{view.tradeName ?? T.tradeNameNone}</dd>
          </>
        )}
      </dl>
      <p className={styles.muted}>{T.legalNameNote}</p>

      {view.viewer.canUpdateTradeName ? (
        <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={command.busy} noValidate>
          <label htmlFor={inputId}>{T.tradeNameLabel}</label>
          <input
            id={inputId}
            type="text"
            value={shown}
            maxLength={TRADE_NAME_MAX}
            disabled={locked}
            aria-describedby={fieldError === null ? hintId : `${hintId} ${errorId}`}
            aria-invalid={fieldError !== null}
            onChange={(event) => {
              setDraft(event.target.value)
              setFieldError(null)
            }}
          />
          <p id={hintId} className={styles.muted}>
            {T.tradeNameHint} {T.sharedNote}
          </p>
          {fieldError === null ? null : (
            <p id={errorId} className={styles.error} role="alert">
              {fieldError}
            </p>
          )}
          <div className={styles.actions}>
            <button type="submit" className={styles.button} disabled={!changed || locked}>
              {command.busy ? T.saving : T.save}
            </button>
          </div>
        </form>
      ) : (
        <p className={styles.muted} data-trade-name-read-only>
          {T.readOnlyNote}
        </p>
      )}

      <div aria-live="polite">
        {command.notice === null ? null : (
          <p
            className={command.notice.tone === 'ok' ? styles.ok : styles.error}
            role={command.notice.tone === 'ok' ? 'status' : 'alert'}
            data-notice={command.notice.tone}
          >
            {command.notice.text}
          </p>
        )}
      </div>
      {command.unresolved && !command.busy ? (
        <div className={styles.actions}>
          <button type="button" className={styles.button} onClick={() => void command.retry()}>
            {T.retrySame}
          </button>
        </div>
      ) : null}
    </div>
  )
}
