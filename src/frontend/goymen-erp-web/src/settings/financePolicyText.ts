export const FINANCE_POLICY_TEXT = {
  title: 'Finans politika kayıtları', subject: 'Finans politikaları', intro: 'Eşikler yalnız açık kayıt ve yayımlama ile oluşur. Geçmiş kayıtlar değişmez; mevcut zorunlu onaylar ve yetkiler korunur.',
  matrix: 'Onay matrisi', cash: 'Kasa sayım farkı', empty: 'Bu kapsam için henüz politika kaydı yok. Kayıt yokluğu sıfır eşik değildir.',
  operation: 'İşlem', currency: 'Para birimi', role: 'Onaylayıcı rol', step: 'Onay adımı', first: 'Birinci adım', second: 'İkinci adım', amount: 'Eşik tutarı', cashAccount: 'Kasa',
  from: 'İlk geçerli gün', through: 'Son geçerli gün (isteğe bağlı)', reason: 'Gerekçe', choose: 'Seçin', addRule: 'Matrise satır ekle', remove: 'Satırı çıkar',
  prepare: 'Taslak hazırla', publish: 'Yayımla', retire: 'Yayını sonlandır', retireFrom: 'İlk geçersiz gün', pending: 'Kaydediliyor…', retry: 'Aynı işlemi yeniden denetle',
  unknown: 'Sonuç bilinmiyor. İstek ve işlem anahtarı korundu; aynı işlemi yeniden denetleyin.', saved: 'İşlem kaydedildi.', replay: 'Önceki işlemin kaydı doğrulandı.', refused: 'İşlem reddedildi. Güncel kayıt ve kaynak sürümlerini kontrol edin.',
  history: 'Sürüm geçmişi', state: 'Durum', version: 'Sürüm', stateVersion: 'Durum sürümü', source: 'Kaynak sürümü', by: 'Hazırlayan', publishedBy: 'Yayımlayan',
  draft: 'Taslak', published: 'Yayımlanmış', retired: 'Sonlandırılmış', preview: 'Yayımlanmış kaydı denetle', previewAmount: 'Denetlenecek tutar / sayım farkı', previewDate: 'Denetleme iş günü',
  noPolicy: 'Bu tarih ve kapsam için okunabilir yayımlanmış kayıt yok.', exceeds: 'Fark eşik üstünde.', within: 'Fark eşik üstünde değil.', matches: 'Tutarın ulaştığı matris kuralları',
  matrixRule: 'Matris eşiğine eşit tutar da kapsanır. Kasa farkı için eşik üstü karşılaştırılır.', noSources: 'Gerekli gerçek kaynaklar yok; kaynak sahibi kayıtları tamamlamalı.',
  permissions: 'Yayımlama ve sonlandırma için hazırlama ile yayımlama yetkisi; matris için ayrıca kritik aile yayımlama yetkisi gerekir.',
  reload: 'Güncel kaynakları oku', sourceChanged: 'Kaynak sürümleri yayımlanırken yeniden doğrulanır.', noActivation: 'Bu kayıt ekranı mevcut Finans komutlarında yeni onay veya yetki ataması yapmaz.'
} as const
