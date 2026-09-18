import { createContext, useEffect, useState } from 'react'
import { fetchAccountOptions, type AccountOptionsOutcome } from './api'
import type { OrderDraftOptions } from './orderDraftForm'

/**
 * Where the create form's selection options come from. `ready` only when every reference the
 * form needs (accounts, currencies, products with their order units) is offered by its real owner;
 * otherwise `unavailable` names what is missing and why, so the screen withholds the form with an
 * honest reason (never a guessed option, never a default).
 */
export type ReferenceSourceName = 'accounts' | 'currencies' | 'products'

export type MissingReference = {
  readonly source: ReferenceSourceName
  /** `no-list-route`: the owner offers no list on main; `http-<status>[:<code>]`, `network`, `malformed`: the real answer. */
  readonly reason: string
}

export type ReferenceSourceState =
  | { readonly status: 'unavailable'; readonly missing?: readonly MissingReference[] }
  | { readonly status: 'ready'; readonly options: OrderDraftOptions }

export type UseReferenceOptions = (companyId: string) => ReferenceSourceState

export const unavailableReferenceSource: UseReferenceOptions = () => ({ status: 'unavailable' })

/**
 * On main the Sales reference routes are: accounts = a LIST; currency and product = VALIDATORS of
 * one chosen id for a date. The form needs lists for all three, so with the real API the source is
 * `ready` for none of them yet: the accounts list is read for real (its answer is measured and
 * shown), currencies and products are `no-list-route` until their owners publish a list
 * (BROWSER-CI v2b). Nothing here invents an option.
 */
export const NO_LIST_ROUTE: readonly MissingReference[] = [
  { source: 'currencies', reason: 'no-list-route' },
  { source: 'products', reason: 'no-list-route' },
]

function accountsMissing(outcome: AccountOptionsOutcome): MissingReference | null {
  switch (outcome.type) {
    case 'ready':
      return null
    case 'http':
      return { source: 'accounts', reason: `http-${outcome.status}${outcome.code === null ? '' : `:${outcome.code}`}` }
    case 'network':
      return { source: 'accounts', reason: 'network' }
    case 'malformed':
      return { source: 'accounts', reason: 'malformed' }
  }
}

/** The real-API source: reads the accounts list once per company; `ready` only when nothing is missing. */
export const useHttpReferenceOptions: UseReferenceOptions = (companyId) => {
  const [state, setState] = useState<{ readonly companyId: string; readonly value: ReferenceSourceState } | null>(null)
  useEffect(() => {
    if (companyId === '') return
    const controller = new AbortController()
    fetchAccountOptions(companyId, controller.signal).then((outcome) => {
      if (controller.signal.aborted) return
      const missing = [...NO_LIST_ROUTE]
      const accounts = accountsMissing(outcome)
      if (accounts !== null) missing.unshift(accounts)
      const value: ReferenceSourceState =
        missing.length === 0 && outcome.type === 'ready'
          ? { status: 'ready', options: { accounts: outcome.accounts, currencies: [], products: [] } }
          : { status: 'unavailable', missing }
      setState({ companyId, value })
    })
    return () => controller.abort()
  }, [companyId])
  // Until the real answer arrives, or for another company, nothing is offered.
  return state !== null && state.companyId === companyId ? state.value : { status: 'unavailable' }
}

/**
 * The running app binds the real source by default (S03); tests and isolated screens provide their
 * own through the context. A provided source always wins over the default.
 */
export const SalesReferenceSourceContext = createContext<UseReferenceOptions>(useHttpReferenceOptions)
