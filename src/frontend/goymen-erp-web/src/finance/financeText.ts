/**
 * Turkish user-facing text for the Finance manual receipt screens.
 *
 * Same rule as the other lanes: text is chosen from a decoded state or a stable issue
 * `code`, never parsed from server prose, and never claims more than we know. In Finance
 * that rule has a sharper edge — "tahsil edildi", "bakiye" or "0" where the server said
 * nothing is a false money statement.
 */

import type { FieldError, ManualReceiptFields } from './manualReceiptForm'
import type { RefusalReason, UnknownReason } from './receiptCommand'
import type { SourceStatusView } from './sourceView'

export const SOURCE_SUBJECT = 'finansal kaynak'
export const SOURCE_LIST_SUBJECT = 'finansal kaynaklar'
export const BANK_ACCOUNTS_SUBJECT = 'banka hesapları'

export const FIELD_LABEL: Readonly<Record<keyof ManualReceiptFields, string>> = {
  treasuryAccountId: 'Banka hesabı',
  amount: 'Tutar',
  occurredOn: 'Banka işlem tarihi',
  valueOn: 'Valör tarihi',
  bankReference: 'Banka referansı',
  description: 'Açıklama',
}

export const FIELD_HINT: Readonly<Partial<Record<keyof ManualReceiptFields, string>>> = {
  amount: 'Ondalık için virgül kullanın; nokta veya binlik ayırıcı yazmayın. Para birimi banka hesabınınkidir.',
  valueOn: 'Bilinmiyorsa boş bırakın.',
  bankReference: 'Yoksa boş bırakın; banka tarafından doğrulanmış sayılmaz.',
}

export const FIELD_ERROR: Readonly<Record<FieldError, string>> = {
  required: 'Bu alan zorunludur.',
  format: 'Tutarı yalnız rakam ve ondalık için tek virgülle yazın (ör. 1000,50).',
  'not-positive': 'Tutar sıfırdan büyük olmalıdır.',
  'date-format': 'Geçerli bir tarih girin (YYYY-AA-GG).',
  'bank-account-ineligible': 'Bu banka hesabı tahsilat için kullanılamıyor.',
  'scale-exceeded': 'İzin verilenden fazla ondalık basamak var; tutar yuvarlanmaz, düzeltin.',
}

export const BANK_ACCOUNT_PLACEHOLDER = 'Banka hesabı seçin'
export const NO_USABLE_BANK_ACCOUNT = 'Bu şirkette tahsilata uygun banka hesabı yok.'

export const STAGE_LABEL: Readonly<Record<SourceStatusView['stage'], string>> = {
  draft: 'Taslak',
  recorded: 'Kesin',
  reversed: 'Ters kayıtla düzeltildi',
}

export const STAGE_NOTICE: Readonly<Record<SourceStatusView['stage'], string>> = {
  draft:
    'Taslak — para etkisi yok. Bu kayıt banka tahsilatı, banka bakiyesi veya dağıtılmamış tutar değildir.',
  recorded: 'Kesin kaynak kaydı. Fatura veya açık borç kapatmaz; dağıtım ayrı bir işlemdir.',
  reversed:
    'Bu kaynak bağlı ters kayıtla düzeltildi. Orijinal kayıt korunur; bu işlem bankadan para iadesi değildir.',
}

export const STAGE_TERM = 'Durum'
export const AMOUNT_LABEL = 'Tutar'
export const UNALLOCATED_LABEL = 'Dağıtılmamış tutar'
export const AMOUNT_REDACTED = 'Bu yetkiyle gösterilmiyor'
export const AMOUNT_UNAVAILABLE = 'Şu anda alınamadı'
export const AMOUNT_UNREADABLE = 'Tutar gösterilemiyor'

export const COLUMN = {
  sourceNumber: 'Kaynak no',
  stage: 'Durum',
  occurredOn: 'İşlem tarihi',
  valueOn: 'Valör',
  bankAccount: 'Banka hesabı',
  amount: 'Tutar',
  currency: 'Para birimi',
  actions: 'İşlem',
} as const

export const OPEN_SOURCE = 'Aç'
export const NEXT_PAGE = 'Sonraki sayfa'
export const PREVIOUS_PAGE = 'Önceki sayfa'
export const BANK_NAME_UNAVAILABLE = 'Hesap adı alınamadı'
export const VALUE_DATE_NONE = '—'
export const LIST_EMPTY = 'Bu şirkette henüz finansal kaynak yok.'

export const DETAIL = {
  sourceNumber: 'Kaynak no',
  occurredOn: 'Banka işlem tarihi',
  valueOn: 'Valör tarihi',
  bankAccount: 'Banka hesabı',
  bankReference: 'Banka referansı',
  description: 'Açıklama',
  recordedAt: 'Kesin kayıt zamanı',
  reversedBy: 'Düzelten ters kayıt',
  reverses: 'Düzelttiği kaynak',
  currency: 'Para birimi',
  none: '—',
} as const

// No technical id as final presentation (Business 5745076917). No source number series is decided, so the
// server's "source number" is the record's id: it is shown as a labelled reference, never as a number.
export const NO_SOURCE_NUMBER = 'Numara verilmedi'

/**
 * THE reference shape of Finance (ORG #356 INFO-2): "ref. …" and the last 8 characters of a technical id, the same
 * length Accounting uses. Every Finance screen shows a reference this way — a currency without its code, a source
 * without a number, a reversal link — so one reference never reads shorter than another.
 */
export const financeRef = (id: string) => `ref. …${id.slice(-8)}`
const GUID_TEXT = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** A real number as given; the record's own id (no series yet) as "Numara verilmedi · ref. …". */
export function sourceNumberShown(sourceNumber: string, sourceId: string): string {
  return sourceNumber === sourceId || GUID_TEXT.test(sourceNumber)
    ? `${NO_SOURCE_NUMBER} · ${financeRef(sourceId)}`
    : sourceNumber
}

export const PARTY_LABEL = 'Cari hesabı'
export const PARTY_LABEL_PENDING = 'Banka hesabı ve işlem tarihi seçilince cari etiketi okunur.'

/** Amount label with the server's currency display code, when the context is known. */
export function amountLabelFor(displayCode: string | null): string {
  return displayCode === null ? 'Tutar' : `Tutar (${displayCode})`
}

export function scaleHintFor(scale: number): string {
  return scale === 0 ? 'Bu para biriminde ondalık basamak kullanılmaz.' : `En fazla ${scale} ondalık basamak.`
}

/** Why saving is closed while the draft read context is not ready. Never claims a balance or grant. */
export const DRAFT_CONTEXT_TEXT = {
  'needs-selection': 'Para birimi ve tutar profili, banka hesabı ile işlem tarihi seçilince okunur. O zamana kadar taslak kaydedilemez.',
  loading: 'Para birimi, tutar profili ve cari etiketi okunuyor…',
  'invalid-request': 'Seçim geçersiz; banka hesabı ve işlem tarihini yeniden seçin.',
  forbidden: 'Bu taslak bağlamını okuma yetkiniz yok; taslak kaydedilemez.',
  'not-found': 'Seçilen banka hesabı veya cari hesap bu şirkette bu işlem için uygun değil.',
  unavailable:
    'Tutar profili, banka kaynağı veya finans okuma yetkisi şu anda sağlanamıyor; taslak kaydı kapalı. Bu bir bakiye veya sıfır tutar bilgisi değildir.',
  unauthenticated: 'Oturum sonlandı; yeniden giriş yapın.',
  malformed: 'Sunucu yanıtı beklenen biçimde değil; taslak kaydı güvenli biçimde kapatıldı.',
  'network-error': 'Sunucuya ulaşılamadı; taslak kaydı kapalı.',
} as const
export const DRAFT_CONTEXT_RETRY = 'Bağlamı yeniden oku'
export const SAVE_DRAFT = 'Taslağı kaydet'
export const DRAFT_SAVED = 'Taslak kaydedildi — para etkisi yok.'

export const RECORD_ACTION = 'Kesin kayda al'
export const RECORD_CONFIRM_TITLE = 'Kesin kayıt'
export const RECORD_CONFIRM_TEXT =
  'Bu işlem bir kez gerçekleşmiş banka tahsilatı kaynağı kaydeder. Fatura kapatmaz, dağıtım yapmaz.'
export const RECORD_CONFIRM = 'Kesin kaydı onayla'
export const CANCEL = 'Vazgeç'
export const RECORD_DONE = 'Kesin kayıt yapıldı.'

export const REVERSE_ACTION = 'Ters kayıt öner'
export const REVERSE_DATE = 'Ters kayıt işlem tarihi'
export const REVERSE_REASON = 'Gerekçe'
export const REVERSE_REASON_REQUIRED = 'Gerekçe zorunludur.'
export const REVERSE_CONFIRM = 'Onaya gönder'
export const REVERSE_DONE = 'Ters kayıt önerisi oluşturuldu; başka bir yetkilinin onayı bekleniyor.'

export const REREAD_AFTER_SUCCESS =
  'İşlem tamamlandı ancak güncel kayıt okunamadı. İşlem yeniden gönderilmez; yalnız kaydı yeniden okuyun.'
export const REREAD = 'Kaydı yeniden oku'
export const REREAD_AFTER_CONFLICT =
  'Kayıt başka bir işlemle değişti. Güncel kayıt okunmadan aynı sürümle yeniden gönderilmez; güncel kaydı inceleyip yeniden onaylayın.'

export const UNKNOWN_TITLE = 'İşlemin sonucu bilinmiyor'
export const UNKNOWN_REASON: Readonly<Record<UnknownReason, string>> = {
  'no-reply': 'Sunucudan yanıt alınamadı. İşlem kaydedilmiş olabilir.',
  malformed: 'Sunucu yanıtı beklenen biçimde değildi. İşlem kaydedilmiş olabilir.',
  unavailable: 'Finans servisi işlemi şu anda sonuçlandıramadı. İşlem kaydedilmiş olabilir.',
  'idempotency-conflict': 'Önceki gönderim aynı anahtarla farklı içerikte çakışmıştı. İşlem kaydedilmiş olabilir.',
}
export const RESEND_SAME = 'Aynı işlemi tekrar gönder'
export const ABANDON = 'Bu girişimi bırak'
export const UNKNOWN_FROZEN =
  'Sonuç netleşene kadar alanlar kilitli. Tekrar gönderim aynı işlem anahtarını kullanır; yeni kayıt açmaz.'

export const CONFLICT_TITLE = 'İşlem anahtarı farklı içerikle kullanılmış'
export const CONFLICT_TEXT =
  'Bu, önceki gönderimin yazılmadığını kanıtlamaz. Kaydı yeniden okuyun; yeni anahtar otomatik üretilmez.'

export const REFUSAL_TITLE: Readonly<Record<RefusalReason, string>> = {
  denied: 'Bu işlem için yetkiniz yok.',
  validation: 'Sunucu kaydı kabul etmedi.',
  'version-conflict': 'Kayıt başka bir işlemle değişti. Güncel kaydı okuyup yeniden inceleyin.',
}
export const REFUSAL_AFTER_UNKNOWN =
  'Tekrar gönderim reddedildi; bu, ilk gönderimin sonucunu değiştirmez.'

/** Stable issue codes from #258 PreparationValidator / Settings adapter. */
const ISSUE_TEXT: Readonly<Record<string, string>> = {
  identity_required: 'Kayıt kimlikleri eksik.',
  company_required: 'Şirket bağlamı yok.',
  bank_account_unavailable: 'Banka hesabı bilgisi alınamadı.',
  bank_account_ineligible: 'Banka hesabı bu şirkette, bu para biriminde veya tahsilat için kullanılamıyor.',
  party_account_unavailable: 'Cari hesap bilgisi alınamadı.',
  party_account_ineligible: 'Cari hesap bu şirkette aktif değil veya taraf ile eşleşmiyor.',
  settings_provider_unavailable: 'Tutar hassasiyet profili şu anda alınamadı.',
  settings_access_denied: 'Ayar bilgisine erişim yetkiniz yok.',
  profile_mismatch: 'Tutar hassasiyet profili bu hesap, para birimi veya tarih için geçerli değil.',
  settings_decimal_invalid: 'Tutar geçerli bir pozitif sayı değil.',
  settings_decimal_scale_exceeded: 'Tutarda izin verilenden fazla ondalık basamak var; yuvarlanmadı.',
  settings_profile_invalid: 'Tutar hassasiyet profili geçersiz.',
  finance_period_scope_mismatch: 'Dönem bilgisi bu işlemle eşleşmiyor.',
  finance_period_ineligible: 'İşlem tarihi uygun bir döneme ait değil.',
  finance_period_metadata_invalid: 'Dönem bilgisi eksik veya geçersiz.',
  // FIN-UI-1a (bank-account master, terminal refusals)
  draft_not_found: 'Bu taslak bu şirkette bulunamadı.',
  version_conflict: 'Kayıt başka bir işlemle değişti; güncel sürümü okuyun.',
  display_name_invalid: 'Hesap adı boş olamaz ve başında/sonunda boşluk olamaz.',
  finance_value_does_not_fit: 'Bir alan izin verilen uzunluğu aşıyor.',
  currency_not_a_candidate: 'Seçilen para birimi bu şirketin para birimleri arasında değil.',
  bank_account_not_found: 'Bu banka hesabı bu şirkette bulunamadı.',
  bank_account_not_proposed: 'Bu hesap onay beklemiyor; zaten onaylanmış olabilir.',
  finance_approver_is_proposer: 'Öneriyi yapan kişi kendi onay veya ret kararını veremez; başka bir yetkili karar vermeli.',
  finance_reversal_proposal_not_found: 'Ters kayıt önerisi bu şirkette bulunamadı.',
  finance_reversal_proposal_not_pending: 'Bu ters kayıt önerisi artık onay beklemiyor.',
  finance_source_not_found: 'Düzeltilecek finansal kaynak bu şirkette bulunamadı.',
  finance_source_not_recorded: 'Yalnız kesin kayda alınmış bir kaynak için ters kayıt önerilebilir.',
  finance_reversal_has_allocations: 'Bu kaynağa dağıtım bağlı; ters kayıt onaylanamaz.',
  finance_allocation_rule_unavailable: 'Dağıtım durumu doğrulanamadı; ters kayıt onayı kapalı.',
  finance_period_rule_unavailable: 'Ters kayıt dönemi doğrulanamadı; onay kapalı.',
  finance_period_not_open: 'Ters kayıt işlem tarihi açık bir muhasebe dönemine ait değil.',
  concurrent_change: 'Bu kaynak başka bir işlemle değişti; güncel durumu yeniden okuyun.',
  idempotency_key_invalid: 'İşlem anahtarı geçersiz.',
  settings_money_profile_not_found: 'Seçilen para birimi için onaylı tutar profili yok.',
}

export function issueText(code: string): string {
  return ISSUE_TEXT[code] ?? `Sunucu bu kaydı reddetti (kod: ${code}).`
}

export const PORT_MISSING_DETAIL = 'Finans servisi bu ortamda bağlı değil.'

// F-02 route pages (FIN-UI-1b)
export const RECEIPTS_TITLE = 'Tahsilat taslakları'
export const RECEIPTS_NOTICE =
  'Taslaklar para etkisi taşımaz. Yeni bir tahsilat taslağı ilgili cari hesabın ekranından başlatılır.'
export const RECEIPT_DRAFT_TITLE = 'Yeni tahsilat taslağı'
export const RECEIPT_DRAFT_NEEDS_PARTY =
  'Tahsilat taslağı bir cari hesaptan başlatılır. Cari hesabın ekranındaki "Tahsilat taslağı" bağlantısını kullanın.'
export const RECEIPT_DETAIL_TITLE = 'Tahsilat taslağı'
export const OPEN_SAVED_DRAFT = 'Kaydedilen taslağı aç'
export const BACK_TO_RECEIPTS = 'Tahsilat taslaklarına dön'
