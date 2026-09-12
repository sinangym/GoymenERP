/**
 * U-02 Draft create form rules, pinned to the published backend contract
 * (`CreateProductDraftRequest`, `ProductDraftValidator.ValidateCreateAsync`).
 *
 * The server is the authority; everything here mirrors a bound it also enforces.
 *
 * WHAT THE FORM OFFERS, and what it deliberately does not:
 *  - `name` and `groupId` are the create minimum the contract states, and the group
 *    comes from `GET /products/code-groups` — never typed;
 *  - `classId` / `familyId` come from `GET /products/reference-options`, scoped to
 *    the chosen group. They are CHOSEN, never typed: §19.3 forbids the client
 *    producing a reference;
 *  - `references` (template, attributes, units, trackingPolicyId) are NOT offered.
 *    §19.6 and the coordinator keep attribute/unit completeness in the next slice,
 *    so opening them here would pull an unfinished decision forward;
 *  - roles ARE offered, because their vocabulary is fully defined by the contract.
 */

import { PRODUCT_ROLE, type ProductRoleName } from './contract'
import type { CreateProductDraftPayload, ProductRoleWrites } from './api'
import { PRODUCT_CREATE_TEXT } from './productText'

/** `Product.MaximumNameLength`. */
export const NAME_MAX = 250

/**
 * A role the user has not answered.
 *
 * The contract's write vocabulary is exactly `manual`+boolean or
 * `not-evaluated`+null, and "Omitted create roles are not-evaluated". So `unset`
 * maps to OMISSION, not to a `not-evaluated` payload entry — the two are equivalent
 * server-side and omitting is the smaller claim.
 *
 * The picker is tri-state because a checkbox cannot represent "not answered": an
 * unticked box and a deliberate "no" would be the same state, which is precisely
 * the collapse §5.2 forbids.
 */
export type RoleChoice = 'yes' | 'no' | 'unset'

export type CreateFormRoles = Readonly<Record<ProductRoleName, RoleChoice>>

export interface CreateFormValues {
  readonly name: string
  /** A code-group id from `GET /products/code-groups`. `''` means nothing chosen. */
  readonly groupId: string
  /** A class id from `reference-options`. `''` means none — a valid Draft state. */
  readonly classId: string
  /** A family id from `reference-options`. `''` means none. */
  readonly familyId: string
  readonly roles: CreateFormRoles
}

/**
 * Field ids, shared by the validator, the inputs and the server-error mapper.
 *
 * They match the server's `ProductFieldError.field` tokens (`name`, `groupId`,
 * `roles`) so a server error lands on the input it names. A message attached to a
 * field the form does not render is one the user can neither see nor clear.
 */
export const CREATE_FIELD = {
  name: 'name',
  groupId: 'groupId',
  classId: 'classId',
  familyId: 'familyId',
  roles: 'roles',
} as const

export type CreateFieldId = (typeof CREATE_FIELD)[keyof typeof CREATE_FIELD]

/** Declaration order is DOM order, so the summary lists errors as they appear. */
export const CREATE_FIELD_ORDER: readonly CreateFieldId[] = [
  CREATE_FIELD.name,
  CREATE_FIELD.groupId,
  CREATE_FIELD.classId,
  CREATE_FIELD.familyId,
  CREATE_FIELD.roles,
]

export const CREATE_FIELD_LABEL: Record<CreateFieldId, string> = {
  [CREATE_FIELD.name]: PRODUCT_CREATE_TEXT.nameLabel,
  [CREATE_FIELD.groupId]: PRODUCT_CREATE_TEXT.groupLabel,
  [CREATE_FIELD.classId]: PRODUCT_CREATE_TEXT.classLabel,
  [CREATE_FIELD.familyId]: PRODUCT_CREATE_TEXT.familyLabel,
  [CREATE_FIELD.roles]: PRODUCT_CREATE_TEXT.rolesLabel,
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
  groupId: '',
  classId: '',
  familyId: '',
  roles: EMPTY_ROLES,
}

/** Whether the user has touched anything. Drives the unsaved guard. */
export function isCreateFormPristine(values: CreateFormValues): boolean {
  return (
    values.name === '' &&
    values.groupId === '' &&
    values.classId === '' &&
    values.familyId === '' &&
    PRODUCT_ROLE.every((role) => values.roles[role] === 'unset')
  )
}

/**
 * Validate and normalize.
 *
 * `availableGroupIds` are the ids `GET /products/code-groups` actually returned.
 * Checking against them is not duplicated server validation: it is the difference
 * §19.3 draws between a reference the server offered and one the client produced.
 * A remembered-but-gone selection falls on the wrong side of that line too.
 */
export function validateCreateForm(
  values: CreateFormValues,
  availableGroupIds: readonly string[],
  availableClassIds: readonly string[] = [],
  availableFamilyIds: readonly string[] = [],
): CreateFormValidation {
  const errors: CreateFormErrors = {}

  const name = values.name.trim()
  if (name.length === 0) errors.name = PRODUCT_CREATE_TEXT.validation.nameRequired
  else if (name.length > NAME_MAX) errors.name = PRODUCT_CREATE_TEXT.validation.nameTooLong

  /**
   * The group is REQUIRED because the server mints the code from its prefix.
   * Leaving it empty is not an incomplete Draft — it is a create the server cannot
   * answer at all.
   */
  if (values.groupId === '') {
    errors.groupId = PRODUCT_CREATE_TEXT.validation.groupRequired
  } else if (!availableGroupIds.includes(values.groupId)) {
    errors.groupId = PRODUCT_CREATE_TEXT.validation.groupUnknown
  }

  /**
   * MISSING is fine; INVALID is not. §19.3 draws exactly this line: "Draft'ta
   * tamamlanmamış referans boş kalabilir; gönderilmiş bilinmeyen/uyumsuz referans
   * reddedilir." An unchosen class is an incomplete Draft, which is allowed; a class
   * that is not on the server's list is a reference this client must not send —
   * including one remembered after the options changed under it.
   */
  if (values.classId !== '' && !availableClassIds.includes(values.classId)) {
    errors.classId = PRODUCT_CREATE_TEXT.validation.classUnknown
  }
  if (values.familyId !== '' && !availableFamilyIds.includes(values.familyId)) {
    errors.familyId = PRODUCT_CREATE_TEXT.validation.familyUnknown
  }

  /**
   * NO role is required. DEC-PRD-001 makes roles independent of the group, and a
   * Draft may be saved incomplete — requiring one would move an activation-time
   * check onto the save, which §6.2 keeps separate.
   */

  if (Object.keys(errors).length > 0) return { ok: false, errors }

  const roles = rolesPayload(values.roles)
  return {
    ok: true,
    payload: {
      name,
      groupId: values.groupId,
      // ABSENT rather than null when nothing was chosen: both are optional in the
      // request, and omitting claims less than sending an explicit null.
      ...(values.classId !== '' ? { classId: values.classId } : {}),
      ...(values.familyId !== '' ? { familyId: values.familyId } : {}),
      // Omitted entirely when nothing was answered: the source treats an absent
      // `Roles` and an all-not-evaluated one identically, and absent claims less.
      ...(Object.keys(roles).length > 0 ? { roles } : {}),
    },
  }
}

/**
 * The answered roles, in the contract's tagged write vocabulary.
 *
 * An `unset` role is OMITTED rather than sent as `false`. Sending `false` is the
 * silent persist §5.2 forbids: the server would record a determined "no" for a
 * question nobody answered, and the card would then show a decided role with
 * `manual` provenance.
 */
export function rolesPayload(roles: CreateFormRoles): ProductRoleWrites {
  const payload: Record<string, { value: boolean; provenance: 'manual' }> = {}
  for (const role of PRODUCT_ROLE) {
    const choice = roles[role]
    if (choice === 'yes') payload[role] = { value: true, provenance: 'manual' }
    else if (choice === 'no') payload[role] = { value: false, provenance: 'manual' }
  }
  return payload as ProductRoleWrites
}

/**
 * Merge server field errors over client ones.
 *
 * The server wins on any field it names, and only on fields this form renders. A
 * `field` the form does not have is dropped rather than shown detached — but the
 * caller still surfaces it as a general failure, so nothing goes silent.
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
