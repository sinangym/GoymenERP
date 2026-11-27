/**
 * F-02 Tahsilat / Ödeme — the route pages around the existing components (FIN-T04b screens):
 * the draft list, a new draft entered from a Cari account, and one draft.
 *
 * - A Cari account entry keeps its party/account pair. The independent entry reuses quick create,
 *   then the current company owner read; draft-context validates the account, versions, date and bank.
 * - The detail page offers Record and the FIN-T06 reversal-proposal flow only when shell capabilities allow them.
 *   It never substitutes the unpublished legacy direct-Reverse action.
 */

import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchCounterparties } from '../cari/api'
import { CariQuickCreate } from '../cari/CariQuickCreate'
import { findQuickCreatedOffer } from '../cari/quickCreateSelection'
import { PARTY_ROLE_FLAG } from '../cari/contract'
import { decodeSourceDetailFor, isBoundUuid, type SourceDetail } from './contract'
import {
  FINANCE_CAPABILITY,
  FINANCE_ROUTE,
  LEDGER_QUERY,
  partyLedgerPath,
  receiptDetailPath,
} from './capabilities'
import { useFinanceBinding, useFinanceRead } from './financePort'
import { attemptStoreKey, recallSlot } from './receiptCommand'
import { PartyLedger, type PartyLedgerRange } from './PartyLedger'
import { LEDGER_BACK, LEDGER_NEEDS_ACCOUNT, LEDGER_OPEN, LEDGER_OPEN_HINT } from './partyLedgerText'
import { FinanceSourceDetail } from './FinanceSourceDetail'
import { FinanceSourceList } from './FinanceSourceList'
import {
  BACK_TO_RECEIPTS,
  OPEN_SAVED_DRAFT,
  RECEIPTS_NOTICE,
  RECEIPTS_TITLE,
  RECEIPT_DETAIL_TITLE,
  RECEIPT_DRAFT_NEEDS_PARTY,
  RECEIPT_DRAFT_TITLE,
  SOURCE_SUBJECT,
} from './financeText'
import { ManualReceiptDraftForm } from './ManualReceiptDraftForm'
import { CustomerInstrumentReceiveForm } from './CustomerInstruments'
import { CustomerInstrumentRiskBlock } from './CustomerInstrumentRisk'
import styles from './Finance.module.css'
import { RecordNotesSection } from '../work/RecordNotesSection'

/** DEC-FIN-016: how the customer pays on the new-receipt page — a bank / POS receipt draft, or a cheque / note. */
const RECEIPT_INSTRUMENT_LABEL = 'Tahsilat aracı'
const RECEIPT_INSTRUMENT_OPTION = {
  transfer: 'Banka havalesi / EFT veya müşteri POS',
  instrument: 'Müşteri çeki veya senedi (portföye al)',
} as const

/** The browser's own calendar days: the default range is this month up to today, as the user sees them. */
function defaultRange(): PartyLedgerRange {
  const now = new Date()
  const pad = (value: number) => String(value).padStart(2, '0')
  const month = `${now.getFullYear()}-${pad(now.getMonth() + 1)}`
  return { from: `${month}-01`, to: `${month}-${pad(now.getDate())}` }
}

/** Exactly one value of a query parameter, or null. */
function single(search: URLSearchParams, name: string): string | null {
  return search.getAll(name).length === 1 ? search.get(name) : null
}

/**
 * The first-release party ledger on the F-02 list (no route of its own yet): the party account and the range are the
 * address's, so a row's source opens on its own screen and the browser's back returns to the same range.
 */
function PartyLedgerPage({ search }: { readonly search: URLSearchParams }) {
  const navigate = useNavigate()
  const partyId = single(search, LEDGER_QUERY.party)
  const companyAccountId = single(search, LEDGER_QUERY.account)
  if (!isBoundUuid(partyId) || !isBoundUuid(companyAccountId)) {
    return (
      <section aria-labelledby="finance-party-ledger-title">
        <h1 id="finance-party-ledger-title">{LEDGER_OPEN}</h1>
        <p role="status">{LEDGER_NEEDS_ACCOUNT}</p>
        <p><Link to={FINANCE_ROUTE.receiptsList}>{LEDGER_BACK}</Link></p>
      </section>
    )
  }
  const from = single(search, LEDGER_QUERY.from)
  const to = single(search, LEDGER_QUERY.to)
  // No range in the address yet: this month; an address that names one keeps exactly it (a malformed one is shown so).
  const range: PartyLedgerRange = from === null && to === null ? defaultRange() : { from: from ?? '', to: to ?? '' }
  return (
    <>
      <PartyLedger partyId={partyId} companyAccountId={companyAccountId}
        range={range}
        onRange={(next) => navigate(partyLedgerPath(partyId, companyAccountId, next))} />
      <p><Link to={FINANCE_ROUTE.receiptsList}>{LEDGER_BACK}</Link></p>
    </>
  )
}

/** "Cari ekstre" for one party account, offered wherever F-02 knows the party and its company account. */
export function PartyLedgerLink({ partyId, companyAccountId }: { readonly partyId: string; readonly companyAccountId: string }) {
  return (
    <p>
      <Link to={partyLedgerPath(partyId, companyAccountId)}>{LEDGER_OPEN}</Link>
      <span className={styles.hint}>{` — ${LEDGER_OPEN_HINT}`}</span>
    </p>
  )
}

/** The receipt detail's party account, from the same source read the detail itself makes (one request). */
function SourcePartyLedgerLink({ sourceId }: { readonly sourceId: string }) {
  const detail = useFinanceRead('source', [sourceId], (port, scope, signal) => port.sourceDetail(scope, { sourceId }, signal),
    decodeSourceDetailFor(sourceId))
  if (detail.state.kind !== 'ready') return null
  const source = detail.state.data.source
  return <PartyLedgerLink partyId={source.partyId} companyAccountId={source.companyAccountId} />
}

export function ReceiptListPage() {
  const navigate = useNavigate()
  const [search] = useSearchParams()
  if (search.has(LEDGER_QUERY.party) || search.has(LEDGER_QUERY.account)) return <PartyLedgerPage search={search} />
  return (
    <section aria-labelledby="finance-receipts-title">
      <h1 id="finance-receipts-title">{RECEIPTS_TITLE}</h1>
      <p className={styles.notice}>{RECEIPTS_NOTICE}</p>
      <FinanceSourceList onOpen={(sourceId) => navigate(receiptDetailPath(sourceId))} />
    </section>
  )
}

export function ReceiptDraftPage() {
  const shell = useShellState(), context = contextKey(shell)
  const [search] = useSearchParams()
  const companyId = !capabilityFailClosed(shell) && shell.activeContext?.scopeMode === 'single'
    ? shell.activeContext.companyId : null
  return <ReceiptDraftEntry key={JSON.stringify([context === null ? null : contextKeyString(context),
    shell.sessionEpoch, shell.routeEntryGeneration, search.toString()])} search={search} companyId={companyId} />
}

function ReceiptDraftEntry({ search, companyId }: { readonly search: URLSearchParams; readonly companyId: string | null }) {
  const boundary = useAuthBoundary(), bound = useFinanceBinding()
  const independent = !search.has('partyId') && !search.has('companyAccountId')
  const retained = independent && bound !== null
    ? recallSlot(attemptStoreKey(bound.binding, 'draft-save', 'new')).outstanding : null
  const [createdParty, setCreatedParty] = useState<{ partyId: string; companyAccountId: string } | null>(() => {
    const request = retained?.snapshot.request
    if (request?.operation !== 'draft-save' || request.body.draftId !== null) return null
    const { partyId, companyAccountId } = request.body.preparation
    // Recover only the original current actor/session/company slot. The native form owns retry/body/key.
    return isBoundUuid(partyId) && isBoundUuid(companyAccountId) ? { partyId, companyAccountId } : null
  })
  const [quickUnresolved, setQuickUnresolved] = useState(false)
  const partyId = search.get('partyId')
  const companyAccountId = search.get('companyAccountId')
  const [saved, setSaved] = useState<SourceDetail | null>(null)
  const [instrument, setInstrument] = useState<'transfer' | 'instrument'>('transfer')
  const party =
    isBoundUuid(partyId) && isBoundUuid(companyAccountId) ? { partyId, companyAccountId } : independent ? createdParty : null

  return (
    <section aria-labelledby="finance-receipt-draft-title">
      <h1 id="finance-receipt-draft-title">{RECEIPT_DRAFT_TITLE}</h1>
      {independent && companyId !== null ? <CariQuickCreate initialRoles={PARTY_ROLE_FLAG.Customer} disabled={retained !== null}
        onUnresolvedChange={setQuickUnresolved} onCreated={async (record, signal) => {
          if (signal.aborted || record.account.companyId !== companyId
            || createdParty !== null && (createdParty.partyId !== record.id || createdParty.companyAccountId !== record.account.id)) return false
          const captured = boundary.capture()
          const found = await findQuickCreatedOffer(signal, async cursor => {
            const result = await fetchCounterparties({ mode: 'single', companyId }, {
              partyState: 'active', accountState: 'active', sort: 'legalName', direction: 'asc', pageSize: 100,
              q: record.code, cursor,
            }, signal)
            if (result.type === 'http' && [401, 403].includes(result.status)) boundary.report(result.status, captured)
            return result.type === 'ok-single' ? result.data : null
          }, item => item.id === record.id && item.isActive && item.companyAccounts.length === 1
            && item.companyAccounts[0].isActive && item.companyAccounts[0].companyId === companyId
            && item.companyAccounts[0].id === record.account.id)
          if (found === null || signal.aborted) return false
          setCreatedParty({ partyId: found.id, companyAccountId: found.companyAccounts[0].id })
          // The native draft-context read still supplies current party/account versions before saving.
          return true
        }} /> : null}
      {party === null ? (
        <p role="status">{RECEIPT_DRAFT_NEEDS_PARTY}</p>
      ) : (
        <div onSubmitCapture={event => {
          if (quickUnresolved) { event.preventDefault(); event.stopPropagation() }
        }}>
          <fieldset disabled={quickUnresolved}>
          <div className={styles.field}>
            <label htmlFor="finance-receipt-instrument">{RECEIPT_INSTRUMENT_LABEL}</label>
            <select id="finance-receipt-instrument" value={instrument}
              onChange={(event) => setInstrument(event.target.value === 'instrument' ? 'instrument' : 'transfer')}>
              <option value="transfer">{RECEIPT_INSTRUMENT_OPTION.transfer}</option>
              <option value="instrument">{RECEIPT_INSTRUMENT_OPTION.instrument}</option>
            </select>
          </div>
          {instrument === 'transfer'
            ? <ManualReceiptDraftForm party={party} onSaved={setSaved} />
            : <CustomerInstrumentReceiveForm party={party} />}
          {/* The customer's own cheque / note risk, beside the receipt entered from its account (facts only). */}
          <CustomerInstrumentRiskBlock party={party} />
          <PartyLedgerLink partyId={party.partyId} companyAccountId={party.companyAccountId} />
          </fieldset>
        </div>
      )}
      {saved === null ? null : (
        <p>
          <Link to={receiptDetailPath(saved.source.id)}>{OPEN_SAVED_DRAFT}</Link>
        </p>
      )}
      <p>
        <Link to={FINANCE_ROUTE.receiptsList}>{BACK_TO_RECEIPTS}</Link>
      </p>
    </section>
  )
}

export function ReceiptDetailPage() {
  const { sourceId } = useParams()
  const shell = useShellState()
  // FIN-T05/T06: controls are offered only when the reviewed shell authority returns their capabilities.
  const canRecord = hasCapability(shell, FINANCE_CAPABILITY.receiptsRecord)
  const canProposeReversal = hasCapability(shell, FINANCE_CAPABILITY.receiptsReversePropose)
  return (
    <section aria-labelledby="finance-receipt-detail-title">
      <h1 id="finance-receipt-detail-title">{RECEIPT_DETAIL_TITLE}</h1>
      {isBoundUuid(sourceId) ? (
        <>
          <FinanceSourceDetail sourceId={sourceId} offerRecord={canRecord} offerReverse={canProposeReversal} />
          <RecordNotesSection recordType="finance-manual-receipt" recordId={sourceId} />
          <SourcePartyLedgerLink sourceId={sourceId} />
        </>
      ) : (
        <StateBlock state={{ kind: 'not-found' }} subject={SOURCE_SUBJECT} />
      )}
      <p>
        <Link to={FINANCE_ROUTE.receiptsList}>{BACK_TO_RECEIPTS}</Link>
      </p>
    </section>
  )
}
