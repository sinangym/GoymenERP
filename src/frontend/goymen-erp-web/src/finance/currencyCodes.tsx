/**
 * A currency CODE only from a real read (Business 5745076917: no technical id as final presentation).
 *
 * The one read that labels a currency by its code is Settings' currency candidates
 * (`GET …/bank-accounts/currency-candidates`), and that read is the bank-account PROPOSE permission's
 * (the F-04 rule, BankAccountsPage). So a caller who may propose gets the code; anyone else gets the
 * existing honest reference (`currencyFallback`: "Para birimi kimliği …xxxx"), never a guessed or default
 * code. No new read, no new permission, no policy.
 */

import type { ReactNode } from 'react'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { decodeCurrencyCandidates } from './bankAccountContract'
import { currencyFallback } from './bankAccountText'
import { FINANCE_CAPABILITY } from './capabilities'
import { useFinanceRead } from './financePort'

export type CurrencyCodes = ReadonlyMap<string, string> | null

/** The code when a real read gave it, otherwise the labelled reference. */
export function currencyShown(currencyId: string, codes: CurrencyCodes): string {
  return codes?.get(currencyId) ?? currencyFallback(currencyId)
}

/** Renders `children` with the candidate codes for a caller who may read them, and with `null` for anyone else. */
export function WithCurrencyCodes({ children }: { readonly children: (codes: CurrencyCodes) => ReactNode }) {
  const shell = useShellState()
  return hasCapability(shell, FINANCE_CAPABILITY.bankAccountsPropose) ? (
    <CandidateCodes>{children}</CandidateCodes>
  ) : (
    <>{children(null)}</>
  )
}

/** Only mounted for a caller who may propose: the candidate read is that permission's. */
function CandidateCodes({ children }: { readonly children: (codes: CurrencyCodes) => ReactNode }) {
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
  return <>{children(codes)}</>
}
