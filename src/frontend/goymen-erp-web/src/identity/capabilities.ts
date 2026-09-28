/**
 * Capability ids of "Kullanıcılar ve yetkiler" (AT-004 read-only access view) — a STATEMENT OF INTENT, not a grant.
 *
 * Both mirror `IdentityShellCapabilityProvider` exactly and both stand on ONE server permission,
 * `identity.assignments.read`, company-wide, in one company:
 *
 *   identity.assignments.list <- identity.assignments.read   (SY-01 read subset: users + the selected user's grants)
 *   identity.roles.detail     <- identity.assignments.read   (SY-02 read subset: one role's permissions + holders)
 *
 * Listing an id here grants nothing. `hasCapability` answers false for an id the server did not grant this caller,
 * `CapabilityRoute` then closes the route onto the neutral not-found block, and the nav entry is not drawn. Every
 * request is re-authorized server-side regardless (ERP-000K §10.2).
 *
 * THERE IS NO WRITE ID, deliberately. Assigning, revoking or editing a grant is blocked on a user decision
 * (NEEDS-SINAN #66 5738113623, OPEN-013); the screens must not offer a control whose server does not exist.
 */

export const IDENTITY_CAPABILITY = {
  /** Route `/ayarlar/kullanicilar-ve-yetkiler`. The only entry point; it carries the sidebar label. */
  accessList: 'identity.assignments.list',
  /**
   * Route `/ayarlar/kullanicilar-ve-yetkiler/roller/:roleId`. A separate id from the list for the reason #182 made
   * every detail separate, even though both stand on the same read today: that is a fact about who holds them.
   */
  roleDetail: 'identity.roles.detail',
} as const

export type IdentityCapabilityId = (typeof IDENTITY_CAPABILITY)[keyof typeof IDENTITY_CAPABILITY]

export const IDENTITY_ROUTE: Record<IdentityCapabilityId, string> = {
  [IDENTITY_CAPABILITY.accessList]: '/ayarlar/kullanicilar-ve-yetkiler',
  [IDENTITY_CAPABILITY.roleDetail]: '/ayarlar/kullanicilar-ve-yetkiler/roller/:roleId',
}

/** Nav label: the list gets one; the role card is reached from a row, never from the sidebar. */
export const IDENTITY_NAV_LABEL: Record<IdentityCapabilityId, string> = {
  [IDENTITY_CAPABILITY.accessList]: 'Kullanıcılar ve yetkiler',
  [IDENTITY_CAPABILITY.roleDetail]: '',
}

/** The URL parameter that carries the selected user, so back, reload and reopen show the same selection. */
export const SELECTED_USER_PARAM = 'kullanici'

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

/** Only an id this surface could have emitted is ever honoured or sent; anything else is not a record here. */
export function isWellFormedId(value: string | null | undefined): value is string {
  return typeof value === 'string' && UUID.test(value)
}

/** The access list, with the selected user when there is a well-formed one. */
export function accessListPath(selectedUserId?: string | null): string {
  const path = IDENTITY_ROUTE[IDENTITY_CAPABILITY.accessList]
  return isWellFormedId(selectedUserId)
    ? `${path}?${SELECTED_USER_PARAM}=${encodeURIComponent(selectedUserId)}`
    : path
}

/** One role's card; carries the selected user onward so its back link returns to the same selection. */
export function roleDetailPath(roleId: string, selectedUserId?: string | null): string {
  const path = `/ayarlar/kullanicilar-ve-yetkiler/roller/${encodeURIComponent(roleId)}`
  return isWellFormedId(selectedUserId)
    ? `${path}?${SELECTED_USER_PARAM}=${encodeURIComponent(selectedUserId)}`
    : path
}
