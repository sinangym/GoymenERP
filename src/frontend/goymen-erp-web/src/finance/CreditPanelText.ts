/**
 * Turkish text for the Cari card's Finance panel (F018 plan §L3; FNS-D27 (2), FNS-D31 (4), FNS-D58).
 *
 * Same rule as ./CollateralText.ts: chosen from a decoded state or a stable code, never parsed from server prose. The
 * panel only READS: limits and collaterals are edited on their own Finance screens. A figure Finance cannot compute
 * exactly says "hesaplanamadı" (fail-closed); nothing here suggests a zero, a default or an unlimited limit.
 */

import type { CreditPanelFigureName } from './CreditPanelContract'

export const CREDIT_PANEL_TEXT = {
  title: 'Kredi limiti ve teminat',
  subject: 'kredi limiti ve teminat bilgisi',
  notice:
    'Tutarlar para birimi başına, çevrilmeden gösterilir. Teminat yalnız kendi para birimindeki alacağı karşılar; ' +
    'başka para birimindeki teminat karşılık sayılmaz, kurla çevrilmez. Brüt risk ve limit boşluğu Satış risk hesabındadır; bu ' +
    'panel yalnız Finans\'ın kayıtlarını okur. Evraklı risk ayrı gösterilir, teminat veya riske eklenmez.',
  asOf: 'Gösterim tarihi (şirket iş günü, Türkiye saati)',
  forbidden: 'Bu cari hesabın kredi limiti ve teminat bilgisini görme yetkiniz yok.',
  zero: 'Yok (sıfır)',
  unavailable: 'hesaplanamadı',
  notSet: 'Limit tanımlı değil',
  withheld: 'Yetkiniz yok / gösterilmiyor',
  openCreditLimits: 'Kredi limitlerini aç',
  openCollaterals: 'Teminatları aç',
} as const

export const CREDIT_PANEL_LABEL: Readonly<Record<CreditPanelFigureName, string>> = {
  creditLimit: 'Kredi limiti',
  openReceivables: 'Açık alacak',
  heldCollateral: 'Mevcut teminat',
  weightedCollateral: 'Sayılan teminat',
  collateralUtilization: 'Teminat doluluğu',
  documentaryRisk: 'Evraklı risk',
}

export const CREDIT_PANEL_HINT: Readonly<Record<CreditPanelFigureName, string>> = {
  creditLimit: 'Hesabın onaylı, geçerli kredi limiti.',
  openReceivables: 'Açık alacak kalemleri, dağıtılmamış tahsilatlar düşülerek.',
  heldCollateral: 'Gösterim tarihinde geçerli, alınmış teminatların kayıtlı tutarı.',
  weightedCollateral: 'Aynı teminatlar × Ayarlar\'daki tür oranı / 100.',
  collateralUtilization:
    'Açık alacağın aynı para birimindeki sayılan teminatla karşılanan kısmı; para birimi başına küçük olan. ' +
    'Başka para birimindeki teminat karşılık sayılmaz (sıfır).',
  documentaryRisk: 'Tahsil edilmemiş ve iade edilmemiş müşteri çek/senetleri. Ayrı gösterilir.',
}

/** Stable reason codes → why a figure is not shown. An unknown code is named, never guessed. */
export const CREDIT_PANEL_REASON: Readonly<Record<string, string>> = {
  finance_credit_limit_not_set: 'Bu hesap için onaylı kredi limiti yok; sıfır veya sınırsız sayılmaz.',
  finance_credit_limit_unreadable: 'Kredi limiti kaydı tam okunamadı.',
  finance_open_item_unreadable: 'Açık kalemlerden biri tam okunamadı; kısmi toplam gösterilmez.',
  finance_collateral_unreadable: 'Teminat kayıtlarından biri tam okunamadı; kısmi toplam gösterilmez.',
  finance_business_day_unavailable:
    'Şirket iş günü bu sunucuda belirlenemedi; teminat geçerliliği UTC tarihiyle tahmin edilmez.',
  finance_collateral_type_rate_unavailable:
    'Bir teminat türünün oranı Ayarlar\'dan okunamadı; varsayılan oran kullanılmaz.',
  finance_collateral_utilization_source_unavailable:
    'Açık alacak veya sayılan teminat hesaplanamadığı için doluluk hesaplanamaz.',
  finance_documentary_risk_status_unknown: 'Bir çek/senedin durumu tanınmıyor; evraklı risk hesaplanmaz.',
  finance_documentary_risk_unreadable: 'Bir çek/senedin tutarı tam okunamadı; kısmi toplam gösterilmez.',
  finance_documentary_risk_withheld: 'Çek/senet tutarlarını görme yetkiniz yok.',
  finance_credit_limit_withheld: 'Kredi limitlerini görme yetkiniz yok.',
  finance_open_receivables_withheld: 'Açık alacak kredi limiti yetkisiyle gösterilir.',
  finance_collateral_withheld: 'Teminatları görme yetkiniz yok.',
  finance_collateral_utilization_withheld: 'Doluluk için hem kredi limiti hem teminat yetkisi gerekir.',
}

export const creditPanelReasonText = (code: string): string =>
  CREDIT_PANEL_REASON[code] ?? `Tanımsız gerekçe kodu: ${code}`
