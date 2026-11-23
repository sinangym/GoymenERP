/**
 * F-027 / KLN-D38 — the Cari card's dated identity (VKN/TCKN, official legal name, e-belge status) and its succession.
 *
 * What the user can do here, and nothing more:
 *  - READ the dated history. Until the first dated change it is empty and the current values apply "since always"; the
 *    panel says so instead of inventing rows. A reason is shown only to the company that recorded the change.
 *  - SEE the successor (the cari that took over in the selected company) and the predecessors this cari took over.
 *  - RECORD that a NEW legal person's cari succeeds this one, when the server says this actor may (the succession
 *    permission AND the account status permission, company-wide). Fenced on THIS cari's company-account version. The
 *    account becomes passive; NO balance moves: the balance moves only by an approved transfer voucher in Finance.
 *
 * A wrong VKN/TCKN is NOT fixed here: it stays on the same cari and goes through the approved change request of the tax
 * identity panel. The read is a side read of the card and never moves its command-readiness slot; the command uses the
 * card's own contract context. Every user-facing word lives in ./identityHistory.ts.
 */

import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContractContext } from './clientContract'
import {
  IDENTITY_HISTORY_PROBLEM_CODE,
  IDENTITY_HISTORY_TEXT as T,
  identityHistoryProblemCodeOf,
  LEGAL_BASIS_MAX,
  SUCCESSION_KINDS,
  checkSuccession,
  type SuccessionDraft,
  type SuccessionLink,
} from './identityHistory'
import { fetchIdentityHistory, recordPartySuccession } from './identityHistoryApi'
import { fetchDeactivationWarning, type SideReadOutcome } from './deactivationWarningApi'
import { DEACTIVATION_WARNING_OUTCOME, type DeactivationWarningItem, type DeactivationWarningView } from './deactivationWarning'
import { CARI_ACCOUNT_STATUS_TEXT } from './cariCommandText'
import { useSuccessionCommand } from './useSuccessionCommand'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariTradeName.module.css'

/** The query kind of this panel's read; the card refreshes it after a names change. */
export const IDENTITY_HISTORY_QUERY_KIND = 'counterparty-identity-history'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
  /** Called after a recorded succession: the account became passive, so the card's reads refresh. */
  readonly onSucceeded?: () => void
}

const EMPTY_DRAFT: SuccessionDraft = { successorPartyId: '', kind: '', legalBasis: '', effectiveOn: '' }

/** ALS-D99: the old account's deactivation warning, read only after the server asked for its acknowledgement. */
type SuccessionWarning =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly view: DeactivationWarningView }
  | { readonly status: 'failed' }

function warningLine(item: DeactivationWarningItem): string {
  const W = CARI_ACCOUNT_STATUS_TEXT.warning
  switch (item.outcome) {
    case DEACTIVATION_WARNING_OUTCOME.open:
      return W.outcome.open(item.openItemCount ?? 0)
    case DEACTIVATION_WARNING_OUTCOME.clear:
      return W.outcome.clear
    case DEACTIVATION_WARNING_OUTCOME.denied:
      return W.outcome.denied
    default:
      return W.outcome.unavailable
  }
}

function LinkFacts({ link }: { readonly link: SuccessionLink }) {
  return (
    <dl className={styles.facts}>
      <dt>{T.successionKind[link.kind]}</dt>
      <dd data-succession-other={link.otherPartyId}>
        {link.otherPartyLegalName} ({link.otherPartyCode})
      </dd>
      <dt>{T.effectiveOnLabel}</dt>
      <dd>{link.effectiveOn}</dd>
      <dt>{T.legalBasisLabel}</dt>
      <dd>{link.legalBasis}</dd>
    </dl>
  )
}

export function CariIdentityHistoryPanel({ partyId, companyId, namespace, contractContext, onSucceeded }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const formId = useId()
  const errorId = useId()
  const queryKey = useMemo(() => [namespace, IDENTITY_HISTORY_QUERY_KIND, partyId, companyId], [
    namespace,
    partyId,
    companyId,
  ])

  const read = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchIdentityHistory(partyId, companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })

  const refresh = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey })
  }, [queryClient, queryKey])
  const command = useSuccessionCommand(companyId, contractContext, `cari.succession:${partyId}`, refresh)
  const [draft, setDraft] = useState<SuccessionDraft>(EMPTY_DRAFT)
  const [fieldError, setFieldError] = useState<{ field: keyof SuccessionDraft; message: string } | null>(null)
  const [warning, setWarning] = useState<SuccessionWarning | null>(null)
  const [acknowledged, setAcknowledged] = useState(false)
  const [acknowledgementMissing, setAcknowledgementMissing] = useState(false)

  const readWarning = useCallback(async () => {
    setWarning({ status: 'loading' })
    setAcknowledged(false)
    const captured = boundary.capture()
    const answer: SideReadOutcome<DeactivationWarningView> = await fetchDeactivationWarning(partyId, companyId)
    if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, captured)
    setWarning(answer.type === 'ok' ? { status: 'ready', view: answer.data } : { status: 'failed' })
  }, [boundary, companyId, partyId])

  if (read.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-identity-history" data-state="loading">
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
      <div className={styles.panel} data-testid="cari-identity-history" data-state={state}>
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
  const locked = command.busy || command.unresolved
  const canRecord = view.viewer.canRecordSuccession && view.successor === null

  const update = (field: keyof SuccessionDraft, value: string) => {
    setDraft((current) => ({ ...current, [field]: value }))
    setFieldError(null)
  }
  const describedBy = (field: keyof SuccessionDraft, hintId: string) =>
    fieldError?.field === field ? `${hintId} ${errorId}` : hintId

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (locked) return
    const check = checkSuccession(draft, view.partyId)
    if (!check.ok) {
      setFieldError({ field: check.field, message: check.message })
      return
    }
    setFieldError(null)
    // ALS-D99: once the server asked for it, the acknowledgement is required before the next send, and only the warning
    // read for this form travels with it.
    const acknowledging = warning?.status === 'ready' && warning.view.acknowledgementRequired
    if (warning !== null && (warning.status !== 'ready' || (acknowledging && !acknowledged))) {
      setAcknowledgementMissing(warning.status === 'ready')
      return
    }
    setAcknowledgementMissing(false)
    const payload = acknowledging ? { ...check.value, exposureWarningAcknowledged: true as const } : check.value
    let warningRequired = false
    const ok = await command.run(
      `record:${partyId}${acknowledging ? ':acknowledged' : ''}`,
      async (context) => {
        const outcome = await recordPartySuccession(context, partyId, view.accountVersion, payload)
        warningRequired = outcome.type === 'http' && outcome.status === 422
          && identityHistoryProblemCodeOf(outcome.body) === IDENTITY_HISTORY_PROBLEM_CODE.deactivationWarningAcknowledgementRequired
        return outcome
      },
      T.saved,
    )
    if (ok) {
      setDraft(EMPTY_DRAFT)
      setWarning(null)
      setAcknowledged(false)
      onSucceeded?.()
    } else if (warningRequired) {
      await readWarning()
    }
  }

  return (
    <div className={styles.panel} data-testid="cari-identity-history" data-state="ready">
      <h3 className={styles.title}>{T.heading}</h3>

      <h4 className={styles.title}>{T.identityHeading}</h4>
      {view.revisions.length === 0 ? (
        <p className={styles.muted} data-identity-history-empty>
          {T.noRevisions}
        </p>
      ) : (
        <ol data-identity-revisions>
          {view.revisions.map((revision) => (
            <li key={`${revision.attribute}:${revision.sequence}`} data-revision-kind={revision.kind}>
              <span>{T.attribute[revision.attribute]}</span>
              {': '}
              <span className={styles.value}>{revision.value}</span>
              {' — '}
              <span>{T.revisionKind[revision.kind]}</span>
              {', '}
              <span>{revision.effectiveFrom ?? T.sinceAlways}</span>
              {revision.reason === null ? null : (
                <>
                  {' — '}
                  <span>
                    {T.reasonLabel}: {revision.reason}
                  </span>
                </>
              )}
              {revision.recordedInActiveCompany || revision.kind === 'Baseline' ? null : (
                <span className={styles.muted}> ({T.recordedElsewhere})</span>
              )}
            </li>
          ))}
        </ol>
      )}

      <h4 className={styles.title}>{T.statusHeading}</h4>
      {view.eDocumentStatusRevisions.length === 0 ? (
        <p className={styles.muted} data-status-history-empty>
          {T.noStatusRevisions}
        </p>
      ) : (
        <ol data-status-revisions>
          {view.eDocumentStatusRevisions.map((revision) => (
            <li key={revision.sequence}>
              {revision.effectiveFrom}: {T.status[revision.status]}
              {revision.recordedInActiveCompany ? null : (
                <span className={styles.muted}> ({T.recordedElsewhere})</span>
              )}
            </li>
          ))}
        </ol>
      )}

      {view.successor === null ? null : (
        <section data-successor aria-label={T.successorHeading}>
          <h4 className={styles.title}>{T.successorHeading}</h4>
          <LinkFacts link={view.successor} />
          <p className={styles.muted}>{T.successorNote}</p>
        </section>
      )}

      {view.predecessors.length === 0 ? null : (
        <section data-predecessors aria-label={T.predecessorsHeading}>
          <h4 className={styles.title}>{T.predecessorsHeading}</h4>
          {view.predecessors.map((link) => (
            <LinkFacts key={link.predecessorPartyId} link={link} />
          ))}
        </section>
      )}

      {canRecord ? (
        <form
          className={styles.form}
          onSubmit={(event) => void submit(event)}
          aria-busy={command.busy}
          aria-labelledby={`${formId}-heading`}
          noValidate
        >
          <h4 id={`${formId}-heading`} className={styles.title}>
            {T.successionFormHeading}
          </h4>
          <p className={styles.muted}>{T.successionFormNote}</p>
          <p className={styles.muted} data-balance-note>
            {T.balanceNote}
          </p>

          <label htmlFor={`${formId}-successor`}>{T.successorIdLabel}</label>
          <input
            id={`${formId}-successor`}
            type="text"
            value={draft.successorPartyId}
            disabled={locked}
            required
            aria-describedby={describedBy('successorPartyId', `${formId}-successor-hint`)}
            aria-invalid={fieldError?.field === 'successorPartyId'}
            onChange={(event) => update('successorPartyId', event.target.value)}
          />
          <p id={`${formId}-successor-hint`} className={styles.muted}>
            {T.successorIdHint}
          </p>

          <label htmlFor={`${formId}-kind`}>{T.kindLabel}</label>
          <select
            id={`${formId}-kind`}
            value={draft.kind}
            disabled={locked}
            required
            aria-describedby={fieldError?.field === 'kind' ? errorId : undefined}
            aria-invalid={fieldError?.field === 'kind'}
            onChange={(event) => update('kind', event.target.value)}
          >
            <option value="">{T.kindPlaceholder}</option>
            {SUCCESSION_KINDS.map((kind) => (
              <option key={kind} value={kind}>
                {T.successionKind[kind]}
              </option>
            ))}
          </select>

          <label htmlFor={`${formId}-basis`}>{T.legalBasisLabel}</label>
          <textarea
            id={`${formId}-basis`}
            value={draft.legalBasis}
            maxLength={LEGAL_BASIS_MAX}
            disabled={locked}
            required
            aria-describedby={describedBy('legalBasis', `${formId}-basis-hint`)}
            aria-invalid={fieldError?.field === 'legalBasis'}
            onChange={(event) => update('legalBasis', event.target.value)}
          />
          <p id={`${formId}-basis-hint`} className={styles.muted}>
            {T.legalBasisHint}
          </p>

          <label htmlFor={`${formId}-effective`}>{T.effectiveOnLabel}</label>
          <input
            id={`${formId}-effective`}
            type="date"
            value={draft.effectiveOn}
            disabled={locked}
            required
            aria-describedby={describedBy('effectiveOn', `${formId}-effective-hint`)}
            aria-invalid={fieldError?.field === 'effectiveOn'}
            onChange={(event) => update('effectiveOn', event.target.value)}
          />
          <p id={`${formId}-effective-hint`} className={styles.muted}>
            {T.effectiveOnHint}
          </p>

          {warning === null ? null : (
            <section aria-label={T.warningHeading} data-succession-warning={warning.status}>
              <h4>{T.warningHeading}</h4>
              {warning.status === 'loading' ? (
                <p className={styles.muted} role="status">{T.warningLoading}</p>
              ) : warning.status === 'failed' ? (
                <>
                  <p className={styles.error} role="alert">{T.warningUnavailable}</p>
                  <button type="button" className={styles.button} disabled={locked} onClick={() => void readWarning()}>
                    {T.warningReadAgain}
                  </button>
                </>
              ) : (
                <>
                  <ul data-warning-items>
                    {warning.view.items.map((item) => (
                      <li key={item.kind} data-kind={item.kind} data-outcome={item.outcome}>
                        {CARI_ACCOUNT_STATUS_TEXT.warning.kind[item.kind]}: {warningLine(item)}
                      </li>
                    ))}
                  </ul>
                  {warning.view.acknowledgementRequired ? (
                    <label htmlFor={`${formId}-acknowledge`}>
                      <input
                        id={`${formId}-acknowledge`}
                        type="checkbox"
                        checked={acknowledged}
                        disabled={locked}
                        aria-invalid={acknowledgementMissing ? 'true' : undefined}
                        onChange={(event) => {
                          setAcknowledged(event.target.checked)
                          setAcknowledgementMissing(false)
                        }}
                      />{' '}
                      {T.warningAcknowledge}
                    </label>
                  ) : null}
                  {acknowledgementMissing ? (
                    <p className={styles.error} role="alert">{T.warningAcknowledgeRequired}</p>
                  ) : null}
                </>
              )}
            </section>
          )}

          {fieldError === null ? null : (
            <p id={errorId} className={styles.error} role="alert">
              {fieldError.message}
            </p>
          )}
          <div className={styles.actions}>
            <button type="submit" className={styles.button} disabled={locked}>
              {command.busy ? T.submitting : T.submit}
            </button>
          </div>
        </form>
      ) : view.successor === null ? (
        <p className={styles.muted} data-succession-read-only>
          {T.readOnlyNote}
        </p>
      ) : null}

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
