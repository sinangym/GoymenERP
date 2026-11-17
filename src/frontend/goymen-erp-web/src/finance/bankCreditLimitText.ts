export const CREDIT_LIMIT_TEXT = {
  title: 'Banka ve kredi türü limiti', intro: 'Bu banka/cari hesabı, kredi grubu ve para birimi için ortak limit. Rakam yalnız sisteme kaydedilmiş onaylı anapara hareketlerinin netidir; geçmiş banka kullanımının tamamlandığı veya kullanılabilir kredi tutarı olduğu anlamına gelmez.',
  amount: 'Kaynak belgedeki limit', recorded: 'Kayıtlı anapara neti', projected: 'Öneri onaylanırsa kayıtlı anapara neti',
  exceeded: 'Limit aşımı var. Bu uyarı kaydı veya onayı engellemez.', noLimit: 'Onaylı limit yok; aşım değerlendirmesi yapılamıyor.',
  pending: 'Limit önerisi onay bekliyor.', superseded: 'Yeni onaylı sürümle değiştirilmiş kaynak', own: 'Kendi önerinizi onaylayamazsınız.',
  reference: 'Kaynak belge referansı', revision: 'Kaynak belge revizyonu', date: 'Kaynak belge tarihi', reason: 'Gerekçe',
  propose: 'Limit öner', approve: 'Limiti onayla', reject: 'Limiti reddet', refresh: 'Limiti ve kullanımı yenile',
  unknown: 'Sonuç doğrulanamadı; aynı istek ve anahtarla yeniden sorgulayın.', retry: 'Aynı limit isteğini yeniden gönder',
  refused: 'Limit isteği uygulanmadı.', done: 'Limit işlemi kaydedildi.', empty: 'Bu anahtar için limit kaydı yok.',
  statuses: { Proposed: 'Onay bekliyor', Approved: 'Onaylı', Rejected: 'Reddedildi' },
} as const
