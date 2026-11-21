export const MANUAL_QUEUE_TEXT = {
  heading: 'Kuyruk sırasını değiştir', moving: 'Taşınacak plan', before: 'Önüne yerleştirilecek plan', end: 'Kuyruğun sonu',
  reason: 'Sıra değişikliği gerekçesi', save: 'Sırayı kaydet', resend: 'Sıra değişikliğini aynı anahtarla yeniden gönder',
  help: 'Elle verilen sıra termin ve puan değişse de korunur. Yeni iş, kendi termin ve puanına göre ilk daha geç işin önüne girer.',
  blocked: 'Bekleyen sıra işlemi güvenle okunamadı veya saklanamadı. Yeni sıra işlemi gönderilmez; kuyruk yalnız okunabilir.',
  unknown: 'Sıra değişikliğinin sonucu bilinmiyor. Alanlar kilitli; aynı istek ve anahtarla yeniden gönderin.',
  invalid: 'Bir plan ve farklı bir yer seçin; 1–256 karakterlik gerekçe girin.', applied: 'Kuyruk sırası kaydedildi; güncel sıra yeniden okunuyor.',
  conflict: 'Kuyruk veya plan değişti. Güncel sıra yeniden okunuyor; değişikliği güncel sürümde yapın.',
  refused: 'Sıra değişikliği uygulanmadı. Kuyruk ve yetkiniz yeniden kontrol ediliyor.', empty: 'Taşınabilecek kuyruk üyesi yok.',
} as const
