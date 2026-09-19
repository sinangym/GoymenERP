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
/** Shown when the currency's code is not known to this caller (the candidate read is the proposer's). */
export const currencyFallback = (currencyId: string) => `Para birimi kimliği …${currencyId.slice(-4)}`

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
  'Öneri kaydedilir ancak onaylanana kadar kullanılamaz. Banka adı, IBAN veya şube bu adımda istenmez.'
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
} as const
