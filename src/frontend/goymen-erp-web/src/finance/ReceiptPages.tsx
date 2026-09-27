/**
 * F-02 Tahsilat / Ödeme — the route pages around the existing components (FIN-T04b screens):
 * the draft list, a new draft entered from a Cari account, and one draft.
 *
 * - The new draft needs the party and its company account from its ENTRY: the Cari account screen
 *   links here with both (`receiptDraftPath`). Finance has no party picker and invents none; the
 *   draft-context read validates both server-side for this company, date and bank account.
 * - The detail page offers Record and the FIN-T06 reversal-proposal flow only when shell capabilities allow them.
 *   It never substitutes the unpublished legacy direct-Reverse action.
 */

import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { isBoundUuid, type SourceDetail } from './contract'
import { FINANCE_CAPABILITY, FINANCE_ROUTE, INSTRUMENT_QUERY, receiptDetailPath } from './capabilities'
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
import { CustomerInstrumentPortfolio, CustomerInstrumentReceiveForm } from './CustomerInstruments'
import styles from './Finance.module.css'

/** DEC-FIN-016: how the customer pays on the new-receipt page — a bank / POS receipt draft, or a cheque / note. */
const RECEIPT_INSTRUMENT_LABEL = 'Tahsilat aracı'
const RECEIPT_INSTRUMENT_OPTION = {
  transfer: 'Banka havalesi / EFT veya müşteri POS',
  instrument: 'Müşteri çeki veya senedi (portföye al)',
} as const

export function ReceiptListPage() {
  const navigate = useNavigate()
  const [search, setSearch] = useSearchParams()
  const selected = search.get(INSTRUMENT_QUERY)
  return (
    <section aria-labelledby="finance-receipts-title">
      <h1 id="finance-receipts-title">{RECEIPTS_TITLE}</h1>
      <p className={styles.notice}>{RECEIPTS_NOTICE}</p>
      <FinanceSourceList onOpen={(sourceId) => navigate(receiptDetailPath(sourceId))} />
      {/* DEC-FIN-016: the customer cheque / note portfolio; hidden for an actor without its own read grant. */}
      <CustomerInstrumentPortfolio
        selectedId={isBoundUuid(selected) ? selected : null}
        onSelect={(instrumentId) => setSearch(instrumentId === null ? {} : { [INSTRUMENT_QUERY]: instrumentId })}
      />
    </section>
  )
}

export function ReceiptDraftPage() {
  const [search] = useSearchParams()
  const partyId = search.get('partyId')
  const companyAccountId = search.get('companyAccountId')
  const [saved, setSaved] = useState<SourceDetail | null>(null)
  const [instrument, setInstrument] = useState<'transfer' | 'instrument'>('transfer')
  const party =
    isBoundUuid(partyId) && isBoundUuid(companyAccountId) ? { partyId, companyAccountId } : null

  return (
    <section aria-labelledby="finance-receipt-draft-title">
      <h1 id="finance-receipt-draft-title">{RECEIPT_DRAFT_TITLE}</h1>
      {party === null ? (
        <p role="status">{RECEIPT_DRAFT_NEEDS_PARTY}</p>
      ) : (
        <>
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
        </>
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
        <FinanceSourceDetail sourceId={sourceId} offerRecord={canRecord} offerReverse={canProposeReversal} />
      ) : (
        <StateBlock state={{ kind: 'not-found' }} subject={SOURCE_SUBJECT} />
      )}
      <p>
        <Link to={FINANCE_ROUTE.receiptsList}>{BACK_TO_RECEIPTS}</Link>
      </p>
    </section>
  )
}
