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
import type { StockMeasure, StockMeasureFilter, StockMeasureInputProblem } from './stockDisplayApi'

export const STOCK_TEXT = {
  heading: 'Stok / Malzeme Pozisyonu',
  searchLabel: 'Etiket kodu',
  searchButton: 'Ara',
  searchTooLong: 'Etiket kodu en çok 64 karakter olabilir.',
  /** The panel's button; closed, it says how many filters it holds so none is applied out of sight. */
  allFilters: (held: number) => held > 0 ? `Tüm filtreler (${held})` : 'Tüm filtreler',
  classificationFilter: 'Sınıf',
  classificationAll: 'Tümü',
  familyFilter: 'Ürün ailesi',
  familyAll: 'Tümü',
  familyLoading: 'Ürün aileleri yükleniyor…',
  /** Three different facts: the list could not be read, the person may not see it — and "no family" cannot happen. */
  familyUnreadable: 'Ürün ailesi listesi okunamadı.',
  familyForbidden: 'Ürün ailesi listesini görme yetkiniz yok.',
  familyRetry: 'Yeniden dene',
  measureForm: 'Ölçü filtreleri',
  measureApply: 'Ölçüleri uygula',
  measureHelp: 'Ondalık için virgül yazın (ör. 1,5). Boş bırakılan ölçü filtrelenmez.',
  measure: {
    thickness: { name: 'Kalınlık', mode: 'Kalınlık koşulu', exact: 'Kalınlık (mm)', min: 'Kalınlık alt sınır (mm)', max: 'Kalınlık üst sınır (mm)' },
    width: { name: 'En', mode: 'En koşulu', exact: 'En (mm)', min: 'En alt sınır (mm)', max: 'En üst sınır (mm)' },
  } satisfies Record<StockMeasure, { name: string; mode: string; exact: string; min: string; max: string }>,
  measureMode: { exact: 'Tam değer', range: 'Aralık' },
  measureProblem: {
    format: 'Yalnız rakam yazın; ondalık için virgül kullanın, binlik ayırıcı kullanmayın (ör. 1250 veya 1,5).',
    leadingZero: 'Sayının başına sıfır yazmayın (ör. 3 veya 0,5).',
    precision: 'En çok üç ondalık basamak yazılabilir; değer yuvarlanmaz.',
    zero: 'Sıfırdan büyük bir değer yazın.',
  } satisfies Record<StockMeasureInputProblem, string>,
  measureOrder: 'Alt sınır, üst sınırdan büyük olamaz.',
  columns: 'Sütunlar',
  visibleColumns: 'Görünen sütunlar',
  columnToggle: (column: string) => `${column} sütunu`,
  activeFilters: 'Etkin filtreler',
  clearFilters: 'Filtreleri temizle',
  results: 'Stok listesi',
  loading: 'Stok pozisyonları yükleniyor…',
  loadingMore: 'Sonraki stok satırları yükleniyor…',
  emptyNoFilter: 'Bu şirkette eşleşen fiziksel stok yok.',
  emptyFiltered: 'Filtrelere uyan fiziksel stok yok.',
  loadMore: 'Daha fazla göster',
  loadMoreFailed: 'Sonraki stok satırları okunamadı.',
  /** A continuation the server no longer accepts (expired, or the list changed under it): only a fresh start helps. */
  loadMoreExpired: 'Sonraki sayfa artık açılamıyor; listeyi baştan yükleyin.',
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
    family: 'Ürün ailesi',
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
  /** An exact owner filter names one owner, not a category: the contract publishes no owner name. */
  selectedCounterparty: 'seçili cari',
  selectedCompany: 'seçili şirket',
  /** A filtered family whose name is not known here (the list is still loading, unreadable or does not hold it). */
  selectedFamily: 'seçili ürün ailesi',
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

/**
 * "kalınlık × en", plus "× boy" when the Product card publishes a nominal length (`4.0`); "—" when the Product owner
 * published no accepted nominal measures. A null length (a coil, a strip) adds nothing: no 0, no lot measurement.
 */
export function dimensionText(technical: {
  readonly thicknessMm: string
  readonly widthMm: string
  readonly lengthMm?: string | null
} | null): string {
  if (technical === null) return STOCK_TEXT.unknown
  const section = `${formatMm(technical.thicknessMm)} × ${formatMm(technical.widthMm)}`
  return technical.lengthMm === null || technical.lengthMm === undefined
    ? section
    : `${section} × ${formatMm(technical.lengthMm)}`
}

/**
 * One measure filter in words, in the table's own number writing: "1,5 mm", "1–3 mm", "en az 1 mm", "en çok 3 mm".
 * Both ends of a range are part of it, as on the server.
 */
export function measureFilterText({ exact, min, max }: StockMeasureFilter): string | null {
  if (exact !== null) return `${formatMm(exact)} mm`
  if (min !== null && max !== null) return `${formatMm(min)}–${formatMm(max)} mm`
  if (min !== null) return `en az ${formatMm(min)} mm`
  return max !== null ? `en çok ${formatMm(max)} mm` : null
}
