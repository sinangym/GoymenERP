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
import {
  decodeBankAccountMasterPage,
  decodeCurrencyCandidates,
  type BankAccountCard,
  type BankAccountMasterItem,
} from './bankAccountContract'
import { isBankFrozen } from './bankAccountCommand'
import {
  APPROVE,
  APPROVED_NOTICE,
  APPROVED_REPLAYED,
  APPROVE_CANCEL,
  APPROVE_CONFIRM,
  APPROVE_CONFIRM_TEXT,
  APPROVE_CONFIRM_TITLE,
  BANK_CARD,
  BANK_COLUMN,
  BANK_ELIGIBILITY,
  BANK_IBAN_CHANGE_APPROVE,
  BANK_IBAN_CHANGE_LINK,
  BANK_PENDING_IBAN_NOTICE,
  BANK_PENDING_IBAN_OWN,
  BANK_PENDING_IBAN_TITLE,
  BANK_UNDECLARED_HINT,
  bankPendingIban,
  bankPendingIbanReason,
  BANK_MASTER_EMPTY,
  BANK_MASTER_NOTICE,
  BANK_MASTER_SUBJECT,
  BANK_MASTER_TITLE,
  BANK_OWN_PROPOSAL,
  BANK_PROPOSE_LINK,
  BANK_STATE_APPROVED,
  BANK_STATE_PENDING,
  currencyFallback,
  IBAN_CHANGE_APPROVED,
  IBAN_CHANGE_ERROR,
  IBAN_CHANGE_NEW,
  IBAN_CHANGE_NOTICE,
  IBAN_CHANGE_PROPOSED,
  IBAN_CHANGE_REASON,
  IBAN_CHANGE_REASON_HINT,
  IBAN_CHANGE_REPLAYED,
  IBAN_CHANGE_SUBMIT,
  IBAN_CHANGE_TITLE,
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
        <AccountTableWithCodes items={list.state.data.items} canApprove={canApprove} canPropose onSettled={list.refetch} />
      ) : (
        <AccountTable items={list.state.data.items} codes={null} canApprove={canApprove} canPropose={false} onSettled={list.refetch} />
      )}
    </section>
  )
}

/** Only mounted for a caller who may propose: the candidate read is that permission's. */
function AccountTableWithCodes(props: {
  readonly items: readonly BankAccountMasterItem[]
  readonly canApprove: boolean
  readonly canPropose: boolean
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
  canPropose,
  onSettled,
}: {
  readonly items: readonly BankAccountMasterItem[]
  readonly codes: ReadonlyMap<string, string> | null
  readonly canApprove: boolean
  readonly canPropose: boolean
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
          {items.map(({ account, isOwnProposal, card, isOwnPendingIbanProposal }) => (
            <tr
              key={account.id}
              data-account-state={account.isActive ? 'approved' : 'pending'}
              data-money-eligibility={card.moneyEligibility}
            >
              <th scope="row">
                {account.displayName}
                <CardLine card={card} />
              </th>
              <td>{codes?.get(account.currencyId) ?? currencyFallback(account.currencyId)}</td>
              <td>
                <p data-testid={`bank-${account.id}-state`}>{account.isActive ? BANK_STATE_APPROVED : BANK_STATE_PENDING}</p>
                {/*
                  B3: the declaration is its own sentence beside the approval state, never merged into
                  it. Approved-and-undeclared is a real combination — the card exists and is not for
                  money — and one line saying "onaylı" would hide exactly that.
                */}
                <p data-testid={`bank-${account.id}-eligibility`}>{BANK_ELIGIBILITY[card.moneyEligibility]}</p>
                {card.moneyEligibility === 'Undeclared' ? (
                  <p className={styles.hint}>{BANK_UNDECLARED_HINT}</p>
                ) : null}
                {card.pendingIbanChange ? (
                  <div data-testid={`bank-${account.id}-pending-iban`}>
                    <p>{BANK_PENDING_IBAN_TITLE}</p>
                    <p>{bankPendingIban(card.pendingIbanChange.iban)}</p>
                    <p>{bankPendingIbanReason(card.pendingIbanChange.reason)}</p>
                    <p className={styles.hint}>{BANK_PENDING_IBAN_NOTICE}</p>
                  </div>
                ) : null}
              </td>
              <td>
                {account.isActive ? null : isOwnProposal ? (
                  <span>{BANK_OWN_PROPOSAL}</span>
                ) : canApprove ? (
                  <ApproveControl accountId={account.id} name={account.displayName} version={account.version} onSettled={onSettled} />
                ) : null}
                {/*
                  D4: the change's own Ö4, offered to someone other than the person who proposed it.
                  The server refuses a self-approval regardless; this only avoids offering a control
                  that cannot succeed.
                */}
                {account.isActive && card.pendingIbanChange ? (
                  isOwnPendingIbanProposal ? (
                    <span>{BANK_PENDING_IBAN_OWN}</span>
                  ) : canApprove ? (
                    <IbanChangeApproveControl accountId={account.id} version={account.version} onSettled={onSettled} />
                  ) : null
                ) : null}
                {account.isActive && !card.pendingIbanChange && canPropose ? (
                  <IbanChangeProposeControl accountId={account.id} version={account.version} onSettled={onSettled} />
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

/**
 * K2's card beneath the account's name. An empty mandatory text is a row written before K2 — it is
 * said as "Girilmemiş" rather than left blank, because a blank cell reads as "nothing to say" and
 * this cell has something to say. The IBAN is shown as stored; nothing is masked here, since
 * reaching this list already requires the narrow bank-account read.
 */
function CardLine({ card }: { readonly card: BankAccountCard }) {
  const text = (value: string) => (value.trim().length === 0 ? BANK_CARD.missing : value)
  return (
    <p className={styles.hint}>
      {`${BANK_CARD.bank}: ${text(card.bankName)} · ${BANK_CARD.type}: ${text(card.accountType)} · `}
      {`${BANK_CARD.iban}: ${card.iban ?? BANK_CARD.noIban}`}
    </p>
  )
}

/**
 * D4: propose a new IBAN for an approved account. The form is on the row rather than its own screen
 * because it needs nothing the row does not already have, and a new route would need a capability
 * registration for what is a control of this list.
 *
 * The IBAN's STRUCTURE is not judged here — the checksum is the server's `FinanceIban`, and a second
 * copy on the screen would be the same rule written twice. This only requires that a value and a
 * reason were entered at all; the server decides whether the value is an IBAN.
 */
function IbanChangeProposeControl({
  accountId,
  version,
  onSettled,
}: {
  readonly accountId: string
  readonly version: string
  readonly onSettled: () => void
}) {
  const command = useBankAccountCommand('bank-account-iban-change', accountId)
  const [open, setOpen] = useState(false)
  const [iban, setIban] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<keyof typeof IBAN_CHANGE_ERROR | null>(null)
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
    return <p role="status">{state.replayed ? IBAN_CHANGE_REPLAYED : IBAN_CHANGE_PROPOSED}</p>
  }

  const frozen = isBankFrozen(state)
  if (!open || frozen) {
    return (
      <div>
        <button type="button" disabled={frozen || !command.available} onClick={() => setOpen(true)}>
          {BANK_IBAN_CHANGE_LINK}
        </button>
        <CommandAttemptPanel command={command} />
      </div>
    )
  }

  return (
    <div role="group" aria-labelledby={`finance-iban-change-${accountId}`}>
      <p id={`finance-iban-change-${accountId}`} className={styles.attemptTitle}>
        {IBAN_CHANGE_TITLE}
      </p>
      <p>{IBAN_CHANGE_NOTICE}</p>
      <div className={styles.field}>
        <label htmlFor={`finance-iban-change-value-${accountId}`}>{IBAN_CHANGE_NEW}</label>
        <input
          id={`finance-iban-change-value-${accountId}`}
          value={iban}
          onChange={(event) => setIban(event.target.value)}
        />
      </div>
      <div className={styles.field}>
        <label htmlFor={`finance-iban-change-reason-${accountId}`}>{IBAN_CHANGE_REASON}</label>
        <input
          id={`finance-iban-change-reason-${accountId}`}
          value={reason}
          aria-describedby={`finance-iban-change-reason-hint-${accountId}`}
          onChange={(event) => setReason(event.target.value)}
        />
        <p id={`finance-iban-change-reason-hint-${accountId}`} className={styles.hint}>
          {IBAN_CHANGE_REASON_HINT}
        </p>
      </div>
      {error ? <p className={styles.error}>{IBAN_CHANGE_ERROR[error]}</p> : null}
      <div className={styles.actions}>
        <button
          type="button"
          disabled={!command.available}
          onClick={() => {
            const next =
              iban.trim().length === 0 ? 'ibanRequired' : reason.trim().length === 0 ? 'reasonRequired' : null
            setError(next)
            if (next !== null) return
            command.submit({
              operation: 'bank-account-iban-change',
              accountId,
              body: { expectedVersion: version, iban: iban.trim(), reason: reason.trim() },
            })
          }}
        >
          {IBAN_CHANGE_SUBMIT}
        </button>
        <button type="button" onClick={() => setOpen(false)}>
          {APPROVE_CANCEL}
        </button>
      </div>
      <CommandAttemptPanel command={command} />
    </div>
  )
}

/** D4: approve the waiting IBAN. Ö4 is the server's; this control is simply not offered to its proposer. */
function IbanChangeApproveControl({
  accountId,
  version,
  onSettled,
}: {
  readonly accountId: string
  readonly version: string
  readonly onSettled: () => void
}) {
  const command = useBankAccountCommand('bank-account-iban-change-approve', accountId)
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
    return <p role="status">{state.replayed ? IBAN_CHANGE_REPLAYED : IBAN_CHANGE_APPROVED}</p>
  }

  const frozen = isBankFrozen(state)
  return (
    <div>
      <button
        type="button"
        disabled={frozen || !command.available}
        onClick={() =>
          command.submit({
            operation: 'bank-account-iban-change-approve',
            accountId,
            body: { expectedVersion: version },
          })
        }
      >
        {BANK_IBAN_CHANGE_APPROVE}
      </button>
      <CommandAttemptPanel command={command} />
    </div>
  )
}
