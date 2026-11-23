import { PRICE_LIST_CAPABILITY } from '../../product/priceLists/capabilities'
import type { WorkInboxKind } from './types'

/**
 * WP7-2: the master data decisions (Organization VKN change, Cari VKN/TCKN change, Product package standard version and price
 * list publication). Only price list publication has its own approve control in the shell snapshot; the others are decided on
 * screens that requesters reach too, so they do not open the inbox route on their own (`capability: null`).
 */
export const MASTER_DATA_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'organization.company-tax-id-change.v1', module: 'organization', capability: null },
  { kind: 'counterparty.tax-identity-change.v1', module: 'counterparty', capability: null },
  { kind: 'counterparty.legal-name-change.v1', module: 'counterparty', capability: null },
  { kind: 'product.package-standard-version.v1', module: 'product', capability: null },
  { kind: 'product.price-list-version.v1', module: 'product', capability: PRICE_LIST_CAPABILITY.publish },
]
