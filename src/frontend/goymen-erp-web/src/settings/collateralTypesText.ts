import type { CollateralKind } from './collateralTypesContract'
export const COLLATERAL_KIND_TEXT: Record<CollateralKind, string> = { BankGuaranteedPayment: 'Banka garantili ödeme', Dbs: 'DBS', GuaranteeLetter: 'Teminat mektubu', CreditCard: 'Kredi kartı', Vinov: 'Vinov', Mortgage: 'İpotek' }
export const COLLATERAL_TYPES_TEXT = {
  title: 'Teminat türleri ve oranları', intro: 'Şirketin teminat türleri. Çek ve senet teminat sayılmaz. İlk kayıt oranı %100’dür; sonraki oran değişiklikleri gerekçesiyle saklanır.', subject: 'Teminat türleri', empty: 'Henüz teminat türü kaydedilmedi.',
  add: 'Teminat türü ekle', edit: 'Yeni sürüm', code: 'Kod', name: 'Ad', kind: 'Teminat sınıfı', choose: 'Seçiniz', rate: 'Oran (%)', active: 'Etkin', inactive: 'Pasif', state: 'Durum', version: 'Sürüm', who: 'Kaydeden', when: 'Kayıt zamanı', reason: 'Gerekçe',
  save: 'Kaydet', cancel: 'Vazgeç', pending: 'Kaydediliyor…', retry: 'Aynı işlemi yeniden dene', unknown: 'Sonuç doğrulanamadı. Aynı işlem anahtarıyla yeniden deneyin.', refused: 'İşlem reddedildi. Bilgileri denetleyin.', saved: 'Teminat türü kaydedildi.', replay: 'Önceki işlem sonucu doğrulandı.', conflict: 'Kayıt değişmiş veya bulunamıyor. Güncel liste yeniden okundu.', initial: 'İlk sürüm oranı: %100. Bu form oran hesaplamaz.',
} as const
