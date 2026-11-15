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

  it('names the missing termin refusal of the approval (URT-D66)', () => {
    expect(lifecycleFailureText('approve', 422, 'manufacturing_plan_due_date_required')).toBe(
      'Termini girilmemiş plan onaylanmaz; önce planın terminini girin.',
    )
  })

  it('names the past termin refusal of the approval and of the order opening (URT-D70)', () => {
    expect(lifecycleFailureText('approve', 422, 'manufacturing_due_date_in_past')).toBe(
      'Planın termini bugünden önce; plan onaylanmadı. Önce terminini bugün veya sonrasına taşıyın.',
    )
    expect(lifecycleFailureText('create-order', 422, 'manufacturing_due_date_in_past')).toBe(
      'Planın termini bugünden önce; üretim emri açılmadı. Önce planın terminini bugün veya sonrasına taşıyın.',
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
