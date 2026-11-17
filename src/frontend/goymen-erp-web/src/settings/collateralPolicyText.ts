export const COLLATERAL_POLICY_TEXT = {
  banks: 'Banka kurumları', rates: 'Teminat azaltım oranları', intro: 'Ad ve EFT kodu açıkça kaydedilir. Banka adı, IBAN veya banka hesabından kimlik üretilmez. Eski teminatlar otomatik eşleştirilmez.',
  bank: 'Banka kurumu', name: 'Banka adı', eft: 'EFT kodu', active: 'Aktif', add: 'Yeni banka ekle', copy: 'Son listeyi açıkça taslağa kopyala', entries: 'Taslak banka listesi',
  scope: 'Oran kapsamı', company: 'Şirket geneli — tür oranının altında', typeBank: 'Teminat türü ve banka', typeBankDays: 'Teminat türü, banka ve bitişe kalan gün', type: 'Teminat türü',
  percentage: 'Azaltım oranı (%)', days: 'Bitişine en fazla kaç gün kaldığında', daysInfo: 'Üst sınır dahildir. Bitiş tarihi olmayan teminata gün sayısı atanmaz.',
  source: 'Banka kaynak sürümü', typeSource: 'Teminat türü kaynak sürümü', noBanks: 'Bu iş günü için yayımlanmış okunabilir banka listesi yok.',
  priority: 'Tür+banka+kalan gün, sonra tür+banka, sonra mevcut teminat türü oranı uygulanır. Eksik tür kaynağı şirket oranıyla tamamlanmaz.',
  noDefault: 'Yeni oran veya gün sayısı varsayılmaz. Mevcut tür oranları kendi kayıt ekranında korunur.', preview: 'Gerçek oran kaynağını denetle', expiry: 'Teminat bitiş günü (yoksa boş)', noBank: 'Banka bağı olmayan kayıt',
  noRates: 'Henüz özel oran kaydı yok.', emptyBanks: 'Banka listesi sürümü yok.', unpublished: 'Taslak banka listesi teminatta seçilemez.', percentLabel: 'Oran', selected: 'Seçilen kaynak', noSource: 'Oran kaynağı okunamıyor.'
} as const
