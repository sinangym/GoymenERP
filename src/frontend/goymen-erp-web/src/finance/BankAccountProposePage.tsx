/**
 * F-04 — propose a bank account (FIN-UI-1a `POST /finance/bank-accounts`).
 *
 * - The currency is picked from Settings' candidates (`GET …/currency-candidates`, the propose
 *   authority) exactly as given: order kept, NOTHING preselected — even a single candidate must be
 *   chosen by the user. No candidates is said as such; an unusable source is its own state.
 * - Name rules are the server's (non-blank, no surrounding spaces, at most 200) checked here only to
 *   spare a round trip; the server decides.
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
  PROPOSE_CURRENCY,
  PROPOSE_CURRENCY_PLACEHOLDER,
  PROPOSE_ERROR,
  PROPOSE_NAME,
  PROPOSE_NAME_HINT,
  PROPOSE_NOTICE,
  PROPOSE_NO_CANDIDATES,
  PROPOSE_SUBJECT,
  PROPOSE_SUBMIT,
  PROPOSE_TITLE,
} from './bankAccountText'
import { FINANCE_ROUTE } from './capabilities'
import { CommandAttemptPanel } from './CommandAttemptPanel'
import { useBankAccountCommand, useFinanceRead } from './financePort'
import styles from './Finance.module.css'

export const FINANCE_BANK_PROPOSE_DIRTY_SCOPE = 'finance.bank-account.propose'
const NAME_MAXIMUM = 200

type NameError = keyof typeof PROPOSE_ERROR

function validateProposal(name: string, currencyId: string): { name?: NameError; currency?: NameError } {
  const errors: { name?: NameError; currency?: NameError } = {}
  if (name.trim().length === 0) errors.name = 'nameRequired'
  else if (name !== name.trim()) errors.name = 'nameSpaces'
  else if (name.length > NAME_MAXIMUM) errors.name = 'nameTooLong'
  if (currencyId === '') errors.currency = 'currencyRequired'
  return errors
}

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
  const [errors, setErrors] = useState<{ name?: NameError; currency?: NameError }>({})
  const frozen = isBankFrozen(command.state)
  const done = command.state.phase === 'succeeded'

  // Unsaved input and an unresolved key are dirty work: a company switch must ask first.
  const dirty = frozen || (!done && (name !== '' || currencyId !== ''))
  const shellDispatch = useShellDispatch()
  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: FINANCE_BANK_PROPOSE_DIRTY_SCOPE })
  }, [dirty, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: FINANCE_BANK_PROPOSE_DIRTY_SCOPE }), [shellDispatch])

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    if (frozen || done) return
    const found = validateProposal(name, currencyId)
    setErrors(found)
    if (found.name !== undefined || found.currency !== undefined) return
    command.submit({ operation: 'bank-account-propose', body: { displayName: name, currencyId } })
  }

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
          <div className={styles.field}>
            <label htmlFor="finance-bank-name">{PROPOSE_NAME}</label>
            <input
              id="finance-bank-name"
              value={name}
              disabled={frozen}
              aria-invalid={errors.name !== undefined}
              aria-describedby={errors.name ? 'finance-bank-name-error' : 'finance-bank-name-hint'}
              onChange={(event) => setName(event.target.value)}
            />
            <p id="finance-bank-name-hint" className={styles.hint}>
              {PROPOSE_NAME_HINT}
            </p>
            {errors.name ? (
              <p id="finance-bank-name-error" className={styles.error}>
                {PROPOSE_ERROR[errors.name]}
              </p>
            ) : null}
          </div>
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
