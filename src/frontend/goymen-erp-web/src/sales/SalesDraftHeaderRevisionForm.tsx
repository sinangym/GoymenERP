import { useContext, useEffect, useId, useRef, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { SALES_HEADER_WRITE_CAPABILITY } from './capabilities'
import { APPROVAL_STATE, isTerminalState, REV_0, SALES_PROBLEM_CODE, type DraftDetail, type DraftMutation } from './contract'
import { contactLabel } from './format'
import {
  buildHeaderRevisionRequest,
  DUE_LIMITS,
  headerFormValues,
  type DraftHeader,
  type HeaderFormValues,
  type HeaderRevisionRequest,
  type OwnerRef,
} from './headerRevision'
import { fetchDraftHeader, fetchSalesOwners, sendDraftHeaderRevision, type SalesOwnersOutcome } from './headerRevisionApi'
import {
  abandon,
  applyOutcome,
  beginSend,
  decideSubmit,
  generateOrderAttemptKey,
  isOutcomeStillUnknown,
  liveSnapshot,
  orderAttemptBinding,
  type OrderAttemptSnapshot,
  type OrderAttemptState,
} from './orderAttempt'
import { SalesContactInformationSourceContext, SalesCurrencySourceContext, SalesReferenceSourceContext } from './referenceSource'
import { HEADER_REVISION_TEXT as T } from './salesText'
import { SalesDueTermsEditor } from './SalesDueTermsEditor'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesPages.module.css'
import { Button } from '../ui/Button'

type HeaderAttempt = OrderAttemptState<HeaderRevisionRequest, DraftMutation>

const scopeOf = (draftId: string) => `sales.draft.header-revision:${draftId}`

let rowSeed = 0
const nextRowKey = () => `row-${(rowSeed += 1)}`

/**
 * API-04 — "Başlığı revize et" on S-04 behind the `sales.orders.header.write` control: the customer account, the currency, the due
 * terms (instalments: basis, days, share), the sales owner (SAT-D27 picker) and the delivery address / contact, with a required
 * reason, sent as ONE keyed attempt (the quantity revision's attempt rules: an unknown outcome keeps the key and retries the
 * same payload). The revisable header is read when the form opens (`GET …/header`); an approved order re-enters approval and
 * the screen says what ships meanwhile (SAT-D103) and what a customer/currency change stops. The server decides every rule;
 * the screen computes no due date and adds no shares.
 */
export function SalesDraftHeaderRevisionForm({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const allowed = hasCapability(shell, SALES_HEADER_WRITE_CAPABILITY)
  const [open, setOpen] = useState(false)
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const header = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-header', detail.draft.id, detail.draft.version],
    enabled: allowed && open && namespace !== null,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    // A recorded revision re-reads the header for the new version; the editor stays mounted meanwhile (its outcome notice too).
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftHeader(detail.draft.id, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  if (!allowed) return null
  const stateAllowsRevision = !isTerminalState(detail.draft.state) && (
    detail.draft.approvalState === APPROVAL_STATE.preparing ||
    detail.draft.approvalState === APPROVAL_STATE.rejected ||
    detail.draft.approvalState === APPROVAL_STATE.approved)

  if (!open) {
    return (
      <div className={styles.actions} data-testid="sales-header-revision">
        <Button type="button" variant="secondary" disabled={!stateAllowsRevision} onClick={() => setOpen(true)}>{T.open}</Button>
        {!stateAllowsRevision ? <p className={styles.muted}>{T.stateUnavailable}</p> : null}
      </div>
    )
  }

  const outcome = header.data
  if (outcome === undefined) {
    return <p role="status" className={styles.muted} data-testid="sales-header-revision-loading">{T.loading}</p>
  }

  if (outcome.type !== 'ok-single') {
    const forbidden = outcome.type === 'http' && outcome.status === 403
    return (
      <div className={styles.actions} data-testid="sales-header-revision-error">
        <p role="alert" className={styles.notice}>{forbidden ? T.readForbidden : T.readFailed}</p>
        <Button type="button" variant="secondary" onClick={() => void header.refetch()}>{T.retryRead}</Button>
        <Button type="button" variant="ghost" className={styles.linkButton} onClick={() => setOpen(false)}>{T.close}</Button>
      </div>
    )
  }

  if (outcome.data.dueTermsDisclosure === 'Withheld') {
    // Without the price read the current due terms are unknown here: nothing is offered that could keep or clear them blindly
    // (the server refuses such a revision too).
    return (
      <div className={styles.actions} data-testid="sales-header-revision-withheld">
        <p role="status" className={styles.notice}>{T.dueTermsWithheld}</p>
        <Button type="button" variant="ghost" className={styles.linkButton} onClick={() => setOpen(false)}>{T.close}</Button>
      </div>
    )
  }

  return (
    <HeaderRevisionEditor
      shell={shell}
      detail={detail}
      companyId={companyId}
      header={outcome.data}
      stateAllowsRevision={stateAllowsRevision}
      onClose={() => setOpen(false)}
      onRecorded={onRecorded}
    />
  )
}

function HeaderRevisionEditor({
  shell,
  detail,
  companyId,
  header,
  stateAllowsRevision,
  onClose,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly header: DraftHeader
  readonly stateAllowsRevision: boolean
  readonly onClose: () => void
  readonly onRecorded: () => void
}) {
  const [values, setValues] = useState<HeaderFormValues>(() => headerFormValues(header, nextRowKey))
  const [attempt, setAttempt] = useState<HeaderAttempt>({ phase: 'idle' })
  const [localError, setLocalError] = useState<string | null>(null)
  const [accountSearch, setAccountSearch] = useState('')
  const [ownerSearch, setOwnerSearch] = useState('')
  const attemptRef = useRef(attempt)
  attemptRef.current = attempt
  // A newer header (after a recorded revision or another change) restarts the values unless an attempt is in flight or unknown.
  const [seenVersion, setSeenVersion] = useState(header.draftVersion)
  if (seenVersion !== header.draftVersion && attempt.phase !== 'submitting' && !isOutcomeStillUnknown(attempt)) {
    setSeenVersion(header.draftVersion)
    setValues(headerFormValues(header, nextRowKey))
  }
  const headingId = useId()
  const reasonId = useId()
  const accountId = useId()
  const currencyId = useId()
  const addressId = useId()
  const contactId = useId()
  const ownerId = useId()

  const useReferences = useContext(SalesReferenceSourceContext)
  const references = useReferences(companyId, detail.draft.businessDate, accountSearch)
  const accounts = references.status === 'ready' ? references.options.accounts : references.header?.accounts ?? null
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const currencies = useCurrencies(companyId, detail.draft.businessDate)
  const useContactInformation = useContext(SalesContactInformationSourceContext)
  const contactInformation = useContactInformation(companyId, values.partyId, values.companyAccountId)
  const owners = useSalesOwners(companyId, ownerSearch)

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const disabled = frozen || !stateAllowsRevision
  const notice = revisionNotice(attempt)

  async function send(snapshot: OrderAttemptSnapshot<HeaderRevisionRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<HeaderRevisionRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const outcome = await sendDraftHeaderRevision(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
  }

  function start() {
    setLocalError(null)
    const built = buildHeaderRevisionRequest(header, values)
    if (!built.ok) {
      setLocalError(T.localError(built.error))
      return
    }
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/header-revision`, binding, companyId, payload: built.payload },
      generateOrderAttemptKey,
    )
    if (decision.type === 'send') void send(decision.snapshot)
  }

  function retry() {
    const snapshot = liveSnapshot(attemptRef.current)
    if (snapshot !== null && attemptRef.current.phase !== 'submitting') void send(snapshot)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
  }

  const set = (patch: Partial<HeaderFormValues>) => setValues((current) => ({ ...current, ...patch }))

  function chooseAccount(value: string) {
    const [partyId, companyAccountId] = value.split('|')
    if (partyId === undefined || companyAccountId === undefined) return
    // Another account's addresses and contacts are not this one's: the references are chosen again for the new account.
    set({ partyId, companyAccountId, deliveryAddress: null, contact: null })
  }

  const accountValue = `${values.partyId}|${values.companyAccountId}`
  const currentAccountListed = accounts?.some((a) => `${a.partyId}|${a.companyAccountId}` === accountValue) ?? false
  const currencyListed = currencies.status === 'ready' && currencies.data.some((c) => c.currencyId === values.currencyId)
  const refValue = (ref: OwnerRef | null) => (ref === null ? '' : `${ref.id}|${ref.expectedVersion}`)
  const parseRef = (value: string): OwnerRef | null => {
    const [id, expectedVersion] = value.split('|')
    return id === undefined || expectedVersion === undefined || id === '' ? null : { id, expectedVersion }
  }
  const addresses = contactInformation.status === 'ready' ? contactInformation.data.addresses : []
  const contacts = contactInformation.status === 'ready' ? contactInformation.data.contacts : []
  const ownerList = owners.type === 'ready' ? owners.data.owners : []

  return (
    <section className={styles.actions} aria-labelledby={headingId} data-testid="sales-header-revision-form">
      <h3 id={headingId}>{T.heading}</h3>
      <p className={styles.muted}>{T.description}</p>
      {detail.draft.approvalState === APPROVAL_STATE.approved ? <p className={styles.muted}>{T.approvedWarning}</p> : null}
      <p className={styles.muted}>{T.customerWarning}</p>
      {header.customerOrCurrencyRevisionPending ? <p role="status" className={styles.notice}>{T.pendingCustomer}</p> : null}
      {!stateAllowsRevision ? <p className={styles.muted}>{T.stateUnavailable}</p> : null}
      {localError === null ? null : <p role="alert" className={styles.notice}>{localError}</p>}
      {notice === null ? null : <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice}>{notice}</p>}

      <fieldset className={styles.line} disabled={disabled}>
        <legend>{T.account}</legend>
        <label htmlFor={`${accountId}-search`}>{T.accountSearch}</label>
        <input id={`${accountId}-search`} value={accountSearch} onChange={(event) => setAccountSearch(event.target.value)} />
        <label htmlFor={accountId}>{T.account}</label>
        <select id={accountId} value={accountValue} onChange={(event) => chooseAccount(event.target.value)}
          disabled={accounts === null}>
          {currentAccountListed ? null : <option value={accountValue}>{T.currentAccount}</option>}
          {(accounts ?? []).map((a) => (
            <option key={`${a.partyId}|${a.companyAccountId}`} value={`${a.partyId}|${a.companyAccountId}`}>{a.label}</option>
          ))}
        </select>
        {accounts === null ? <span className={styles.muted}>{T.accountsUnavailable}</span> : null}

        <label htmlFor={currencyId}>{T.currency}</label>
        <select id={currencyId} value={values.currencyId} disabled={currencies.status !== 'ready'}
          onChange={(event) => set({ currencyId: event.target.value })}>
          {currencyListed ? null : <option value={values.currencyId}>{T.currentCurrency}</option>}
          {currencies.status === 'ready'
            ? currencies.data.map((c) => <option key={c.currencyId} value={c.currencyId}>{c.code}</option>)
            : null}
        </select>
        {currencies.status === 'unavailable' ? <span className={styles.muted}>{T.currenciesUnavailable}</span> : null}
      </fieldset>

      <fieldset className={styles.line} disabled={disabled}>
        <legend>{T.deliveryAddress}</legend>
        <label htmlFor={addressId}>{T.deliveryAddress}</label>
        <select id={addressId} value={refValue(values.deliveryAddress)} disabled={contactInformation.status !== 'ready'}
          onChange={(event) => set({ deliveryAddress: parseRef(event.target.value) })}>
          <option value="">{T.none}</option>
          {values.deliveryAddress !== null && !addresses.some((a) => a.id === values.deliveryAddress?.id)
            ? <option value={refValue(values.deliveryAddress)}>{T.current(values.deliveryAddress.id)}</option> : null}
          {addresses.map((a) => <option key={a.id} value={`${a.id}|${a.version}`}>{a.text}</option>)}
        </select>
        <label htmlFor={contactId}>{T.contact}</label>
        <select id={contactId} value={refValue(values.contact)} disabled={contactInformation.status !== 'ready'}
          onChange={(event) => set({ contact: parseRef(event.target.value) })}>
          <option value="">{T.none}</option>
          {values.contact !== null && !contacts.some((c) => c.id === values.contact?.id)
            ? <option value={refValue(values.contact)}>{T.current(values.contact.id)}</option> : null}
          {contacts.map((c) => <option key={c.id} value={`${c.id}|${c.version}`}>{contactLabel(c)}</option>)}
        </select>
        {contactInformation.status === 'unavailable' ? <span className={styles.muted}>{T.contactUnavailable}</span> : null}
      </fieldset>

      <fieldset className={styles.line} disabled={disabled}>
        <legend>{T.salesOwner}</legend>
        <label htmlFor={`${ownerId}-search`}>{T.salesOwnerSearch}</label>
        <input id={`${ownerId}-search`} value={ownerSearch} onChange={(event) => setOwnerSearch(event.target.value)} />
        <label htmlFor={ownerId}>{T.salesOwner}</label>
        <select id={ownerId} value={values.salesOwnerUserId ?? ''} disabled={owners.type !== 'ready'}
          onChange={(event) => set({ salesOwnerUserId: event.target.value === '' ? null : event.target.value })}>
          <option value="">{T.none}</option>
          {values.salesOwnerUserId !== null && !ownerList.some((o) => o.userId === values.salesOwnerUserId) ? (
            <option value={values.salesOwnerUserId}>
              {values.salesOwnerUserId === header.salesOwnerUserId && header.salesOwnerDisplayName !== null
                ? header.salesOwnerDisplayName : T.current(values.salesOwnerUserId)}
            </option>
          ) : null}
          {ownerList.map((o) => <option key={o.userId} value={o.userId}>{o.displayName}</option>)}
        </select>
        {owners.type === 'ready' && owners.data.hasMore ? <span className={styles.muted}>{T.salesOwnersMore}</span> : null}
        {owners.type !== 'ready' && owners.type !== 'loading' ? <span className={styles.muted}>{T.salesOwnersUnavailable}</span> : null}
      </fieldset>

      <SalesDueTermsEditor
        rows={values.instalments}
        onChange={(instalments) => set({ instalments })}
        nextKey={nextRowKey}
        disabled={disabled}
        testId="sales-header-due-terms"
      />

      <label htmlFor={reasonId}>{T.reason}</label>
      <textarea id={reasonId} value={values.reason} maxLength={DUE_LIMITS.reason} required aria-describedby={`${reasonId}-hint`}
        disabled={disabled} onChange={(event) => set({ reason: event.target.value })} />
      <span id={`${reasonId}-hint`} className={styles.muted}>{T.reasonHint}</span>
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <>
          <Button type="button" variant="secondary" onClick={retry}>{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={leave}>{T.abandon}</Button>
        </>
      ) : null}
      <Button type="button" variant="primary" disabled={disabled} onClick={start}>{T.submit}</Button>
      <Button type="button" variant="ghost" className={styles.linkButton} disabled={frozen} onClick={onClose}>{T.close}</Button>
    </section>
  )
}

type OwnersState = SalesOwnersOutcome | { readonly type: 'loading' }

/** SAT-D27: the sales-owner picker's list for the server's own search, re-read when the search changes. */
function useSalesOwners(companyId: string, q: string): OwnersState {
  const [state, setState] = useState<{ readonly key: string; readonly value: SalesOwnersOutcome } | null>(null)
  const key = `${companyId}|${q.trim()}`
  useEffect(() => {
    const controller = new AbortController()
    void fetchSalesOwners(companyId, q, controller.signal).then((outcome) => {
      if (!controller.signal.aborted) setState({ key, value: outcome })
    })
    return () => controller.abort()
  }, [companyId, q, key])
  return state !== null && state.key === key ? state.value : { type: 'loading' }
}

function revisionNotice(attempt: HeaderAttempt): string | null {
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting': return T.submitting
    case 'unknown':
    case 'abandoned': return attempt.reason === 'service-unavailable' ? `${T.unavailable} ${T.unknown}` : T.unknown
    case 'completed':
      // SAT-D126: an approved order whose ONLY change was the sales owner / delivery references stays approved.
      return attempt.result.isReplay ? T.replayed
        : attempt.result.detail.draft.approvalState === APPROVAL_STATE.approved ? T.completedNoReapproval
          : attempt.result.detail.draft.approvalState === APPROVAL_STATE.preparing && attempt.result.detail.draft.revision !== REV_0
            ? T.completed : T.completedUnapproved
    case 'failed': {
      const code = attempt.problem?.code ?? null
      const errors = attempt.problem?.errors ?? []
      const blocked = code === SALES_PROBLEM_CODE.revisionBlocked ? errors.map((error) => T.blockedSource(error.field)).join(' ') : ''
      const fields = attempt.failure === 'validation' || attempt.failure === 'conflict'
        ? errors.map((error) => T.fieldCode[error.code] ?? '').filter((text) => text !== '').join(' ')
        : ''
      const base = attempt.failure === 'unauthenticated' ? T.unauthenticated
        : attempt.failure === 'forbidden' ? T.forbidden
          : attempt.failure === 'not-found' ? T.notFound
            : attempt.failure === 'conflict' ? (code === SALES_PROBLEM_CODE.versionConflict ? T.versionConflict : T.conflict)
              : attempt.failure === 'validation' ? T.validation
                : T.unexpected
      return [base, blocked, fields].filter((part) => part !== '').join(' ')
    }
    default: return null
  }
}
