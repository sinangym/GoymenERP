export const PAYMENT_ALLOCATION_TEXT = {
  heading: 'Ödemenin borçlara dağıtımı', account: 'Dağıtımın ait olduğu cari', item: 'Açık borç', choose: 'Seçin',
  amount: 'Dağıtım tutarı', reason: 'Dağıtım veya bırakma gerekçesi', submit: 'Dağıtım oluştur', release: 'Dağıtımı bırak',
  retry: 'Aynı anahtarla yeniden dene',
  finalCheck: 'Son kez sorgula ve denemeyi kapat',
  finalCheckHelp: 'Aynı anahtar ve içerik son kez gönderilir. Önceki gönderim kaydedildiyse kayıt gösterilir; sunucu başka bir kesin yanıt verirse deneme kapatılır ve form açılır.',
  finalOpen: 'Sunucudan kesin yanıt alınamadı; deneme açık kalıyor. Bağlantı veya oturum düzelince yeniden sorgulayın.',
  abandoned: 'Son sorguda eşleştirme kaydedilmedi ve deneme kapatıldı. Önceki gönderimin etkisini eşleştirme listesinden kontrol edin; gerekiyorsa yeniden girin.', previous: 'Önceki sayfa', next: 'Sonraki sayfa', refresh: 'Yeniden oku',
  original: 'Asıl cari', transferred: 'Devir fişi', available: 'Kullanılabilir', noDueDate: 'Vadesiz',
  empty: 'Bu sayfada dağıtılabilir açık borç yok.', saved: 'Dağıtım işlemi kaydedildi.',
  unknown: 'Sonuç bilinmiyor; aynı anahtar ve içerik korunuyor. Yeni işlem başlatılamaz.',
  refused: 'İşlem kaydedilmedi. Kaynakları yeniden okuyup tekrar seçin.', conflict: 'Kaynak değişti. Güncel kayıtlardan yeniden seçin.',
  statuses: { Active: 'Etkin', Released: 'Bırakıldı' },
} as const
