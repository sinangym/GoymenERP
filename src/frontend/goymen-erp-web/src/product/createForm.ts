/**
 * U-02 Draft create form rules (JIT spec §10), framework-independent so they can be
 * tested and reused without a DOM.
 *
 * §10.3: "Server validation tek gerçektir. Client aynı temel kontrolleri erken geri
 * bildirim için yapabilir fakat server sonucunu ezmez." So everything below mirrors
 * a bound the server will also enforce, and nothing below is a rule of its own.
 */

import { PRODUCT_ROLE, type ProductRoleName } from './contract'
import type { CreateProductDraftPayload } from './api'
import { PRODUCT_CREATE_TEXT } from './productText'

export const NAME_MAX = 250
export const CLASS_MAX = 120
export const GROUP_MAX = 120
export const FAMILY_MAX = 120
export const TRACKING_POLICY_MAX = 120

/**
 * A role the user has not answered yet.
 *
 * §5.2: an unevaluated role is never persisted as `false`. That rule only survives
 * if the FORM can represent "not answered" as a third value — a checkbox cannot,
 * because an unticked box and a deliberate "no" are the same state. So the picker
 * is tri-state and `'unset'` is its initial value.
 */
export type RoleChoice = 'yes' | 'no' | 'unset'

export type CreateFormRoles = Readonly<Record<ProductRoleName, RoleChoice>>

export interface CreateFormValues {
  readonly name: string
  readonly productClass: string
  readonly productGroup: string
  readonly productFamily: string
  readonly roles: CreateFormRoles
  readonly trackingPolicy: string
}

/**
 * Field ids, shared by the validator, the inputs and the server-error mapper.
 *
 * §10.3 requires a field-level error to be programmatically associated with its
 * input. That only holds if all three agree on one set of names — a server error
 * naming a field the form does not render would otherwise attach a message to
 * nothing, which the user can neither see nor clear.
 */
export const CREATE_FIELD = {
  name: 'name',
  productClass: 'productClass',
  productGroup: 'productGroup',
  productFamily: 'productFamily',
  trackingPolicy: 'trackingPolicy',
} as const

export type CreateFieldId = (typeof CREATE_FIELD)[keyof typeof CREATE_FIELD]

/** Declaration order is the DOM order, so "jump to the first error" means the first. */
export const CREATE_FIELD_ORDER: readonly CreateFieldId[] = [
  CREATE_FIELD.name,
  CREATE_FIELD.productClass,
  CREATE_FIELD.productGroup,
  CREATE_FIELD.productFamily,
  CREATE_FIELD.trackingPolicy,
]

export const CREATE_FIELD_LABEL: Record<CreateFieldId, string> = {
  [CREATE_FIELD.name]: PRODUCT_CREATE_TEXT.nameLabel,
  [CREATE_FIELD.productClass]: PRODUCT_CREATE_TEXT.classLabel,
  [CREATE_FIELD.productGroup]: PRODUCT_CREATE_TEXT.groupLabel,
  [CREATE_FIELD.productFamily]: PRODUCT_CREATE_TEXT.familyLabel,
  [CREATE_FIELD.trackingPolicy]: PRODUCT_CREATE_TEXT.trackingPolicyLabel,
}

export type CreateFormErrors = Partial<Record<CreateFieldId, string>>

export type CreateFormValidation =
  | { readonly ok: true; readonly payload: CreateProductDraftPayload }
  | { readonly ok: false; readonly errors: CreateFormErrors }

const EMPTY_ROLES: CreateFormRoles = {
  purchasable: 'unset',
  sellable: 'unset',
  productionInput: 'unset',
  productionOutput: 'unset',
}

export const EMPTY_CREATE_FORM: CreateFormValues = {
  name: '',
  productClass: '',
  productGroup: '',
  productFamily: '',
  roles: EMPTY_ROLES,
  trackingPolicy: '',
}

/** Whether the user has touched anything. Drives the §10.4 unsaved guard. */
export function isCreateFormPristine(values: CreateFormValues): boolean {
  return (
    values.name === '' &&
    values.productClass === '' &&
    values.productGroup === '' &&
    values.productFamily === '' &&
    values.trackingPolicy === '' &&
    PRODUCT_ROLE.every((role) => values.roles[role] === 'unset')
  )
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length === 0 ? null : trimmed
}

/**
 * Validate and normalize.
 *
 * Trim is applied exactly as §8.1 says the server applies it, so what is validated
 * is what is sent. Optional fields become `null` rather than `''` — an empty string
 * is a value, and "no group" is the absence of one.
 */
export function validateCreateForm(values: CreateFormValues): CreateFormValidation {
  const errors: CreateFormErrors = {}

  const name = values.name.trim()
  if (name.length === 0) errors.name = PRODUCT_CREATE_TEXT.validation.nameRequired
  else if (name.length > NAME_MAX) errors.name = PRODUCT_CREATE_TEXT.validation.nameTooLong

  const productClass = values.productClass.trim()
  if (productClass.length === 0) {
    errors.productClass = PRODUCT_CREATE_TEXT.validation.classRequired
  } else if (productClass.length > CLASS_MAX) {
    errors.productClass = PRODUCT_CREATE_TEXT.validation.classTooLong
  }

  const productGroup = values.productGroup.trim()
  if (productGroup.length > GROUP_MAX) {
    errors.productGroup = PRODUCT_CREATE_TEXT.validation.groupTooLong
  }

  const productFamily = values.productFamily.trim()
  if (productFamily.length > FAMILY_MAX) {
    errors.productFamily = PRODUCT_CREATE_TEXT.validation.familyTooLong
  }

  const trackingPolicy = values.trackingPolicy.trim()
  if (trackingPolicy.length > TRACKING_POLICY_MAX) {
    errors.trackingPolicy = PRODUCT_CREATE_TEXT.validation.trackingPolicyTooLong
  }

  /**
   * NO role is required, and an all-`unset` roles picker is a COMPLETE form.
   *
   * DEC-PRD-001 makes roles independent of class and group, and §6.1 lets a Draft be
   * saved incomplete — "Draft eksik kaydedilebilir; activation completeness
   * fail-closed'dur". Requiring a role here would move an activation-time check
   * onto the save, which is precisely the boundary §6.2 keeps separate.
   */

  if (Object.keys(errors).length > 0) return { ok: false, errors }

  return {
    ok: true,
    payload: {
      name,
      classification: {
        class: productClass,
        group: blankToNull(values.productGroup),
        family: blankToNull(values.productFamily),
      },
      roles: rolesPayload(values.roles),
      trackingPolicy: blankToNull(values.trackingPolicy),
    },
  }
}

/**
 * Only the roles the user actually decided.
 *
 * An `unset` role is OMITTED from the payload rather than sent as `false`. Sending
 * `false` is exactly the silent persist §5.2 forbids: the server would record a
 * determined "no" for a question nobody answered, and the 360 would then show a
 * decided role with `manual` provenance.
 */
export function rolesPayload(
  roles: CreateFormRoles,
): Partial<Readonly<Record<ProductRoleName, boolean>>> {
  const payload: Partial<Record<ProductRoleName, boolean>> = {}
  for (const role of PRODUCT_ROLE) {
    const choice = roles[role]
    if (choice === 'yes') payload[role] = true
    else if (choice === 'no') payload[role] = false
  }
  return payload
}

/**
 * Merge server field errors over client ones.
 *
 * The server wins on any field it names (§10.3: server validation is the single
 * truth), and client messages survive only on fields the server did not mention.
 */
export function mergeServerErrors(
  clientErrors: CreateFormErrors,
  serverErrors: Readonly<Record<string, string>>,
): CreateFormErrors {
  const merged: CreateFormErrors = { ...clientErrors }
  for (const field of CREATE_FIELD_ORDER) {
    const message = serverErrors[field]
    if (message !== undefined) merged[field] = message
  }
  return merged
}
