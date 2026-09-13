/**
 * Where the create form's choices come from — and, today, the honest answer that
 * they come from nowhere.
 *
 * The form needs, for the COMMITTED company: offered Party + active company-account
 * pairs, currencies, and usable products with their version and offered order
 * units. #257 maps each to a SERVER-side owner port (authorized Party read + account
 * check, `IProductSalesEligibilityV1`, `ISettingsPrerequisiteProvider`) and says, on
 * purpose, "There is deliberately no fabricated Sales selection endpoint". No HTTP
 * read exists for any of them, and none is invented here.
 *
 * So the default source is `unavailable`, and the create screen fails closed on it:
 * no picker is filled with guessed ids, no product is assumed usable, no unit is
 * assumed kg. Tests supply a controlled source; that is component evidence (NOT_ENV),
 * never proof that an owner offered anything.
 */

import { createContext } from 'react'
import type { OrderDraftOptions } from './orderDraftForm'

export type ReferenceSourceState =
  | { readonly status: 'unavailable' }
  | { readonly status: 'ready'; readonly options: OrderDraftOptions }

/** Resolves the choices for one company. A hook, so a real source can read the server. */
export type UseReferenceOptions = (companyId: string) => ReferenceSourceState

export const unavailableReferenceSource: UseReferenceOptions = () => ({ status: 'unavailable' })

export const SalesReferenceSourceContext = createContext<UseReferenceOptions>(unavailableReferenceSource)
