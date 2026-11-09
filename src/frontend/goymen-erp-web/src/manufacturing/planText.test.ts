import { describe, expect, it } from 'vitest'
import { lifecycleFailureText } from './planText'

describe('lifecycleFailureText', () => {
  it('names the preparer refusal of the approval (U2-d, URT-D2)', () => {
    expect(lifecycleFailureText('approve', 422, 'manufacturing_plan_self_approval')).toBe(
      'Planın bu sürümünü siz hazırladınız; onayı başka bir kullanıcı vermelidir.',
    )
  })

  it('names the two allocation refusals of the approval (U3-a, URT-D21/D3)', () => {
    expect(lifecycleFailureText('approve', 409, 'inventory_manufacturing_allocation_insufficient_free_stock')).toBe(
      'Plan girdileri için stokta yeterli serbest miktar yok; plan onaylanmadı.',
    )
    expect(lifecycleFailureText('approve', 409, 'inventory_manufacturing_parent_label_version_conflict')).toBe(
      'Girdi etiketi onay sırasında değişti; plan onaylanmadı. Yeniden deneyin.',
    )
  })

  it('keeps the generic texts for other refusals', () => {
    expect(lifecycleFailureText('approve', 409, 'manufacturing_version_conflict')).toBe(
      'Plan onayı uygulanmadı (manufacturing_version_conflict).',
    )
    expect(lifecycleFailureText('approve', 422, 'other_rule')).toBe(
      'Plan onayı iş kuralı tarafından reddedildi (other_rule).',
    )
  })
})
