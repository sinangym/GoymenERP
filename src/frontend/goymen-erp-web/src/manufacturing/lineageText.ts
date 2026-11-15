/**
 * URT-S22 lineage user text, in Turkish and in its own file (TEK-03): the panel holds no literal text.
 *
 * The shares are kg shares from the one backend share calculator (URT-D63) and are shown as the server wrote them,
 * unrounded: no display rounding is chosen here. While the shares are not calculated the panel says why and shows no
 * share. A code this file does not know is shown as it came, never guessed into a nearby meaning.
 */

import type { OrderLineageShareStatus } from './lineageContract'

export const LINEAGE_TEXT = {
  heading: 'Soy ağacı',
  help:
    'Emrin çıktı etiketlerinin hangi girdi etiketlerinden üretildiği (geri iz) ve girdi etiketlerinin hangi ' +
    'çıktılara girdiği (ileri iz). Paylar tüketilen kg üzerinden, sunucudaki tek pay hesaplayıcısından gelir ve ' +
    'yuvarlanmadan gösterilir.',
  show: 'Soy ağacını göster',
  hide: 'Soy ağacını gizle',
  subject: 'soy ağacı',
  tabsLabel: 'Soy ağacı yönü',
  backwardTab: 'Geri iz',
  forwardTab: 'İleri iz',
  backwardCaption: 'Geri iz: her çıktı etiketi ve onu oluşturan bütün girdi etiketleri',
  forwardCaption: 'İleri iz: her girdi etiketi ve girdiği bütün çıktılar',
  outputLabel: 'Çıktı etiketi',
  outputDeclaration: 'Çıktı beyanı',
  inputLabel: 'Girdi etiketi',
  consumedKg: 'Tüketilen (kg)',
  share: 'Pay',
  totalConsumedKg: 'Toplam tüketilen (kg)',
  shareStatus: 'Pay durumu',
  reason: 'Neden',
  order: 'Emir',
  noShare: '—',
  noInputs: 'Bu çıktı için girdi etiketi kaydı yok.',
  upstreamHeading: 'Bir adım geri: bu emrin girdi etiketlerini üreten diğer emirler',
  downstreamHeading: 'Bir adım ileri: bu emrin çıktı etiketlerini tüketen diğer emirler',
  noUpstream: 'Bu emrin girdi etiketlerini üreten başka bir emir kaydı yok.',
  noDownstream: 'Bu emrin çıktı etiketleri henüz başka bir emirde tüketilmedi.',
  truncated: 'Soy ağacı okuma sınırına ulaştı; burada gösterilen liste soy ağacının tamamı değil.',
} as const

export const LINEAGE_SHARE_STATUS_TEXT: Record<OrderLineageShareStatus, string> = {
  calculated: 'Pay hesaplandı',
  pending: 'Pay bekliyor',
  unreadable: 'Pay okunamadı',
}

const CODE_TEXT: Record<string, string> = {
  lineage_no_input: 'Çıktının girdi etiketi kaydı yok; pay hesaplanmadı.',
  lineage_input_invalid: 'Bir girdi kaydı geçersiz; pay hesaplanmadı.',
  lineage_input_duplicate: 'Bir girdi etiketi iki kez geldi; pay hesaplanmadı.',
  lineage_input_without_consumption: 'Bir girdi etiketinin tüketilen kg değeri yok; pay hesaplanmadı.',
  lineage_arithmetic_overflow: 'Pay hesabı sınırı aştı.',
}

/** The calculator's code in the user's words, or the code as it came — never guessed. */
export function lineageCodeText(code: string): string {
  return CODE_TEXT[code] ?? code
}
