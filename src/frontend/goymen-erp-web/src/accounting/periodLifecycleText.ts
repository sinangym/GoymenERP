export const PERIOD_LIFECYCLE_TEXT = {
  show: 'Kapanış ve devir geçmişi', hide: 'Geçmişi kapat', title: 'Dönem kapanışı ve devir',
  intro: 'Kapanış ile sonraki açık döneme devir birlikte kaydedilir. Dönemi yeniden açmayı farklı bir yetkili onaylar; önceki devir fişi korunur ve ters kayıt oluşturulur.',
  empty: 'Bu dönem için kapanış ve devir kaydı yok.', missing: 'Bu kapanışın devir kanıtı bulunamadı. Yeniden açma önerilemez.',
  closed: 'Kapanış tarihi', count: 'Devir fişindeki bakiye sayısı', reversed: 'Devir terslendi', target: 'Hedef dönem',
  propose: 'Yeniden açmayı öner', approve: 'Yeniden açmayı onayla', reason: 'Yeniden açma gerekçesi',
  pending: 'Farklı yetkilinin onayı bekleniyor.', own: 'Hazırladığınız öneriyi onaylayamazsınız.',
  unknown: 'İşlemin sonucu bilinmiyor. Aynı istek ve anahtarla sonucu doğrulayın.', resend: 'Aynı isteği yeniden gönder',
  refused: 'İşlem uygulanmadı. Güncel dönem ve yetki bilgisini kontrol edin.', applied: 'İşlem kaydedildi; dönem yeniden okunuyor.',
  invalid: '1–1000 karakterlik gerekçe yazın.', noAuthority: 'Bu işlem için dönem yetkiniz yok.',
} as const
