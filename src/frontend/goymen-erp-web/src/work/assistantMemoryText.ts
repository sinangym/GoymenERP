import type { AssistantMemoryDisabledReason, AssistantMemoryKind, AssistantMemoryState } from './assistantMemoryContract'

/** WP-C6 (AI-D30) — Turkish texts of the "Asistan hafızam" screen. */
export const ASSISTANT_MEMORY_TEXT = {
  heading: 'Asistan hafızam', subject: 'asistan hafızası', homeLink: 'Asistan hafızamı aç',
  intro: 'Burada asistanınızın sizin için hatırladığı kısa tercihler ve notlar durur. ERP kayıtlarının kopyası değildir; yalnız siz düzenler ve silersiniz.',
  dataOnly: 'Hafıza kayıtları asistana yalnız veri olarak verilir; talimat olarak çalıştırılmaz ve yetki vermez.',
  // YER TUTUCU: çalışan bilgilendirme metni hukukçu teyidiyle hazırlanacak ve insan onayıyla yayımlanacak (AI-D30).
  noticeHeading: 'Çalışan bilgilendirmesi',
  noticePlaceholder: '[YER TUTUCU] Bilgilendirme metni hukukçu teyidi ve onay bekliyor. Onaylanana kadar hafızaya yeni kayıt eklenemez.',
  noticeVersion: 'Onaylı bilgilendirme sürümü',
  writableYes: 'Hafızaya kayıt eklenebilir.', writableNo: 'Şu an hafızaya kayıt eklenemez:',
  includeInactive: 'Pasif ve kapatılan kayıtları da göster',
  retentionNote: 'Bir yıl kullanılmayan kayıt pasife alınır ve asistana verilmez; silinmez.',
  newHeading: 'Yeni kayıt', kindLabel: 'Tür', bodyLabel: 'Metin', bodyHint: 'Tek satır, en çok 500 karakter.',
  save: 'Kaydet', saving: 'Kaydediliyor…', edit: 'Düzelt', saveEdit: 'Düzeltmeyi kaydet', cancel: 'Vazgeç', remove: 'Sil', confirmRemove: 'Silmeyi onayla',
  colKind: 'Tür', colBody: 'Metin', colState: 'Durum', colExpires: 'Geçerlilik sonu', colActions: 'İşlem',
  saved: 'Kayıt eklendi.', revised: 'Kayıt düzeltildi.', removed: 'Kayıt silindi.', replayed: 'İşlem daha önce yapılmıştı; aynı sonuç gösteriliyor.',
  more: 'Daha fazla kayıt var.',
  // Refusals by stable code (never by server text).
  disabled: 'Hafıza şu an kapalı; kayıt eklenemez veya düzeltilemez.', closedForUser: 'Hafızanız yönetici tarafından kapatıldı.',
  limitReached: 'Kayıt sınırına ulaştınız; önce bir kaydı silin.', notActive: 'Bu kayıt artık etkin değil.',
  versionConflict: 'Kayıt başka bir işlemle değişti; listeyi yenileyin.', sameKeyMismatch: 'Önceki deneme farklı içerikle kaydedilmiş; sayfayı yenileyin.',
  concurrent: 'Kayıt aynı anda değişti; listeyi yenileyip tekrar deneyin.', validation: 'Metni kontrol edin: tek satır ve en çok 500 karakter olmalı.',
  forbidden: 'Bu işlem için yetkiniz yok.', notFound: 'Kayıt bulunamadı.', unknownOutcome: 'Sonuç alınamadı. Aynı işlem güvenle tekrar denenebilir.',
} as const

export const ASSISTANT_MEMORY_KIND_TEXT: Record<AssistantMemoryKind, string> = { preference: 'Tercih', note: 'Not' }
export const ASSISTANT_MEMORY_STATE_TEXT: Record<AssistantMemoryState, string> = { active: 'Etkin', passive: 'Pasif (1 yıl kullanılmadı)', closed: 'Kapatıldı' }
export const ASSISTANT_MEMORY_REASON_TEXT: Record<AssistantMemoryDisabledReason, string> = {
  pilot_off: 'Pilot kapalı.',
  retention_invalid: 'Saklama süresi ayarı geçersiz.',
  entry_limit_undecided: 'Kişi başı kayıt sınırı henüz belirlenmedi.',
  information_notice_pending: 'Çalışan bilgilendirme metni onay bekliyor.',
  closed_for_user: 'Hafızanız yönetici tarafından kapatıldı.',
}
