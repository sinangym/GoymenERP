/**
 * F-04 — propose a bank account (FIN-UI-1a `POST /finance/bank-accounts`).
 *
 * - The currency is picked from Settings' candidates (`GET …/currency-candidates`, the propose
 *   authority) exactly as given: order kept, NOTHING preselected — even a single candidate must be
 *   chosen by the user. No candidates is said as such; an unusable source is its own state.
 * - K2's card (#66 5748258299): bank name and account type are mandatory, branch name/code, IBAN and
 *   account number are holdable. B3 (#66 5750700247) adds the money-movement DECLARATION, asked as a
 *   statement the person makes — never inferred from which fields they filled in. That is the whole
 *   point of the answer: eligibility comes from the declaration, so typing an IBAN must not silently
 *   make a card money-eligible.
 * - Name and length rules are the server's, checked here only to spare a round trip; the server
 *   decides. The IBAN's STRUCTURE is deliberately not re-checked here — the checksum lives in
 *   `FinanceIban` on the server and a screen that re-implemented it would be a second source of a
 *   rule. The form only knows that declaring the card money-eligible requires an IBAN to be present.
 * - One command with ADR-011 discipline (./bankAccountCommand.ts). A success is a PROPOSAL: not
 *   active, not usable until someone else approves (Ö4). The form closes after it — submitting again
 *   would be a second proposal.
 */

import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellDispatch } from '../shell/shellContext'
import { decodeCurrencyCandidates } from './bankAccountContract'
import { isBankFrozen } from './bankAccountCommand'
import {
  BACK_TO_LIST,
  PROPOSED_NOTICE,
  PROPOSED_REPLAYED,
  PROPOSE_ACCOUNT_NUMBER,
  PROPOSE_BANK,
  PROPOSE_BRANCH_CODE,
  PROPOSE_BRANCH_NAME,
  PROPOSE_CURRENCY,
  PROPOSE_CURRENCY_PLACEHOLDER,
  PROPOSE_ELIGIBLE,
  PROPOSE_ELIGIBLE_HINT,
  PROPOSE_ERROR,
  PROPOSE_IBAN,
  PROPOSE_NAME,
  PROPOSE_NAME_HINT,
  PROPOSE_NOTICE,
  PROPOSE_NO_CANDIDATES,
  PROPOSE_OPTIONAL,
  PROPOSE_SUBJECT,
  PROPOSE_SUBMIT,
  PROPOSE_TITLE,
  PROPOSE_TYPE,
  PROPOSE_TYPE_HINT,
} from './bankAccountText'
import { FINANCE_ROUTE } from './capabilities'
import { CommandAttemptPanel } from './CommandAttemptPanel'
import { useBankAccountCommand, useFinanceRead } from './financePort'
import styles from './Finance.module.css'

export const FINANCE_BANK_PROPOSE_DIRTY_SCOPE = 'finance.bank-account.propose'
const NAME_MAXIMUM = 200
const TYPE_MAXIMUM = 64

type FieldError = keyof typeof PROPOSE_ERROR
type Errors = {
  name?: FieldError
  currency?: FieldError
  bankName?: FieldError
  accountType?: FieldError
  iban?: FieldError
}

interface Draft {
  readonly name: string
  readonly currencyId: string
  readonly bankName: string
  readonly accountType: string
  readonly eligible: boolean
  readonly iban: string
}

function validateProposal(draft: Draft): Errors {
  const errors: Errors = {}
  if (draft.name.trim().length === 0) errors.name = 'nameRequired'
  else if (draft.name !== draft.name.trim()) errors.name = 'nameSpaces'
  else if (draft.name.length > NAME_MAXIMUM) errors.name = 'nameTooLong'
  if (draft.currencyId === '') errors.currency = 'currencyRequired'
  if (draft.bankName.trim().length === 0) errors.bankName = 'bankRequired'
  else if (draft.bankName.length > NAME_MAXIMUM) errors.bankName = 'bankTooLong'
  if (draft.accountType.trim().length === 0) errors.accountType = 'typeRequired'
  else if (draft.accountType.length > TYPE_MAXIMUM) errors.accountType = 'typeTooLong'
  // B3 + K2: the IBAN requirement attaches to the DECLARATION, not to the other fields. Its shape is
  // the server's to judge; all this knows is that "for payment or collection" needs one at all.
  if (draft.eligible && draft.iban.trim().length === 0) errors.iban = 'ibanRequired'
  return errors
}

/** A holdable text: blank and absent are the same thing, and neither is invented. */
const held = (value: string): string | null => (value.trim().length === 0 ? null : value.trim())

export function BankAccountProposePage() {
  const candidates = useFinanceRead(
    'bank-account-currency-candidates',
    [],
    (port, scope, signal) => port.bankAccountCurrencyCandidates(scope, signal),
    decodeCurrencyCandidates,
  )
  const command = useBankAccountCommand('bank-account-propose', 'new')
  const [name, setName] = useState('')
  const [currencyId, setCurrencyId] = useState('')
  const [bankName, setBankName] = useState('')
  const [accountType, setAccountType] = useState('')
  const [branchName, setBranchName] = useState('')
  const [branchCode, setBranchCode] = useState('')
  const [iban, setIban] = useState('')
  const [accountNumber, setAccountNumber] = useState('')
  // Unchecked by default, and that is fail-closed on purpose: a card nobody declared for money must
  // not become money-eligible because a checkbox started on.
  const [eligible, setEligible] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const frozen = isBankFrozen(command.state)
  const done = command.state.phase === 'succeeded'

  const entered = [name, currencyId, bankName, accountType, branchName, branchCode, iban, accountNumber]
  // Unsaved input and an unresolved key are dirty work: a company switch must ask first.
  const dirty = frozen || (!done && (entered.some((value) => value !== '') || eligible))
  const shellDispatch = useShellDispatch()
  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: FINANCE_BANK_PROPOSE_DIRTY_SCOPE })
  }, [dirty, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: FINANCE_BANK_PROPOSE_DIRTY_SCOPE }), [shellDispatch])

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    if (frozen || done) return
    const found = validateProposal({ name, currencyId, bankName, accountType, eligible, iban })
    setErrors(found)
    if (Object.keys(found).length > 0) return
    command.submit({
      operation: 'bank-account-propose',
      body: {
        displayName: name,
        currencyId,
        bankName: bankName.trim(),
        accountType: accountType.trim(),
        moneyMovementEligible: eligible,
        branchName: held(branchName),
        branchCode: held(branchCode),
        iban: held(iban),
        accountNumber: held(accountNumber),
      },
    })
  }

  const text = (
    id: string,
    label: string,
    value: string,
    setValue: (next: string) => void,
    options: { readonly hint?: string; readonly error?: FieldError; readonly optional?: boolean } = {},
  ) => (
    <div className={styles.field}>
      <label htmlFor={id}>
        {label}
        {options.optional ? ` (${PROPOSE_OPTIONAL})` : ''}
      </label>
      <input
        id={id}
        value={value}
        disabled={frozen}
        aria-invalid={options.error !== undefined}
        aria-describedby={options.error ? `${id}-error` : options.hint ? `${id}-hint` : undefined}
        onChange={(event) => setValue(event.target.value)}
      />
      {options.hint ? (
        <p id={`${id}-hint`} className={styles.hint}>
          {options.hint}
        </p>
      ) : null}
      {options.error ? (
        <p id={`${id}-error`} className={styles.error}>
          {PROPOSE_ERROR[options.error]}
        </p>
      ) : null}
    </div>
  )

  return (
    <section aria-labelledby="finance-bank-propose-title">
      <h1 id="finance-bank-propose-title">{PROPOSE_TITLE}</h1>
      <p className={styles.notice}>{PROPOSE_NOTICE}</p>
      {command.state.phase === 'succeeded' ? (
        <div role="status">
          <p>{command.state.replayed ? PROPOSED_REPLAYED : PROPOSED_NOTICE}</p>
          <p>
            <Link to={FINANCE_ROUTE.bankAccountsList}>{BACK_TO_LIST}</Link>
          </p>
        </div>
      ) : candidates.state.kind !== 'ready' ? (
        <StateBlock state={candidates.state} subject={PROPOSE_SUBJECT} onRetry={candidates.refetch} />
      ) : candidates.state.data.items.length === 0 ? (
        <p role="status">{PROPOSE_NO_CANDIDATES}</p>
      ) : (
        <form className={styles.form} onSubmit={onSubmit} noValidate>
          {text('finance-bank-name', PROPOSE_NAME, name, setName, { hint: PROPOSE_NAME_HINT, error: errors.name })}
          <div className={styles.field}>
            <label htmlFor="finance-bank-currency">{PROPOSE_CURRENCY}</label>
            <select
              id="finance-bank-currency"
              value={currencyId}
              disabled={frozen}
              aria-invalid={errors.currency !== undefined}
              aria-describedby={errors.currency ? 'finance-bank-currency-error' : undefined}
              onChange={(event) => setCurrencyId(event.target.value)}
            >
              <option value="">{PROPOSE_CURRENCY_PLACEHOLDER}</option>
              {candidates.state.data.items.map((candidate) => (
                <option key={candidate.currencyId} value={candidate.currencyId}>
                  {`${candidate.code} — ${candidate.displayLabel}`}
                </option>
              ))}
            </select>
            {errors.currency ? (
              <p id="finance-bank-currency-error" className={styles.error}>
                {PROPOSE_ERROR[errors.currency]}
              </p>
            ) : null}
          </div>
          {text('finance-bank-bank', PROPOSE_BANK, bankName, setBankName, { error: errors.bankName })}
          {text('finance-bank-type', PROPOSE_TYPE, accountType, setAccountType, {
            hint: PROPOSE_TYPE_HINT,
            error: errors.accountType,
          })}
          <div className={styles.field}>
            <label htmlFor="finance-bank-eligible">
              <input
                id="finance-bank-eligible"
                type="checkbox"
                checked={eligible}
                disabled={frozen}
                aria-describedby="finance-bank-eligible-hint"
                onChange={(event) => setEligible(event.target.checked)}
              />
              {PROPOSE_ELIGIBLE}
            </label>
            <p id="finance-bank-eligible-hint" className={styles.hint}>
              {PROPOSE_ELIGIBLE_HINT}
            </p>
          </div>
          {text('finance-bank-iban', PROPOSE_IBAN, iban, setIban, {
            error: errors.iban,
            optional: !eligible,
          })}
          {text('finance-bank-branch-name', PROPOSE_BRANCH_NAME, branchName, setBranchName, { optional: true })}
          {text('finance-bank-branch-code', PROPOSE_BRANCH_CODE, branchCode, setBranchCode, { optional: true })}
          {text('finance-bank-account-number', PROPOSE_ACCOUNT_NUMBER, accountNumber, setAccountNumber, {
            optional: true,
          })}
          <div className={styles.actions}>
            <button type="submit" disabled={frozen || !command.available}>
              {PROPOSE_SUBMIT}
            </button>
          </div>
          <CommandAttemptPanel command={command} />
        </form>
      )}
      {command.state.phase === 'succeeded' ? null : (
        <p>
          <Link to={FINANCE_ROUTE.bankAccountsList}>{BACK_TO_LIST}</Link>
        </p>
      )}
    </section>
  )
}
