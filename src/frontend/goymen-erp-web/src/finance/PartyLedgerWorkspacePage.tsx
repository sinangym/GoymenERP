import { useQuery } from '@tanstack/react-query'
import { useNavigate, useSearchParams } from 'react-router'
import { fetchCounterparties } from '../cari/api'
import type { CounterpartyListItem } from '../cari/contract'
import { LEDGER_QUERY, partyLedgerPath } from './capabilities'
import { isBoundUuid } from './contract'
import { PartyLedger, type PartyLedgerRange } from './PartyLedger'
import {
  WORKSPACE_ACCOUNTS_INVALID,
  WORKSPACE_ACCOUNTS_LOADING,
  WORKSPACE_NO_ACCOUNTS,
  WORKSPACE_PICK,
  WORKSPACE_PICK_HINT,
  WORKSPACE_PICK_LABEL,
} from './partyLedgerText'
import styles from './Finance.module.css'

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
  const counterparties = useQuery({
    queryKey: ['finance', companyId, 'party-ledger-workspace-counterparties'],
    queryFn: ({ signal }) => fetchCounterparties(
      { mode: 'single', companyId },
      { partyState: 'active', accountState: 'active', sort: 'legalName', direction: 'asc', pageSize: 100 },
      signal,
    ),
  })
  const parties = counterparties.data?.type === 'ok-single'
    ? counterparties.data.data.items.filter(singleActiveAccount)
    : []
  const partyId = single(search, LEDGER_QUERY.party)
  const companyAccountId = single(search, LEDGER_QUERY.account)
  const chosen = isBoundUuid(partyId) && isBoundUuid(companyAccountId)
  const from = single(search, LEDGER_QUERY.from)
  const to = single(search, LEDGER_QUERY.to)
  const range: PartyLedgerRange = from === null && to === null ? defaultRange() : { from: from ?? '', to: to ?? '' }

  const pick = (id: string) => {
    const party = parties.find((item) => item.id === id)
    if (party !== undefined) navigate(partyLedgerPath(party.id, party.companyAccounts[0].id, undefined, base))
  }

  return (
    <section aria-labelledby="finance-party-ledger-workspace-title">
      <h1 id="finance-party-ledger-workspace-title">{title}</h1>
      <p className={styles.notice}>{WORKSPACE_PICK_HINT}</p>
      {counterparties.isPending ? <p role="status">{WORKSPACE_ACCOUNTS_LOADING}</p> : null}
      {counterparties.data !== undefined && counterparties.data.type !== 'ok-single'
        ? <p role="alert">{WORKSPACE_ACCOUNTS_INVALID}</p>
        : null}
      {counterparties.data?.type === 'ok-single' && parties.length === 0
        ? <p role="status">{WORKSPACE_NO_ACCOUNTS}</p>
        : null}
      {parties.length > 0 ? (
        <p>
          <label htmlFor="party-ledger-workspace-party">{WORKSPACE_PICK_LABEL}</label>{' '}
          <select id="party-ledger-workspace-party" value={chosen ? partyId : ''}
            onChange={(event) => pick(event.target.value)}>
            <option value="">{WORKSPACE_PICK}</option>
            {parties.map((party) => <option key={party.id} value={party.id}>{partyLabel(party)}</option>)}
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

function single(search: URLSearchParams, name: string): string | null {
  return search.getAll(name).length === 1 ? search.get(name) : null
}

function defaultRange(now: Date = new Date()): PartyLedgerRange {
  const pad = (value: number) => String(value).padStart(2, '0')
  const month = `${now.getFullYear()}-${pad(now.getMonth() + 1)}`
  return { from: `${month}-01`, to: `${month}-${pad(now.getDate())}` }
}

function singleActiveAccount(item: CounterpartyListItem): boolean {
  return item.isActive && item.companyAccounts.length === 1 && item.companyAccounts[0].isActive
}

function partyLabel(item: CounterpartyListItem): string {
  return item.tradeName === null ? `${item.code} - ${item.legalName}` : `${item.code} - ${item.legalName} (${item.tradeName})`
}
