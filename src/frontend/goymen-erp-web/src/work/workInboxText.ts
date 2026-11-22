/**
 * W1-A Work inbox — every user-facing word of the "Benden karar bekleyen" view: kind and module names, the owner rule
 * codes and reference roles of the explanation card (AI-D32), the approve permission names ("kimde bekliyor"), the
 * duration indicator (YOL-D18) and the measurement-free screen texts. One map per concept; an unknown code is shown by a
 * fail-closed generic text beside its technical code, never guessed.
 */

import { CAPABILITY_NAV_LABEL } from '../shell/navigation'
import { DURATION_CLASS_HOURS, type WorkInboxDurationClass } from './workInboxContract'

/** Kind → Turkish name (one per connected owner source). */
export const WORK_INBOX_KIND_TEXT: Readonly<Record<string, string>> = {
  'sales.price-floor-exception.v1': 'Taban fiyat altı istisnası',
  'sales.discount-exception.v1': 'İskonto istisnası',
  'sales.risk-exception.v1': 'Risk istisnası',
  'logistics.dispatch-note.v1': 'İrsaliye onayı',
  'logistics.plan-excess.v1': 'Plan üstü sevk',
  'logistics.weighing-difference.v1': 'Tartım farkı',
  'manufacturing.plan.v1': 'Üretim planı onayı',
  'purchasing.order.v1': 'Alış siparişi onayı',
  'purchasing.incoming-invoice-variance.v1': 'Gelen fatura farkı',
  'purchasing.goods-receipt-batch.v1': 'Mal kabul partisi',
  'finance.party-credit-limit.v1': 'Cari kredi limiti teklifi',
  'finance.manual-bank-payment.v1': 'Elle banka ödemesi',
}

export const WORK_INBOX_MODULE_TEXT: Readonly<Record<string, string>> = {
  sales: 'Satış',
  logistics: 'Lojistik',
  manufacturing: 'Üretim',
  purchasing: 'Satın alma',
  finance: 'Finans',
}

/** Owner rule code → the sentence the explanation card shows. Codes of other modules are added as their sources land. */
export const WORK_INBOX_RULE_TEXT: Readonly<Record<string, string>> = {
  // Finance
  'finance.credit-limit-change': 'Limit değişikliği önerisi',
  'finance.credit-limit-new': 'İlk kez limit tanımlama önerisi',
  'finance.credit-limit-currency-change': 'Para birimi değişen limit önerisi (fark hesaplanmaz)',
  'finance.credit-limit-removal': 'Limit kaldırma önerisi',
  'finance.credit-limit-revision-stale': 'Öneriden sonra limit değişti; bu öneri onaylanamaz, yalnız reddedilebilir',
  'finance.manual-bank-payment-approval-required': 'Her elle banka ödemesi hazırlayandan farklı bir kişinin onayını ister',
  // Sales (price figures may be absent when the actor may not read prices: only what is present is shown)
  'sales.price-below-floor': 'Taban fiyatın altında satış (taban / teklif edilen net fiyat / fark)',
  'sales.discount-over-limit': 'İskonto rol limitini aşıyor',
  'sales.discount-limit-undefined': 'Rol için iskonto limiti tanımlı değil; her iskonto onaya düşer',
  'sales.discount-without-list-price': 'Liste fiyatı olmayan satır; iskonto ölçülemediği için onay gerekir',
  'sales.shipment-risk-exception': 'Tek sevkiyatlık risk istisnası talebi (risk limiti aşımı)',
  // Purchasing
  'purchasing.order-approval-required': 'Her satın alma siparişi hazırlayandan farklı bir kişinin onayını gerektirir',
  'purchasing.order-supplier-revised': 'Onaylı sipariş cari değişikliğiyle yeniden taslağa döndü; tekrar onay bekliyor',
  'purchasing.invoice-price-variance': 'Fatura satır tutarı kabul edilen tutardan tolerans dışında farklı',
  'purchasing.invoice-quantity-variance': 'Fatura miktarı kabul edilen miktarla aynı değil',
  'purchasing.invoice-exchange-rate-variance': 'Fatura kuru kabuldeki kontrol kurundan tolerans dışında farklı',
  'purchasing.goods-receipt-kg-outside-tolerance': 'Teslim alınan kg tolerans yüzdesinin dışında',
  'purchasing.goods-receipt-kg-tolerance-not-set': 'Ayarlar\'da kg toleransı tanımlı değil; her fark onay bekler',
  'purchasing.goods-receipt-batch-approval-required': 'Toplu mal kabulü onay bekliyor',
  'purchasing.goods-receipt-kg-difference': 'Teslim alınan toplam kg ile beklenen kg arasındaki fark',
  // Logistics
  'logistics.dispatch-note-draft': 'İrsaliye taslağı onay bekliyor; onaylanınca numaralanır',
  'logistics.plan-excess-quantity': 'Talimat miktarının üzerinde sevk önerildi (plan / fazla miktar)',
  'logistics.plan-excess-already-approved': 'Bu satırda daha önce onaylanmış plan üstü miktar var',
  'logistics.plan-excess-line-blocked': 'Satır blokeli; blok kalkmadan onaylanamaz',
  'logistics.weighing-excess-rate': 'Karşılanmayan kantar fazlası onay oranına ulaştı',
  'logistics.weighing-excess-rate-unreadable': 'Onay oranı okunamadı; fark güvenli tarafta onaya düştü',
  'logistics.weighing-scale-difference': 'İrsaliye kg ile kantar kg farkı',
  'logistics.weighing-uncovered-excess': 'Serbest stokla karşılanmayan fazla kg',
  'logistics.weighing-entered-by-unknown': 'Kantarı veya sevki gireni tam bilinmiyor; onaylanamaz',
  // Manufacturing
  'manufacturing.plan-first-approval': 'Taslak plan ilk onayını bekliyor',
  'manufacturing.plan-reapproval-after-order-cancel': 'Emri iptal edilen onaylı plan yeniden onay bekliyor',
  'manufacturing.plan-rejected-not-revised': 'Plan reddedildi ve henüz revize edilmedi',
  'manufacturing.plan-due-date-missing': 'Termin girilmemiş; plan onaylanamaz',
}

/** Reference role → label in "Dayandığı kayıtlar". */
export const WORK_INBOX_ROLE_TEXT: Readonly<Record<string, string>> = {
  'company-account': 'Cari hesap',
  party: 'Cari',
  currency: 'Para birimi',
  'current-currency': 'Mevcut limitin para birimi',
  'bank-account': 'Banka hesabı',
  document: 'Bağlı belge',
  'sales-order-draft': 'Satış siparişi',
  'price-list': 'Fiyat listesi',
  product: 'Ürün',
  supplier: 'Tedarikçi',
  'purchase-request': 'Satın alma talebi',
  'purchase-order': 'Satın alma siparişi',
  'goods-receipt-label': 'Etiket',
  'loading-instruction': 'Yükleme talimatı',
  'dispatch-note': 'İrsaliye',
  'manufacturing-order': 'Üretim emri',
}

/** The owners' approve permission names ("kimde bekliyor"). */
export const WORK_INBOX_PERMISSION_TEXT: Readonly<Record<string, string>> = {
  'sales.orders.price-floor-exception.approve': 'Taban fiyat altı istisnası onayı',
  'sales.orders.discount-exception.approve': 'İskonto istisnası onayı',
  'sales.orders.risk-exception.approve': 'Risk istisnası onayı',
  'logistics.dispatch-notes.approve': 'İrsaliye onayı',
  'logistics.shipments.plan-excess.approve': 'Plan üstü sevk onayı',
  'logistics.weighing-differences.approve': 'Tartım farkı onayı',
  'manufacturing.plans.approve': 'Üretim planı onayı',
  'purchasing.orders.approve': 'Alış siparişi onayı',
  'purchasing.invoices.approve-variance': 'Alış faturası fark onayı',
  'purchasing.goods-receipts.approve-variance': 'Mal kabul fark onayı',
  'finance.credit-limits.approve': 'Cari kredi limiti onayı',
  'finance.manual-bank-payment.approve': 'Elle banka ödemesi onayı',
}

export const inboxKindText = (kind: string): string => WORK_INBOX_KIND_TEXT[kind] ?? kind
export const inboxModuleText = (module: string): string => WORK_INBOX_MODULE_TEXT[module] ?? module
/** Null when the code has no words here: the card then shows the generic text and the code. */
export const inboxRuleText = (code: string): string | null => WORK_INBOX_RULE_TEXT[code] ?? null
export const inboxRoleText = (role: string): string | null => WORK_INBOX_ROLE_TEXT[role] ?? null

/** This map first, then a non-empty shell navigation label of the same name, else the raw permission name. */
export function inboxPermissionText(permission: string): string {
  const own = WORK_INBOX_PERMISSION_TEXT[permission]
  if (own !== undefined) return own
  const nav = (CAPABILITY_NAV_LABEL as Readonly<Record<string, string | undefined>>)[permission]
  return nav !== undefined && nav.length > 0 ? nav : permission
}

/**
 * The owner's invariant decimal text ("-1234.5") in Turkish digits ("-1.234,5") by string handling only: no float,
 * nothing rounded or recomputed. Anything not in the contract form is returned unchanged.
 */
export function formatInvariantDecimal(value: string): string {
  const match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(value)
  if (match === null) return value
  const [, sign, integer, fraction] = match
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, '.')
  return `${sign}${grouped}${fraction === undefined ? '' : `,${fraction}`}`
}

/** A number with its unit: Turkish percent sign before the number, any other unit after it. */
export function formatWithUnit(value: string, unit: string | null): string {
  const number = formatInvariantDecimal(value)
  if (unit === null) return number
  return unit === '%' ? `%${number}` : `${number} ${unit}`
}

/** A document label that is still a bare GUID (no number yet) is shown as "…" and its last 8 characters. */
export function shortDocumentLabel(label: string): string {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(label) ? `…${label.slice(-8)}` : label
}

/** "4 saat" / "2 iş günü" from the server's class and length. */
export const durationClassText = (durationClass: WorkInboxDurationClass, limit: number): string =>
  durationClass === DURATION_CLASS_HOURS ? `${limit} saat` : `${limit} iş günü`

export const WORK_INBOX_TEXT = {
  heading: 'İş kutusu',
  scope:
    'Kararınızı bekleyen kayıtlar en eskisi önce gösterilir. Yalnız onaylama yetkiniz olan türler ve başkalarının hazırladığı kayıtlar listelenir.',
  nativeOnly:
    'Karar bu ekrandan verilmez; kaydın sahibi modül ekranında, o modülün yetki, kişi ayrılığı ve sürüm kurallarıyla verilir.',
  viewsLabel: 'İş kutusu görünümleri',
  viewAwaitingMyDecision: 'Benden karar bekleyen',
  viewPreparedByMe: 'Benim hazırladıklarım',
  viewOverdue: 'Geciken',
  viewAll: 'Tümü',
  soon: 'Yakında',
  subject: 'kararınızı bekleyen kalemler',
  listLabel: 'Kararınızı bekleyen kalemler (en eskisi önce)',
  noInboxAuthority: 'Bu görünüme bağlı onay türlerinden hiçbirinin onay yetkisi bu oturumda görünmüyor.',
  module: 'Modül',
  document: 'Belge',
  waiting: 'Bekliyor',
  duration: 'Süre',
  awaiting: 'Kimde bekliyor',
  awaitingText: (permissionLabel: string) => `${permissionLabel} iznine sahip onaycılar`,
  openOwner: 'Sahip ekranda aç',
  openOwnerLabel: (kindText: string, documentLabel: string) => `${kindText} — ${documentLabel}: sahip ekranda aç`,
  explanationToggle: 'Neden onaya düştü',
  rulesHeading: 'Neden onaya düştü',
  referencesHeading: 'Dayandığı kayıtlar',
  noReferences: 'Bağlı kayıt yok.',
  explanationNote: 'Bu açıklama sunucudaki kural sonuçlarından üretilir; yapay zekâ kullanılmaz.',
  ruleUnknown: 'Kural açıklaması tanımlı değil',
  roleUnknown: 'Kayıt türü tanımlı değil',
  threshold: 'Eşik',
  actual: 'Gerçekleşen',
  deviation: 'Sapma',
  /** Duration indicator (YOL-D18). */
  due: (dueText: string) => `Son: ${dueText}`,
  overdue: 'Süresi geçti',
  durationUndefined: 'Süre tanımlı değil',
  durationUnavailable: 'Süre okunamadı',
  /** Filters. */
  filtersLabel: 'Filtre',
  filterKind: 'Onay türü',
  filterModule: 'Modül',
  filterAll: 'Tümü',
  filterOverdue: 'Yalnız süresi geçenler',
  clearFilters: 'Filtreyi temizle',
  /** Source notices. */
  unavailable: (labels: readonly string[]) =>
    `Şu onay türleri şu anda okunamadı, kalemleri listede yok: ${labels.join(', ')}.`,
  hasMore: (labels: readonly string[]) =>
    `Şu onay türlerinde listede gösterilenden fazla bekleyen kayıt var; tamamı sahip ekranda: ${labels.join(', ')}.`,
  emptyReadable: 'Okunabilen onay türlerinde kararınızı bekleyen kalem yok.',
  /** The B-25 queue under the inbox items. */
  legacyHeading: 'Diğer bağlı onay kaynakları',
} as const

/** The inbox URL's filter members (kept in the address for Back/Forward and sharing). */
export const INBOX_FILTER_PARAM = { kind: 'tur', module: 'modul', overdue: 'geciken' } as const
