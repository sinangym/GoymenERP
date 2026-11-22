/** W1-B (DALGA_PLANI_v2 1.3) — the command palette's user-facing Turkish texts, localizable in one place. */

import type { PaletteCreateCapability } from './paletteCommands'

export const PALETTE_TEXT = {
  /** The toolbar button's visible words; the shortcut is announced through `aria-keyshortcuts`. */
  open: 'Komutlar',
  /** Shown next to the button on a keyboard host. */
  shortcutHint: 'Ctrl+K',
  dialog: 'Komut paleti',
  inputLabel: 'Komut ara',
  inputPlaceholder: 'Ekran adı yazın veya arama metni girin…',
  listLabel: 'Komutlar',
  close: 'Kapat',
  groupRecent: 'Son açılanlar',
  groupScreens: 'Ekrana git',
  groupCreate: 'Yeni kayıt',
  groupSearch: 'Genel arama',
  /** Capabilities not resolved yet (bootstrap, company switch): nothing is offered, fail-closed. */
  capabilitiesPending: 'Yetkiler doğrulanıyor; komutlar yetki bilgisi çözülünce gösterilir.',
  noCommands: 'Bu şirket bağlamında açabileceğiniz ekran yok.',
  noMatch: 'Yazdığınızla eşleşen komut yok.',
  offline: 'Bağlantı yok: ekran açılır ama verisini yükleyemeyebilir.',
  searchFor: (term: string) => `“${term}” için genel aramada ara`,
  searchHint: 'Cari, ürün, satış siparişi ve etiket kayıtlarında arar.',
  /** Read after an option's label for its kind. */
  kindScreen: 'ekran',
  kindCreate: 'yeni kayıt ekranı',
  kindSearch: 'arama',
  /** Live count for screen-reader users. */
  resultCount: (count: number) => (count === 0 ? 'Sonuç yok' : `${count} komut`),
  footer: 'Ok tuşlarıyla seçin, Enter ile açın, Esc ile kapatın. Palet yalnız ekran açar; kayıt yapmaz.',
} as const

/**
 * The "Yeni …" words, one per create screen the palette may open. A label here grants nothing: the option appears only
 * while the server's snapshot carries that create capability (the same one its route gate checks).
 */
export const PALETTE_CREATE_LABEL: Readonly<Record<PaletteCreateCapability, string>> = {
  'cari.parties.create': 'Yeni cari kart',
  'product.items.create': 'Yeni ürün',
  'sales.drafts.create': 'Yeni satış siparişi taslağı',
  'sales.customer-returns.create': 'Yeni müşteri iadesi',
  'purchasing.requests.create': 'Yeni satın alma talebi',
  'purchasing.orders.create': 'Yeni satın alma siparişi',
  'purchasing.service-orders.create': 'Yeni hizmet siparişi',
  'manufacturing.plans.create': 'Yeni üretim planı',
  'logistics.instructions.create': 'Yeni yükleme talimatı',
  'logistics.subcontract-deliveries.create': 'Yeni fason teslim',
  'finance.bank-accounts.propose': 'Yeni banka hesabı önerisi',
  'accounting.periods.propose': 'Yeni dönem açılış önerisi',
  'accounting.normal-journal-drafts.create': 'Yeni normal fiş taslağı',
}
