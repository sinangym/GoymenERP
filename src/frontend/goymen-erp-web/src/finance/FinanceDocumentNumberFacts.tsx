import { FINANCE_DOCUMENT_NUMBER_TEXT as T, type FinanceDocumentNumber } from './documentNumber'

export function FinanceDocumentNumberFacts({ number, kind, recorded = true }: {
  readonly number: FinanceDocumentNumber | undefined
  readonly kind: 'receipt' | 'voucher'
  readonly recorded?: boolean
}) {
  return <div><dt>{T[kind]}</dt><dd>{number?.number ?? (recorded ? T.legacy : T.notRecorded)}</dd>
    {number !== undefined && <><dt>{T.format}</dt><dd>{number.formatVersion}</dd></>}</div>
}
