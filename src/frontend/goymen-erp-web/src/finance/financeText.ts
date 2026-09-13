/**
 * Turkish user-facing text for the Finance manual receipt screens.
 *
 * Same rule as the other lanes: text is chosen from a decoded state, never parsed from
 * a localized server `title`/`detail`, and never claims more than we know. In Finance
 * that rule has a sharper edge — a sentence that says "tahsil edildi", "bakiye" or
 * "0" where the server said nothing is a false money statement.
 */

import type { FieldError, ManualReceiptFields } from './manualReceiptForm'
import type { RecordGateReason, SourceSummaryView } from './sourceView'

export const SOURCE_SUBJECT = 'finansal kaynak'

export const FIELD_LABEL: Readonly<Record<keyof ManualReceiptFields, string>> = {
  partyId: 'Cari',
  companyAccountId: 'Cari hesabı',
  treasuryAccountId: 'Banka hesabı',
  currencyId: 'Para birimi',
  amount: 'Tutar',
  occurredOn: 'Banka işlem tarihi',
  valueOn: 'Valör tarihi',
  bankReference: 'Banka referansı',
}

export const FIELD_HINT: Readonly<Partial<Record<keyof ManualReceiptFields, string>>> = {
  amount: 'Ondalık için virgül kullanın; nokta veya binlik ayırıcı yazmayın.',
  valueOn: 'Bilinmiyorsa boş bırakın.',
  bankReference: 'Yoksa boş bırakın; banka tarafından doğrulanmış sayılmaz.',
}

export const FIELD_ERROR: Readonly<Record<FieldError, string>> = {
  required: 'Bu alan zorunludur.',
  format: 'Tutarı yalnız rakam ve ondalık için tek virgülle yazın (ör. 1000,50).',
  'not-positive': 'Tutar sıfırdan büyük olmalıdır.',
  'date-format': 'Geçerli bir tarih girin (YYYY-AA-GG).',
}

export const STAGE_LABEL: Readonly<Record<SourceSummaryView['stage'], string>> = {
  draft: 'Taslak',
  recorded: 'Kesin',
  reversed: 'Ters kayıtla düzeltildi',
}

export const STAGE_NOTICE: Readonly<Record<SourceSummaryView['stage'], string>> = {
  draft:
    'Taslak — para etkisi yok. Bu kayıt banka tahsilatı, banka bakiyesi veya dağıtılmamış tutar değildir.',
  recorded:
    'Kesin kaynak kaydı. Fatura veya açık borç kapatmaz; dağıtım ayrı bir işlemdir.',
  reversed:
    'Bu kaynak bağlı ters kayıtla düzeltildi. Orijinal kayıt korunur; bu işlem bankadan para iadesi değildir.',
}

export const AMOUNT_LABEL = 'Tutar'
export const UNALLOCATED_LABEL = 'Dağıtılmamış tutar'
export const STAGE_TERM = 'Durum'
export const AMOUNT_WITHHELD = 'Bu yetkiyle gösterilmiyor'
export const UNALLOCATED_UNAVAILABLE = 'Şu anda alınamadı'
export const AMOUNT_UNREADABLE = 'Tutar gösterilemiyor'

export const RECORD_GATE_TITLE = 'Kesin kayıt şu anda kapalı'

export const RECORD_GATE_REASON: Readonly<Record<RecordGateReason, string>> = {
  'precision-profile-missing': 'Bu hesap ve para birimi için kabul edilmiş tutar hassasiyeti yok.',
  'period-not-open': 'İşlem tarihi açık bir döneme ait değil.',
  'authority-missing': 'Bu şirkette kesin kayıt yetkiniz yok.',
  'policy-missing': 'Gerekli finans politikası tanımlı değil.',
  'not-declared': 'Kesin kayıt koşulları sunucu tarafından henüz bildirilmedi.',
}
