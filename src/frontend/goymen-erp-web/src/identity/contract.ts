/**
 * Identity access-view wire contract — runtime decoders (AT-004, read-only).
 *
 * Read off the lane's own backend (`IdentityEndpoints.MapAccessView`, `IdentityAccessReadContracts.cs`):
 *
 *   GET /api/v1/identity/access/users             -> IdentityAccessUserSummary[]      (unpaged)
 *   GET /api/v1/identity/access/users/{userId}    -> IdentityAccessUserDetail | 404   (code-less)
 *   GET /api/v1/identity/access/roles             -> IdentityAccessRoleSummary[]      (unpaged)
 *   GET /api/v1/identity/access/roles/{roleId}    -> IdentityAccessRoleDetail | 404   (code-less)
 *
 * Every decoder fails CLOSED: one bad row invalidates the whole unpaged list (a list one row short cannot be told
 * apart from a correct one), and a detail must be the record the route asked for. Unknown `validity` and scope
 * `type` tokens are rejected rather than guessed — on an authority view a misread state is worse than no view.
 *
 * What the contract deliberately does NOT carry: e-mail, issuer, external subject. There is nothing to decode for
 * them and the decoders never pass through extra fields.
 */

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID.test(value)
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isCount(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

/** An ISO-8601 instant with an explicit offset, as the server emits `DateTimeOffset`. */
function isInstant(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/.test(value) &&
    !Number.isNaN(Date.parse(value))
  )
}

export const VALIDITY = ['effective', 'scheduled', 'ended', 'revoked', 'inactive'] as const
export type Validity = (typeof VALIDITY)[number]

export const SCOPE_TYPE = ['Company', 'Site', 'Location', 'WorkCenter'] as const
export type ScopeType = (typeof SCOPE_TYPE)[number]

export interface AccessScope {
  readonly type: ScopeType
  readonly id: string
}

export interface AccessPerson {
  readonly userId: string
  readonly displayName: string
}

export interface AccessUserSummary {
  readonly userId: string
  readonly displayName: string
  readonly isActive: boolean
  readonly assignmentCount: number
  readonly effectiveAssignmentCount: number
}

export interface AccessAssignment {
  readonly assignmentId: string
  readonly roleId: string
  readonly roleCode: string
  readonly roleName: string
  readonly roleIsActive: boolean
  /** Raw stored status (Active / Revoked / Expired); `validity` is what the screen shows. */
  readonly status: string
  readonly validity: Validity
  readonly validFromUtc: string
  readonly validUntilUtc: string | null
  readonly createdAtUtc: string
  readonly approvedBy: AccessPerson | null
  readonly scopes: readonly AccessScope[]
}

export interface AccessEffectivePermission {
  readonly code: string
  readonly isCritical: boolean
  readonly roleCodes: readonly string[]
}

export interface AccessUserDetail {
  readonly userId: string
  readonly displayName: string
  readonly isActive: boolean
  readonly assignments: readonly AccessAssignment[]
  readonly effectivePermissions: readonly AccessEffectivePermission[]
}

export interface AccessPermission {
  readonly code: string
  readonly isCritical: boolean
}

export interface AccessRoleSummary {
  readonly roleId: string
  readonly code: string
  readonly name: string
  readonly isSystem: boolean
  readonly isActive: boolean
  readonly permissionCount: number
  readonly holderCount: number
  readonly effectiveHolderCount: number
}

export interface AccessRoleHolder {
  readonly assignmentId: string
  readonly userId: string
  readonly displayName: string
  readonly userIsActive: boolean
  readonly status: string
  readonly validity: Validity
  readonly validFromUtc: string
  readonly validUntilUtc: string | null
  readonly approvedBy: AccessPerson | null
  readonly scopes: readonly AccessScope[]
}

export interface AccessRoleDetail {
  readonly roleId: string
  readonly code: string
  readonly name: string
  readonly isSystem: boolean
  readonly isActive: boolean
  readonly permissions: readonly AccessPermission[]
  readonly holders: readonly AccessRoleHolder[]
}

function decodeArray<T>(raw: unknown, decode: (entry: unknown) => T | null): readonly T[] | null {
  if (!Array.isArray(raw)) return null
  const items: T[] = []
  for (const entry of raw) {
    const item = decode(entry)
    if (item === null) return null
    items.push(item)
  }
  return items
}

function decodeValidity(value: unknown): Validity | null {
  return (VALIDITY as readonly unknown[]).includes(value) ? (value as Validity) : null
}

function decodeScope(raw: unknown): AccessScope | null {
  if (!isRecord(raw)) return null
  const { type, id } = raw
  if (!(SCOPE_TYPE as readonly unknown[]).includes(type) || !isUuid(id)) return null
  return { type: type as ScopeType, id }
}

function decodePerson(raw: unknown): AccessPerson | null | undefined {
  if (raw === null) return null
  if (!isRecord(raw)) return undefined
  const { userId, displayName } = raw
  if (!isUuid(userId) || !isNonEmptyString(displayName)) return undefined
  return { userId, displayName }
}

function decodeUntil(value: unknown): string | null | undefined {
  if (value === null) return null
  return isInstant(value) ? value : undefined
}

export function decodeUserSummary(raw: unknown): AccessUserSummary | null {
  if (!isRecord(raw)) return null
  const { userId, displayName, isActive, assignmentCount, effectiveAssignmentCount } = raw
  if (
    !isUuid(userId) ||
    !isNonEmptyString(displayName) ||
    typeof isActive !== 'boolean' ||
    !isCount(assignmentCount) ||
    !isCount(effectiveAssignmentCount) ||
    effectiveAssignmentCount > assignmentCount
  ) {
    return null
  }
  return { userId, displayName, isActive, assignmentCount, effectiveAssignmentCount }
}

export function decodeUserSummaries(raw: unknown): readonly AccessUserSummary[] | null {
  return decodeArray(raw, decodeUserSummary)
}

function decodeAssignment(raw: unknown): AccessAssignment | null {
  if (!isRecord(raw)) return null
  const validity = decodeValidity(raw.validity)
  const approvedBy = decodePerson(raw.approvedBy)
  const validUntilUtc = decodeUntil(raw.validUntilUtc)
  const scopes = decodeArray(raw.scopes, decodeScope)
  const {
    assignmentId,
    roleId,
    roleCode,
    roleName,
    roleIsActive,
    status,
    validFromUtc,
    createdAtUtc,
  } = raw
  if (
    !isUuid(assignmentId) ||
    !isUuid(roleId) ||
    !isNonEmptyString(roleCode) ||
    !isNonEmptyString(roleName) ||
    typeof roleIsActive !== 'boolean' ||
    !isNonEmptyString(status) ||
    validity === null ||
    !isInstant(validFromUtc) ||
    validUntilUtc === undefined ||
    !isInstant(createdAtUtc) ||
    approvedBy === undefined ||
    scopes === null
  ) {
    return null
  }
  return {
    assignmentId,
    roleId,
    roleCode,
    roleName,
    roleIsActive,
    status,
    validity,
    validFromUtc,
    validUntilUtc,
    createdAtUtc,
    approvedBy,
    scopes,
  }
}

function decodeEffectivePermission(raw: unknown): AccessEffectivePermission | null {
  if (!isRecord(raw)) return null
  const { code, isCritical } = raw
  const roleCodes = decodeArray(raw.roleCodes, (entry) => (isNonEmptyString(entry) ? entry : null))
  if (!isNonEmptyString(code) || typeof isCritical !== 'boolean' || roleCodes === null || roleCodes.length === 0) {
    return null
  }
  return { code, isCritical, roleCodes }
}

/** The user card; it must be the user the route asked for, or it is malformed. */
export function decodeUserDetail(raw: unknown, requestedUserId: string): AccessUserDetail | null {
  if (!isRecord(raw)) return null
  const { userId, displayName, isActive } = raw
  const assignments = decodeArray(raw.assignments, decodeAssignment)
  const effectivePermissions = decodeArray(raw.effectivePermissions, decodeEffectivePermission)
  if (
    !isUuid(userId) ||
    userId.toLowerCase() !== requestedUserId.toLowerCase() ||
    !isNonEmptyString(displayName) ||
    typeof isActive !== 'boolean' ||
    assignments === null ||
    assignments.length === 0 ||
    effectivePermissions === null
  ) {
    return null
  }
  return { userId, displayName, isActive, assignments, effectivePermissions }
}

export function decodeRoleSummary(raw: unknown): AccessRoleSummary | null {
  if (!isRecord(raw)) return null
  const { roleId, code, name, isSystem, isActive, permissionCount, holderCount, effectiveHolderCount } = raw
  if (
    !isUuid(roleId) ||
    !isNonEmptyString(code) ||
    !isNonEmptyString(name) ||
    typeof isSystem !== 'boolean' ||
    typeof isActive !== 'boolean' ||
    !isCount(permissionCount) ||
    !isCount(holderCount) ||
    !isCount(effectiveHolderCount) ||
    effectiveHolderCount > holderCount
  ) {
    return null
  }
  return { roleId, code, name, isSystem, isActive, permissionCount, holderCount, effectiveHolderCount }
}

export function decodeRoleSummaries(raw: unknown): readonly AccessRoleSummary[] | null {
  return decodeArray(raw, decodeRoleSummary)
}

function decodePermission(raw: unknown): AccessPermission | null {
  if (!isRecord(raw)) return null
  const { code, isCritical } = raw
  if (!isNonEmptyString(code) || typeof isCritical !== 'boolean') return null
  return { code, isCritical }
}

function decodeHolder(raw: unknown): AccessRoleHolder | null {
  if (!isRecord(raw)) return null
  const validity = decodeValidity(raw.validity)
  const approvedBy = decodePerson(raw.approvedBy)
  const validUntilUtc = decodeUntil(raw.validUntilUtc)
  const scopes = decodeArray(raw.scopes, decodeScope)
  const { assignmentId, userId, displayName, userIsActive, status, validFromUtc } = raw
  if (
    !isUuid(assignmentId) ||
    !isUuid(userId) ||
    !isNonEmptyString(displayName) ||
    typeof userIsActive !== 'boolean' ||
    !isNonEmptyString(status) ||
    validity === null ||
    !isInstant(validFromUtc) ||
    validUntilUtc === undefined ||
    approvedBy === undefined ||
    scopes === null
  ) {
    return null
  }
  return {
    assignmentId,
    userId,
    displayName,
    userIsActive,
    status,
    validity,
    validFromUtc,
    validUntilUtc,
    approvedBy,
    scopes,
  }
}

/** The role card; it must be the role the route asked for, or it is malformed. Holders may be empty. */
export function decodeRoleDetail(raw: unknown, requestedRoleId: string): AccessRoleDetail | null {
  if (!isRecord(raw)) return null
  const { roleId, code, name, isSystem, isActive } = raw
  const permissions = decodeArray(raw.permissions, decodePermission)
  const holders = decodeArray(raw.holders, decodeHolder)
  if (
    !isUuid(roleId) ||
    roleId.toLowerCase() !== requestedRoleId.toLowerCase() ||
    !isNonEmptyString(code) ||
    !isNonEmptyString(name) ||
    typeof isSystem !== 'boolean' ||
    typeof isActive !== 'boolean' ||
    permissions === null ||
    holders === null
  ) {
    return null
  }
  return { roleId, code, name, isSystem, isActive, permissions, holders }
}
