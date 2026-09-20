import { financeRef } from './financeText'

/**
 * Turkish text for F-04 Banka Hesapları (the bank-account master, FIN-UI-1a).
 *
 * Same rule as ./financeText.ts: chosen from a decoded state or a stable code, never parsed from
 * server prose. An account here is a MASTER record — no balance, no statement, no money movement —
 * so no text may suggest one.
 */

export const BANK_MASTER_TITLE = 'Banka hesapları'
export const BANK_MASTER_SUBJECT = 'banka hesapları'
export const BANK_MASTER_NOTICE =
  'Hesap kaydı tanımdır; bakiye, ekstre veya para hareketi göstermez. Önerilen bir hesap, önerenden farklı bir yetkili onaylayana kadar tahsilatta kullanılamaz.'
export const BANK_MASTER_EMPTY = 'Bu şirkette gösterilecek banka hesabı yok.'
export const BANK_PROPOSE_LINK = 'Yeni banka hesabı öner'

export const BANK_COLUMN = {
  name: 'Hesap adı',
  currency: 'Para birimi',
  state: 'Durum',
  action: 'İşlem',
} as const

export const BANK_STATE_APPROVED = 'Onaylı — tahsilatta kullanılabilir'
export const BANK_STATE_PENDING = 'Onay bekliyor'
export const BANK_OWN_PROPOSAL = 'Sizin öneriniz — başka bir yetkili onaylamalı.'

/**
 * B3 (Sinan #66 5750700247). Three separate sentences, not two, because the answer's point is that an
 * UNDECLARED card is not a "no": it is a card nobody has answered for, and Sinan asks for that state
 * to be shown specifically. Saying "para hareketine uygun değil" for it would be the screen making a
 * declaration the record does not contain.
 */
export const BANK_ELIGIBILITY = {
  Eligible: 'Para hareketine uygun',
  NotEligible: 'Para hareketine uygun değil — tanım olarak tutuluyor',
  Undeclared: 'Para hareketine uygunluk beyanı tamamlanmamış',
} as const

/** Why an undeclared card is refused, said once, where the state is shown. */
export const BANK_UNDECLARED_HINT =
  'Bu kart beyan edilmeden önce oluşturulmuş. Yetkili bir güncelleme ve Ö4 onayı tamamlanana kadar ödeme/tahsilatta kullanılamaz; kart ve geçmişi olduğu gibi duruyor.'

/** K2's card fields on the master row. An empty mandatory text is a pre-K2 row, said as such. */
export const BANK_CARD = {
  bank: 'Banka',
  type: 'Hesap türü',
  iban: 'IBAN',
  missing: 'Girilmemiş',
  noIban: 'IBAN yok',
} as const

/** D4 (#66 5749046401): the IBAN change waiting for a second authorised person. */
export const BANK_PENDING_IBAN_TITLE = 'IBAN değişikliği onay bekliyor'
export const bankPendingIban = (iban: string) => `Önerilen IBAN: ${iban}`
export const bankPendingIbanReason = (reason: string) => `Gerekçe: ${reason}`
export const BANK_PENDING_IBAN_NOTICE =
  'Onaylanana kadar para işlemlerinde hesabın mevcut onaylı IBAN’ı kullanılmaya devam eder.'
export const BANK_PENDING_IBAN_OWN = 'Bu değişikliği siz önerdiniz — başka bir yetkili onaylamalı.'
export const BANK_IBAN_CHANGE_LINK = 'IBAN değişikliği öner'
export const BANK_IBAN_CHANGE_APPROVE = 'IBAN değişikliğini onayla'
/**
 * Shown when the currency's code is not known to this caller (the candidate read is the proposer's).
 * One reference shape across Finance (ORG #356 INFO-2): the shared `financeRef`, the last 8 characters, as
 * Accounting shows a reference too. It is a labelled reference, never the currency's name.
 */
export const currencyFallback = (currencyId: string) => `Para birimi ${financeRef(currencyId)}`

export const APPROVE = 'Onayla'
export const APPROVE_CONFIRM_TITLE = 'Bu hesabı onaylıyor musunuz?'
export const APPROVE_CONFIRM_TEXT =
  'Onaydan sonra hesap tahsilat taslaklarında seçilebilir. Onay tektir; hesabı öneren kişi onaylayamaz.'
export const APPROVE_CONFIRM = 'Evet, onayla'
export const APPROVE_CANCEL = 'Vazgeç'
export const APPROVED_NOTICE = 'Hesap onaylandı. Liste sunucudan yeniden okundu.'
export const APPROVED_REPLAYED = 'Bu onay daha önce kaydedilmişti; aynı sonuç gösteriliyor.'

export const PROPOSE_TITLE = 'Yeni banka hesabı öner'
export const PROPOSE_NOTICE =
  'Öneri kaydedilir ancak onaylanana kadar kullanılamaz. Banka adı ve hesap türü zorunludur; şube, IBAN ve hesap numarası isteğe bağlıdır.'
export const PROPOSE_BANK = 'Banka adı'
export const PROPOSE_TYPE = 'Hesap türü'
export const PROPOSE_TYPE_HINT = 'Bankanın kullandığı tür (ör. "Vadesiz TL", "Kredi hesabı").'
export const PROPOSE_BRANCH_NAME = 'Şube adı'
export const PROPOSE_BRANCH_CODE = 'Şube kodu'
export const PROPOSE_IBAN = 'IBAN'
export const PROPOSE_ACCOUNT_NUMBER = 'Hesap numarası'
export const PROPOSE_OPTIONAL = 'isteğe bağlı'

/**
 * B3's declaration on the form. It is asked as a statement the person makes, not inferred from the
 * fields they filled in: eligibility comes from the declaration, so adding an IBAN to a card must
 * never silently make it money-eligible.
 */
export const PROPOSE_ELIGIBLE = 'Bu hesap ödeme/tahsilatta kullanılacak'
export const PROPOSE_ELIGIBLE_HINT =
  'İşaretlerseniz geçerli bir IBAN zorunludur. İşaretlemezseniz hesap tanım olarak kaydedilir ve para hareketinde kullanılamaz (IBAN’sız özel kredi/vadeli hesaplar böyle tanımlanır).'
export const PROPOSE_NAME = 'Hesap adı'
export const PROPOSE_NAME_HINT = 'Ekipte hesabı tanıyacağınız ad (ör. "Ana TL hesabı").'
export const PROPOSE_CURRENCY = 'Para birimi'
export const PROPOSE_CURRENCY_PLACEHOLDER = 'Para birimi seçin'
export const PROPOSE_NO_CANDIDATES = 'Bu şirket için tanımlı para birimi yok; öneri yapılamaz.'
export const PROPOSE_SUBMIT = 'Öner'
export const PROPOSE_SUBJECT = 'para birimleri'
export const PROPOSED_NOTICE = 'Hesap önerildi; onay bekliyor. Başka bir yetkili onaylayana kadar kullanılamaz.'
export const PROPOSED_REPLAYED = 'Bu öneri daha önce kaydedilmişti; aynı sonuç gösteriliyor.'
export const BACK_TO_LIST = 'Banka hesaplarına dön'

export const PROPOSE_ERROR = {
  nameRequired: 'Hesap adı zorunludur.',
  nameSpaces: 'Hesap adının başında veya sonunda boşluk olamaz.',
  nameTooLong: 'Hesap adı en fazla 200 karakter olabilir.',
  currencyRequired: 'Para birimi seçin.',
  bankRequired: 'Banka adı zorunludur.',
  bankTooLong: 'Banka adı en fazla 200 karakter olabilir.',
  typeRequired: 'Hesap türü zorunludur.',
  typeTooLong: 'Hesap türü en fazla 64 karakter olabilir.',
  // B3 + K2: the rule attaches to the DECLARATION, so the message says which choice requires it.
  ibanRequired: 'Ödeme/tahsilatta kullanılacak hesapta IBAN zorunludur.',
  ibanInvalid: 'IBAN biçimi geçersiz. IBAN doğru biçimde olsa bile hesabın size ait olduğunu kanıtlamaz.',
} as const

/** D4: proposing the IBAN change of an approved account. */
export const IBAN_CHANGE_TITLE = 'IBAN değişikliği öner'
export const IBAN_CHANGE_NOTICE =
  'Yeni IBAN, sizden farklı bir yetkili onaylayana kadar kullanılmaz. O ana kadar para işlemlerinde mevcut onaylı IBAN geçerlidir; eski değer ve geçmiş işlem bağları korunur.'
export const IBAN_CHANGE_NEW = 'Yeni IBAN'
export const IBAN_CHANGE_REASON = 'Gerekçe'
export const IBAN_CHANGE_REASON_HINT = 'Denetimde eski/yeni değerle birlikte görünür.'
export const IBAN_CHANGE_SUBMIT = 'Değişikliği öner'
export const IBAN_CHANGE_PROPOSED = 'IBAN değişikliği önerildi; onay bekliyor.'
export const IBAN_CHANGE_REPLAYED = 'Bu öneri daha önce kaydedilmişti; aynı sonuç gösteriliyor.'
export const IBAN_CHANGE_APPROVED = 'IBAN değişikliği onaylandı. Liste sunucudan yeniden okundu.'
export const IBAN_CHANGE_ERROR = {
  ibanRequired: 'Yeni IBAN zorunludur.',
  ibanInvalid: 'IBAN biçimi geçersiz.',
  reasonRequired: 'Gerekçe zorunludur.',
  reasonTooLong: 'Gerekçe en fazla 500 karakter olabilir.',
} as const
