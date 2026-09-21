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
import { FINANCE_CAPABILITY, FINANCE_ROUTE, receiptDetailPath } from './capabilities'
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
import styles from './Finance.module.css'

export function ReceiptListPage() {
  const navigate = useNavigate()
  return (
    <section aria-labelledby="finance-receipts-title">
      <h1 id="finance-receipts-title">{RECEIPTS_TITLE}</h1>
      <p className={styles.notice}>{RECEIPTS_NOTICE}</p>
      <FinanceSourceList onOpen={(sourceId) => navigate(receiptDetailPath(sourceId))} />
    </section>
  )
}

export function ReceiptDraftPage() {
  const [search] = useSearchParams()
  const partyId = search.get('partyId')
  const companyAccountId = search.get('companyAccountId')
  const [saved, setSaved] = useState<SourceDetail | null>(null)
  const party =
    isBoundUuid(partyId) && isBoundUuid(companyAccountId) ? { partyId, companyAccountId } : null

  return (
    <section aria-labelledby="finance-receipt-draft-title">
      <h1 id="finance-receipt-draft-title">{RECEIPT_DRAFT_TITLE}</h1>
      {party === null ? (
        <p role="status">{RECEIPT_DRAFT_NEEDS_PARTY}</p>
      ) : (
        <ManualReceiptDraftForm party={party} onSaved={setSaved} />
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
  // FIN-T05a: the Record control only for the holder of its capability (declared in FIN-T05's central round).
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
