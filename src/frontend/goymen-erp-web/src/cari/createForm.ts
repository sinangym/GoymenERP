/**
 * C-02A form rules (spec §3.1), framework-independent so they can be tested and
 * reused without a DOM. The backend is the authority; this is early feedback that
 * mirrors it field for field and nothing more.
 */

import { PARTY_KIND, type PartyKind } from './contract'
import type { CreateCounterpartyPayload } from './commandApi'
import { CARI_CREATE_TEXT } from './cariCommandText'
import { ALL_ROLE_FLAGS } from './wireGuards'

export const LEGAL_NAME_MAX = 250
export const TRADE_NAME_MAX = 200

export interface CreateFormValues {
  readonly kind: '' | PartyKind
  readonly legalName: string
  readonly tradeName: string
  readonly roles: number
}

export interface CreateFormErrors {
  readonly kind?: string
  readonly legalName?: string
  readonly tradeName?: string
  readonly roles?: string
}

export type CreateFormValidation =
  | { readonly ok: true; readonly payload: CreateCounterpartyPayload }
  | { readonly ok: false; readonly errors: CreateFormErrors }

export const EMPTY_CREATE_FORM: CreateFormValues = { kind: '', legalName: '', tradeName: '', roles: 0 }

export function isCreateFormPristine(values: CreateFormValues): boolean {
  return (
    values.kind === '' && values.legalName === '' && values.tradeName === '' && values.roles === 0
  )
}

/**
 * Validate and normalize. Trim is applied exactly as the server applies it; blank
 * trade name becomes `null`; roles must carry at least one known bit and nothing
 * outside the flag range.
 */
export function validateCreateForm(values: CreateFormValues): CreateFormValidation {
  const errors: { -readonly [K in keyof CreateFormErrors]: CreateFormErrors[K] } = {}

  if (values.kind !== PARTY_KIND.LegalEntity && values.kind !== PARTY_KIND.NaturalPerson) {
    errors.kind = CARI_CREATE_TEXT.validation.kind
  }

  const legalName = values.legalName.trim()
  if (legalName.length === 0) errors.legalName = CARI_CREATE_TEXT.validation.legalNameRequired
  else if (legalName.length > LEGAL_NAME_MAX) errors.legalName = CARI_CREATE_TEXT.validation.legalNameTooLong

  const tradeName = values.tradeName.trim()
  if (tradeName.length > TRADE_NAME_MAX) errors.tradeName = CARI_CREATE_TEXT.validation.tradeNameTooLong

  if (values.roles < 1 || values.roles > ALL_ROLE_FLAGS || (values.roles & ~ALL_ROLE_FLAGS) !== 0) {
    errors.roles = CARI_CREATE_TEXT.validation.roles
  }

  if (Object.keys(errors).length > 0) return { ok: false, errors }

  return {
    ok: true,
    payload: {
      kind: values.kind as PartyKind,
      legalName,
      tradeName: tradeName.length === 0 ? null : tradeName,
      roles: values.roles,
    },
  }
}
