/** SAT-D134 (EK19) / SAT-D149 (EK20): the texts of the product item type panel (localizable; never inline). */

import type { ProductItemType } from './itemTypeApi'

export const ITEM_TYPE_LABEL: Record<ProductItemType, string> = {
  Steel: 'Demir',
  Scrap: 'Hurda',
  Service: 'Hizmet',
}

export const ITEM_TYPE_TEXT = {
  heading: 'Kalem tipi (vergi)',
  label: 'Kalem tipi',
  unset: 'Belirtilmemiş — siparişte vergi varsayılanı gelmez ("kaynak yok")',
  note: 'KDV ve tevkifat bu tipe göre Ayarlar > Vergi varsayılanından gelir; sipariş satırında değiştirilebilir. Ürün kartında oran tutulmaz.',
  shared: 'Kalem tipi ortak ürün kartındadır: üç şirkette de aynıdır; burada yapılan değişiklik hepsini etkiler.',
  changedElsewhere: 'Son değişiklik başka bir şirkette yapıldı.',
  loading: 'Kalem tipi yükleniyor…',
  failed: 'Kalem tipi şu anda okunamadı.',
  unauthenticated: 'Oturumunuz sona erdi; kalem tipini görmek için yeniden giriş yapın.',
  change: 'Kalem tipini değiştir',
  choose: 'Kalem tipi seçin',
  formLabel: 'Kalem tipi değişikliği',
  reasonLabel: 'Gerekçe',
  save: 'Kaydet',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  cancel: 'Vazgeç',
  saved: 'Kalem tipi kaydedildi.',
  conflict: 'Kalem tipi siz açtıktan sonra değişti; güncel hâli yüklendi.',
  reasonRequired: 'Gerekçe zorunludur.',
  refused: 'Kalem tipi kaydedilemedi.',
  unknownOutcome: 'İşlemin sonucu bilinmiyor. Aynı işlemi yeniden deneyin; iki kez uygulanmaz.',
} as const
