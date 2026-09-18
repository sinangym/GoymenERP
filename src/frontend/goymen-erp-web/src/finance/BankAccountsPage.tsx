/**
 * F-04 Banka Hesapları — the company's bank-account master (FIN-UI-1a routes, #325).
 *
 * - The list is the server's `GET /finance/bank-accounts/master`: a read-only caller gets approved
 *   accounts only; a proposer or approver also sees pending proposals, each marked when it is the
 *   caller's own (least privilege is the server's, never filtered here).
 * - The approval is a CONTROL of this list (`finance.bank-accounts.approve`): offered on a pending
 *   row only when that control is published for the caller AND the row is not the caller's own
 *   proposal (Ö4). The server refuses a self-approval regardless.
 * - Each approval is its own command (ADR-011 on the client, ./bankAccountCommand.ts); after a
 *   settled answer the list is re-read, the command is never re-sent.
 * - A currency code is shown only from Settings' candidates, which only a proposer may read; anyone
 *   else sees the currency's identity, never a guessed code.
 */

import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { decodeBankAccountMasterPage, decodeCurrencyCandidates, type BankAccountMasterItem } from './bankAccountContract'
import { isBankFrozen } from './bankAccountCommand'
import {
  APPROVE,
  APPROVED_NOTICE,
  APPROVED_REPLAYED,
  APPROVE_CANCEL,
  APPROVE_CONFIRM,
  APPROVE_CONFIRM_TEXT,
  APPROVE_CONFIRM_TITLE,
  BANK_COLUMN,
  BANK_MASTER_EMPTY,
  BANK_MASTER_NOTICE,
  BANK_MASTER_SUBJECT,
  BANK_MASTER_TITLE,
  BANK_OWN_PROPOSAL,
  BANK_PROPOSE_LINK,
  BANK_STATE_APPROVED,
  BANK_STATE_PENDING,
  currencyFallback,
} from './bankAccountText'
import { FINANCE_CAPABILITY, FINANCE_ROUTE } from './capabilities'
import { CommandAttemptPanel } from './CommandAttemptPanel'
import { useBankAccountCommand, useFinanceRead } from './financePort'
import styles from './Finance.module.css'

export function BankAccountsPage() {
  const shell = useShellState()
  const canPropose = hasCapability(shell, FINANCE_CAPABILITY.bankAccountsPropose)
  const canApprove = hasCapability(shell, FINANCE_CAPABILITY.bankAccountsApprove)
  const list = useFinanceRead(
    'bank-account-master',
    [],
    (port, scope, signal) => port.listBankAccountMaster(scope, signal),
    decodeBankAccountMasterPage,
  )

  return (
    <section aria-labelledby="finance-bank-accounts-title">
      <h1 id="finance-bank-accounts-title">{BANK_MASTER_TITLE}</h1>
      <p className={styles.notice}>{BANK_MASTER_NOTICE}</p>
      {canPropose ? (
        <p>
          <Link to={FINANCE_ROUTE.bankAccountsPropose}>{BANK_PROPOSE_LINK}</Link>
        </p>
      ) : null}
      {list.state.kind !== 'ready' ? (
        <StateBlock state={list.state} subject={BANK_MASTER_SUBJECT} onRetry={list.refetch} />
      ) : list.state.data.items.length === 0 ? (
        <StateBlock state={{ kind: 'empty-no-data' }} subject={BANK_MASTER_EMPTY} />
      ) : canPropose ? (
        <AccountTableWithCodes items={list.state.data.items} canApprove={canApprove} onSettled={list.refetch} />
      ) : (
        <AccountTable items={list.state.data.items} codes={null} canApprove={canApprove} onSettled={list.refetch} />
      )}
    </section>
  )
}

/** Only mounted for a caller who may propose: the candidate read is that permission's. */
function AccountTableWithCodes(props: {
  readonly items: readonly BankAccountMasterItem[]
  readonly canApprove: boolean
  readonly onSettled: () => void
}) {
  const candidates = useFinanceRead(
    'bank-account-currency-candidates',
    [],
    (port, scope, signal) => port.bankAccountCurrencyCandidates(scope, signal),
    decodeCurrencyCandidates,
  )
  const codes =
    candidates.state.kind === 'ready'
      ? new Map(candidates.state.data.items.map((item) => [item.currencyId, item.code]))
      : null
  return <AccountTable {...props} codes={codes} />
}

function AccountTable({
  items,
  codes,
  canApprove,
  onSettled,
}: {
  readonly items: readonly BankAccountMasterItem[]
  readonly codes: ReadonlyMap<string, string> | null
  readonly canApprove: boolean
  readonly onSettled: () => void
}) {
  return (
    <div className={styles.list}>
      <table>
        <thead>
          <tr>
            <th scope="col">{BANK_COLUMN.name}</th>
            <th scope="col">{BANK_COLUMN.currency}</th>
            <th scope="col">{BANK_COLUMN.state}</th>
            <th scope="col">{BANK_COLUMN.action}</th>
          </tr>
        </thead>
        <tbody>
          {items.map(({ account, isOwnProposal }) => (
            <tr key={account.id} data-account-state={account.isActive ? 'approved' : 'pending'}>
              <th scope="row">{account.displayName}</th>
              <td>{codes?.get(account.currencyId) ?? currencyFallback(account.currencyId)}</td>
              <td>{account.isActive ? BANK_STATE_APPROVED : BANK_STATE_PENDING}</td>
              <td>
                {account.isActive ? null : isOwnProposal ? (
                  <span>{BANK_OWN_PROPOSAL}</span>
                ) : canApprove ? (
                  <ApproveControl accountId={account.id} name={account.displayName} version={account.version} onSettled={onSettled} />
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** One pending account's approval: confirm, then one command; the list is re-read once per settled key. */
function ApproveControl({
  accountId,
  name,
  version,
  onSettled,
}: {
  readonly accountId: string
  readonly name: string
  readonly version: string
  readonly onSettled: () => void
}) {
  const command = useBankAccountCommand('bank-account-approve', accountId)
  const [confirming, setConfirming] = useState(false)
  const reread = useRef<string | null>(null)
  const { state } = command

  useEffect(() => {
    const settledKey =
      state.phase === 'succeeded' || (state.phase === 'refused' && state.reason === 'version-conflict')
        ? state.snapshot.key
        : null
    if (settledKey !== null && reread.current !== settledKey) {
      reread.current = settledKey
      onSettled()
    }
  }, [state, onSettled])

  if (state.phase === 'succeeded') {
    return <p role="status">{state.replayed ? APPROVED_REPLAYED : APPROVED_NOTICE}</p>
  }

  const frozen = isBankFrozen(state)
  return (
    <div>
      {confirming && !frozen ? (
        <div role="group" aria-labelledby={`finance-approve-${accountId}`}>
          <p id={`finance-approve-${accountId}`} className={styles.attemptTitle}>
            {APPROVE_CONFIRM_TITLE}
          </p>
          <p>{APPROVE_CONFIRM_TEXT}</p>
          <div className={styles.actions}>
            <button
              type="button"
              onClick={() => {
                if (
                  command.submit({ operation: 'bank-account-approve', accountId, body: { expectedVersion: version } }) ===
                  'started'
                ) {
                  setConfirming(false)
                }
              }}
            >
              {APPROVE_CONFIRM}
            </button>
            <button type="button" onClick={() => setConfirming(false)}>
              {APPROVE_CANCEL}
            </button>
          </div>
        </div>
      ) : (
        <button
          type="button"
          disabled={frozen || !command.available}
          aria-label={`${APPROVE}: ${name}`}
          onClick={() => setConfirming(true)}
        >
          {APPROVE}
        </button>
      )}
      <CommandAttemptPanel command={command} />
    </div>
  )
}
