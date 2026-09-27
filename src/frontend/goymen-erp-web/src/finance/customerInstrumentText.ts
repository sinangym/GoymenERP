/**
 * DEC-FIN-016 first release — the Turkish words of the customer cheque / note portfolio. Kept apart from
 * ./financeText.ts so the receipt screens' copy does not move.
 */

import type {
  CustomerInstrumentAction,
  CustomerInstrumentKind,
  CustomerInstrumentRiskBucketName,
  CustomerInstrumentStatus,
} from './customerInstrumentContract'

export const PORTFOLIO_TITLE = 'Çek / senet portföyü'
export const PORTFOLIO_SUBJECT = 'çek/senet portföyü'
export const PORTFOLIO_NOTICE =
  'Müşteriden alınan çek ve senetler. Portföye almak para etkisi yaratmaz; müşterinin borcu çek/senet tahsil '
  + 'edildiğinde nominal tutarla bir kez kapanır.'
export const PORTFOLIO_UNAVAILABLE = 'Çek/senet portföyü şu anda kullanılamıyor.'
export const PORTFOLIO_EMPTY = 'Portföyde çek veya senet yok.'
export const INSTRUMENT_SUBJECT = 'çek/senet'
export const OPEN_INSTRUMENT = 'Aç'
export const CLOSE_INSTRUMENT = 'Kapat'

export const KIND_LABEL: Readonly<Record<CustomerInstrumentKind, string>> = {
  Cheque: 'Müşteri çeki',
  PromissoryNote: 'Müşteri senedi',
}

export const STATUS_LABEL: Readonly<Record<CustomerInstrumentStatus, string>> = {
  InPortfolio: 'Portföyde',
  AtBankForCollection: 'Bankada (tahsilde)',
  Collected: 'Tahsil edildi',
  Bounced: 'Karşılıksız',
  Protested: 'Protestolu',
  ReturnedToCustomer: 'Müşteriye iade edildi',
}

export function actionLabel(action: CustomerInstrumentAction, kind: CustomerInstrumentKind): string {
  switch (action) {
    case 'DepositForCollection':
      return 'Bankaya tahsile ver'
    case 'WithdrawFromBank':
      return 'Bankadan portföye geri al'
    case 'Dishonour':
      return kind === 'Cheque' ? 'Karşılıksız olarak işaretle' : 'Protesto olarak işaretle'
    case 'ReturnToCustomer':
      return 'Müşteriye iade et'
    case 'Collect':
      return 'Tahsil edildi olarak kaydet'
  }
}

export const COLUMN = {
  kind: 'Tür',
  serial: 'Seri / belge no',
  drawer: 'Keşideci / borçlu',
  dueOn: 'Vade',
  amount: 'Tutar',
  currency: 'Para birimi',
  status: 'Durum',
  actions: 'İşlem',
} as const

export const FACT = {
  kind: 'Tür',
  serial: 'Seri / belge no',
  drawer: 'Keşideci / borçlu',
  bank: 'Banka / şube',
  dueOn: 'Vade tarihi',
  receivedOn: 'Alış tarihi',
  amount: 'Nominal tutar',
  currency: 'Para birimi',
  status: 'Durum',
  statusChangedOn: 'Son durum tarihi',
  treasury: 'Banka hesabı',
  description: 'Açıklama',
  collectionSource: 'Tahsilat kaydı',
} as const

export const NONE = 'Yok'
export const MOVEMENTS_TITLE = 'Hareketler'
export const MOVEMENT_RECEIVED = 'Portföye alındı'
export const OPEN_COLLECTION_SOURCE = 'Tahsilat kaydını aç'
export const COLLECTED_NOTICE =
  'Tahsil edildi: tek para olayı oluştu ve müşteri nominal tutarla bir kez alacaklandı. Tahsilat kaydı diğer '
  + 'tahsilatlar gibi açık kalemlere dağıtılır.'

/** The parts of DEC-FIN-016 this release does not do, each with the blocker that holds it. */
export const NOT_YET = [
  'Ciro (tedarikçiye) — müşteri alacağının hangi anda kapanacağı ve tedarikçi tarafı kararı bekleniyor.',
  'Bankaya teminat — teminat/risk politikası (OPEN-010) bekleniyor.',
  'Tahsil sonrası ters akış (tahsil edilmiş çekin iadesi) — karar bekleniyor; tahsilat kaydı ters kayda kapalıdır.',
  'Muhasebe fişi — hesap planı ve fiş onayı kararları bekleniyor.',
] as const
export const NOT_YET_TITLE = 'Bu sürümde yok'

export const FORM = {
  title: 'Çek / senet ile tahsilat (portföye al)',
  notice: 'Portföye almak para etkisi yaratmaz. Müşteri borcu çek/senet tahsil edildiğinde kapanır.',
  kind: 'Tür',
  serial: 'Seri / belge no',
  drawer: 'Keşideci / borçlu',
  bankName: 'Çekin bankası',
  bankNameNote: 'Senet için banka (opsiyonel)',
  branchName: 'Şube (opsiyonel)',
  dueOn: 'Vade tarihi',
  receivedOn: 'Alış tarihi',
  amount: 'Nominal tutar',
  currency: 'Para birimi',
  currencyPlaceholder: 'Para birimi seçin',
  noCurrency: 'Tahsilata uygun banka hesabı olmadığından para birimi seçilemiyor.',
  description: 'Açıklama (opsiyonel)',
  submit: 'Portföye al',
  submitting: 'Kaydediliyor',
  saved: 'Portföye alındı — para etkisi yok.',
  openSaved: 'Portföyde aç',
} as const

export const ACTION_FORM = {
  date: 'İşlem tarihi',
  collectedOn: 'Tahsil tarihi',
  valueOn: 'Valör (opsiyonel)',
  bank: 'Banka hesabı',
  bankPlaceholder: 'Banka hesabı seçin',
  note: 'Not (opsiyonel)',
  confirm: 'Onayla',
  cancel: 'Vazgeç',
} as const

export const MESSAGE = {
  invalid: 'Alanları kontrol edin.',
  unknown: 'Sonuç bilinmiyor. Aynı işlem aynı anahtarla yeniden gönderilebilir.',
  denied: 'Bu işlem için yetkiniz yok.',
  unavailable: 'Servis şu anda kullanılamıyor.',
  conflict: 'Kayıt başka bir işlemle değişti; yeniden açıp tekrar deneyin.',
  refused: 'İşlem reddedildi',
  done: 'İşlem kaydedildi.',
} as const

/** A refusal code in the user's words where one is known; the code itself otherwise (never hidden). */
export const ISSUE_TEXT: Readonly<Record<string, string>> = {
  instrument_duplicate: 'Bu çek/senet portföyde zaten kayıtlı.',
  instrument_bank_name_required: 'Çekin bankası zorunludur.',
  instrument_transition_not_allowed: 'Bu durumdayken bu işlem yapılamaz.',
  instrument_date_before_previous_movement: 'Tarih önceki hareketten önce olamaz.',
  instrument_bank_mismatch: 'Çek hangi bankaya verildiyse o banka hesabına tahsil edilir.',
  bank_account_ineligible: 'Banka hesabı bu çek/senet için uygun değil (onaylı ve aynı para biriminde olmalı).',
  finance_period_not_open: 'Bu tarih için muhasebe dönemi açık değil.',
  finance_period_rule_unavailable: 'Dönem kontrolü yapılamadı.',
  settings_decimal_scale_exceeded: 'Tutarın ondalık hanesi izin verilenden fazla.',
  settings_money_profile_not_found: 'Bu para birimi için onaylı para profili yok.',
  party_account_ineligible: 'Cari hesap uygun değil.',
}

// Risk view (user decision 2026-09-27): facts only, no limit or warning (OPEN-010).
export const RISK_TITLE = 'Çek/senet riski'
export const RISK_NOTICE =
  'Müşteri borcu çek/senet tahsil edildiğinde kapanır; risk durumu ayrıca izlenir. Tablo seçilen günün resmidir: o güne '
  + 'kadar alınmış çek/senetler, o gün geçerli durumlarıyla. Yalnız olgular gösterilir: limit veya uyarı eşiği yoktur '
  + '(OPEN-010).'
export const RISK_AS_OF = 'Tarih itibarıyla'
export const RISK_EMPTY = 'Çek/senet riski yok.'
export const RISK_UNAVAILABLE = 'Çek/senet riski şu anda alınamıyor.'
export const RISK_CUSTOMERS = 'Carilere göre'

export const RISK_LABEL: Readonly<Record<CustomerInstrumentRiskBucketName, string>> = {
  inPortfolioNotDue: 'Portföyde — vadesi gelmemiş',
  inPortfolioOverdue: 'Portföyde — vadesi geçmiş',
  atBankForCollection: 'Bankada (tahsilde)',
  dishonoured: 'Karşılıksız / protestolu',
  returnedToCustomer: 'Müşteriye iade edildi',
  collected: 'Tahsil edildi',
  openExposure: 'Açık risk (tahsil ve iade edilmemiş)',
}
