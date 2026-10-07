/**
 * ST-01 stock list words and display rules (Stok UX FAZ-1, coordinator handoff STOK-UX-d821e6b8 r7).
 *
 * The page title, the "Etiket kodu" field and the `?etiket=` parameter keep their accepted names: a
 * terminology rename is a separate package. No API name, enum value or id reaches the person; where the
 * contract publishes no readable value (location, another group company's ownership) the cell says "—"
 * rather than a guessed name or a developer explanation.
 */
import { formatDecimalTextTr } from '../finance/decimalText'
import type { StockClassification, StockPartyKind } from './stockDisplayContract'

export const STOCK_TEXT = {
  heading: 'Stok / Malzeme Pozisyonu',
  searchLabel: 'Etiket kodu',
  searchButton: 'Ara',
  searchTooLong: 'Etiket kodu en çok 64 karakter olabilir.',
  otherFilters: 'Diğer filtreler',
  classificationFilter: 'Sınıf',
  classificationAll: 'Tümü',
  columns: 'Sütunlar',
  visibleColumns: 'Görünen sütunlar',
  activeFilters: 'Etkin filtreler',
  clearFilters: 'Filtreleri temizle',
  loading: 'Stok pozisyonları yükleniyor…',
  loadingMore: 'Sonraki stok satırları yükleniyor…',
  emptyNoFilter: 'Bu şirkette eşleşen fiziksel stok yok.',
  emptyFiltered: 'Filtrelere uyan fiziksel stok yok.',
  windowFull: 'Liste penceresi doldu; filtreleri daraltın.',
  loadMore: 'Daha fazla göster',
  loadMoreFailed: 'Sonraki stok satırları okunamadı.',
  restart: 'Listeyi baştan yükle',
  stateSubject: 'stok pozisyonları',
  noCompany: 'Yetkili tek şirket bağlamı seçilmeden stok pozisyonları görüntülenemez.',
  unknown: '—',
  productCard: 'Ürün kartı',
  locationCard: 'Lokasyon kartı',
  heat: 'Döküm',
  coil: 'Rulo',
  custodian: 'Malı tutan',
  column: {
    label: 'Etiket / lot',
    product: 'Ürün',
    dimension: 'Ölçü (mm)',
    classification: 'Sınıf',
    location: 'Lokasyon',
    owner: 'Mal sahibi',
    physical: 'Fiziksel kg',
    blocked: 'Bloklu kg',
    allocated: 'Tahsisli kg',
    available: 'Kullanılabilir kg',
    freePhysical: 'Blokajsız fiziksel kg',
    actions: 'İşlemler',
  },
  filterChip: {
    label: 'Etiket kodu',
    product: 'Ürün',
    location: 'Lokasyon',
    owner: 'Mal sahibi',
    classification: 'Sınıf',
    remove: (what: string) => `${what} filtresini kaldır`,
  },
  /** The row filter actions: each filters by the row's own exact id, never by a guessed or typed value. */
  filterBy: {
    product: (labelCode: string) => `${labelCode}: bu ürüne göre filtrele`,
    location: (labelCode: string) => `${labelCode}: bu lokasyona göre filtrele`,
    owner: (labelCode: string) => `${labelCode}: bu mal sahibine göre filtrele`,
  },
  /** A filtered location or product whose readable name is not in the rows on screen. */
  selectedLocation: 'seçili lokasyon',
  selectedProduct: 'seçili ürün',
  selectedOwner: 'seçili mal sahibi',
} as const

/** The Inventory classification axis in the accepted Turkish words; "Hurda" is a value, never the column. */
export const CLASSIFICATION_TEXT: Record<StockClassification, string> = {
  Standard: 'Standart',
  SecondQuality: 'İkinci kalite',
  Scrap: 'Hurda',
}

/** A label with no classification yet (the existing ST-02 wording). */
export const UNCLASSIFIED_TEXT = 'Sınıflandırılmamış'

export function classificationText(value: StockClassification | null): string {
  return value === null ? UNCLASSIFIED_TEXT : CLASSIFICATION_TEXT[value]
}

/**
 * Who owns the stock, in the decided words. A company owner other than the company on screen is a real case (a
 * group company's goods), and "Şirket malı" there would read as this company's own: the contract publishes no
 * company name, so the cell says "—" and keeps the exact-id filter.
 */
export function ownerText(kind: StockPartyKind, id: string, companyId: string): string {
  if (kind === 'Counterparty') return 'Cari malı'
  return id === companyId ? 'Şirket malı' : STOCK_TEXT.unknown
}

/** Who holds the stock, under the same rule; drawn only where it differs from the owner. */
export function custodianText(kind: StockPartyKind, id: string, companyId: string): string {
  if (kind === 'Counterparty') return 'Cari'
  return id === companyId ? 'Şirket' : STOCK_TEXT.unknown
}

/**
 * Kilograms as Turkish text: thousands grouped with ".", the fraction after ",". The wire value is canonical
 * (no trailing zeros, at most three fraction digits), so this only regroups it — it never rounds, pads or trims
 * (Ö2: 17920 → 17.920; 18450.25 → 18.450,25).
 */
export function formatKg(wire: string): string {
  return formatDecimalTextTr(wire) ?? STOCK_TEXT.unknown
}

/**
 * Nominal millimetres under the same Ö2 rule: trailing fraction zeros dropped, nothing rounded or cut ("1.50" →
 * "1,5"; "1250" → "1.250"). A value with more than three fraction digits is shown in full rather than rounded.
 */
export function formatMm(value: string): string {
  const negative = value.startsWith('-')
  const unsigned = negative ? value.slice(1) : value
  const [whole, fraction = ''] = unsigned.split('.')
  const trimmed = fraction.replace(/0+$/, '')
  const formatted = formatDecimalTextTr(trimmed === '' ? whole : `${whole}.${trimmed}`)
  return formatted === null ? STOCK_TEXT.unknown : `${negative ? '-' : ''}${formatted}`
}

/** "kalınlık × en", or "—" when the Product owner published no accepted nominal measures. */
export function dimensionText(technical: { readonly thicknessMm: string; readonly widthMm: string } | null): string {
  return technical === null ? STOCK_TEXT.unknown : `${formatMm(technical.thicknessMm)} × ${formatMm(technical.widthMm)}`
}
