import { PRICE_LIST_CAPABILITY } from '../../product/priceLists/capabilities'
import type { WorkInboxKind } from './types'

/**
 * WP7-2: the master data decisions (Organization VKN change, Cari VKN/TCKN change, Product package standard version and price
 * list publication). Cari identity decisions use their standalone Work purposes; price publication uses its existing control.
 * Other kinds retain their native screens and do not open the inbox route on their own (`capability: null`).
 */
export const MASTER_DATA_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'organization.company-tax-id-change.v1', module: 'organization', capability: null },
  { kind: 'counterparty.tax-identity-change.v1', module: 'counterparty', capability: 'cari.tax-identity-change.work-decision' },
  { kind: 'counterparty.legal-name-change.v1', module: 'counterparty', capability: 'cari.legal-name-change.work-decision' },
  { kind: 'product.package-standard-version.v1', module: 'product', capability: null },
  { kind: 'product.price-list-version.v1', module: 'product', capability: PRICE_LIST_CAPABILITY.publish },
]
