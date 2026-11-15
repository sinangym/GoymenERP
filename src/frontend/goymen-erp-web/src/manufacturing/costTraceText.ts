/**
 * URT-S19 cost trace user text, in Turkish and in its own file (TEK-03): the panel holds no literal text.
 *
 * The trace shows the order's raw data (DEC-PRDCT-009: enerji, işçilik ve genel gider sonraya; ham veri şimdi). The
 * cost itself comes only from the one backend cost calculator; while it cannot answer, the panel says
 * "Maliyet hesaplanamıyor" with the server's reason and never shows a 0 or an estimate. A code this file does not know
 * is shown as it came, never guessed into a nearby meaning.
 */

import type { CostTraceCostStatus } from './costTraceContract'

export const COST_TRACE_TEXT = {
  heading: 'Maliyet izi',
  help:
    'Emrin kayıtlı ham verisi: hangi ana rulodan ne kadar kg verildiği, iade edildiği ve tüketildiği, hangi ' +
    'çıktının beyan edildiği ve emrin zaman çizgisi. Enerji, işçilik ve genel gider bu izde yer almaz.',
  method: 'Maliyet yöntemi',
  subject: 'maliyet izi',
  lotsCaption: 'Girdi lotları (ana rulolar)',
  parentRoll: 'Ana rulo',
  plannedKg: 'Planlanan (kg)',
  issuedKg: 'Verilen (kg)',
  returnedKg: 'İade (kg)',
  consumedKg: 'Tüketilen (kg)',
  issueCount: 'Verme sayısı',
  returnCount: 'İade sayısı',
  firstIssuedAt: 'İlk verme',
  lastIssuedAt: 'Son verme',
  owner: 'Ticari sahip',
  companyOwned: 'Şirket malı',
  customerOwned: 'Fason (taraf ve hesap kayıtlı)',
  totals: 'Toplam',
  noLots: 'Emrin planında girdi lotu yok.',
  outputsCaption: 'Beyan edilen çıktılar (ters kayıtlar düşülmüş)',
  outputClass: 'Çıktı sınıfı',
  netKg: 'Net (kg)',
  declarationCount: 'Beyan sayısı',
  reversalCount: 'Ters kayıt sayısı',
  noOutputs: 'Henüz beyan edilmiş çıktı yok.',
  timelineHeading: 'Zaman çizgisi',
  firstFactAt: 'İlk kayıt',
  lastFactAt: 'Son kayıt',
  closedAt: 'Kapanış',
  stoppage: 'Duruş',
  duration: 'Süre',
  noMoment: '—',
  salesHeading: 'Satış siparişi bağlantısı',
  noSalesLink: 'Emir bir satış siparişi satırına bağlı değil.',
  salesOrder: 'Sipariş',
  salesOrderLine: 'Sipariş satırı',
  customer: 'Müşteri',
  product: 'Ürün',
  lineRevision: 'Satır revizyonu',
  linkedAt: 'Bağlanma zamanı',
  costHeading: 'Maliyet',
  costBasis: 'Maliyet esası',
  costPerKg: 'Mamul maliyeti (TL/kg)',
  rawMaterialCostPerKg: 'Hammadde maliyeti (TL/kg)',
  weighedScrapExpense: 'Tartılan hurda gideri (TL)',
  notes: 'Maliyet notları',
  reason: 'Neden',
  neverZero:
    'Maliyet yalnız sunucudaki maliyet hesaplayıcısından gelir. Hesaplanamadığında sıfır gösterilmez; neden yazılır.',
} as const

export const COST_TRACE_COST_STATUS_TEXT: Record<CostTraceCostStatus, string> = {
  available: 'Maliyet hesaplandı',
  pending: 'Maliyet bekliyor — kesinleşmedi',
  unreadable: 'Maliyet okunamadı',
  unavailable: 'Maliyet hesaplanamıyor',
}

const METHOD_TEXT: Record<string, string> = {
  'weighted-average-raw-material-kg': 'Ağırlıklı ortalama (hammadde kg maliyeti esas)',
}

const CODE_TEXT: Record<string, string> = {
  time_records_not_recorded: 'Zaman kaydı tutulmuyor; süre hesaplanmaz.',
  cost_source_unavailable: 'Girdi maliyeti kaynağı bağlı değil (stoktan verilen maliyet henüz okunamıyor).',
  cost_source_scope_mismatch: 'Maliyet kaynağı başka bir şirket veya emir için cevap verdi; kullanılmadı.',
  cost_answer_invalid: 'Maliyet cevabı bu emre ait değil veya geçersiz; kullanılmadı.',
  company_required: 'Şirket bağlamı gerekli.',
  order_required: 'Emir gerekli.',
  order_not_found: 'Emir bulunamadı.',
  order_not_closed: 'Emir kapanmadan maliyet kesinleşmez.',
  scrap_price_missing: 'Hurdanın tartıldığı ayın hurda fiyatı girilmemiş.',
  scrap_price_unreadable: 'Hurda fiyatı okunamadı.',
  scrap_price_above_raw_material_cost: 'Hurda fiyatı hammadde maliyetinin üzerinde; maliyet hesaplanmadı.',
  input_cost_unreadable: 'Girdi maliyeti okunamadı.',
  scrap_setting_unreadable: 'Hurda maliyet ayarı okunamadı.',
  mass_unbalanced: 'Kütle dengesi tutmuyor; maliyet hesaplanmadı.',
  product_cost_not_positive: 'Mamul maliyeti sıfırdan büyük çıkmadı; maliyet hesaplanmadı.',
  cost_input_invalid: 'Maliyet girdisi geçersiz.',
  cost_arithmetic_overflow: 'Maliyet hesabı sınırı aştı.',
  cost_label_invalid: 'Bir etiketin maliyet verisi geçersiz.',
  cost_label_duplicate: 'Bir etiket iki kez geldi.',
  cost_label_mass_mismatch: 'Etiket kütleleri emrin çıktısıyla uyuşmuyor.',
  scrap_label_cost_not_decided: 'Hurda etiketinin maliyeti için karar yok.',
  paint_cost_not_included: 'Boya maliyeti dahil değil.',
  non_kg_input_cost_not_included: 'Kg dışı birimli girdi maliyeti dahil değil.',
  'production-input-average-plus-scrap': 'Üretim girdisi ortalaması + hurda',
  'production-input-average-less-scrap-value': 'Üretim girdisi ortalaması − hurda değeri',
}

/** The method's name, or the code as it came. */
export function costTraceMethodText(code: string): string {
  return METHOD_TEXT[code] ?? code
}

/** A reason, note or basis code in the user's words, or the code as it came — never guessed. */
export function costTraceCodeText(code: string): string {
  return CODE_TEXT[code] ?? code
}
