/** AT-002 — the global search's user-facing Turkish texts (localizable in one place). */

import { MAXIMUM_TERM_LENGTH, MINIMUM_TERM_LENGTH, SEARCH_KIND, type SearchKind } from './searchContract'

export const SEARCH_TEXT = {
  boxLabel: 'Genel arama',
  boxPlaceholder: 'Cari, ürün, sipariş no, etiket kodu…',
  submit: 'Ara',
  /** The toolbar button's accessible name: it contains the visible "Ara" and never collides with a page's own "Ara". */
  boxSubmit: 'Genel aramada ara',
  heading: 'Arama sonuçları',
  pageLabel: 'Aranan metin',
  termHint: `En az ${MINIMUM_TERM_LENGTH}, en fazla ${MAXIMUM_TERM_LENGTH} karakter.`,
  termInvalid: `Aramak için ${MINIMUM_TERM_LENGTH}–${MAXIMUM_TERM_LENGTH} karakter girin.`,
  emptyPrompt: 'Aramak istediğiniz metni yazın.',
  noResults: 'Bu arama için gösterilecek sonuç yok.',
  noResultsHint: 'Yalnız görme yetkiniz olan kayıtlar aranır.',
  groupEmpty: 'Eşleşen kayıt yok.',
  more: 'Daha fazla sonuç var.',
  moreInList: 'Tümünü listede gör',
  narrow: 'Aramayı daraltın.',
  scope: 'Yalnız seçili şirketin kayıtları ve görme yetkiniz olan türler gösterilir.',
  noDetail: 'Bu kaydın ekranını açma yetkiniz yok.',
} as const

export const SEARCH_KIND_LABEL: Readonly<Record<SearchKind, string>> = {
  [SEARCH_KIND.party]: 'Cari kartlar',
  [SEARCH_KIND.product]: 'Ürünler',
  [SEARCH_KIND.salesOrder]: 'Satış siparişleri',
  [SEARCH_KIND.materialLabel]: 'Stok etiketleri',
}

/** The owners' state names the screen knows; any other is shown as the owner sent it, never guessed. */
export const SEARCH_STATE_LABEL: Readonly<Record<string, string>> = {
  Complete: 'Belgeler tamam',
  PendingDocuments: 'Belge/kalite bekliyor',
  Active: 'Aktif',
  Inactive: 'Pasif',
  Draft: 'Taslak',
  Preparing: 'Hazırlanıyor',
  PendingApproval: 'Onay bekliyor',
  Approved: 'Onaylı',
  Rejected: 'Reddedildi',
  Withdrawn: 'Geri çekildi',
  Cancelled: 'İptal',
}

export const stateLabel = (state: string) => SEARCH_STATE_LABEL[state] ?? state
