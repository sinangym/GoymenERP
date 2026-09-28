/**
 * User text of the first-release party ledger (cari hesap ekstresi). Turkish, in one localizable file.
 */

import type { PartyLedgerBalanceSide, PartyLedgerEntryKind } from './partyLedgerContract'

export const LEDGER_TITLE = 'Cari ekstre'
export const LEDGER_OPEN = 'Cari ekstre'
export const LEDGER_OPEN_HINT = 'Bu cari hesabın kesinleşmiş finans kayıtlarını tarih aralığıyla gösterir.'
export const LEDGER_NOTICE =
  'Yalnız kesinleşmiş kayıtlar: fatura taksitleri (açık kalem), tahsilatlar, ters kayıtlar, çek/senet tahsilleri ve '
  + 'ödemeler. Taslaklar ve dağıtımlar (kapama) satır değildir; dağıtım bakiyeyi değiştirmez.'
/** OPEN-018: carry-forward is an open decision — say so, never imply an opening balance. */
export const LEDGER_NO_CARRY_FORWARD =
  'Devir yok (OPEN-018): bakiye seçilen aralığın ilk gününde sıfırdan başlar; aralıktan önceki kayıtlar taşınmaz.'
export const LEDGER_PER_CURRENCY = 'Her para birimi ayrı gösterilir; tutarlar çevrilmez ve toplanmaz.'
export const LEDGER_FROM = 'Başlangıç tarihi'
export const LEDGER_TO = 'Bitiş tarihi'
export const LEDGER_SHOW = 'Ekstreyi göster'
export const LEDGER_RANGE_INVALID = 'Başlangıç ve bitiş için geçerli tarihler seçin; bitiş başlangıçtan önce olamaz.'
export const LEDGER_EMPTY = 'Bu aralıkta bu cari hesap için kesinleşmiş kayıt yok.'
export const LEDGER_TOO_MANY =
  'Bu aralıkta tek ekstrede gösterilebilecekten fazla kayıt var. Bakiyeyi eksik göstermemek için ekstre verilmedi; '
  + 'aralığı daraltın.'
export const LEDGER_REFUSED_INVALID = 'Sunucu bu tarih aralığını kabul etmedi; aralığı kontrol edin.'
export const LEDGER_UNAVAILABLE =
  'Cari ekstre şu anda alınamadı: bu okuma henüz yetkilendirilmemiş olabilir ya da servis geçici olarak kapalı.'
export const LEDGER_FORBIDDEN = 'Bu cari hesabın ekstresini görme yetkiniz yok.'
export const LEDGER_NEEDS_ACCOUNT = 'Cari ekstre bir cari hesaptan açılır; cari ve hesabı eksik ya da geçersiz.'
export const LEDGER_BACK = 'Tahsilat taslaklarına dön'

export const COLUMN_DATE = 'Tarih'
export const COLUMN_KIND = 'İşlem'
export const COLUMN_SOURCE = 'Kaynak'
export const COLUMN_DUE = 'Vade'
export const COLUMN_DEBIT = 'Borç'
export const COLUMN_CREDIT = 'Alacak'
export const COLUMN_BALANCE = 'Bakiye'
export const TOTALS = 'Toplam'
export const CLOSING_BALANCE = 'Aralık sonu bakiyesi'

export const KIND_LABEL: Readonly<Record<PartyLedgerEntryKind, string>> = {
  OpenItem: 'Fatura taksiti (açık kalem)',
  ManualReceipt: 'Tahsilat',
  ManualReceiptReversal: 'Tahsilat ters kaydı',
  CustomerInstrumentCollection: 'Çek/senet tahsili',
  ManualBankPayment: 'Ödeme',
}

/** Borçlu / alacaklı from the company's view of the party's account. */
export const BALANCE_SIDE_SUFFIX: Readonly<Record<PartyLedgerBalanceSide, string>> = {
  Debit: ' (B)',
  Credit: ' (A)',
  Zero: '',
}

export const BALANCE_SIDE_HINT = '(B) borç bakiyesi — cari borçlu; (A) alacak bakiyesi — cari alacaklı.'

export const OPEN_RECEIPT = 'Tahsilatı aç'
export const OPEN_REVERSAL = 'Ters kaydı aç'
export const OPEN_ORIGINAL = 'Ters kaydedilen tahsilatı aç'
export const OPEN_INSTRUMENT = 'Çek/senedi aç'
export const OPEN_COLLECTION_RECEIPT = 'Tahsilat kaydını aç'
export const OPEN_PAYMENT = 'Ödemeyi aç'
/** An admitted open item has no detail screen yet: its official document is shown by reference only. */
export const OPEN_ITEM_NO_SCREEN = 'Resmî belge (ayrıntı ekranı yok)'

export function reference(id: string): string {
  return `ref. …${id.slice(-8)}`
}

export function accountReference(partyId: string, companyAccountId: string): string {
  return `Cari ref. …${partyId.slice(-8)} · hesap ref. …${companyAccountId.slice(-8)}`
}
