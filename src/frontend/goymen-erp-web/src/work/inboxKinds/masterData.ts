import { PRICE_LIST_WORK_CAPABILITY } from '../../product/priceLists/capabilities'
import type { WorkInboxKind } from './types'

/**
 * WP7-2: the master data decisions (Organization VKN change, Cari VKN/TCKN change, Product package standard version and price
 * list publication). These existing rows use their standalone Work purposes; price publication retains its Read + Publish authority.
 * Native read-screen paths remain available through their own capabilities.
 */
export const MASTER_DATA_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'organization.company-tax-id-change.v1', module: 'organization', capability: 'organization.company-tax-id-change.work-decision' },
  { kind: 'counterparty.tax-identity-change.v1', module: 'counterparty', capability: 'cari.tax-identity-change.work-decision' },
  { kind: 'counterparty.legal-name-change.v1', module: 'counterparty', capability: 'cari.legal-name-change.work-decision' },
  { kind: 'product.package-standard-version.v1', module: 'product', capability: 'price-lists.package-standard-version.work-decision' },
  { kind: 'product.price-list-version.v1', module: 'product', capability: PRICE_LIST_WORK_CAPABILITY },
]
