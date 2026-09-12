import { describe, expect, it } from 'vitest'
import {
  EMPTY_CREATE_FORM,
  isCreateFormPristine,
  mergeServerErrors,
  rolesPayload,
  validateCreateForm,
  type CreateFormValues,
} from './createForm'

function form(over: Partial<CreateFormValues> = {}): CreateFormValues {
  return { ...EMPTY_CREATE_FORM, name: 'Siyah sac', productClass: 'Yassı ürün', ...over }
}

describe('validateCreateForm', () => {
  it('accepts a minimal form and trims what it sends', () => {
    const result = validateCreateForm(form({ name: '  Siyah sac  ' }))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.payload.name).toBe('Siyah sac')
    expect(result.payload.classification.class).toBe('Yassı ürün')
  })

  it('requires a name and a class', () => {
    const result = validateCreateForm(form({ name: '   ', productClass: '' }))
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.errors.name).toBeDefined()
    expect(result.errors.productClass).toBeDefined()
  })

  it('turns a blank optional field into null rather than an empty string', () => {
    const result = validateCreateForm(form({ productGroup: '   ', productFamily: 'Sac' }))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.payload.classification.group).toBeNull()
    expect(result.payload.classification.family).toBe('Sac')
  })

  it('accepts a form where no role was answered', () => {
    /**
     * DEC-PRD-001 makes roles independent of class/group, and §6.1 lets a Draft be
     * saved incomplete. Requiring a role here would move an activation-time check
     * onto the save, which §6.2 keeps separate.
     */
    const result = validateCreateForm(form())
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.payload.roles).toEqual({})
  })

  it('rejects an over-long name', () => {
    const result = validateCreateForm(form({ name: 'x'.repeat(251) }))
    expect(result.ok).toBe(false)
  })
})

describe('rolesPayload', () => {
  it('omits an unanswered role instead of sending false', () => {
    /**
     * THE RULE §5.2 exists for: sending `false` would make the server record a
     * determined "no" for a question nobody answered, and the 360 would then show a
     * decided role with `manual` provenance.
     */
    const payload = rolesPayload({
      purchasable: 'yes',
      sellable: 'no',
      productionInput: 'unset',
      productionOutput: 'unset',
    })
    expect(payload).toEqual({ purchasable: true, sellable: false })
    expect(Object.hasOwn(payload, 'productionInput')).toBe(false)
  })

  it('sends nothing when nothing was answered', () => {
    expect(rolesPayload(EMPTY_CREATE_FORM.roles)).toEqual({})
  })
})

describe('isCreateFormPristine', () => {
  it('is pristine at mount and dirty after any answer', () => {
    expect(isCreateFormPristine(EMPTY_CREATE_FORM)).toBe(true)
    expect(isCreateFormPristine({ ...EMPTY_CREATE_FORM, name: 'a' })).toBe(false)
    // A role answered but every text field empty still counts as work in progress.
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
      { name: 'istemci mesajı', productClass: 'istemci sınıf' },
      { name: 'sunucu mesajı' },
    )
    expect(merged.name).toBe('sunucu mesajı')
    // ...and leaves the client message standing where the server said nothing.
    expect(merged.productClass).toBe('istemci sınıf')
  })

  it('ignores a server field the form does not render', () => {
    const merged = mergeServerErrors({}, { unknownField: 'x' })
    expect(merged).toEqual({})
  })
})
