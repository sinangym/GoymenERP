/**
 * UI-51 / S-06 "Satış Onayları" — the Turkish texts of the Sales approval queue. Read-only: a decision is given on the
 * order's own screen (Sipariş 360), with that screen's authority and separation of duties.
 */

export const SALES_APPROVALS_TEXT = {
  heading: 'Satış Onayları',
  note: 'Onay bekleyen satış kayıtları. Karar siparişin kendi ekranında verilir; bu liste karar vermez.',
  stateSubject: 'satış onayları',
  openLink: 'Satış Onayları',
  orders: {
    heading: 'Onay bekleyen sipariş taslakları',
    caption: 'İç onay bekleyen satış siparişi taslakları',
    documentNo: 'Belge no',
    revision: 'Revizyon',
    submittedBy: 'Onaya gönderen',
    submittedAt: 'Gönderim zamanı',
    ownDraft: 'Kendi kaydınız',
    ownDraftYes: 'Evet — siz oluşturdunuz',
    ownDraftNo: 'Hayır',
    actions: 'İşlem',
    openOrder: 'Siparişte aç',
    openControl: 'Fiyat kontrolü',
  },
  loadMore: 'Daha fazla göster',
  loadingMore: 'Yükleniyor…',
  end: 'Listenin sonu.',
  nextPageFailed: 'Sonraki sayfa okunamadı; gösterilen satırlar korunuyor.',
  nextPageSubject: 'sonraki sayfa',
  restart: 'Listeyi baştan oku',
  terminal: 'Liste artık okunamıyor; satırlar güncel olmayabilir.',
  unavailable: {
    heading: 'Bu ekranda henüz listelenmeyen onaylar',
    note: 'Aşağıdaki onay türlerinin bekleyen kayıt listesi için sunucu okuma ucu yok. Bu türler siparişin kendi ekranında görülür ve karara bağlanır.',
    items: [
      'Taban fiyat altı istisnaları',
      'İskonto limiti aşımları',
      'Kalan miktar değişikliği talepleri',
      'Fazla teslim onayları',
      'Satış faturası onayları',
      'Süresi geçmiş teklif dönüşüm onayları',
      'Satış talebi onayları',
      'Risk limiti istisnaları',
    ],
  },
} as const
