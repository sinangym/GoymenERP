/**
 * M-04 Muhasebe Fişleri (ACC-V0), READ-ONLY: the journal list and one journal.
 *
 * A journal is a derived accounting record of a source fact (Master §620), never a second money
 * movement. Amounts are the server's text, shown regrouped and never summed, rounded or padded here;
 * the totals are the server's. No voucher is created, approved or reversed on these screens (ACC-V1
 * waits on its decisions), so there is no such button.
 */

import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router'
import { fetchJournal, fetchJournals } from './api'
import { ACCOUNTING_ROUTE, journalDetailPath } from './capabilities'
import type { JournalDetail, JournalPage, JournalSummary } from './contract'
import {
  ACCOUNT_COLUMN,
  ACCOUNT_REF_NOTE,
  ALL_JOURNALS,
  DERIVED_NOTE,
  JOURNALS_INTRO,
  JOURNALS_TITLE,
  JOURNAL_DETAIL_TITLE,
  JOURNAL_KIND_LABEL,
  MORE,
  OPEN_JOURNAL,
  OPEN_LINKED_JOURNAL,
  PERIOD_FILTER_NOTE,
  SOURCE_RECORD,
  UNMAPPED_ACCOUNT,
  shortRef,
} from './accountingText'
import { formatDecimalTextTr } from '../finance/decimalText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { ACCOUNTING_CAPABILITY } from './capabilities'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './Accounting.module.css'

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** The server's amount text regrouped for tr-TR; anything outside the grammar was refused by the decoder. */
function amount(text: string): string {
  return formatDecimalTextTr(text) ?? text
}

function useSingleCompany() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  return { shell, namespace, companyId, singleCompany }
}

// ---------------------------------------------------------------------------------
// List
// ---------------------------------------------------------------------------------

export function JournalsPage() {
  const { namespace } = useSingleCompany()
  const [params] = useSearchParams()
  const periodId = params.get('periodId')
  return <Journals key={`${namespace ?? 'no-context'}::${periodId ?? ''}`} namespace={namespace} periodParam={periodId} />
}

function Journals({ namespace, periodParam }: { readonly namespace: string | null; readonly periodParam: string | null }) {
  const { shell, companyId, singleCompany } = useSingleCompany()
  // The detail is its own capability and is published only once a slice has accepted it (ACC-V0c: acc01 opened
  // no journal). While the server does not publish it, the row shows the journal's reference and no dead link.
  const canOpenDetail = hasCapability(shell, ACCOUNTING_CAPABILITY.journalsDetail)
  const boundary = useAuthBoundary()
  // A malformed filter is never read as "all journals": it is not sent, and the page says so.
  const periodId = periodParam !== null && GUID_RE.test(periodParam) ? periodParam : null
  const filterInvalid = periodParam !== null && periodId === null

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'accounting-journals', periodId ?? ''],
    enabled: namespace !== null && singleCompany && !filterInvalid,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchJournals(companyId ?? '', periodId, null, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const [more, setMore] = useState<readonly JournalSummary[]>([])
  const [nextCursor, setNextCursor] = useState<string | null | undefined>(undefined)
  const [loadingMore, setLoadingMore] = useState(false)

  if (!singleCompany) {
    return (
      <ListFrame periodId={null}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="muhasebe fişleri için tek şirket bağlamı" />
      </ListFrame>
    )
  }

  if (filterInvalid) {
    return (
      <ListFrame periodId={null}>
        <StateBlock state={{ kind: 'not-found' }} subject="dönem" />
      </ListFrame>
    )
  }

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<JournalPage>
  if (state.kind !== 'ready') {
    return (
      <ListFrame periodId={periodId}>
        <StateBlock state={state} subject="muhasebe fişleri" onRetry={() => void query.refetch()} />
      </ListFrame>
    )
  }

  const rows = [...state.data.items, ...more]
  const cursor = nextCursor === undefined ? state.data.nextCursor : nextCursor
  const loadMore = async () => {
    if (cursor === null || loadingMore) return
    setLoadingMore(true)
    const snapshot = boundary.capture()
    const outcome = await fetchJournals(companyId ?? '', periodId, cursor)
    setLoadingMore(false)
    if (outcome.type === 'ok-single') {
      setMore((current) => [...current, ...outcome.data.items])
      setNextCursor(outcome.data.nextCursor)
    } else if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, snapshot)
    }
  }

  return (
    <ListFrame periodId={periodId}>
      {rows.length === 0 ? (
        <StateBlock state={{ kind: periodId === null ? 'empty-no-data' : 'empty-filtered' }} subject="muhasebe fişi" />
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">Fiş türü</th>
                <th scope="col">Muhasebe tarihi</th>
                <th scope="col">Kaynak bağlantısı</th>
                <th scope="col" className={styles.amount}>
                  Borç toplamı
                </th>
                <th scope="col" className={styles.amount}>
                  Alacak toplamı
                </th>
                <th scope="col">Satır</th>
                <th scope="col">
                  <span className={styles.visuallyHidden}>İşlem</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.journalId} data-journal={row.journalId}>
                  <td>{JOURNAL_KIND_LABEL[row.kind]}</td>
                  <td>{row.accountingDate}</td>
                  <td>
                    <SourceLink journal={row} />
                  </td>
                  <td className={styles.amount}>{amount(row.totalDebit)}</td>
                  <td className={styles.amount}>{amount(row.totalCredit)}</td>
                  <td>{row.lineCount}</td>
                  <td>
                    {/* ACC-V0c: the detail is published only when a slice has accepted it; a list must never offer
                        a link the shell would close onto its neutral not-found block. */}
                    {canOpenDetail ? (
                      <Link to={journalDetailPath(row.journalId)} aria-label={`${OPEN_JOURNAL}: ${row.accountingDate} ${JOURNAL_KIND_LABEL[row.kind]} ${shortRef(row.journalId)}`}>
                        {OPEN_JOURNAL}
                      </Link>
                    ) : (
                      // Two references can share their last eight characters (the fixture's journal and source do),
                      // so this one carries its full id in the title exactly as the source reference does — a reader
                      // must never see two identical-looking references meaning different things.
                      <span className={styles.mono} title={row.journalId}>{shortRef(row.journalId)}</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {cursor !== null ? (
        <div className={styles.actions}>
          <button type="button" disabled={loadingMore} onClick={() => void loadMore()}>
            {MORE}
          </button>
        </div>
      ) : null}
    </ListFrame>
  )
}

function SourceLink({ journal }: { readonly journal: JournalSummary }) {
  return (
    <span>
      {SOURCE_RECORD}{' '}
      <span className={styles.mono} title={journal.sourceId}>
        {shortRef(journal.sourceId)}
      </span>
      <br />
      <span className={styles.help}>
        {journal.postingPurpose} · {journal.postingRuleVersion}
      </span>
    </span>
  )
}

function ListFrame({ periodId, children }: { readonly periodId: string | null; readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="accounting-journals-title" className={styles.page}>
      <h1 id="accounting-journals-title">{JOURNALS_TITLE}</h1>
      <p className={styles.help}>{JOURNALS_INTRO}</p>
      {periodId !== null ? (
        <div className={styles.actions}>
          <p className={styles.help}>{PERIOD_FILTER_NOTE}</p>
          <Link to={ACCOUNTING_ROUTE.journalsList}>{ALL_JOURNALS}</Link>
        </div>
      ) : null}
      {children}
    </section>
  )
}

// ---------------------------------------------------------------------------------
// Detail
// ---------------------------------------------------------------------------------

export function JournalDetailPage() {
  const { namespace } = useSingleCompany()
  const { journalId } = useParams()
  return <Detail key={`${namespace ?? 'no-context'}::${journalId ?? ''}`} namespace={namespace} journalId={journalId ?? ''} />
}

function Detail({ namespace, journalId }: { readonly namespace: string | null; readonly journalId: string }) {
  const { shell, companyId, singleCompany } = useSingleCompany()
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'accounting-journal', journalId],
    enabled: namespace !== null && singleCompany && GUID_RE.test(journalId),
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchJournal(companyId ?? '', journalId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  if (!singleCompany) {
    return (
      <DetailFrame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="muhasebe fişi için tek şirket bağlamı" />
      </DetailFrame>
    )
  }

  if (!GUID_RE.test(journalId)) {
    return (
      <DetailFrame>
        <StateBlock state={{ kind: 'not-found' }} subject="muhasebe fişi" />
      </DetailFrame>
    )
  }

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<JournalDetail>
  if (state.kind !== 'ready') {
    return (
      <DetailFrame>
        <StateBlock state={state} subject="muhasebe fişi" onRetry={() => void query.refetch()} />
      </DetailFrame>
    )
  }

  const { journal, lines } = state.data
  return (
    <DetailFrame>
      <p className={styles.help} data-note="derived">
        {DERIVED_NOTE}
      </p>
      <dl className={styles.form}>
        <div className={styles.control}>
          <dt>Fiş türü</dt>
          <dd>{JOURNAL_KIND_LABEL[journal.kind]}</dd>
        </div>
        <div className={styles.control}>
          <dt>Muhasebe tarihi</dt>
          <dd>{journal.accountingDate}</dd>
        </div>
        <div className={styles.control}>
          <dt>İşlem tarihi</dt>
          <dd>{journal.transactionDate}</dd>
        </div>
        <div className={styles.control}>
          <dt>Valör</dt>
          <dd>{journal.valueDate ?? '—'}</dd>
        </div>
        <div className={styles.control}>
          <dt>Kaynak bağlantısı</dt>
          <dd>
            <SourceLink journal={journal} />
          </dd>
        </div>
        {journal.linkedJournalId !== null ? (
          <div className={styles.control}>
            <dt>Bağlı fiş</dt>
            <dd>
              <Link to={journalDetailPath(journal.linkedJournalId)}>{OPEN_LINKED_JOURNAL}</Link>
            </dd>
          </div>
        ) : null}
      </dl>
      <p className={styles.help}>{ACCOUNT_REF_NOTE}</p>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Satır</th>
              <th scope="col">{ACCOUNT_COLUMN}</th>
              <th scope="col" className={styles.amount}>
                Borç
              </th>
              <th scope="col" className={styles.amount}>
                Alacak
              </th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.lineNumber}>
                <td>{line.lineNumber}</td>
                <td>
                  {UNMAPPED_ACCOUNT}{' '}
                  <span className={styles.mono} title={line.accountId}>
                    {shortRef(line.accountId)}
                  </span>
                </td>
                <td className={styles.amount}>{amount(line.debit)}</td>
                <td className={styles.amount}>{amount(line.credit)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr data-totals="balanced">
              <th scope="row" colSpan={2}>
                Toplam
              </th>
              <td className={styles.amount}>{amount(journal.totalDebit)}</td>
              <td className={styles.amount}>{amount(journal.totalCredit)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
      <div className={styles.actions}>
        <Link to={ACCOUNTING_ROUTE.journalsList}>{ALL_JOURNALS}</Link>
      </div>
    </DetailFrame>
  )
}

function DetailFrame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="accounting-journal-title" className={styles.page}>
      <h1 id="accounting-journal-title">{JOURNAL_DETAIL_TITLE}</h1>
      {children}
    </section>
  )
}
