/**
 * F-027 / EK-18 #76-#77 — duplicate Cari merge on the SURVIVING card.
 *
 * What the user can do here, and nothing more:
 *  - SEE the same-VKN/TCKN duplicates the selected company holds, or — on a card that was merged away — the card it was
 *    merged into (its code only when this company holds it).
 *  - APPROVE merging one duplicate into this card, when the server says this actor holds the group manager's merge
 *    approval in the selected company. The server requires it again in every company holding either card. Both party
 *    versions of the read are fenced; a reason is required; the irreversibility is confirmed explicitly, because there
 *    is no unmerge (#77).
 *
 * What is offered comes from the read's `viewer` block, measured by the server for this actor and company; the server
 * re-authorizes every command. The read is a side read of the card and never moves its command-readiness slot; the
 * command uses the card's own contract context.
 */

import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContractContext } from './clientContract'
import { MERGE_REASON_MAX, MERGE_TEXT as T, checkMergeReason } from './merge'
import { approveMerge, fetchMerge } from './mergeApi'
import { useMergeCommand } from './useMergeCommand'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariTradeName.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
  /** Called after an applied merge so the card's own read (which shows the party state) refreshes. */
  readonly onChanged?: () => void
}

export function CariMergePanel({ partyId, companyId, namespace, contractContext, onChanged }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const candidateId = useId()
  const reasonId = useId()
  const hintId = useId()
  const errorId = useId()
  const confirmId = useId()
  const queryKey = useMemo(() => [namespace, 'counterparty-party-merge', partyId, companyId], [
    namespace,
    partyId,
    companyId,
  ])

  const read = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchMerge(partyId, companyId, signal)
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
  const command = useMergeCommand(companyId, contractContext, `cari.party-merge:${partyId}`, refresh)
  const [selected, setSelected] = useState('')
  const [reason, setReason] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [fieldError, setFieldError] = useState<string | null>(null)

  if (read.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-party-merge" data-state="loading">
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
      <div className={styles.panel} data-testid="cari-party-merge" data-state={state}>
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
  if (view.mergedInto !== null) {
    return (
      <div className={styles.panel} data-testid="cari-party-merge" data-state="merged">
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.muted} data-merged-into>
          {T.mergedInto}
        </p>
        {view.mergedInto.code === null ? (
          <p className={styles.muted}>{T.mergedIntoHidden}</p>
        ) : (
          <dl className={styles.facts}>
            <dt>{T.mergedIntoCode}</dt>
            <dd data-merged-into-code>{view.mergedInto.code}</dd>
          </dl>
        )}
      </div>
    )
  }

  if (view.candidates.length === 0) {
    return (
      <div className={styles.panel} data-testid="cari-party-merge" data-state="empty">
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.muted}>{T.noCandidates}</p>
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
      </div>
    )
  }

  // A selection that is no longer offered (the list was read again) is not sent.
  const candidate = view.candidates.find((item) => item.partyId === selected) ?? null
  const locked = command.busy || command.unresolved

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (locked) return
    if (candidate === null) {
      setFieldError(T.candidateRequired)
      return
    }
    const check = checkMergeReason(reason)
    if (!check.ok) {
      setFieldError(check.message)
      return
    }
    if (!confirmed) {
      setFieldError(T.confirmRequired)
      return
    }
    setFieldError(null)
    const approval = {
      mergedPartyId: candidate.partyId,
      mergedPartyExpectedVersion: candidate.version,
      reason: check.value,
    }
    const ok = await command.run(
      `approve:${partyId}:${candidate.partyId}`,
      (context) => approveMerge(context, partyId, view.version, approval),
      T.approved,
    )
    if (ok) {
      setSelected('')
      setReason('')
      setConfirmed(false)
    }
  }

  return (
    <div className={styles.panel} data-testid="cari-party-merge" data-state="ready">
      <h3 className={styles.title}>{T.heading}</h3>
      <p className={styles.muted}>{T.candidatesIntro}</p>
      {view.viewer.canApproveMerge ? (
        <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={command.busy} noValidate>
          <label htmlFor={candidateId}>{T.candidateLabel}</label>
          <select
            id={candidateId}
            value={candidate === null ? '' : candidate.partyId}
            disabled={locked}
            onChange={(event) => {
              setSelected(event.target.value)
              setFieldError(null)
            }}
          >
            <option value="">{T.candidatePlaceholder}</option>
            {view.candidates.map((item) => (
              <option key={item.partyId} value={item.partyId}>
                {`${item.code} — ${item.legalName}`}
              </option>
            ))}
          </select>
          <label htmlFor={reasonId}>{T.reasonLabel}</label>
          <textarea
            id={reasonId}
            value={reason}
            maxLength={MERGE_REASON_MAX}
            required
            rows={3}
            disabled={locked}
            aria-describedby={fieldError === null ? hintId : `${hintId} ${errorId}`}
            aria-invalid={fieldError !== null}
            onChange={(event) => {
              setReason(event.target.value)
              setFieldError(null)
            }}
          />
          <p id={hintId} className={styles.muted}>
            {T.reasonHint}
          </p>
          <p className={styles.muted} data-merge-warning>
            {T.warning}
          </p>
          <label htmlFor={confirmId}>
            <input
              id={confirmId}
              type="checkbox"
              checked={confirmed}
              disabled={locked}
              onChange={(event) => {
                setConfirmed(event.target.checked)
                setFieldError(null)
              }}
            />{' '}
            {T.confirmLabel}
          </label>
          {fieldError === null ? null : (
            <p id={errorId} className={styles.error} role="alert">
              {fieldError}
            </p>
          )}
          <div className={styles.actions}>
            <button type="submit" className={styles.button} disabled={locked}>
              {command.busy ? T.approving : T.approve}
            </button>
          </div>
        </form>
      ) : (
        <>
          <ul data-merge-candidates>
            {view.candidates.map((item) => (
              <li key={item.partyId}>{`${item.code} — ${item.legalName}`}</li>
            ))}
          </ul>
          <p className={styles.muted} data-merge-read-only>
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
