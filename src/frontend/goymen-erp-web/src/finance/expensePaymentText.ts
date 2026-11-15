/**
 * F-25 Masraf ödemeleri — every user-visible text of the screen (TEK-03): the payment not tied to a party
 * (FNS-D25). The expense item is shown by the label of the stable code the server sends; the record keeps only
 * the item's identity, never this text.
 */

import type { ExpensePaymentSourceKind, ExpensePaymentStatus } from './expensePaymentContract'

export const EXPENSE_PAYMENT_TEXT = {
  heading: 'Masraf Ödemeleri',
  intro: 'Cariye bağlı olmayan ödemeler. Kayıt hazırlanır, ayrı bir kişi onaylar ve ödeme gerçekleştiğinde kesin kayda alınır.',
  subject: 'masraf ödemeleri',
  listCaption: 'Masraf ödemeleri, en yeni önce',
  nextPageSubject: 'sonraki sayfa',
  loadMore: 'Daha fazla göster',
  loadingMore: 'Yükleniyor…',
  detailSubject: 'masraf ödemesi',
  detailHeading: 'Ödeme ayrıntısı',
  close: 'Kapat',
  open: 'Aç',
  openName: (occurredOn: string, description: string) => `Masraf ödemesini aç: ${occurredOn}, ${description}`,
  valueOnShort: 'Valör tarihi',
  referenceShort: 'Referans',
  accountOption: (displayName: string, currency: string) => `${displayName} (${currency})`,
  expenseItemChoose: 'Masraf kalemi seçin',
  columnOccurredOn: 'İşlem tarihi',
  columnSource: 'Kaynak',
  columnItem: 'Masraf kalemi',
  columnAmount: 'Tutar',
  columnDescription: 'Açıklama',
  columnStatus: 'Durum',
  columnAction: 'İşlem',
  newPayment: 'Yeni masraf ödemesi',
  formHeading: 'Yeni masraf ödemesi',
  cancelForm: 'Vazgeç',
  sourceKind: 'Ödeme kaynağı',
  sourceKindChoose: 'Kaynak seçin',
  account: 'Hesap',
  accountChoose: 'Hesap seçin',
  noEligibleAccount: 'Bu kaynakta ödeme yapılabilecek onaylı ve etkin hesap yok.',
  accountsSubject: 'ödeme hesapları',
  occurredOn: 'İşlem tarihi',
  valueOn: 'Valör tarihi (isteğe bağlı)',
  amount: 'Tutar',
  amountHelp: 'Ondalık ayırıcı olarak virgül kullanın; yuvarlama yapılmaz.',
  description: 'Açıklama',
  descriptionHelp: 'Zorunlu. Ödemenin neden yapıldığını yazın.',
  reference: 'Referans (isteğe bağlı)',
  expenseItem: 'Masraf kalemi',
  expenseItemsSubject: 'masraf kalemleri',
  expenseItemsEmpty: 'Şirket için tanımlı masraf kalemi yok; ödeme hazırlanamaz.',
  draftContextSubject: 'para birimi ve para profili',
  draftContextWaiting: 'Para birimi ve para profili, kaynak, hesap ve işlem tarihi seçildikten sonra okunur.',
  currency: 'Para birimi',
  saveDraft: 'Taslağı kaydet',
  saving: 'Kaydediliyor…',
  approve: 'Onayla',
  approving: 'Onaylanıyor…',
  ownDraftCannotApprove: 'Bu taslağı siz hazırladınız; onayı farklı bir kişi vermelidir.',
  record: 'Kesin kayda al',
  recording: 'Kaydediliyor…',
  recordConfirm: 'Ödemenin bankadan veya kasadan çıktığını onaylıyorum.',
  recordNeedsConfirm: 'Kesin kayıt için ödemenin çıktığını onaylayın.',
  balanceNote: 'Kesin kayıtta hesabın güncel bakiyesi sunucuda denetlenir; bakiye yetmiyorsa veya okunamıyorsa kayıt reddedilir.',
  preparedBy: 'Hazırlayan',
  approvedBy: 'Onaylayan',
  recordedBy: 'Kaydeden',
  preparedAt: 'Hazırlanma',
  approvedAt: 'Onay',
  recordedAt: 'Kayıt',
  noValue: '—',
  applied: 'İşlem tamamlandı.',
  appliedReplay: 'Bu istek daha önce işlenmişti; kayıtlı sonuç gösteriliyor.',
  failedHeading: 'İşlem reddedildi.',
  conflict: 'Kayıt siz bakarken değişti. Güncel hâli yükleyip yeniden deneyin.',
  reload: 'Güncel hâli yükle',
  unknownHeading: 'Sonuç bilinmiyor.',
  unknownBody: 'İsteğin sunucuya ulaşıp ulaşmadığı anlaşılamadı. Aynı isteği aynı anahtarla yeniden gönderin; ödeme iki kez oluşmaz.',
  previousUnknown: 'Önceki deneme sonuçlanmadı. Aynı isteği yeniden gönderin.',
  retrySame: 'Aynı isteği yeniden gönder',
  forbiddenCommand: 'Bu işlem için yetkiniz yok.',
  sessionEnded: 'Oturumunuz sona erdi; yeniden giriş yapın.',
  badRequest: 'İstek sunucu tarafından okunamadı.',
  notFound: 'Ödeme bulunamadı.',
  invalidAmount: {
    required: 'Tutar zorunludur.',
    format: 'Tutar geçerli bir sayı değil.',
    'not-positive': 'Tutar sıfırdan büyük olmalıdır.',
  },
  descriptionRequired: 'Açıklama zorunludur.',
  descriptionTooLong: (max: number) => `Açıklama en fazla ${max} karakter olabilir.`,
  referenceTooLong: (max: number) => `Referans en fazla ${max} karakter olabilir.`,
  dateRequired: 'İşlem tarihi zorunludur.',
} as const

export const SOURCE_KIND_LABEL: Record<ExpensePaymentSourceKind, string> = { Bank: 'Banka', Cash: 'Kasa' }

export const STATUS_LABEL: Record<ExpensePaymentStatus, string> = {
  Draft: 'Taslak',
  Approved: 'Onaylandı',
  Recorded: 'Kesin kayıtlı',
}

/** FNS-D25: one item today. An unknown code is shown as unknown, never guessed. */
const EXPENSE_ITEM_LABEL: Readonly<Record<string, string>> = { 'general-expense': 'Masraf' }

export function expenseItemLabel(code: string | null): string {
  return code === null ? 'Bilinmeyen kalem' : EXPENSE_ITEM_LABEL[code] ?? 'Bilinmeyen kalem'
}

const ISSUE_MESSAGE: Readonly<Record<string, string>> = {
  expense_payment_source_invalid: 'Ödeme kaynağı geçersiz.',
  identity_required: 'Hesap veya para birimi eksik.',
  expense_item_required: 'Masraf kalemi zorunludur.',
  expense_item_unknown: 'Masraf kalemi tanınmıyor.',
  expense_payment_description_required: 'Açıklama zorunludur.',
  expense_payment_description_too_long: 'Açıklama çok uzun.',
  expense_payment_reference_invalid: 'Referans geçersiz.',
  value_date_before_occurred_date: 'Valör tarihi işlem tarihinden önce olamaz.',
  expense_payment_treasury_account_ineligible: 'Seçilen hesap ödeme için uygun değil.',
  expense_payment_treasury_account_passivation_pending:
    'Seçilen kasanın pasife alma talebi onay bekliyor; bu kasadan yeni ödeme kaydedilemez.',
  expense_payment_not_approved: 'Ödeme onaylanmadan kesin kayda alınamaz.',
  expense_payment_not_confirmed: 'Ödemenin çıktığını onaylamanız gerekir.',
  payment_not_found: 'Ödeme bulunamadı.',
  payment_not_draft: 'Yalnız taslak değiştirilebilir.',
  payment_approval_invalid: 'Ödeme onaylanamaz: taslak değil ya da hazırlayan kişi onaylıyor.',
  payment_identity_invalid: 'Ödeme kimliği geçersiz.',
  version_conflict: 'Kayıt siz bakarken değişti.',
  concurrent_change: 'Kayıt aynı anda başka bir işlemle değişti.',
  expected_version_invalid: 'Sürüm bilgisi geçersiz.',
  profile_mismatch: 'Para profili bu ödeme için geçerli değil.',
  settings_profile_invalid: 'Para profili geçersiz.',
  settings_provider_unavailable: 'Para profili şu anda okunamıyor.',
  finance_operation_mapping_unavailable: 'Para işlemi ayarı tanımlı değil.',
  finance_treasury_balance_unavailable: 'Hesabın güncel bakiyesi okunamadı; kesin kayıt yapılamaz.',
  finance_treasury_balance_insufficient: 'Hesabın güncel bakiyesi bu ödeme için yetmiyor.',
  idempotency_key_invalid: 'İstek anahtarı geçersiz.',
  same_key_payload_mismatch: 'Aynı anahtarla farklı bir istek gönderildi.',
}

export function issueMessage(code: string): string {
  return ISSUE_MESSAGE[code] ?? `Sunucu isteği reddetti (${code}).`
}
