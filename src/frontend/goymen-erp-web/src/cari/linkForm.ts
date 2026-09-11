/**
 * CARI-LINK submit rules (spec §7.3), framework-independent.
 *
 * `hasFreshSelection` is the caller's statement that the selected Party is present
 * in the CURRENT target-scoped candidate response. A navigation hint or a stale
 * row is not a selection (§7.2), and the workspace derives the flag from the fresh
 * page, never from the id alone.
 */

import type { LinkCompanyAccountPayload } from './commandApi'
import { CARI_LINK_TEXT } from './cariCommandText'
import { ALL_ROLE_FLAGS } from './wireGuards'

export interface LinkFormValues {
  readonly hasFreshSelection: boolean
  readonly roles: number
}

export interface LinkFormErrors {
  readonly selection?: string
  readonly roles?: string
}

export type LinkFormValidation =
  | { readonly ok: true; readonly payload: LinkCompanyAccountPayload }
  | { readonly ok: false; readonly errors: LinkFormErrors }

export function validateLinkForm(values: LinkFormValues): LinkFormValidation {
  const errors: { -readonly [K in keyof LinkFormErrors]: LinkFormErrors[K] } = {}
  if (!values.hasFreshSelection) errors.selection = CARI_LINK_TEXT.validation.selection
  if (values.roles < 1 || values.roles > ALL_ROLE_FLAGS || (values.roles & ~ALL_ROLE_FLAGS) !== 0) {
    errors.roles = CARI_LINK_TEXT.validation.roles
  }
  if (Object.keys(errors).length > 0) return { ok: false, errors }
  // Roles only. No reason, no company, no version, no copy of the source roles.
  return { ok: true, payload: { roles: values.roles } }
}
