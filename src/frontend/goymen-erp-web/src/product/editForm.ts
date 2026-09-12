/**
 * U-02 Draft BASIC EDIT form rules, pinned to `EditProductDraftRequest` /
 * `ProductDraftChanges`.
 *
 * The one idea this module exists for: an edit sends a DIFF, not a form.
 *
 * The contract models each change member as `Optional<T>` — absent means unchanged,
 * present-and-null clears a nullable scalar reference. `JSON.stringify` drops an
 * `undefined` property, so "absent" is expressed by not setting the key at all.
 * Sending every field on every save would make each untouched input a write, and a
 * colleague's concurrent change to a field this user never looked at would be
 * silently reverted by them.
 *
 * WHAT IS EDITABLE HERE, and what is not:
 *  - `name` and the manual roles. Their vocabularies are fully defined;
 *  - `classId` / `familyId` are in `ProductDraftChanges`, but no read exists that
 *    would let this screen offer a valid value (contract review gap 1). Offering
 *    only "clear" would be a destructive half-control, so neither is offered;
 *  - `references` for the same reason — every member is a UUID into a catalogue
 *    with no read;
 *  - group and code are immutable in basic edit by the contract's own statement.
 */

import { PRODUCT_ROLE, type ProductCard, type ProductRoleName } from './contract'
import type { EditProductDraftPayload, ProductDraftChanges, ProductRoleWrites } from './api'
import { REASON_MAX } from './api'
import { PRODUCT_EDIT_TEXT } from './productText'
import type { CreateFormRoles, RoleChoice } from './createForm'

export const NAME_MAX = 250

export interface EditFormValues {
  readonly name: string
  readonly roles: CreateFormRoles
  /** Required for a real change: one edit is visible in both Göymen companies. */
  readonly reason: string
}

export const EDIT_FIELD = {
  name: 'name',
  roles: 'roles',
  reason: 'reason',
} as const

export type EditFieldId = (typeof EDIT_FIELD)[keyof typeof EDIT_FIELD]

export const EDIT_FIELD_ORDER: readonly EditFieldId[] = [
  EDIT_FIELD.name,
  EDIT_FIELD.roles,
  EDIT_FIELD.reason,
]

export const EDIT_FIELD_LABEL: Record<EditFieldId, string> = {
  [EDIT_FIELD.name]: PRODUCT_EDIT_TEXT.nameLabel,
  [EDIT_FIELD.roles]: PRODUCT_EDIT_TEXT.rolesLabel,
  [EDIT_FIELD.reason]: PRODUCT_EDIT_TEXT.reasonLabel,
}

export type EditFormErrors = Partial<Record<EditFieldId, string>>

export type EditFormValidation =
  | { readonly ok: true; readonly payload: EditProductDraftPayload }
  | { readonly ok: false; readonly errors: EditFormErrors }

/**
 * A role as the record holds it, in the picker's vocabulary.
 *
 * An unevaluated role seeds as `unset`, not `no` — otherwise merely opening the
 * form and saving an unrelated field would turn every unevaluated role into a
 * determined "no", which is the §5.2 silent persist arriving through the back door.
 */
export function roleChoiceOf(card: ProductCard, role: ProductRoleName): RoleChoice {
  const state = card.product.roles[role]
  if (state.provenance === 'not-evaluated' || state.value === null) return 'unset'
  return state.value ? 'yes' : 'no'
}

/**
 * Whether a role may be edited by hand.
 *
 * The contract makes `derived` server-only and `PreserveDerived` rejects an attempt
 * to overwrite one. A manual value on top of a computed fact would leave the two
 * disagreeing with no way to tell which is current.
 */
export function isRoleEditable(card: ProductCard, role: ProductRoleName): boolean {
  return card.product.roles[role].provenance !== 'derived'
}

/** Seed the form from the card as read. */
export function editFormFrom(card: ProductCard): EditFormValues {
  const roles = {} as Record<ProductRoleName, RoleChoice>
  for (const role of PRODUCT_ROLE) roles[role] = roleChoiceOf(card, role)
  return {
    name: card.product.name,
    roles,
    // Never seeded: a reason belongs to THIS change, and a carried-over one would
    // attach last week's justification to today's edit.
    reason: '',
  }
}

/**
 * The members that actually differ from the card as read.
 *
 * Derived roles are never included. A role returning to `unset` is not expressible
 * and is therefore not sent: `not-evaluated` is the server's statement that it has
 * not determined the role, and a person cannot un-determine something they decided.
 * Omitting leaves the stored value alone, which is honest; the alternative would be
 * inventing a clear-to-unevaluated operation the contract does not define.
 */
export function changesFrom(card: ProductCard, values: EditFormValues): ProductDraftChanges {
  const changes: { name?: string; roles?: ProductRoleWrites } = {}

  const name = values.name.trim()
  if (name !== card.product.name) changes.name = name

  const roles: Record<string, { value: boolean; provenance: 'manual' }> = {}
  for (const role of PRODUCT_ROLE) {
    if (!isRoleEditable(card, role)) continue
    const choice = values.roles[role]
    if (choice === roleChoiceOf(card, role)) continue
    if (choice === 'yes') roles[role] = { value: true, provenance: 'manual' }
    else if (choice === 'no') roles[role] = { value: false, provenance: 'manual' }
  }
  if (Object.keys(roles).length > 0) changes.roles = roles as ProductRoleWrites

  return changes
}

/** Whether anything at all would be written. */
export function hasChanges(card: ProductCard, values: EditFormValues): boolean {
  return Object.keys(changesFrom(card, values)).length > 0
}

/**
 * Whether the user has unsaved work. The reason alone does NOT count: typing a
 * justification and then reverting the field leaves nothing to save, and prompting
 * about it would train people to dismiss the guard.
 */
export function isEditDirty(card: ProductCard, values: EditFormValues): boolean {
  return hasChanges(card, values)
}

export function validateEditForm(
  card: ProductCard,
  values: EditFormValues,
): EditFormValidation {
  const errors: EditFormErrors = {}

  const name = values.name.trim()
  if (name.length === 0) errors.name = PRODUCT_EDIT_TEXT.validation.nameRequired
  else if (name.length > NAME_MAX) errors.name = PRODUCT_EDIT_TEXT.validation.nameTooLong

  const changes = changesFrom(card, values)
  const nothingToSave = Object.keys(changes).length === 0

  /**
   * A reason is required only when something is actually being written.
   *
   * The contract ties the reason to a CHANGE; demanding one for a save that writes
   * nothing would be a rule about the form rather than about the record. The empty
   * save is refused on its own terms instead.
   */
  if (nothingToSave) {
    errors.name = errors.name ?? PRODUCT_EDIT_TEXT.validation.nothingChanged
  } else {
    const reason = values.reason.trim()
    if (reason.length === 0) errors.reason = PRODUCT_EDIT_TEXT.validation.reasonRequired
    else if (reason.length > REASON_MAX) {
      errors.reason = PRODUCT_EDIT_TEXT.validation.reasonTooLong
    }
  }

  if (Object.keys(errors).length > 0) return { ok: false, errors }

  return {
    ok: true,
    payload: {
      // Carried verbatim from the card as READ — never from a conflict response,
      // and never parsed into a number.
      expectedVersion: card.product.version,
      changes,
      reason: values.reason.trim(),
    },
  }
}

/** Merge server field errors over client ones. Same rule as the create form. */
export function mergeServerEditErrors(
  clientErrors: EditFormErrors,
  serverErrors: Readonly<Record<string, string>>,
): EditFormErrors {
  const merged: EditFormErrors = { ...clientErrors }
  for (const field of EDIT_FIELD_ORDER) {
    const message = serverErrors[field]
    if (message !== undefined) merged[field] = message
  }
  return merged
}
