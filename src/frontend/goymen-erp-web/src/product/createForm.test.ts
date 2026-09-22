import { describe, expect, it } from 'vitest'
import {
  EMPTY_CREATE_FORM,
  isCreateFormPristine,
  mergeServerErrors,
  rolesPayload,
  validateCreateForm,
  type CreateFormValues,
} from './createForm'

const GROUP_A = 'aaaaaaa1-0000-0000-0000-000000000001'
const GROUP_B = 'aaaaaaa1-0000-0000-0000-000000000002'
const GROUPS = [GROUP_A, GROUP_B]

function form(over: Partial<CreateFormValues> = {}): CreateFormValues {
  return { ...EMPTY_CREATE_FORM, name: 'Siyah sac', groupId: GROUP_A, ...over }
}

describe('validateCreateForm', () => {
  it('accepts the contract’s create minimum and trims what it sends', () => {
    const result = validateCreateForm(form({ name: '  Siyah sac  ' }), GROUPS)
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.payload.name).toBe('Siyah sac')
    expect(result.payload.groupId).toBe(GROUP_A)
  })

  it('requires a name and a code group', () => {
    const result = validateCreateForm(form({ name: '   ', groupId: '' }), GROUPS)
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.errors.name).toBeDefined()
    // The code is minted from the group's prefix, so an unchosen group is not an
    // incomplete Draft — it is a create the server cannot answer at all.
    expect(result.errors.groupId).toBeDefined()
  })

  it('rejects a group that is no longer on the server’s list', () => {
    // A stale selection falls on the same side of the line as an invented one:
    // neither is a reference the server offered.
    const result = validateCreateForm(form({ groupId: GROUP_A }), [GROUP_B])
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.errors.groupId).toBeDefined()
  })

  it('accepts a form where no role was answered, and sends no roles member', () => {
    /**
     * "Omitted create roles are not-evaluated", so an absent `roles` and an
     * all-not-evaluated one are identical server-side — and absent claims less.
     */
    const result = validateCreateForm(form(), GROUPS)
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(Object.hasOwn(result.payload, 'roles')).toBe(false)
  })

  it('rejects an over-long name', () => {
    const result = validateCreateForm(form({ name: 'x'.repeat(251) }), GROUPS)
    expect(result.ok).toBe(false)
  })
})

describe('rolesPayload', () => {
  it('sends the contract’s tagged shape and omits an unanswered role', () => {
    /**
     * A bare boolean could not distinguish "the user said no" from "nobody has
     * looked", and sending `false` for an unanswered role is the silent persist the
     * provenance tag exists to prevent.
     */
    const payload = rolesPayload({
      purchasable: 'yes',
      sellable: 'no',
      productionInput: 'unset',
      productionOutput: 'unset',
    })
    expect(payload).toEqual({
      purchasable: { value: true, provenance: 'manual' },
      sellable: { value: false, provenance: 'manual' },
    })
    expect(Object.hasOwn(payload, 'productionInput')).toBe(false)
  })

  it('never writes a derived provenance — that is server-only', () => {
    const payload = rolesPayload({
      purchasable: 'yes',
      sellable: 'unset',
      productionInput: 'unset',
      productionOutput: 'unset',
    })
    for (const entry of Object.values(payload)) {
      expect(entry?.provenance).toBe('manual')
    }
  })

  it('sends nothing when nothing was answered', () => {
    expect(rolesPayload(EMPTY_CREATE_FORM.roles)).toEqual({})
  })
})

describe('isCreateFormPristine', () => {
  it('is pristine at mount and dirty after any answer', () => {
    expect(isCreateFormPristine(EMPTY_CREATE_FORM)).toBe(true)
    expect(isCreateFormPristine({ ...EMPTY_CREATE_FORM, name: 'a' })).toBe(false)
    expect(isCreateFormPristine({ ...EMPTY_CREATE_FORM, groupId: GROUP_A })).toBe(false)
    expect(
      isCreateFormPristine({
        ...EMPTY_CREATE_FORM,
        roles: { ...EMPTY_CREATE_FORM.roles, sellable: 'no' },
      }),
    ).toBe(false)
  })
})

describe('mergeServerErrors', () => {
  it('lets the server win on a field it names', () => {
    const merged = mergeServerErrors(
      { name: 'istemci mesajı', groupId: 'istemci grup' },
      { name: 'sunucu mesajı' },
    )
    expect(merged.name).toBe('sunucu mesajı')
    // ...and leaves the client message standing where the server said nothing.
    expect(merged.groupId).toBe('istemci grup')
  })

  it('binds the reference error to the technical fields now rendered', () => {
    expect(mergeServerErrors({}, { references: 'x' })).toEqual({ references: 'x' })
    expect(mergeServerErrors({}, { notAField: 'x' })).toEqual({})
  })
})
