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

import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchCounterpartyDetail } from './detailApi'
import { PARTY_KIND } from './contract'
import { LEGACY_ROLE_HEADING, NO_COMMERCIAL_ROLE_DETAIL, rolesText } from './roleText'
import type { CounterpartyDetail } from './detailContract'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import { StateBlock } from '../shell/StateBlock'
import styles from './CariDetailPage.module.css'

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
      const outcome = await fetchCounterpartyDetail(partyId ?? '', activeCompanyId ?? '', signal)
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

  if (state.kind !== 'ready') {
    return (
      <section aria-labelledby="cari-detail-heading" className={styles.page}>
        {/* The heading stays generic until a record is verified. Putting the id or
            a partial name here would confirm a record exists to a caller who may
            not be entitled to know it — the same non-disclosure rule as the
            404 block itself (§6). */}
        <h1 id="cari-detail-heading">Cari 360</h1>
        <BackToList />
        <StateBlock state={state} subject="cari" onRetry={() => void query.refetch()} />
      </section>
    )
  }

  const detail = state.data
  const account = detail.account

  return (
    <section aria-labelledby="cari-detail-heading" className={styles.page}>
      <h1 id="cari-detail-heading">{detail.legalName}</h1>
      <BackToList />

      <dl className={styles.facts}>
        <Fact label="Cari kodu" value={detail.code} />
        <Fact label="Hukuki unvan" value={detail.legalName} />
        {detail.tradeName !== null && detail.tradeName !== detail.legalName ? (
          <Fact label="Ticari unvan" value={detail.tradeName} />
        ) : null}
        <Fact label="Tür" value={KIND_LABEL[detail.kind]} />

        {/* Two independent facts, never collapsed into one "Durum". A Party can be
            globally active while its account in this company is passive. */}
        <Fact
          label="Taraf durumu"
          value={detail.isActive ? 'Aktif' : 'Pasif'}
          data-party-state={detail.isActive ? 'active' : 'inactive'}
        />

        {/* The company is named from the shell directory, not from the record —
            the response carries an id, and inventing a name from it would be a
            guess. */}
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
