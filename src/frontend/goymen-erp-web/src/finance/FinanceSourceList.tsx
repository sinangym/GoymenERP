/**
 * Authorized source list for the current company (`ReadResult<SourcePage>`), cursor paged.
 *
 * Columns are only what #258 `SourceSummary` actually carries. Two things a finance user
 * would expect are deliberately ABSENT because no published field backs them:
 *  - party / Cari name — the summary has ids only and there is no Finance party reader;
 *  - currency code — only `currencyId` exists; no reader resolves it to a code.
 * Showing a guessed name or a default "TL" would be a false money statement.
 *
 * The bank account name is joined from the FS0 reader. If that read fails the list still
 * renders; the cell says the name is unavailable rather than hiding the row.
 */

import { useState } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { decodeBankAccountPage, decodeSourcePage } from './contract'
import { useFinanceRead } from './financePort'
import {
  BANK_NAME_UNAVAILABLE,
  COLUMN,
  LIST_EMPTY,
  NEXT_PAGE,
  OPEN_SOURCE,
  PREVIOUS_PAGE,
  SOURCE_LIST_SUBJECT,
  STAGE_LABEL,
  VALUE_DATE_NONE,
} from './financeText'
import { amountShown, amountText, stageKey } from './sourceView'
import styles from './Finance.module.css'

export function FinanceSourceList({ onOpen }: { readonly onOpen: (sourceId: string) => void }) {
  /** Cursor trail: index 0 is the first page (cursor null). */
  const [cursors, setCursors] = useState<readonly (string | null)[]>([null])
  const cursor = cursors[cursors.length - 1]
  const list = useFinanceRead('sources', [cursor], (port, scope, signal) => port.listSources(scope, { cursor }, signal), decodeSourcePage)
  const banks = useFinanceRead('bank-accounts', [], (port, scope, signal) => port.listBankAccounts(scope, signal), decodeBankAccountPage)

  if (list.state.kind !== 'ready') {
    return <StateBlock state={list.state} subject={SOURCE_LIST_SUBJECT} onRetry={list.refetch} />
  }
  const page = list.state.data
  if (page.items.length === 0 && cursors.length === 1) {
    return <StateBlock state={{ kind: 'empty-no-data' }} subject={LIST_EMPTY} />
  }
  const bankName = (id: string): string => {
    if (banks.state.kind !== 'ready') return BANK_NAME_UNAVAILABLE
    return banks.state.data.items.find((account) => account.id === id)?.displayName ?? BANK_NAME_UNAVAILABLE
  }

  return (
    <div className={styles.list}>
      <table>
        <thead>
          <tr>
            <th scope="col">{COLUMN.sourceNumber}</th>
            <th scope="col">{COLUMN.stage}</th>
            <th scope="col">{COLUMN.occurredOn}</th>
            <th scope="col">{COLUMN.valueOn}</th>
            <th scope="col">{COLUMN.bankAccount}</th>
            <th scope="col">{COLUMN.amount}</th>
            <th scope="col">{COLUMN.actions}</th>
          </tr>
        </thead>
        <tbody>
          {page.items.map((item) => {
            const amount = amountShown(item.amount)
            return (
              <tr key={item.id} data-stage={stageKey(item)}>
                <th scope="row">{item.sourceNumber}</th>
                <td>{STAGE_LABEL[stageKey(item)]}</td>
                <td>{item.occurredOn}</td>
                <td>{item.valueOn ?? VALUE_DATE_NONE}</td>
                <td>{bankName(item.treasuryAccountId)}</td>
                <td data-amount-kind={amount.kind}>{amountText(amount)}</td>
                <td>
                  <button type="button" onClick={() => onOpen(item.id)} aria-label={`${OPEN_SOURCE}: ${item.sourceNumber}`}>
                    {OPEN_SOURCE}
                  </button>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
      <div className={styles.actions}>
        {cursors.length > 1 ? (
          <button type="button" onClick={() => setCursors((trail) => trail.slice(0, -1))}>
            {PREVIOUS_PAGE}
          </button>
        ) : null}
        {page.nextCursor !== null ? (
          <button type="button" onClick={() => setCursors((trail) => [...trail, page.nextCursor])}>
            {NEXT_PAGE}
          </button>
        ) : null}
      </div>
    </div>
  )
}
