/**
 * FNS-D121 cari IBAN list — wire contract and strict decoders (exact key sets, fail-closed null). IBANs are Confidential
 * personal data (AI-D42); the screen shows them only to a reader the server allowed.
 */
export interface PartyIbanView {
  readonly id: string
  readonly companyId: string
  readonly partyId: string
  readonly iban: string
  readonly bankName: string
  readonly description: string | null
  readonly isActive: boolean
  readonly version: string
  readonly addedBy: string
  readonly addedAtUtc: string
  readonly deactivatedBy: string | null
  readonly deactivatedAtUtc: string | null
  readonly deactivationReason: string | null
}
export interface PartyIbanList { readonly companyId: string; readonly partyId: string; readonly ibans: readonly PartyIbanView[] }
export type PartyIbanOutcome = 'Unavailable' | 'Denied' | 'NotFound' | 'ValidationFailed' | 'VersionConflict' | 'IdempotencyConflict' | 'Succeeded'
export interface PartyIbanCommandResult {
  readonly outcome: PartyIbanOutcome
  readonly resource: PartyIbanView | null
  readonly code: string | null
  readonly replayed: boolean
}

const VIEW_KEYS = ['id', 'companyId', 'partyId', 'iban', 'bankName', 'description', 'isActive', 'version', 'addedBy', 'addedAtUtc',
  'deactivatedBy', 'deactivatedAtUtc', 'deactivationReason'] as const
const OUTCOMES: readonly PartyIbanOutcome[] = ['Unavailable', 'Denied', 'NotFound', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Succeeded']
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const rec = (v: unknown): Record<string, unknown> | null => typeof v === 'object' && v !== null && !Array.isArray(v) ? v as Record<string, unknown> : null
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every((k) => Object.hasOwn(v, k))
const id = (v: unknown): v is string => typeof v === 'string' && UUID.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const text = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.trim() === v
const instant = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && !Number.isNaN(Date.parse(v)) && /[Zz]|[+-]\d{2}:\d{2}$/.test(v)

/** TR + 24 digits with a valid ISO 13616 check (the same rule the server applies; the server stays the authority). */
export function isTurkishIban(value: string): boolean {
  if (!/^TR\d{24}$/.test(value)) return false
  const moved = value.slice(4) + value.slice(0, 4)
  let remainder = 0
  for (const char of moved) {
    const digits = /\d/.test(char) ? char : String(char.charCodeAt(0) - 55)
    for (const digit of digits) remainder = (remainder * 10 + Number(digit)) % 97
  }
  return remainder === 1
}
export const normalizeIban = (value: string) => value.replace(/[\s-]/g, '').toUpperCase()
export const groupIban = (value: string) => value.replace(/(.{4})/g, '$1 ').trim()

export function decodePartyIban(value: unknown, companyId: string, partyId: string): PartyIbanView | null {
  const v = rec(value)
  if (v === null || !exact(v, VIEW_KEYS) || !id(v.id) || v.companyId !== companyId || v.partyId !== partyId
    || typeof v.iban !== 'string' || !isTurkishIban(v.iban) || !text(v.bankName) || (v.description !== null && !text(v.description))
    || typeof v.isActive !== 'boolean' || !text(v.addedBy) || !instant(v.addedAtUtc)) return null
  if (v.isActive ? v.version !== '1' || v.deactivatedBy !== null || v.deactivatedAtUtc !== null || v.deactivationReason !== null
    : v.version !== '2' || !text(v.deactivatedBy) || !instant(v.deactivatedAtUtc) || !text(v.deactivationReason)) return null
  return v as unknown as PartyIbanView
}

export function decodePartyIbanList(value: unknown, companyId: string, partyId: string): PartyIbanList | null {
  const v = rec(value)
  if (v === null || !exact(v, ['companyId', 'partyId', 'ibans']) || v.companyId !== companyId || v.partyId !== partyId
    || !Array.isArray(v.ibans)) return null
  const rows = v.ibans.map((row) => decodePartyIban(row, companyId, partyId))
  if (rows.some((row) => row === null) || new Set(rows.map((row) => row?.id)).size !== rows.length) return null
  return { companyId, partyId, ibans: rows as PartyIbanView[] }
}

export function decodePartyIbanCommandResult(value: unknown, companyId: string, partyId: string): PartyIbanCommandResult | null {
  const v = rec(value)
  if (v === null || !exact(v, ['outcome', 'resource', 'code', 'replayed']) || !OUTCOMES.includes(v.outcome as PartyIbanOutcome)
    || typeof v.replayed !== 'boolean' || (v.code !== null && !text(v.code))) return null
  const resource = v.resource === null ? null : decodePartyIban(v.resource, companyId, partyId)
  if (v.resource !== null && resource === null) return null
  if ((v.outcome === 'Succeeded') !== (resource !== null)) return null
  return { outcome: v.outcome as PartyIbanOutcome, resource, code: v.code as string | null, replayed: v.replayed }
}
