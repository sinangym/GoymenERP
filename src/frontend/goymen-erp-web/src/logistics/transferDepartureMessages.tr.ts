export const TRANSFER_DEPARTURE_TEXT = {
  title: 'Transfer çıkış kayıtları', subject: 'transfer çıkış kaydı', candidates: 'çıkışı yapılan transferler',
  intro: 'Sevk edilmiş transferin çıkış tartısını ve araç çıkışını ayrı kayıtlar olarak kaydedin.',
  transfer: 'Transfer seçin', choose: 'Transfer seçin…', selection: 'Çıkış bilgilerini görmek için bir transfer seçin.',
  empty: 'Gösterilecek sevk edilmiş transfer yok.', windowFull: 'Aday penceresi dolu. Aradığınız transfer görünmüyorsa listeyi yenileyin.',
  refresh: 'Bilgileri yenile', association: 'E-İrsaliye bağlantısı', linked: 'Bağlı', notLinked: 'Bağlı değil',
  unknown: 'Önceki gönderimin sonucu kesinleşmedi. Yeni kayıt açmadan aynı isteği aynı anahtarla tekrar gönderin.',
  legacy: 'Eski kaydın E-İrsaliye bağlantısı doğrulanamıyor. Çıkış kaydı eklenemez.',
  closed: 'Çıkış yapılmayacağı kesinleştirilmiş. Bu transfer için yeni çıkış kaydı eklenemez.',
  weighing: 'Çıkış tartısı', vehicle: 'Araç çıkışı', notRecorded: 'Kaydedilmedi', kind: 'Kaydedilecek olay',
  chooseKind: 'Olay seçin…', occurred: 'Olay zamanı (yerel saat)', reason: 'Kayıt gerekçesi',
  direction: 'Tartım sırası', chooseDirection: 'Tartım sırası seçin…', tareFirst: 'Önce dara, sonra brüt', grossFirst: 'Önce brüt, sonra dara',
  firstKg: 'Birinci tartım (kg)', secondKg: 'İkinci tartım (kg)', weighingHelp: 'Nokta ile en fazla üç ondalık basamak kullanın. Net ağırlığı sunucu hesaplar.',
  save: 'Çıkış kaydını kaydet', pending: 'Çıkış kaydı gönderiliyor…', saved: 'Çıkış kaydı kaydedildi.', replayed: 'Önceki çıkış kaydı doğrulandı.',
  retry: 'Aynı isteği tekrar gönder', invalid: 'Olayı, geçerli zamanı ve gerekçeyi girin; tartı kaydı için tartım sırası ile iki ağırlık zorunludur.',
  refused: 'İstek kabul edilmedi. Bilgileri kontrol edip tekrar deneyin.', bothRecorded: 'Tartı ve araç çıkışı kayıtları tamamlanmış.',
  official: 'Resmî belge', gibSent: 'GİB gönderimi doğrulandı', gibNotSent: 'GİB gönderimi yapılmamış', version: 'Çıkış kayıt sürümü',
} as const
export const TRANSFER_DEPARTURE_STATUS_TEXT = {
  InTransit: 'Yolda', PartiallyAccepted: 'Kısmen teslim alındı', Accepted: 'Teslim alındı', DispatchCancelled: 'Sevk iptal edildi', Closed: 'Kapalı',
} as const
