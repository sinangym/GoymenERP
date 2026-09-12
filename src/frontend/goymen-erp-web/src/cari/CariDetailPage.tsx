/**
 * C-03 Cari 360 — authorized core summary.
 *
 * Scope is the accepted spec §5.1 and nothing beyond it. Deliberately absent, and
 * forbidden by `mockTruthForbidden`: balance, risk, credit limit, open items,
 * orders, stock, documents, bank/tax/registry, timeline, CRM, AI. A tab or a card
 * whose owner API does not exist is not rendered empty — it is not rendered.
 *
 * There is no zero, no dash and no placeholder standing in for a number nobody
 * has computed yet. An empty card reads as "this counterparty has none", which is
 * a claim, and we are not entitled to make it.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchCounterpartyDetail } from './detailApi'
import { PARTY_KIND, PROBLEM_CODE, problemCodeOf } from './contract'
import { API_BASE, isCommandContractReady } from './clientContract'
import { LEGACY_ROLE_HEADING, NO_COMMERCIAL_ROLE_DETAIL, rolesText } from './roleText'
import { CARI_ACCOUNT_STATUS_TEXT as STATUS, CARI_CARD_TEXT as CARD } from './cariCommandText'
import { CARI_CONTROL_CAPABILITY, targetIsActive } from './accountStatusContract'
import { AccountStatusControl, AccountStatusRecoveryNotice } from './AccountStatusControl'
import { useAccountStatusFlow, type AccountStatusApplied } from './useAccountStatusFlow'
import type { CounterpartyDetail } from './detailContract'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { CAPABILITY } from '../shell/capabilities'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import { StateBlock } from '../shell/StateBlock'
import styles from './CariDetailPage.module.css'

/** See CariListPage: a contract refusal is about this client, never about the record. */
const CONTRACT_PROBLEM_CODES = new Set<string>([
  PROBLEM_CODE.clientContractRequired,
  PROBLEM_CODE.clientContractUnsupported,
])

const CONTRACT_REFUSAL_TITLE = 'Uyumlu istemci gerekli'
const CONTRACT_REFUSAL_DETAIL =
  'Bu cari işlemi desteklenen istemci sözleşmesi ile yeniden açılmalıdır. ' +
  'Sayfayı yenileyin; bekleyen bir işlemi otomatik tekrarlamayın.'

const KIND_LABEL: Record<number, string> = {
  [PARTY_KIND.LegalEntity]: 'Tüzel kişi',
  [PARTY_KIND.NaturalPerson]: 'Gerçek kişi',
}

/**
 * Remounted on every context change, exactly like the list (#147 P1-2). A detail
 * screen is worse than a list here: the Cari's NAME is in the heading and the
 * breadcrumb, so a stale render would put one company's counterparty under
 * another company's label.
 */
export function CariDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { partyId } = useParams()
  return <CariDetail key={`${namespace ?? 'no-context'}::${partyId ?? ''}`} namespace={namespace} />
}

function CariDetail({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const entryGeneration = shell.routeEntryGeneration
  const { partyId } = useParams()
  const boundary = useAuthBoundary()
  const directory = useCompanyDirectory()

  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null

  const activeCompany = useMemo(
    () => directory.find((c) => c.id === activeCompanyId) ?? null,
    [directory, activeCompanyId],
  )
  const canEditContact = hasCapability(shell, CAPABILITY.cariAccountsContactUpdate)
  /**
   * The status action's own control capability — read straight from the server snapshot,
   * exactly like the route capabilities, and never derived from the detail grant: reading
   * a record and changing its company account's state are different authorities. The
   * server publishes no such id today, so this is false and the action is absent.
   */
  const canChangeStatus = hasCapability(shell, CARI_CONTROL_CAPABILITY.accountStatusUpdate)
  const queryClient = useQueryClient()

  /**
   * A status change the server APPLIED (204), waiting for the fresh read it owes.
   *
   * The 204 has no body, so nothing about the record's new state is known until that
   * read LANDS — `baseline` is the detail query's `dataUpdatedAt` at the moment of the
   * 204, and only a later stamp counts. A read that is paused (offline) or that fails
   * leaves the cached data exactly where it was, and that data is NOT the fresh read,
   * however settled the query looks (Dirac #250 P2-2). While it is pending the action
   * is closed and the screen says so; the retry it offers is the read's, never the write.
   */
  const [pendingAdopt, setPendingAdopt] = useState<{
    readonly applied: AccountStatusApplied
    readonly baseline: number
  } | null>(null)
  /** The last adopted result, shown until the next window opens. */
  const [saved, setSaved] = useState<AccountStatusApplied | null>(null)

  const query = useQuery({
    queryKey: [
      namespace ?? 'no-context',
      // Entry-scoped for the same reason as C-01: a Back within the stale window
      // must not paint the previous entry's record while its authorization is
      // still being revalidated (#148).
      entryGeneration,
      'counterparty-detail',
      partyId ?? '',
    ],
    enabled: namespace !== null && singleCompanyContext && (partyId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchCounterpartyDetail(
        partyId ?? '',
        activeCompanyId ?? '',
        signal,
        namespace === null ? null : { origin: API_BASE, namespace },
      )
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const state = (
    query.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<CounterpartyDetail>

  /**
   * Adopting the fresh read a 204 owes. The cached reads in this context — the detail
   * AND the list, which shows the account's state in its rows — are dropped when the
   * control reports the success; this effect closes the pending state once the detail
   * read has actually landed, and leaves it open (read recovery) while it has not.
   */
  const readStamp = query.dataUpdatedAt
  const onApplied = useCallback(
    (applied: AccountStatusApplied) => {
      setSaved(null)
      setPendingAdopt({ applied, baseline: readStamp })
      if (namespace !== null) {
        void queryClient.invalidateQueries({ predicate: (q) => q.queryKey[0] === namespace })
      }
    },
    [namespace, queryClient, readStamp],
  )
  const onBeginNewAttempt = useCallback(() => setSaved(null), [])
  const readOutcomeType = query.data?.type ?? null
  const fetchStatus = query.fetchStatus
  useEffect(() => {
    if (pendingAdopt === null || fetchStatus !== 'idle') return
    // Settled, but on the SAME data the 204 was issued against: not the fresh read.
    if (readStamp <= pendingAdopt.baseline || readOutcomeType !== 'ok-single') return
    // oxlint-disable-next-line react/set-state-in-effect -- adopts the fresh read a definite 204 owes, once
    setSaved(pendingAdopt.applied)
    setPendingAdopt(null)
  }, [pendingAdopt, fetchStatus, readStamp, readOutcomeType])

  /**
   * The action's whole state — attempt, window, reason, dirty scope — lives HERE, above
   * the branches below, so a read that fails during a same-key retry cannot unmount it.
   */
  const contractContext = useMemo(
    () => (namespace === null ? null : { origin: API_BASE, namespace }),
    [namespace],
  )
  const readyDetail = state.kind === 'ready' ? state.data : null
  const statusTriggerRef = useRef<HTMLButtonElement | null>(null)
  const statusSummaryRef = useRef<HTMLParagraphElement | null>(null)
  const statusUnresolvedRef = useRef<HTMLDivElement | null>(null)
  const flow = useAccountStatusFlow({
    triggerRef: statusTriggerRef,
    summaryRef: statusSummaryRef,
    unresolvedRef: statusUnresolvedRef,
    partyId: partyId ?? '',
    companyId: activeCompanyId,
    namespace,
    detail: readyDetail,
    readStamp,
    refetch: query.refetch,
    // Computed each render: the slot moves when the detail read records itself, and
    // that read is what re-renders this card.
    contractReady: isCommandContractReady(contractContext),
    disabled: pendingAdopt !== null,
    onApplied,
    onBeginNewAttempt,
  })

  // C-03A is single-company, like C-01A. Group scope has no accepted per-company
  // representation, and a detail screen cannot honestly show "the" account when
  // there could be several.
  if (!singleCompanyContext) {
    return (
      <section aria-labelledby="cari-detail-heading" className={styles.page}>
        <h1 id="cari-detail-heading">Cari 360</h1>
        <StateBlock
          state={{ kind: 'no-authorized-company' }}
          subject="Cari 360 için tek şirket bağlamı"
        />
      </section>
    )
  }

  const contractRefusal =
    query.data?.type === 'http' &&
    query.data.status === 409 &&
    CONTRACT_PROBLEM_CODES.has(problemCodeOf(query.data.body) ?? '')

  if (contractRefusal) {
    return (
      <section aria-labelledby="cari-detail-heading" className={styles.page}>
        <h1 id="cari-detail-heading">Cari 360</h1>
        <BackToList />
        <div role="alert" data-state="client-contract">
          <p>
            <strong>{CONTRACT_REFUSAL_TITLE}</strong>
          </p>
          <p>{CONTRACT_REFUSAL_DETAIL}</p>
        </div>
      </section>
    )
  }

  if (state.kind !== 'ready') {
    return (
      <section aria-labelledby="cari-detail-heading" className={styles.page}>
        {/* The heading stays generic until a record is verified. Putting the id or
            a partial name here would confirm a record exists to a caller who may
            not be entitled to know it — the same non-disclosure rule as the
            404 block itself (§6). */}
        <h1 id="cari-detail-heading">Cari 360</h1>
        <BackToList />
        {pendingAdopt !== null ? (
          // A READ recovery: the change was applied, the read after it was not. The retry
          // below is the read's own; no second PUT exists on this path.
          <p className={styles.recovery} role="status" data-state="read-recovery">
            {STATUS.readRecovery}
          </p>
        ) : null}
        <StateBlock state={state} subject="cari" onRetry={() => void query.refetch()} />
        {/* An unresolved attempt keeps its recovery — same key, same body, explicit retry
            or abandon — while the record cannot be read (Dirac #250 P2-1). */}
        {canChangeStatus ? <AccountStatusRecoveryNotice flow={flow} unresolvedRef={statusUnresolvedRef} /> : null}
      </section>
    )
  }

  const detail = state.data
  const account = detail.account

  return (
    <section aria-labelledby="cari-detail-heading" className={styles.page}>
      <h1 id="cari-detail-heading">{detail.legalName}</h1>
      <BackToList />

      {/**
        * TWO SECTIONS, and the split is not cosmetic.
        *
        * One Party is shared by every company the user is authorized in; the account
        * under it belongs to ONE of them. Presented as a single flat list, "Pasif"
        * read as a fact about the counterparty when it was a fact about this
        * company's relationship with it — and once fields here become editable, the
        * same flat list would leave the user unsure whether they are about to change
        * what every other company sees. So each fact now sits under the thing it is
        * actually about, and each section says whose data it is.
        */}
      <section aria-labelledby="cari-identity-heading" className={styles.block} data-section="identity">
        <h2 id="cari-identity-heading" className={styles.blockTitle}>
          {CARD.identityHeading}
        </h2>
        <p className={styles.blockNote}>{CARD.identityNote}</p>
        <dl className={styles.facts}>
          <Fact label="Cari kodu" value={detail.code} />
          <Fact label="Hukuki unvan" value={detail.legalName} />
          {detail.tradeName !== null && detail.tradeName !== detail.legalName ? (
            <Fact label="Ticari unvan" value={detail.tradeName} />
          ) : null}
          <Fact label="Tür" value={KIND_LABEL[detail.kind]} />
          {/* The GLOBAL party state. Its counterpart lives in the other section, and
              the two are never collapsed into one "Durum". */}
          <Fact
            label="Taraf durumu"
            value={detail.isActive ? 'Aktif' : 'Pasif'}
            data-party-state={detail.isActive ? 'active' : 'inactive'}
          />
        </dl>
      </section>

      <section aria-labelledby="cari-account-heading" className={styles.block} data-section="account">
        <h2 id="cari-account-heading" className={styles.blockTitle}>
          {CARD.accountHeading}
        </h2>
        <p className={styles.blockNote}>{CARD.accountNote}</p>
        <dl className={styles.facts}>
          {/* The company is named from the shell directory, not from the record —
              the response carries an id, and inventing a name from it would be a
              guess. It heads this section because every fact below is scoped to it. */}
          <Fact
            label="Aktif şirket"
            value={activeCompany === null ? '—' : `${activeCompany.legalName} (${activeCompany.code})`}
          />
          <Fact
            label="Şirket hesabı durumu"
            value={account.isActive ? 'Aktif' : 'Pasif'}
            data-account-state={account.isActive ? 'active' : 'inactive'}
          />
          {/* Named as history and STATED when absent (ILK_DILIM §7). The record's own
              screen has room to say the second half out loud: a counterparty with no
              commercial classification is not a record someone forgot to finish. */}
          <Fact
            label={LEGACY_ROLE_HEADING}
            value={rolesText(account.roles, NO_COMMERCIAL_ROLE_DETAIL)}
            data-roles={account.roles === 0 ? 'none' : 'legacy'}
          />
        </dl>
        {/**
          * Offered only where the server granted the editor's own capability.
          *
          * Not decoration: without the gate the card would advertise an editor whose
          * Save the user may not be permitted to use, and a button that exists in order
          * to fail is worse than no button. The API re-authorizes the read and the basic
          * update on every request regardless — this only decides what is offered.
          */}
        {canEditContact ? (
          <Link className={styles.action} to={`/cari/${encodeURIComponent(detail.id)}/bilgiler`}>
            {CARD.editContact}
          </Link>
        ) : null}

        {/**
          * The result is the FRESH READ's, never the command's. If the read agrees with
          * what was asked, the outcome is named as asked. If it does not — a replay of a
          * deactivation the account has since been reactivated out of — the screen says a
          * past command's result was reported and states the CURRENT state, and it does
          * not claim the account left an active-only list (Dirac #250 P2-4).
          */}
        <div aria-live="polite" className={styles.srOnly} data-testid="cari-account-status-announcement">
          {saved !== null
            ? targetIsActive(saved.intent) === account.isActive
              ? STATUS.announceSaved[saved.intent]
              : STATUS.currentStateNote(account.isActive)
            : ''}
        </div>

        {saved !== null ? (
          <div
            role="status"
            className={styles.saved}
            data-state="account-status-saved"
            data-intent={saved.intent}
            data-agrees={targetIsActive(saved.intent) === account.isActive ? 'yes' : 'no'}
          >
            {targetIsActive(saved.intent) === account.isActive ? (
              <>
                <p className={styles.savedTitle}>{STATUS.savedTitle[saved.intent]}</p>
                <p>{STATUS.savedDetail}</p>
                {saved.replayed ? <p data-state="replayed">{STATUS.savedReplayed}</p> : null}
              </>
            ) : (
              <>
                <p className={styles.savedTitle}>{STATUS.savedSupersededTitle}</p>
                <p>{STATUS.savedSupersededDetail}</p>
                <p data-state="current-state">{STATUS.currentStateNote(account.isActive)}</p>
              </>
            )}
            {!account.isActive ? <p>{STATUS.savedListNote}</p> : null}
          </div>
        ) : null}

        {pendingAdopt !== null ? (
          // Applied, and the fresh read has not landed: the values above are the OLD read's.
          <div role="status" className={styles.saved} data-state="read-pending">
            <p className={styles.recovery}>{STATUS.readPending}</p>
            <p>
              <button type="button" className={styles.action} onClick={() => void query.refetch()}>
                {STATUS.readAgain}
              </button>
            </p>
          </div>
        ) : null}

        {/**
          * The status action, offered only under its OWN control capability. Not the
          * detail grant and not the contact editor's: the API re-authorizes
          * `counterparty.accounts.status.update` on every request, and a button that
          * exists in order to be refused is worse than no button.
          */}
        {canChangeStatus ? (
          <AccountStatusControl
            flow={flow}
            detail={detail}
            companyLabel={activeCompany === null ? '—' : `${activeCompany.legalName} (${activeCompany.code})`}
            onReadAgain={() => void query.refetch()}
            triggerRef={statusTriggerRef}
            summaryRef={statusSummaryRef}
            unresolvedRef={statusUnresolvedRef}
          />
        ) : null}
      </section>
    </section>
  )
}

function BackToList() {
  return (
    // A distinct accessible name on purpose. The nav already has a link called
    // "Cari Kartlar"; two links with the same name on one page are ambiguous to
    // anyone cycling through links rather than looking at where they sit.
    <Link className={styles.back} to="/cari">
      ← Cari Kartlar listesine dön
    </Link>
  )
}

function Fact({
  label,
  value,
  ...rest
}: {
  readonly label: string
  readonly value: string
} & Record<string, unknown>) {
  return (
    <div className={styles.fact}>
      <dt>{label}</dt>
      <dd {...rest}>{value}</dd>
    </div>
  )
}
