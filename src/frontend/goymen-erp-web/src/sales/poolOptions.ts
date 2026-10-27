/**
 * The S-01 forms' choices, read from the Sales reference routes the order draft form already uses (accounts, currencies,
 * product candidates — the composed `SalesReferenceSourceContext`, and `SalesCurrencySourceContext` for the currencies of
 * a business date). Nothing is invented: a list that cannot be read is reported with its reason and its choices are not
 * offered.
 *
 * Note: on the server those reference reads are bound to `sales.drafts.create`; an actor holding only the request / quote
 * names gets their 403 here, shown as the reason (reported as a backend gap, not worked around).
 */

import { useContext } from 'react'
import type { OrderDraftOptions } from './orderDraftForm'
import {
  SalesCurrencySourceContext,
  SalesReferenceSourceContext,
  currencyOptionsOf,
  type AccountsOffer,
  type CurrencyReadState,
} from './referenceSource'

export interface PoolOptions {
  /** True until the accounts read has answered. */
  readonly loading: boolean
  readonly accounts: OrderDraftOptions['accounts']
  readonly accountsOffer: AccountsOffer | null
  /** Why the accounts cannot be offered, or null. */
  readonly accountsUnavailable: string | null
  /** The product candidates, or null when they cannot be offered (see `productsUnavailable`). */
  readonly products: OrderDraftOptions['products'] | null
  readonly productsUnavailable: string | null
}

export function usePoolOptions(companyId: string, businessDate: string, accountSearch: string, productSearch: string): PoolOptions {
  const useOptions = useContext(SalesReferenceSourceContext)
  const source = useOptions(companyId, businessDate, accountSearch, productSearch)
  if (source.status === 'ready') {
    return {
      loading: false,
      accounts: source.options.accounts,
      accountsOffer: null,
      accountsUnavailable: null,
      products: source.options.products,
      productsUnavailable: null,
    }
  }
  const missing = source.missing ?? []
  const reasonOf = (name: string) => missing.find((entry) => entry.source === name)?.reason ?? null
  const header = source.header
  if (header === undefined) {
    const accountsReason = reasonOf('accounts')
    return {
      loading: accountsReason === null && missing.length === 0,
      accounts: [],
      accountsOffer: null,
      accountsUnavailable: accountsReason ?? (missing.length > 0 ? missing[0].reason : null),
      products: null,
      productsUnavailable: reasonOf('products'),
    }
  }
  const products = header.products
  return {
    loading: false,
    accounts: header.accounts,
    accountsOffer: header.accountsOffer,
    accountsUnavailable: null,
    products: products?.status === 'ready' ? products.data.items : null,
    productsUnavailable:
      products?.status === 'ready' ? null : products?.status === 'unavailable' ? products.reason : reasonOf('products'),
  }
}

/** The currencies usable on the business date (the Settings owner's list, revalidated by Sales). */
export function usePoolCurrencies(companyId: string, businessDate: string): {
  readonly state: CurrencyReadState
  readonly options: OrderDraftOptions['currencies']
} {
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const state = useCurrencies(companyId, businessDate)
  return { state, options: state.status === 'ready' ? currencyOptionsOf(state.data) : [] }
}
