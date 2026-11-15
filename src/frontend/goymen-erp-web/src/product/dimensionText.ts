/** BS-9: the texts of the product card's "Ebat/kalınlık" panel (localizable; never inline). */

import type { DimensionMeasure, ProductSectionKind } from './dimensionApi'

export const SECTION_KIND_LABEL: Record<ProductSectionKind, string> = {
  Sheet: 'Sac / levha',
  Coil: 'Rulo / bant',
  RoundTube: 'Yuvarlak boru',
  Profile: 'Kutu / dikdörtgen profil',
}

export const MEASURE_LABEL: Record<DimensionMeasure, string> = {
  thicknessMm: 'Kalınlık (mm)',
  widthMm: 'En (mm)',
  lengthMm: 'Boy (mm)',
  heightMm: 'Yükseklik (mm)',
  outerDiameterMm: 'Dış çap (mm)',
  wallThicknessMm: 'Et kalınlığı (mm)',
  kgPerMetre: 'Metre ağırlığı (kg/m)',
}

export const DIMENSION_TEXT = {
  heading: 'Ebat / kalınlık',
  kindLabel: 'Kesit türü',
  unset: 'Girilmemiş — birim çevriminde bu ürün için tahmin yapılmaz ("kaynak yok")',
  note: 'Birim çevrimi (kg ↔ adet / metre / m²) bu ölçüleri kullanır; yoğunluk Ayarlar\'daki şirket değeridir, kartta tutulmaz. Ölçüler yuvarlanmaz.',
  tubeKgNote: 'Yuvarlak boruda metre ağırlığı girilirse tablo değeri olarak formülün önüne geçer.',
  shared: 'Ebat/kalınlık ortak ürün kartındadır: üç şirkette de aynıdır; burada yapılan değişiklik hepsini etkiler.',
  changedElsewhere: 'Son değişiklik başka bir şirkette yapıldı.',
  loading: 'Ebat/kalınlık yükleniyor…',
  failed: 'Ebat/kalınlık şu anda okunamadı.',
  notFound: 'Ürün kartı bulunamadı; ebat/kalınlık gösterilemiyor.',
  unavailable: 'Ebat/kalınlık hizmeti şu anda kullanılamıyor.',
  unauthenticated: 'Oturumunuz sona erdi; ebat/kalınlığı görmek için yeniden giriş yapın.',
  retryRead: 'Yeniden dene',
  change: 'Ebat/kalınlığı değiştir',
  choose: 'Kesit türü seçin',
  formLabel: 'Ebat/kalınlık değişikliği',
  optional: '(isteğe bağlı)',
  reasonLabel: 'Gerekçe',
  save: 'Kaydet',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  cancel: 'Vazgeç',
  saved: 'Ebat/kalınlık kaydedildi.',
  conflict: 'Ebat/kalınlık siz açtıktan sonra değişti; güncel hâli yüklendi.',
  reasonRequired: 'Gerekçe zorunludur.',
  measureInvalid: 'Ölçüler kesit türüne uymuyor veya izin verilen ondalık sayısını aşıyor.',
  measureFormat: 'Sıfırdan büyük bir sayı girin; milimetrede en fazla 3, metre ağırlığında en fazla 6 ondalık.',
  measureMissing: 'Bu kesit türü için zorunlu ölçüler eksik.',
  wallTooThick: 'Et kalınlığının iki katı dış ölçüden büyük olamaz.',
  refused: 'Ebat/kalınlık kaydedilemedi.',
  unknownOutcome: 'İşlemin sonucu bilinmiyor. Aynı işlemi yeniden deneyin; iki kez uygulanmaz.',
} as const
