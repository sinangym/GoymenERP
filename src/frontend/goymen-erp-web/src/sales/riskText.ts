/**
 * MOD-01 / API-06 / UI-15: the S-04 customer risk card's user texts. Turkish, in one place, so they stay localizable. They only
 * say what the server decided; no text implies a number the server did not answer (the remaining limit is not served).
 */

import type { RiskFigureKind, RiskFigureState, RiskReasonComponent, RiskResult, RiskTaxBasis } from './riskCheck'
import type { RiskExceptionState } from './riskException'

export const RISK_CHECK_TEXT = {
  heading: 'Müşteri riski',
  note:
    'Brüt risk = açık alacak + faturalanmamış sevkiyat + sevk edilmemiş onaylı sipariş kalanı; karar, bu siparişle birlikte ' +
    'brüt riskin kredi limitiyle limitin kendi para biriminde karşılaştırılmasıdır. Hepsi sunucudaki risk hesaplayıcısından ' +
    'gelir; ekranda toplanmaz, çevrilmez ve yeniden hesaplanmaz.',
  subject: 'müşteri riski',
  unreadable: 'Risk kontrolü okunamadığı için hiçbir sonuç ve tutar gösterilmiyor; risk "limit içinde" sayılmaz.',
  serviceUnavailable:
    'Risk servisi şu anda yanıt veremiyor (Satış kaydı ya da yetki tanımı okunamadı). Sonuç gösterilmiyor; biraz sonra yeniden deneyin.',
  forbidden: 'Bu siparişin risk kontrolünü görme yetkiniz yok.',
  supportCode: (correlationId: string) => `Destek kodu: ${correlationId}`,
  resultLabel: 'Risk sonucu',
  result: {
    Within: 'Limit içinde',
    Over: 'Limit aşılıyor',
    Unavailable: 'Risk kontrolü tamamlanamadı',
  } satisfies Record<RiskResult, string>,
  resultNote: {
    Within: 'Bu siparişle birlikte brüt risk kredi limitini aşmıyor (limite eşit olmak limit içidir).',
    Over: 'Uyarı: bu siparişle birlikte brüt risk kredi limitini aşıyor. Sevkiyatta risk yeniden kesin kontrol edilir.',
    Unavailable:
      'Riskin bir parçası, kur ya da kredi limiti yanıtlanamadığı için karar verilemedi. Bu, "limit içinde" anlamına gelmez; ' +
      'gerekçeler aşağıda.',
  } satisfies Record<RiskResult, string>,
  noSourceYet: 'Risk kalemlerinin hiçbiri henüz bir kaynaktan yanıt vermedi; gösterilecek tutar yok.',
  withheld: 'Risk tutarları fiyat bilgisidir; fiyatları görme yetkiniz olmadığı için gösterilmiyor. Sonuç ve gerekçeler gösteriliyor.',
  hidden: 'Gizli',
  notAvailable: 'Yok',
  recordedZero: 'Kayıtlı tutar yok (sıfır)',
  limitHeading: 'Limit özeti',
  creditLimit: 'Kredi limiti',
  used: 'Kullanılan (brüt risk)',
  usedWithThisOrder: 'Bu siparişle birlikte',
  available: 'Kullanılabilir limit',
  availableNotServed: 'Sunucu bu değeri vermiyor; ekranda hesaplanmaz.',
  limitCurrency: 'Limit para birimi',
  limitCurrencyUnknown: 'Kredi limiti tek bir tutar olarak tanımlı olmadığı için limit para birimi yok.',
  localHeading: 'Yerel para biriminde (bilgi amaçlı)',
  localNote: 'Yerel para birimindeki toplam yalnız bilgidir; karar ona göre verilmez.',
  localUnknown: 'Yerel para biriminin ölçeği okunamadığı için yerel toplam yok.',
  figuresCaption: 'Risk kalemleri',
  column: { kind: 'Kalem', state: 'Durum', amount: 'Tutar', taxBasis: 'Vergi esası', reason: 'Gerekçe' },
  kind: {
    OpenReceivables: 'Açık alacak',
    ShippedNotInvoiced: 'Faturalanmamış sevkiyat',
    ApprovedUnshippedRemainder: 'Sevk edilmemiş onaylı sipariş kalanı',
    ThisOrder: 'Bu sipariş',
    CreditLimit: 'Kredi limiti',
    Collateral: 'Teminat (yalnız gösterim)',
  } satisfies Record<RiskFigureKind, string>,
  state: {
    Available: 'Yanıtlandı',
    NoSource: 'Kaynağı yok',
    Unavailable: 'Okunamadı',
  } satisfies Record<RiskFigureState, string>,
  taxBasis: {
    IncludingTax: 'KDV dahil',
    ExcludingTax: 'KDV hariç',
  } satisfies Record<RiskTaxBasis, string>,
  collateralNote: 'Teminat yalnız gösterilir; brüt riske ve karara girmez (net risk politikası açık, OPEN-010).',
  reasonsHeading: 'Karar verilemeyen gerekçeler',
  reasonComponent: {
    OpenReceivables: 'Açık alacak',
    ShippedNotInvoiced: 'Faturalanmamış sevkiyat',
    ApprovedUnshippedRemainder: 'Onaylı sipariş kalanı',
    ThisOrder: 'Bu sipariş',
    CreditLimit: 'Kredi limiti',
    Collateral: 'Teminat',
    Check: 'Karşılaştırma',
  } satisfies Record<RiskReasonComponent, string>,
  /** Every stable reason code the Sales risk read, its calculator and the Finance owner answer today. */
  reasonCode: {
    // Sales risk read (`SalesDraftRiskReasonCodesV1`).
    party_risk_provider_unavailable: 'Müşteri finans kaynağı (Finans) bağlı değil ya da yanıt vermedi.',
    party_risk_answer_invalid: 'Müşteri finans kaynağı başka bir müşteri/şirket ya da geçersiz bir değer yanıtladı.',
    invoicing_owner_missing: 'Satış faturaları henüz yok; sevk edilip faturalanmamış tutar bilinmiyor.',
    delivery_provider_unavailable: 'Sevkiyat kaynağı (Lojistik) bağlı değil ya da yanıt vermedi.',
    delivery_answer_invalid: 'Sevkiyat kaynağı geçersiz bir değer yanıtladı.',
    order_line_unpriced: 'Onaylı bir siparişin fiyatı olmayan satırı var; fiyatsız satır sıfır sayılmaz.',
    price_unit_conversion_unavailable: 'Bir satırın fiyat birimi sipariş biriminden farklı; birim çevrimi yapılmaz.',
    order_basis_unreadable: 'Yeniden onay bekleyen bir siparişin onaylı miktarları okunamadı.',
    vat_inclusive_amount_unavailable: 'Sipariş tutarı riske KDV dahil girer; KDV dahil tutar henüz hesaplanamıyor (KDV kaynağı yok).',
    order_read_bound_exceeded: 'Müşterinin açık onaylı siparişi tek okumanın sınırından fazla; eksik toplam gösterilmez.',
    // The calculator (`SalesRiskReasonCodes`).
    no_source: 'Bu kalemin kaynağı henüz yok.',
    unavailable: 'Bu kalem okunamadı.',
    exchange_rate_unavailable: 'Başka para birimindeki tutar için gereken TCMB kuru kaydı yok; tahmini kurla çevrilmez.',
    amount_scale_exceeded: 'Tutar para biriminin ondalık ölçeğinden fazla basamak taşıyor; yuvarlanmaz.',
    amount_invalid: 'Tutar geçersiz (negatif ya da sayı sınırı dışında).',
    tax_basis_missing: 'Tutarın KDV esası bildirilmedi.',
    tax_basis_mismatch: 'Kalemler aynı KDV esasında değil; toplanmaz.',
    credit_limit_not_defined: 'Kredi limiti tek bir tutar olarak tanımlı değil; limitsiz ya da sıfır sayılmaz.',
    limit_currency_unavailable: 'Kredi limitinin para biriminin ölçeği okunamadı.',
    // The Finance owner (`FinanceSalesPartyRiskReadPortV1`).
    finance_credit_limit_owner_missing: 'Kredi limiti tanımı (Finans) henüz yok.',
    finance_collateral_owner_missing: 'Teminat tanımı (Finans) henüz yok.',
    finance_open_item_unreadable: 'Açık kalemlerden biri okunamadı; kısmi toplam gösterilmez.',
    finance_customer_unidentified: 'Müşteri Finans tarafında tanımlanamadı.',
  } as Readonly<Record<string, string>>,
  unknownReason: (code: string) => `Tanımsız gerekçe kodu: ${code}`,
  amountIn: (amount: string, currency: string) => `${amount} ${currency}`,
} as const

/** The Turkish text of a reason code; an unknown code is named as such, never hidden. */
export function riskReasonText(code: string): string {
  return RISK_CHECK_TEXT.reasonCode[code] ?? RISK_CHECK_TEXT.unknownReason(code)
}

/**
 * API-07 (EK21 SAT-D153…D162): the single-shipment risk exception section's user texts. An exception releases ONE shipment for
 * the approval's Türkiye business day only; it opens no revision and never shows an amount (SAT-D162).
 */
export const RISK_EXCEPTION_TEXT = {
  heading: 'Tek sevk risk istisnası',
  note:
    'Risk limiti aşıldığında tek bir sevkiyat için istisna istenebilir. Onay, onaylandığı iş gününün (Türkiye saati) sonuna kadar ' +
    'geçerlidir; sevkiyat iptal edilirse istisna düşer. İsteyen kişi kendi talebini onaylayamaz. İstisna sipariş revizyonu açmaz.',
  subject: 'Risk istisnaları',
  empty: 'Bu sipariş için risk istisnası yok.',
  forbidden: 'Risk istisnalarını görme yetkiniz yok.',
  unreadable: 'Risk istisnaları okunamadı; bir istisnanın geçerli olduğu varsayılmaz.',
  serviceUnavailable: 'Risk istisnası servisi şu an kullanılamıyor; bir istisnanın geçerli olduğu varsayılmaz.',
  requestHeading: 'İstisna iste',
  shipment: 'Yükleme talimatı kimliği',
  shipmentHint: 'Lojistik ekranındaki yükleme talimatının kimliği (yalnız bir sevkiyat).',
  reason: 'Gerekçe',
  request: 'Risk istisnası iste',
  approve: 'Onayla',
  reject: 'Reddet',
  rejectionReason: 'Ret gerekçesi',
  retry: 'Aynı isteği yeniden gönder',
  invalidShipment: 'Geçerli bir yükleme talimatı kimliği girin.',
  invalidReason: 'Gerekçe zorunludur.',
  reasonTooLong: (max: number) => `Gerekçe en fazla ${max} karakter olabilir.`,
  invalidRejection: 'Ret için gerekçe zorunludur.',
  submitting: 'Gönderiliyor…',
  requested: 'İstisna talebi kaydedildi; onay bekliyor.',
  approved: 'İstisna onaylandı.',
  rejected: 'İstisna reddedildi.',
  replayed: ' (Aynı istek daha önce kaydedilmişti; kayıtlı sonuç gösteriliyor.)',
  unknown: 'Sonuç bilinmiyor: istek kaydedilmiş olabilir. Aynı isteği yeniden gönderin; yeni bir istek açılmaz.',
  unauthenticated: 'Oturumunuz sona erdi; yeniden giriş yapın.',
  noAuthority: 'Bu işlem için yetkiniz yok.',
  notFound: 'Sipariş ya da istisna bulunamadı.',
  conflict: 'İşlem güncel kayıtla çakıştı; liste yenilendi.',
  failed: 'İşlem tamamlanamadı.',
  failedWithCode: (code: string) => `İşlem tamamlanamadı (${code}).`,
  problem: {
    sales_risk_exception_duplicate: 'Bu sevkiyat için onay bekleyen ya da bugün geçerli bir istisna zaten var.',
    sales_risk_exception_order_not_eligible: 'Sipariş onaylı ve açık değil; istisna istenemez ya da karara bağlanamaz.',
    sales_risk_exception_self_decision: 'İstisnayı isteyen kişi kendi talebini onaylayamaz ya da reddedemez.',
    sales_risk_exception_reason_too_long: 'Gerekçe izin verilen uzunluğu aşıyor; kısaltılmadan kaydedilmez.',
    version_conflict: 'İstisna bu arada değişti; liste yenilendi.',
    sales_state_conflict: 'İstisna zaten karara bağlanmış; liste yenilendi.',
    concurrent_change: 'Kayıt aynı anda değişti; liste yenilendi.',
    same_key_payload_mismatch: 'Aynı istek anahtarı farklı içerikle kullanılmış; yeni bir istek başlatın.',
    validation_failed: 'İstek geçersiz.',
  } as Readonly<Record<string, string>>,
  exceptionHeading: (no: number) => `İstisna #${no}`,
  state: { Requested: 'Onay bekliyor', Approved: 'Onaylandı', Rejected: 'Reddedildi' } as Readonly<Record<RiskExceptionState, string>>,
  shipmentFact: (id: string) => `Yükleme talimatı: ${id}`,
  requestedBy: (who: string, at: string) => `İsteyen: ${who} · ${at}`,
  decidedBy: (who: string, at: string) => `Karar veren: ${who} · ${at}`,
  validToday: (day: string) => `Bugün geçerli (iş günü ${day}).`,
  lapsed: (day: string) => `Süresi doldu (iş günü ${day} sona erdi).`,
  supportCode: (id: string) => `Destek kodu: ${id}`,
} as const
