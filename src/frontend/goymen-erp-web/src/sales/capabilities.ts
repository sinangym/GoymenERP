/**
 * Sales screen mount ids and routes.
 *
 * STATUS: PROPOSED — NOT PUBLISHED. These ids are the frontend's mount keys for the
 * three screens, split the way every other lane splits them (a list grant must not
 * imply a detail or a create grant). They are NOT permissions and grant nothing:
 * #257 states that no Sales permission name exists yet and that absent mappings
 * default deny, and no backend shell provider publishes a `sales.*` capability. So
 * `CapabilityRoute` closes every Sales route onto the neutral not-found block in the
 * running app. That is the intended state until the central owner publishes the real
 * ids — at which point THIS file is renamed to match, and the mount handoff is sent.
 */

export const SALES_CAPABILITY = {
  /** S-02 sipariş taslakları listesi. */
  draftsList: 'sales.drafts.list',
  /** S-04 Sipariş 360 — draft detail. */
  draftsDetail: 'sales.drafts.detail',
  /** S-03 yeni satış siparişi taslağı. */
  draftsCreate: 'sales.drafts.create',
} as const

export type SalesCapabilityId = (typeof SALES_CAPABILITY)[keyof typeof SALES_CAPABILITY]

export const SALES_ROUTE = {
  list: '/satis/siparisler',
  // Static before the parameter route: `yeni` must never be read as a draft id.
  create: '/satis/siparisler/yeni',
  detail: '/satis/siparisler/:draftId',
} as const

export const salesDetailPath = (draftId: string) => `/satis/siparisler/${encodeURIComponent(draftId)}`
