/**
 * The account-scoped operational contact/address contract — the ONE place in this
 * client that names a property of it.
 *
 * SOURCE, pinned: Backend #236 at `9df7f40e3aefc5b02ef42a378c0d1aa800a2fe29` —
 * `docs/features/CARI_CONTACT_INFORMATION_BACKEND_HANDOFF_v1.md` with the DTO
 * (`Contracts/CounterpartyContactInformation.cs`) and the checked-in OpenAPI. Backend is
 * the single schema owner; this file consumes that and asserts nothing of its own.
 *
 * That commit is also the dependency a real acceptance binds to. The DTO and OpenAPI
 * SHAPE is unchanged from the earlier `a6efdc0`, which is why nothing structural here
 * moved with the pin; what changed upstream is that raw control characters are now
 * refused BEFORE the trim, because a CR or LF that vanished in a trim was slipping past
 * validation entirely. `checkField` refuses an interior control character for the same
 * reason — see `hasControlCharacters`.
 *
 * STILL NOT RUNTIME EVIDENCE. That source is ready; its final CI, its two independent
 * reviews and a real environment are not. Nothing here is evidence that the endpoint has
 * been accepted — no live journey has run against it.
 *
 * The limits below are that handoff's exact field matrix, which explicitly REPLACES
 * the earlier Business draft's suggestions — so a superseded number must not survive
 * here, or the client rejects values the contract accepts. `FieldRule.maxLength` stays
 * optional because a field the API owner has not published a maximum for gets no
 * client-side length check at all: the server's own validation decides, and nothing is
 * shortened to fit a number we guessed.
 *
 * READ IS PERMISSIVE ABOUT VALUES, STRICT ABOUT STRUCTURE. `purpose` and `kind` are
 * closed sets for a WRITE, and the decoder still accepts any non-empty string for
 * them: if the server adds a fourth address purpose, a card that refused the whole
 * response would show an operator `malformed-response` instead of their data. That
 * was the exact shape of the `roles = 0` regression. What the decoder does refuse is
 * a missing field, a wrong type, a foreign company and a lossy version token.
 */

import { isBoundUuid, isLosslessVersion, isNonEmptyString } from './wireGuards'
import type { ChangesSpec, FieldRule } from './contactEditModel'

/** `GET/PUT /api/v1/counterparties/{partyId}/contact-information`. */
export function contactInformationPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/contact-information`
}

export interface OperationalChannel {
  readonly id: string
  /** Informational: the graph is fenced by the ACCOUNT version, not by this. */
  readonly version: string
  readonly isActive: boolean
  readonly kind: string
  readonly value: string
  readonly extension: string | null
}

export interface OperationalContact {
  readonly id: string
  readonly version: string
  readonly isActive: boolean
  readonly displayName: string
  readonly jobTitle: string | null
  readonly purpose: string
  readonly channels: readonly OperationalChannel[]
}

export interface OperationalAddress {
  readonly id: string
  readonly version: string
  readonly isActive: boolean
  readonly purpose: string
  readonly countryCode: string
  readonly line1: string
  readonly line2: string | null
  readonly city: string | null
  readonly district: string | null
  readonly postalCode: string | null
}

export interface OperationalContactInformation {
  readonly phone: string | null
  readonly phoneExtension: string | null
  readonly email: string | null
  readonly website: string | null
  readonly addresses: readonly OperationalAddress[]
  readonly contacts: readonly OperationalContact[]
}

export interface ContactInformationView {
  readonly partyId: string
  readonly accountId: string
  readonly companyId: string
  /**
   * The account version. This is the ONE fence for the whole graph — there is no
   * per-child expected version — and it is a canonical Int64 decimal STRING, not a
   * SQL rowversion byte array (1..9223372036854775807, no leading zero, sign,
   * fraction, exponent or whitespace). Carried verbatim and never parsed: past 2^53 a JSON
   * number rounds, and a rounded concurrency token passes checks it should fail.
   */
  readonly version: string
  readonly isActive: boolean
  readonly information: OperationalContactInformation
}

function nullableString(value: unknown): string | null | undefined {
  if (value === null) return null
  if (typeof value === 'string') return value
  return undefined
}

function decodeChannel(raw: unknown): OperationalChannel | null {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return null
  const c = raw as Record<string, unknown>
  if (!isBoundUuid(c.id) || !isLosslessVersion(c.version)) return null
  if (typeof c.isActive !== 'boolean') return null
  if (!isNonEmptyString(c.kind, 200) || !isNonEmptyString(c.value, 4096)) return null
  const extension = nullableString(c.extension)
  if (extension === undefined) return null
  return { id: c.id, version: c.version, isActive: c.isActive, kind: c.kind, value: c.value, extension }
}

function decodeContact(raw: unknown): OperationalContact | null {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return null
  const c = raw as Record<string, unknown>
  if (!isBoundUuid(c.id) || !isLosslessVersion(c.version)) return null
  if (typeof c.isActive !== 'boolean') return null
  if (!isNonEmptyString(c.displayName, 4096) || !isNonEmptyString(c.purpose, 200)) return null
  const jobTitle = nullableString(c.jobTitle)
  if (jobTitle === undefined) return null
  if (!Array.isArray(c.channels)) return null
  const channels: OperationalChannel[] = []
  for (const item of c.channels) {
    const channel = decodeChannel(item)
    if (channel === null) return null
    channels.push(channel)
  }
  return {
    id: c.id,
    version: c.version,
    isActive: c.isActive,
    displayName: c.displayName,
    jobTitle,
    purpose: c.purpose,
    channels,
  }
}

function decodeAddress(raw: unknown): OperationalAddress | null {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return null
  const a = raw as Record<string, unknown>
  if (!isBoundUuid(a.id) || !isLosslessVersion(a.version)) return null
  if (typeof a.isActive !== 'boolean') return null
  if (!isNonEmptyString(a.purpose, 200)) return null
  if (!isNonEmptyString(a.countryCode, 8)) return null
  if (!isNonEmptyString(a.line1, 4096)) return null
  const line2 = nullableString(a.line2)
  const city = nullableString(a.city)
  const district = nullableString(a.district)
  const postalCode = nullableString(a.postalCode)
  if (line2 === undefined || city === undefined || district === undefined || postalCode === undefined) {
    return null
  }
  return {
    id: a.id,
    version: a.version,
    isActive: a.isActive,
    purpose: a.purpose,
    countryCode: a.countryCode,
    line1: a.line1,
    line2,
    city,
    district,
    postalCode,
  }
}

/**
 * Decode the contact-information response, bound to the active company AND to the
 * Party the screen asked about.
 *
 * Both bindings are refusals, not filters: a body about another company or another
 * Party is not something to render a part of. Built field by field, never spread, so
 * a property the server adds later cannot enter the model by accident.
 */
export function decodeContactInformation(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): ContactInformationView | null {
  if (!isBoundUuid(expected.partyId) || !isBoundUuid(expected.companyId)) return null
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return null
  const v = raw as Record<string, unknown>

  if (!isBoundUuid(v.partyId) || v.partyId !== expected.partyId) return null
  if (!isBoundUuid(v.companyId) || v.companyId !== expected.companyId) return null
  if (!isBoundUuid(v.accountId)) return null
  if (!isLosslessVersion(v.version)) return null
  if (typeof v.isActive !== 'boolean') return null

  const rawInfo: unknown = v.information
  if (typeof rawInfo !== 'object' || rawInfo === null || Array.isArray(rawInfo)) return null
  const i = rawInfo as Record<string, unknown>

  const phone = nullableString(i.phone)
  const phoneExtension = nullableString(i.phoneExtension)
  const email = nullableString(i.email)
  const website = nullableString(i.website)
  if (phone === undefined || phoneExtension === undefined || email === undefined || website === undefined) {
    return null
  }
  if (!Array.isArray(i.addresses) || !Array.isArray(i.contacts)) return null

  const addresses: OperationalAddress[] = []
  for (const item of i.addresses) {
    const address = decodeAddress(item)
    if (address === null) return null
    addresses.push(address)
  }
  const contacts: OperationalContact[] = []
  for (const item of i.contacts) {
    const contact = decodeContact(item)
    if (contact === null) return null
    contacts.push(contact)
  }

  return {
    partyId: v.partyId,
    accountId: v.accountId,
    companyId: v.companyId,
    version: v.version,
    isActive: v.isActive,
    information: { phone, phoneExtension, email, website, addresses, contacts },
  }
}

/**
 * The closed sets the API owner publishes. A value outside one is refused on write.
 *
 * They are also what may be rendered as a CHOICE. Contact `purpose` is deliberately
 * absent: it is open text 1..120, and offering a list for it would promise a
 * vocabulary nobody has agreed.
 */
export const ADDRESS_PURPOSES = ['billing', 'delivery', 'operational'] as const
export const CHANNEL_KINDS = ['phone', 'email'] as const

/** Turkish labels for the closed sets. The wire value is never shown to the user. */
export const ADDRESS_PURPOSE_LABEL: Record<string, string> = {
  billing: 'Fatura',
  delivery: 'Teslim',
  operational: 'Operasyon',
}
export const CHANNEL_KIND_LABEL: Record<string, string> = {
  phone: 'Telefon',
  email: 'E-posta',
}

const basic = (rule: Omit<FieldRule, 'scope'>): FieldRule => ({ scope: 'basic', ...rule })

/**
 * The scalars of the operational contact block. All four are nullable, so all four
 * can be cleared with an explicit `null`.
 *
 * The limits are the pinned exact field matrix, which explicitly REPLACES the earlier
 * Business draft's proposed numbers (phone 32 over 64, address 250 over 500, postal 20
 * over 32, jobTitle 120 over 100).
 */
export const CONTACT_SCALAR_RULES: Record<string, FieldRule> = {
  phone: basic({ required: false, kind: 'phone', maxLength: 32 }),
  phoneExtension: basic({ required: false, kind: 'text', maxLength: 10 }),
  email: basic({ required: false, kind: 'email', maxLength: 254 }),
  website: basic({ required: false, kind: 'url', maxLength: 2048 }),
}

export const ADDRESS_FIELD_RULES: Record<string, FieldRule> = {
  purpose: basic({ required: true, kind: 'text', options: ADDRESS_PURPOSES }),
  // Two uppercase ASCII letters. Syntax only — the contract is explicit that this is
  // NOT an authoritative registry check — and NO default: a pre-filled `TR` would be
  // inventing a foreign counterparty's country.
  countryCode: basic({ required: true, kind: 'country', maxLength: 2 }),
  line1: basic({ required: true, kind: 'text', maxLength: 250 }),
  line2: basic({ required: false, kind: 'text', maxLength: 250 }),
  city: basic({ required: false, kind: 'text', maxLength: 100 }),
  district: basic({ required: false, kind: 'text', maxLength: 100 }),
  postalCode: basic({ required: false, kind: 'text', maxLength: 20 }),
}

export const CONTACT_FIELD_RULES: Record<string, FieldRule> = {
  displayName: basic({ required: true, kind: 'text', maxLength: 200 }),
  jobTitle: basic({ required: false, kind: 'text', maxLength: 120 }),
  /** Free explicit text 1..120, NOT an enum — so it is never offered as a list. */
  purpose: basic({ required: true, kind: 'text', maxLength: 120 }),
}

export const CHANNEL_FIELD_RULES: Record<string, FieldRule> = {
  /**
   * Closed, and IMMUTABLE for an existing UUID. Changing it is refused rather than
   * sent: a phone row silently reread as an e-mail row is a different fact about the
   * counterparty, so the edit has to be a new channel.
   */
  kind: basic({ required: true, kind: 'text', options: CHANNEL_KINDS, immutableOnExisting: true }),
  /** Conditional — see `refineChannelRules`. */
  value: basic({ required: true, kind: 'text' }),
  extension: basic({ required: false, kind: 'text', maxLength: 10 }),
}

/**
 * A channel's value is a phone or a mailbox depending on its own `kind`: 32 characters
 * with the punctuation preserved, or 254 characters that have to parse as one address.
 * Until the kind is chosen neither check applies, and nothing is assumed on the user's
 * behalf.
 */
export function channelValueRule(kind: string): FieldRule {
  if (kind === 'phone') return basic({ required: true, kind: 'phone', maxLength: 32 })
  if (kind === 'email') return basic({ required: true, kind: 'email', maxLength: 254 })
  return CHANNEL_FIELD_RULES.value
}

/**
 * An extension extends a PHONE. On an e-mail channel it has nothing to extend, so any
 * value there is refused instead of being sent and rejected.
 */
export function refineChannelRules(
  values: Readonly<Record<string, string>>,
  base: Readonly<Record<string, FieldRule>>,
): Readonly<Record<string, FieldRule>> {
  const kind = (values.kind ?? '').trim()
  return {
    ...base,
    value: channelValueRule(kind),
    extension: kind === 'phone' ? base.extension : { ...base.extension, forbidden: true },
  }
}

/**
 * The same rule one level up: `phoneExtension` requires `phone`.
 *
 * This is what makes the contract's warning visible instead of dangerous. Clearing the
 * phone while an extension is still filled REFUSES the save and says so on the
 * extension — so the user clears it too and the payload carries both nulls. The
 * alternative the contract warns about is a phone-only change that leaves a retained
 * extension behind with nothing to extend; the alternative we refuse to take is
 * deleting the user's extension for them without showing it.
 */
export function refineScalarRules(
  values: Readonly<Record<string, string>>,
  base: Readonly<Record<string, FieldRule>>,
): Readonly<Record<string, FieldRule>> {
  const hasPhone = (values.phone ?? '').trim().length > 0
  return hasPhone ? base : { ...base, phoneExtension: { ...base.phoneExtension, forbidden: true } }
}

/** Property names and nesting exactly as Backend's `changes` object declares them. */
export const CONTACT_CHANGES_SPEC: ChangesSpec = {
  scalars: CONTACT_SCALAR_RULES,
  refineScalars: refineScalarRules,
  collections: [
    { property: 'addresses', rules: ADDRESS_FIELD_RULES },
    {
      property: 'contacts',
      rules: CONTACT_FIELD_RULES,
      nested: { property: 'channels', rules: CHANNEL_FIELD_RULES, refine: refineChannelRules },
    },
  ],
}
