/**
 * F-027 / ALS-D95 — the Cari card's official legal name (resmi/hukuki unvan), edited in place.
 *
 * What the user can do here, and nothing more:
 *  - READ the legal name of the card.
 *  - CHANGE it in one step, without an approval step (19.22), when the server says this actor holds the SEPARATE
 *    legal-name permission in the selected company. Fenced on the party version the form opened on. A blank value is
 *    refused (the legal name is required); a save of the stored value is the server's no-op, so the client does not
 *    send an unchanged value at all. The server writes the old and the new legal name to the audit.
 *
 * What is offered comes from the read's `viewer` block, measured by the server for this actor and company; the server
 * re-authorizes every command. The read is a side read of the card and never moves its command-readiness slot; the
 * command uses the card's own contract context. The trade name stays on its own panel (ALS-D97).
 */

import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContractContext } from './clientContract'
import { LEGAL_NAME_MAX, LEGAL_NAME_TEXT as T, checkLegalName } from './legalName'
import { fetchLegalName, updateLegalName } from './legalNameApi'
import { useLegalNameCommand } from './useLegalNameCommand'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariTradeName.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
  /** Called after an applied change so the card's own read and the names panel (which show the name too) refresh. */
  readonly onChanged?: () => void
}

export function CariLegalNamePanel({ partyId, companyId, namespace, contractContext, onChanged }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const inputId = useId()
  const hintId = useId()
  const errorId = useId()
  const queryKey = useMemo(() => [namespace, 'counterparty-legal-name', partyId, companyId], [
    namespace,
    partyId,
    companyId,
  ])

  const read = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchLegalName(partyId, companyId, signal)
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
  const command = useLegalNameCommand(companyId, contractContext, `cari.legal-name:${partyId}`, refresh)
  /** `null` = the user has not typed; the field then shows the stored value of the latest read. */
  const [draft, setDraft] = useState<string | null>(null)
  const [fieldError, setFieldError] = useState<string | null>(null)

  if (read.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-legal-name" data-state="loading">
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
      <div className={styles.panel} data-testid="cari-legal-name" data-state={state}>
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
  const shown = draft ?? view.legalName
  const check = checkLegalName(shown)
  const changed = check.ok ? check.value !== view.legalName : shown !== view.legalName
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
      (context) => updateLegalName(context, partyId, view.version, target),
      T.saved,
    )
    if (ok) setDraft(null)
  }

  return (
    <div className={styles.panel} data-testid="cari-legal-name" data-state="ready">
      <h3 className={styles.title}>{T.heading}</h3>
      {view.viewer.canUpdateLegalName ? (
        <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={command.busy} noValidate>
          <label htmlFor={inputId}>{T.legalNameLabel}</label>
          <input
            id={inputId}
            type="text"
            value={shown}
            maxLength={LEGAL_NAME_MAX}
            required
            disabled={locked}
            aria-describedby={fieldError === null ? hintId : `${hintId} ${errorId}`}
            aria-invalid={fieldError !== null}
            onChange={(event) => {
              setDraft(event.target.value)
              setFieldError(null)
            }}
          />
          <p id={hintId} className={styles.muted}>
            {T.legalNameHint} {T.sharedNote}
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
        <>
          <dl className={styles.facts}>
            <dt>{T.legalNameLabel}</dt>
            <dd data-legal-name>{view.legalName}</dd>
          </dl>
          <p className={styles.muted} data-legal-name-read-only>
            {T.readOnlyNote}
          </p>
        </>
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
