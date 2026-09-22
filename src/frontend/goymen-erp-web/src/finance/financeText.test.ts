import { describe, expect, it } from 'vitest'
import { issueText } from './financeText'

describe('FIN-T06 refusal text', () => {
  it.each([
    ['finance_approver_is_proposer', 'Öneriyi yapan kişi kendi onay veya ret kararını veremez; başka bir yetkili karar vermeli.'],
    ['finance_reversal_has_allocations', 'Bu kaynağa dağıtım bağlı; ters kayıt onaylanamaz.'],
    ['finance_allocation_rule_unavailable', 'Dağıtım durumu doğrulanamadı; ters kayıt onayı kapalı.'],
    ['finance_period_rule_unavailable', 'Ters kayıt dönemi doğrulanamadı; onay kapalı.'],
    ['finance_period_not_open', 'Ters kayıt işlem tarihi açık bir muhasebe dönemine ait değil.'],
    ['concurrent_change', 'Bu kaynak başka bir işlemle değişti; güncel durumu yeniden okuyun.'],
  ])('maps %s without exposing a raw server code', (code, expected) => {
    expect(issueText(code)).toBe(expected)
    expect(issueText(code)).not.toContain(`kod: ${code}`)
  })
})
