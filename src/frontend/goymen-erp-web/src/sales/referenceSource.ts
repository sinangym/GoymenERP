import { createContext, useContext, useEffect, useState } from 'react'
import {
  fetchAccountOptions,
  fetchContactInformationOptions,
  fetchCurrencyOptions,
  type AccountOption,
  type ReferenceListOutcome,
} from './api'
import type { ContactInformationOptions, CurrencyOption } from './contract'
import type { OrderDraftOptions } from './orderDraftForm'
import { isDateOnly, isReferenceId } from './wirePrimitives'

/**
 * Where the create form's selection options come from. `ready` only when every reference the
 * form needs (accounts, currencies, products with their order units) is offered by its real owner;
 * otherwise `unavailable` names what is missing and why, so the screen withholds the lines and the
 * submit with an honest reason (never a guessed option, never a default). SALES-A2a: the HEADER
 * references (accounts, currencies) are offered as `header` even while the products are missing, so
 * the header selects are real on main; the lines stay withheld until the product list (E1).
 */
export type ReferenceSourceName = 'accounts' | 'currencies' | 'products'

export type MissingReference = {
  readonly source: ReferenceSourceName
  /** `no-list-route`: the owner offers no list on main; `http-<status>[:<code>]`, `network`, `malformed`: the real answer. */
  readonly reason: string
}

/**
 * One keyed list read's own state (SALES-A2a): `idle` while its inputs are incomplete (nothing is
 * asked), `loading` while the real answer is pending, `ready` with the owner's list (an empty list is
 * "the owner lists none", not a failure), `unavailable` with the real reason and a retry.
 */
export type ReferenceReadState<T> =
  | { readonly status: 'idle' }
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly data: T }
  | { readonly status: 'unavailable'; readonly reason: string; readonly retry: () => void }

export type CurrencyReadState = ReferenceReadState<readonly CurrencyOption[]>
export type ContactInformationReadState = ReferenceReadState<ContactInformationOptions>

/** The currencies usable for a sales draft on the business date; idle until a real date is entered. */
export type UseCurrencyOptions = (companyId: string, effectiveOn: string) => CurrencyReadState
/** The chosen party account's active addresses and contacts; idle until an account is chosen. */
export type UseContactInformation = (companyId: string, partyId: string, companyAccountId: string) => ContactInformationReadState

/** What the form can already show while the lines wait for the product list. */
export interface HeaderOptions {
  readonly accounts: readonly AccountOption[]
  readonly currencies: CurrencyReadState
}

export type ReferenceSourceState =
  | { readonly status: 'unavailable'; readonly missing?: readonly MissingReference[]; readonly header?: HeaderOptions }
  | { readonly status: 'ready'; readonly options: OrderDraftOptions }

export type UseReferenceOptions = (companyId: string, businessDate: string) => ReferenceSourceState

export const unavailableReferenceSource: UseReferenceOptions = () => ({ status: 'unavailable' })

/**
 * On main the Sales reference routes are: accounts = a LIST; currencies = a LIST for a business date
 * (SALES-A2a); contact-information = a LIST for a party account (SALES-A2a); product = a VALIDATOR
 * of one chosen id for a date. The form needs a product list too, so with the real API the source is
 * never `ready` yet: products stay `no-list-route` until their owner publishes a list (E1). The
 * accounts and currencies lists are read for real (their answers are measured and shown). Nothing
 * here invents an option.
 */
export const NO_LIST_ROUTE: readonly MissingReference[] = [{ source: 'products', reason: 'no-list-route' }]

function failureReason<T>(outcome: ReferenceListOutcome<T>): string | null {
  switch (outcome.type) {
    case 'ready':
      return null
    case 'http':
      return `http-${outcome.status}${outcome.code === null ? '' : `:${outcome.code}`}`
    case 'network':
      return 'network'
    case 'malformed':
      return 'malformed'
  }
}

function readState<T>(outcome: ReferenceListOutcome<T>, retry: () => void): ReferenceReadState<T> {
  const reason = failureReason(outcome)
  if (reason !== null) return { status: 'unavailable', reason, retry }
  return outcome.type === 'ready' ? { status: 'ready', data: outcome.data } : { status: 'unavailable', reason: 'malformed', retry }
}

type Held<T> = { readonly key: string; readonly value: ReferenceReadState<T> }

/** The accounts list once per company (S03). */
function useHttpAccountOptions(companyId: string): ReferenceReadState<readonly AccountOption[]> {
  const [state, setState] = useState<Held<readonly AccountOption[]> | null>(null)
  const [attempt, setAttempt] = useState(0)
  useEffect(() => {
    if (companyId === '') return
    const controller = new AbortController()
    fetchAccountOptions(companyId, controller.signal).then((outcome) => {
      if (controller.signal.aborted) return
      const retry = () => setAttempt((n) => n + 1)
      const value: ReferenceReadState<readonly AccountOption[]> =
        outcome.type === 'ready'
          ? { status: 'ready', data: outcome.accounts }
          : { status: 'unavailable', reason: failureReason(outcome) ?? 'malformed', retry }
      setState({ key: companyId, value })
    })
    return () => controller.abort()
  }, [companyId, attempt])
  if (companyId === '') return { status: 'idle' }
  return state !== null && state.key === companyId ? state.value : { status: 'loading' }
}

/** SALES-A2a: the real currencies list, re-read whenever the company or the business date changes. */
export const useHttpCurrencyOptions: UseCurrencyOptions = (companyId, effectiveOn) => {
  const key = companyId !== '' && isDateOnly(effectiveOn) ? `${companyId}|${effectiveOn}` : null
  const [state, setState] = useState<Held<readonly CurrencyOption[]> | null>(null)
  const [attempt, setAttempt] = useState(0)
  useEffect(() => {
    if (key === null) return
    const controller = new AbortController()
    fetchCurrencyOptions(effectiveOn, companyId, controller.signal).then((outcome) => {
      if (controller.signal.aborted) return
      setState({ key, value: readState(outcome, () => setAttempt((n) => n + 1)) })
    })
    return () => controller.abort()
  }, [key, companyId, effectiveOn, attempt])
  if (key === null) return { status: 'idle' }
  return state !== null && state.key === key ? state.value : { status: 'loading' }
}

/** SALES-A2a: the real contact information of the chosen account, re-read whenever the account changes. */
export const useHttpContactInformation: UseContactInformation = (companyId, partyId, companyAccountId) => {
  const key =
    companyId !== '' && isReferenceId(partyId) && isReferenceId(companyAccountId)
      ? `${companyId}|${partyId}|${companyAccountId}`
      : null
  const [state, setState] = useState<Held<ContactInformationOptions> | null>(null)
  const [attempt, setAttempt] = useState(0)
  useEffect(() => {
    if (key === null) return
    const controller = new AbortController()
    fetchContactInformationOptions(partyId, companyAccountId, companyId, controller.signal).then((outcome) => {
      if (controller.signal.aborted) return
      setState({ key, value: readState(outcome, () => setAttempt((n) => n + 1)) })
    })
    return () => controller.abort()
  }, [key, companyId, partyId, companyAccountId, attempt])
  if (key === null) return { status: 'idle' }
  return state !== null && state.key === key ? state.value : { status: 'loading' }
}

/**
 * The running app binds the real sources by default; tests and isolated screens provide their own
 * through the contexts. A provided source always wins over the default.
 */
export const SalesCurrencySourceContext = createContext<UseCurrencyOptions>(useHttpCurrencyOptions)
export const SalesContactInformationSourceContext = createContext<UseContactInformation>(useHttpContactInformation)

/** Bound as an option list for the form: the code is the label; the id is the only identity sent back. */
export const currencyOptionsOf = (currencies: readonly CurrencyOption[]): OrderDraftOptions['currencies'] =>
  currencies.map((c) => ({ currencyId: c.currencyId, label: c.code }))

/**
 * The real-API source: reads the accounts list once per company and the currencies list per business
 * date; `ready` only when nothing is missing (never on main: products), otherwise `unavailable` with the
 * measured reasons and the header the form can already offer.
 */
export const useHttpReferenceOptions: UseReferenceOptions = (companyId, businessDate) => {
  const accounts = useHttpAccountOptions(companyId)
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const currencies = useCurrencies(companyId, businessDate)
  // Until the real accounts answer arrives, or for another company, nothing is offered.
  if (accounts.status === 'idle' || accounts.status === 'loading') return { status: 'unavailable' }
  const missing: MissingReference[] = []
  if (accounts.status === 'unavailable') missing.push({ source: 'accounts', reason: accounts.reason })
  if (currencies.status === 'unavailable') missing.push({ source: 'currencies', reason: currencies.reason })
  missing.push(...NO_LIST_ROUTE)
  if (accounts.status === 'unavailable') return { status: 'unavailable', missing }
  return { status: 'unavailable', missing, header: { accounts: accounts.data, currencies } }
}

export const SalesReferenceSourceContext = createContext<UseReferenceOptions>(useHttpReferenceOptions)
