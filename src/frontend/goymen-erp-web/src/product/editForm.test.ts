import { describe, expect, it } from 'vitest'
import {
  changesFrom,
  editFormFrom,
  hasChanges,
  isRoleEditable,
  roleChoiceOf,
  validateEditForm,
  type EditFormValues,
} from './editForm'
import type { ProductCard } from './contract'

function card(over: Partial<ProductCard['product']> = {}): ProductCard {
  return {
    product: {
      id: '11111111-1111-1111-1111-111111111111',
      code: 'SAC-42',
      name: 'Siyah sac',
      groupId: 'aaaaaaa1-0000-0000-0000-000000000001',
      classId: null,
      familyId: null,
      roles: {
        purchasable: { value: true, provenance: 'manual' },
        sellable: { value: false, provenance: 'manual' },
        productionInput: { value: null, provenance: 'not-evaluated' },
        productionOutput: { value: true, provenance: 'derived' },
      },
      lifecycle: { known: true, value: 'Draft' },
      version: '7',
      createdAtUtc: '2026-01-01T00:00:00+00:00',
      updatedAtUtc: '2026-09-12T07:00:00+00:00',
      labels: { group: 'Sac', class: null, family: null, primaryStockUnit: 'kg' },
      completeness: {
        evaluation: 'Incomplete' as const,
        rawEvaluation: 'Incomplete',
        missingFields: [],
      },
      ...over,
    },
    references: {
      templateId: null,
      templateVersion: null,
      attributes: null,
      units: {
        stock: null,
        purchase: null,
        sales: null,
        price: null,
        production: null,
        package: null,
      },
      trackingPolicyId: null,
    },
  }
}

function form(base: ProductCard, over: Partial<EditFormValues> = {}): EditFormValues {
  return { ...editFormFrom(base), reason: 'Ad düzeltildi', ...over }
}

describe('editFormFrom', () => {
  it('seeds from the card as read, but never seeds a reason', () => {
    const seeded = editFormFrom(card())
    expect(seeded.name).toBe('Siyah sac')
    // A reason belongs to THIS change; a carried-over one would attach last week's
    // justification to today's edit.
    expect(seeded.reason).toBe('')
  })

  it('seeds an unevaluated role as unanswered, not as "no"', () => {
    /**
     * Otherwise merely opening the form and saving an unrelated field would turn
     * every unevaluated role into a determined "no" — the silent persist the
     * provenance tag exists to prevent, arriving through the back door.
     */
    expect(roleChoiceOf(card(), 'productionInput')).toBe('unset')
    expect(roleChoiceOf(card(), 'sellable')).toBe('no')
    expect(roleChoiceOf(card(), 'purchasable')).toBe('yes')
  })
})

describe('isRoleEditable', () => {
  it('refuses a derived role', () => {
    // `derived` is server-only and `PreserveDerived` rejects an overwrite. A manual
    // value on top of a computed fact leaves the two disagreeing.
    expect(isRoleEditable(card(), 'productionOutput')).toBe(false)
    expect(isRoleEditable(card(), 'purchasable')).toBe(true)
    expect(isRoleEditable(card(), 'productionInput')).toBe(true)
  })
})

describe('changesFrom', () => {
  it('sends nothing when nothing changed', () => {
    // An untouched form is not a write: an omitted member is `Optional` with no
    // value, which the server reads as unchanged.
    expect(changesFrom(card(), form(card()))).toEqual({})
    expect(hasChanges(card(), form(card()))).toBe(false)
  })

  it('sends ONLY what changed', () => {
    /**
     * The whole reason this is a diff: sending every field would make each untouched
     * input a write, so a colleague's concurrent change to one of them would be
     * silently reverted by someone who never looked at it.
     */
    const base = card()
    const changes = changesFrom(base, form(base, { name: 'Galvaniz sac' }))
    expect(changes).toEqual({ name: 'Galvaniz sac' })
    expect(Object.hasOwn(changes, 'roles')).toBe(false)
  })

  it('sends an editable role change in the tagged write shape', () => {
    const base = card()
    const values = form(base, { roles: { ...editFormFrom(base).roles, sellable: 'yes' } })
    expect(changesFrom(base, values).roles).toEqual({
      sellable: { value: true, provenance: 'manual' },
    })
  })

  it('never sends a derived role, even when its seeded value is re-submitted', () => {
    const base = card()
    const values = form(base, {
      roles: { ...editFormFrom(base).roles, productionOutput: 'no' },
    })
    expect(Object.hasOwn(changesFrom(base, values), 'roles')).toBe(false)
  })

  it('sends a return to "unanswered" as the contract’s tagged not-evaluated value', () => {
    /**
     * `ProductRoleInput.ToManualState` accepts `{value: null, provenance:
     * 'not-evaluated'}` as a write, so this is a state the user CAN assert. Dropping
     * it was a rule this form invented — and a costly one: alone it reported
     * "nothing changed", and alongside another edit it reported success while the
     * old role quietly stayed.
     */
    const base = card()
    const values = form(base, { roles: { ...editFormFrom(base).roles, purchasable: 'unset' } })
    expect(changesFrom(base, values).roles).toEqual({
      purchasable: { value: null, provenance: 'not-evaluated' },
    })
  })

  it('counts a return to "unanswered" as a real change on its own', () => {
    // Otherwise the save is refused as "nothing changed" while the user is looking
    // at a field they just cleared.
    const base = card()
    const values = form(base, { roles: { ...editFormFrom(base).roles, sellable: 'unset' } })
    expect(hasChanges(base, values)).toBe(true)
    const result = validateEditForm(base, values)
    expect(result.ok).toBe(true)
  })

  it('still never writes a derived role, even to unset it', () => {
    const base = card()
    const values = form(base, {
      roles: { ...editFormFrom(base).roles, productionOutput: 'unset' },
    })
    expect(Object.hasOwn(changesFrom(base, values), 'roles')).toBe(false)
  })
})

describe('validateEditForm', () => {
  it('carries the version READ, verbatim', () => {
    const base = card()
    const result = validateEditForm(base, form(base, { name: 'Galvaniz sac' }))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    // A canonical decimal string, never parsed into a number.
    expect(result.payload.expectedVersion).toBe('7')
    expect(typeof result.payload.expectedVersion).toBe('string')
  })

  it('requires a reason when something is actually being written', () => {
    const base = card()
    const result = validateEditForm(base, form(base, { name: 'Galvaniz sac', reason: '  ' }))
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.errors.reason).toBeDefined()
  })

  it('refuses a save that would write nothing, and does not blame the reason', () => {
    // Demanding a reason for a save that writes nothing would be a rule about the
    // form rather than about the record.
    const base = card()
    const result = validateEditForm(base, form(base, { reason: '' }))
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.errors.reason).toBeUndefined()
    expect(result.errors.name).toBeDefined()
  })

  it('enforces the contract’s 1000-character reason bound', () => {
    const base = card()
    const result = validateEditForm(
      base,
      form(base, { name: 'Galvaniz sac', reason: 'x'.repeat(1001) }),
    )
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.errors.reason).toBeDefined()
  })

  it('requires a non-empty name', () => {
    const base = card()
    const result = validateEditForm(base, form(base, { name: '   ' }))
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.errors.name).toBeDefined()
  })
})
