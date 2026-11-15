/**
 * Ayarlar > Vergi > Oran listeleri (F-029 S9, SAT-D181) — every user-facing text of the VAT rate and withholding ratio list
 * section. Kept here, never inline, so the screen stays localizable. Shared actions (save, cancel, reason, date, refusal
 * texts) come from ./taxMasterText.
 */

import type { RateListKind } from './taxRateListContract'

export const RATE_LIST_TEXT = {
  heading: 'KDV ve tevkifat oran listeleri',
  intro:
    'Şirketin kullandığı KDV oranları ve tevkifat oranları. Liste bütün olarak yeni sürüm olur; girilmeden boş kalır ve satış/alış ekranları oran listesini "kaynak yok" olarak görür. Oranlar mali müşavir teyidiyle girilir.',
  subject: 'oran listeleri',
  empty: 'Liste girilmemiş',
  inForceLabel: (label: string) => `${label} — bugün geçerli`,
  scheduledLabel: (label: string) => `${label} — planlı`,
  versionOf: (listVersion: string, date: string) => `Sürüm ${listVersion}, ${date} itibarıyla`,
  columnCode: 'Kod',
  columnName: 'Ad',
  columnPercent: 'Oran',
  columnTaxCode: 'GİB tevkifat kodu',
  percentOf: (percent: string) => `%${percent}`,
  taxCodeMissing: 'kod: kaynak yok',
  entryCount: (count: number) => `${count} oran`,
  publish: 'Yeni oran listesi sürümü',
  vatEntriesLabel: 'KDV oranları (her satır: KOD;Ad;Oran)',
  vatEntriesHelp:
    'Liste bütün olarak yeni sürüm olur; en çok 100 satır. Kod büyük harf, rakam veya "/" (en çok 16); oran 0 ile 100 arası, en çok 4 ondalık (örn. 20 veya 0.5).',
  vatEntriesInvalid: 'Her satır "KOD;Ad;Oran" biçiminde olmalı; oran 0 ile 100 arası olmalı; kodlar tekrar etmemeli.',
  withholdingEntriesLabel: 'Tevkifat oranları (her satır: KOD;Ad;Oran;GİB kodu)',
  withholdingEntriesHelp:
    'Liste bütün olarak yeni sürüm olur; en çok 100 satır. Oran tevkif edilen KDV payıdır (örn. 5/10 için 50), 0\'dan büyük, en fazla 100. GİB kodu isteğe bağlıdır; girilirse listenin başladığı gün yürürlükteki tevkifat kod listesinde olmalıdır.',
  withholdingEntriesInvalid:
    'Her satır "KOD;Ad;Oran" veya "KOD;Ad;Oran;GİB kodu" biçiminde olmalı; oran 0\'dan büyük, en fazla 100 olmalı; kodlar tekrar etmemeli.',
} as const

export const RATE_LIST_LABEL: Record<RateListKind, string> = {
  VatRate: 'KDV oranları',
  WithholdingRatio: 'Tevkifat oranları',
}
