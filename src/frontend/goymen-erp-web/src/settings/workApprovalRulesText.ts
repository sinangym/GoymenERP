import { RULE_CLASS_HOURS, type WorkApprovalRuleClass } from './workApprovalRulesContract'

/** WP1 "Onay kural tablosu" (YOL-D17, YOL-D18) — the words of the read-only rule table section of AY-OS-01. */
export const WORK_APPROVAL_RULES_TEXT = {
  heading: 'Onay kural tablosu',
  intro:
    'Her onay türü için onay izni, süre sınıfı ve yükseltme izni sürümlü olarak tek yerde tutulur. Kayıt yoksa karar varsayılanı geçerlidir. Onay izni türün sahibi modülündür; burada yalnız gösterilir.',
  subject: 'onay kuralları',
  caption: 'Onay türlerinin geçerli kuralları',
  kind: 'Onay türü',
  owner: 'Sahibi (modül)',
  approver: 'Onay izni',
  durationClass: 'Süre sınıfı',
  escalation: 'Yükseltme izni',
  version: 'Geçerli sürüm',
  recorded: 'Kaydeden / zaman',
  reason: 'Gerekçe',
  escalationUndefined: 'Tanımlı değil (yükseltme yapılmaz)',
  defaultVersion: (basis: string) => `0 · karar varsayılanı (${basis})`,
  approverChanged: 'Sahip modülün onay izni değişti; iş kutusu bu türü yeni sürüm kaydedilene kadar okunamaz gösterir.',
  empty: 'İş kutusuna bağlı onay türü yok.',
  historyToggle: (kind: string) => `${kind} — sürüm geçmişi`,
} as const

export const ruleClassText = (value: WorkApprovalRuleClass): string =>
  value === RULE_CLASS_HOURS ? 'Saat ölçekli' : 'İş günü ölçekli'
