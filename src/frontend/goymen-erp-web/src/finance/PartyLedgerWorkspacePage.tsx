import { useQuery } from '@tanstack/react-query'
import { useNavigate, useSearchParams } from 'react-router'
import { fetchCounterparties } from '../cari/api'
import type { CounterpartyListItem, CounterpartyPage } from '../cari/contract'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { LEDGER_QUERY, partyLedgerPath } from './capabilities'
import { isBoundUuid } from './contract'
import { PartyLedger, type PartyLedgerRange } from './PartyLedger'
import {
  WORKSPACE_ACCOUNTS_LOADING,
  WORKSPACE_ACCOUNTS_PARTIAL,
  WORKSPACE_ACCOUNTS_SUBJECT,
  WORKSPACE_NO_ACCOUNTS,
  WORKSPACE_PICK,
  WORKSPACE_PICK_HINT,
  WORKSPACE_PICK_LABEL,
} from './partyLedgerText'
import styles from './Finance.module.css'

/** Page size asked per read and how many pages the picker follows; beyond that the list says it is partial. */
const PICKER_PAGE_SIZE = 100
const PICKER_MAX_PAGES = 5

interface PickerAccount {
  readonly key: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly label: string
}

interface PickerList {
  readonly accounts: readonly PickerAccount[]
  readonly partial: boolean
}

/**
 * B-05 (KLN-D3, FNS-D14): the party ledger and balance summary as a page of its own, opened from Muhasebe > Cari
 * Hesaplar and from Cari > Cari Hesap Takibi. Both are Finance's read of the one open-item sub-ledger; neither is a new
 * master. The chosen party account and the date range live in the address (`LEDGER_QUERY`), so reopening the address
 * shows the same ledger and a row's source can be opened and come back.
 */
export function PartyLedgerWorkspacePage({
  companyId,
  base,
  title,
}: {
  readonly companyId: string
  readonly base: string
  readonly title: string
}) {
  const navigate = useNavigate()
  const [search] = useSearchParams()
  const boundary = useAuthBoundary()
  const counterparties = useQuery({
    queryKey: ['finance', companyId, 'party-ledger-workspace-counterparties'],
    queryFn: async ({ signal }): Promise<ViewOutcome<PickerList>> => {
      // The bindings are captured before the first read, so a late 401/403 of a departed actor is discarded.
      const snapshot = boundary.capture()
      const outcome = await readPickerList(companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const state = (counterparties.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(counterparties.data ?? { type: 'malformed' })) as RemoteViewState<PickerList>
  const list = state.kind === 'ready' ? state.data : null
  const accounts = list?.accounts ?? []
  const partyId = single(search, LEDGER_QUERY.party)
  const companyAccountId = single(search, LEDGER_QUERY.account)
  const chosen = isBoundUuid(partyId) && isBoundUuid(companyAccountId)
  const from = single(search, LEDGER_QUERY.from)
  const to = single(search, LEDGER_QUERY.to)
  const range: PartyLedgerRange = from === null && to === null ? defaultRange() : { from: from ?? '', to: to ?? '' }

  const pick = (key: string) => {
    const account = accounts.find((item) => item.key === key)
    if (account !== undefined) navigate(partyLedgerPath(account.partyId, account.companyAccountId, undefined, base))
  }
  const chosenKey = chosen ? accountKey(partyId, companyAccountId) : ''

  return (
    <section aria-labelledby="finance-party-ledger-workspace-title">
      <h1 id="finance-party-ledger-workspace-title">{title}</h1>
      <p className={styles.notice}>{WORKSPACE_PICK_HINT}</p>
      {state.kind === 'initial-loading' ? <p role="status">{WORKSPACE_ACCOUNTS_LOADING}</p> : null}
      {state.kind !== 'ready' && state.kind !== 'initial-loading' ? (
        <StateBlock state={state} subject={WORKSPACE_ACCOUNTS_SUBJECT} onRetry={() => { void counterparties.refetch() }} />
      ) : null}
      {list !== null && accounts.length === 0 ? <p role="status">{WORKSPACE_NO_ACCOUNTS}</p> : null}
      {list?.partial === true ? <p role="status">{WORKSPACE_ACCOUNTS_PARTIAL}</p> : null}
      {accounts.length > 0 ? (
        <p>
          <label htmlFor="party-ledger-workspace-party">{WORKSPACE_PICK_LABEL}</label>{' '}
          <select id="party-ledger-workspace-party" value={accounts.some((item) => item.key === chosenKey) ? chosenKey : ''}
            onChange={(event) => pick(event.target.value)}>
            <option value="">{WORKSPACE_PICK}</option>
            {accounts.map((account) => <option key={account.key} value={account.key}>{account.label}</option>)}
          </select>
        </p>
      ) : null}
      {chosen ? (
        <PartyLedger partyId={partyId} companyAccountId={companyAccountId} range={range}
          onRange={(next) => navigate(partyLedgerPath(partyId, companyAccountId, next, base))} />
      ) : null}
    </section>
  )
}

/**
 * Follows the opaque cursor up to `PICKER_MAX_PAGES`; a page that fails ends the read with that failure, and a list the
 * server says has more beyond the bound is returned as partial rather than silently cut.
 */
async function readPickerList(companyId: string, signal: AbortSignal): Promise<ViewOutcome<PickerList>> {
  const items: CounterpartyListItem[] = []
  const seen = new Set<string>()
  let cursor: string | undefined
  for (let pageNumber = 0; pageNumber < PICKER_MAX_PAGES; pageNumber += 1) {
    const outcome = await fetchCounterparties(
      { mode: 'single', companyId },
      {
        partyState: 'active', accountState: 'active', sort: 'legalName', direction: 'asc',
        pageSize: PICKER_PAGE_SIZE, cursor,
      },
      signal,
    )
    if (outcome.type !== 'ok-single') return outcome as ViewOutcome<PickerList>
    const page: CounterpartyPage = outcome.data
    for (const item of page.items) {
      // A party that repeats across pages means the continuation is broken: fail closed.
      if (seen.has(item.id)) return { type: 'malformed' }
      seen.add(item.id)
      items.push(item)
    }
    if (!page.hasMore) return { type: 'ok-single', data: { accounts: toAccounts(items), partial: false } }
    if (page.nextCursor === null) return { type: 'malformed' }
    cursor = page.nextCursor
  }
  return { type: 'ok-single', data: { accounts: toAccounts(items), partial: true } }
}

function toAccounts(items: readonly CounterpartyListItem[]): PickerAccount[] {
  return items.filter((item) => item.isActive).flatMap((item) => {
    // Single-company mode decodes to exactly one account per party (a second one is a malformed response), so one
    // active account is one option; a party never has an account here that is silently left out.
    return item.companyAccounts.filter((account) => account.isActive).map((account) => ({
      key: accountKey(item.id, account.id),
      partyId: item.id,
      companyAccountId: account.id,
      label: partyLabel(item),
    }))
  })
}

function accountKey(partyId: string, companyAccountId: string): string {
  return `${partyId}:${companyAccountId}`
}

function single(search: URLSearchParams, name: string): string | null {
  return search.getAll(name).length === 1 ? search.get(name) : null
}

function defaultRange(now: Date = new Date()): PartyLedgerRange {
  const pad = (value: number) => String(value).padStart(2, '0')
  const month = `${now.getFullYear()}-${pad(now.getMonth() + 1)}`
  return { from: `${month}-01`, to: `${month}-${pad(now.getDate())}` }
}

function partyLabel(item: CounterpartyListItem): string {
  return item.tradeName === null ? `${item.code} - ${item.legalName}` : `${item.code} - ${item.legalName} (${item.tradeName})`
}
