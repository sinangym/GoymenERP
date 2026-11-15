/**
 * URT-S17 order cost user text, in Turkish and in its own file (TEK-03): the panel holds no literal text.
 *
 * SAT-D317: an order that is not closed has no receipt cost, so the panel says "Maliyet bekliyor" with the reason and
 * shows no figure; a return is valued at its linked issue's cost of that day inside the server's calculator. Every
 * figure is the server's recorded text. A code this file does not know falls back to the cost trace's words, then to
 * the code as it came — never guessed into a nearby meaning.
 */

import type { OrderCostStatus } from './orderCostContract'
import { costTraceCodeText } from './costTraceText'

export const ORDER_COST_TEXT = {
  heading: 'Emir maliyeti',
  help:
    'Emrin sunucudaki maliyet hesaplayıcısının kaydettiği maliyeti: kg maliyeti 4 ondalık, TL tutarları 2 ondalık ' +
    '(yarım sıfırdan uzağa yuvarlanır); yuvarlama farkı en çok kg tutan iyi ürün satırına yazılır.',
  subject: 'emir maliyeti',
  orderStatus: 'Emir durumu',
  costBasis: 'Maliyet esası',
  costPerKg: 'Mamul maliyeti (TL/kg)',
  rawMaterialCostPerKg: 'Hammadde maliyeti (TL/kg)',
  inputTotal: 'Giren maliyet (TL)',
  weighedScrapExpense: 'Tartılan hurda gideri (TL)',
  distributedTotal: 'Dağıtılan maliyet (TL)',
  remainder: 'Yuvarlama farkı (TL)',
  remainderLine: 'Yuvarlama farkını taşıyan etiket',
  noRemainderLine: 'Yuvarlama farkı yok.',
  linesCaption: 'Çıktı etiketlerine dağıtılan maliyet',
  outputLabel: 'Etiket',
  outputClass: 'Çıktı sınıfı',
  kilograms: 'Kg',
  unitCostPerKg: 'Birim maliyet (TL/kg)',
  amount: 'Tutar (TL)',
  separateScrapExpense: 'Ayrı hurda gideri (TL)',
  lineRemainder: 'Yuvarlama payı (TL)',
  allocationBasis: 'Dağıtım esası',
  notAllocated: 'Dağıtılmadı',
  noLines: 'Maliyet dağıtılan etiket yok.',
  currencyCaption: 'Döviz ile stoğa giren girdiler (yalnız bilgi)',
  issueMovement: 'Verme hareketi',
  currency: 'Döviz',
  foreignUnitCostPerKg: 'Döviz birim maliyeti (/kg)',
  exchangeRate: 'Kur',
  tlUnitCostPerKg: 'TL birim maliyeti (TL/kg)',
  keptKilograms: 'Tüketilen (kg)',
  foreignAmount: 'Döviz tutarı',
  issueReasonsCaption: 'Maliyeti hesaplatmayan girdiler',
  issueReason: 'Neden',
  reason: 'Neden',
  notes: 'Maliyet notları',
  notApplicable: '—',
  neverZero:
    'Maliyet yalnız sunucudaki maliyet hesaplayıcısından gelir. Hesaplanamadığında sıfır gösterilmez; neden yazılır.',
} as const

export const ORDER_COST_STATUS_TEXT: Record<OrderCostStatus, string> = {
  available: 'Maliyet hesaplandı',
  pending: 'Maliyet bekliyor — kesinleşmedi',
  unreadable: 'Maliyet okunamadı',
  unavailable: 'Maliyet hesaplanamıyor',
}

const CODE_TEXT: Record<string, string> = {
  input_cost_zero: 'Girdi maliyeti sıfır kayıtlı; maliyet hesaplanmadı.',
  input_exchange_rate_missing: 'Dövizli girdinin kaydedildiği günün kuru yok; maliyet hesaplanmadı.',
  output_exceeds_input: 'Çıktı kütlesi girdiden fazla; maliyet hesaplanmadı.',
  rounding_remainder_line_missing: 'Yuvarlama farkını taşıyacak iyi ürün satırı yok; maliyet hesaplanmadı.',
  rounding_remainder_not_absorbed: 'Yuvarlama farkı dağıtılamadı; maliyet hesaplanmadı.',
  'rounding-remainder-on-largest-good-product-line': 'Yuvarlama farkı en çok kg tutan iyi ürün satırında.',
  'allocated-at-product-cost-per-kg': 'Mamul kg maliyetiyle',
  'allocated-at-net-raw-material-cost-per-kg': 'Net hammadde kg maliyetiyle',
}

/** A reason, note or basis code in the user's words, or the cost trace's words, or the code as it came. */
export function orderCostCodeText(code: string): string {
  return CODE_TEXT[code] ?? costTraceCodeText(code)
}
