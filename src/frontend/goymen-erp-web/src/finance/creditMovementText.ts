export const CREDIT_MOVEMENT_TEXT = {
  title: 'Gerçek kredi hareketleri', intro: 'Banka / kurum belgesindeki gerçek hareketi girin. Plan satırı ödeme sayılmaz; bilinmeyen TL karşılığı boş kalır.',
  add: 'Hareket öner', kind: 'Hareket türü', kinds: { Drawdown: 'Kredi kullanımı', Repayment: 'Geri ödeme', InterestAccrual: 'Faiz tahakkuku', OpeningBalance: 'Açılış bakiyesi' },
  reference: 'Kaynak belge / hareket referansı', revision: 'Kaynak revizyonu', documentDate: 'Belge tarihi', date: 'İşlem tarihi', accountingDate: 'Düzeltmenin açık dönem tarihi',
  reason: 'Gerekçe', component: 'Belgedeki kalem', components: { principal: 'Anapara', interest: 'Faiz', bsmv: 'BSMV', kkdf: 'KKDF', commission: 'Komisyon', fee: 'Masraf', vat: 'KDV' },
  amount: 'Kaynak para birimi tutarı', tl: 'Belgedeki TL karşılığı (bilinmiyorsa boş)', bank: 'Belgede gerçek banka giriş / çıkışı var', bankAccount: 'Banka hesabı',
  bankAmount: 'Belgedeki gerçek banka tutarı', bankReference: 'Banka hareket referansı', select: 'Seçiniz', propose: 'Onaya gönder', approve: 'Onayla', reject: 'Reddet',
  correction: 'Açılışı ters kayıt + yeni kayıt ile düzelt', correctionNotice: 'Özgün açılış korunur. Seçilen açık dönem tarihinde ters kayıt ve yeni kaynak birlikte oluşur.',
  empty: 'Gerçek hareket bulunmuyor.', unknown: 'Sonuç doğrulanamadı; aynı istek ve anahtarla yeniden sorgulayın.', retry: 'Aynı isteği yeniden gönder',
  own: 'Kendi hareket önerinize karar veremezsiniz.', done: 'Hareket kaydedildi.', refused: 'Hareket uygulanmadı. Güncel kaydı ve kaynak değerleri kontrol edin.',
  noBanks: 'Bu para biriminde hareket yapabilen onaylı banka hesabı bulunamadı.', unknownTl: 'TL karşılığı bilinmiyor', reversal: 'Ters kaydı oluşturuldu',
  statuses: { Proposed: 'Onay bekliyor', Approved: 'Onaylandı', Rejected: 'Reddedildi' }, remove: 'Kalemi kaldır', addComponent: 'Kaynak kalemi ekle', cancel: 'Vazgeç',
} as const
export const CREDIT_POSTING_TEXT = {
  show: 'Muhasebe fiş önerilerini göster / yenile', subject: 'Kredi fiş önerisi', empty: 'Bu kredi için muhasebeye ulaşmış fiş önerisi bulunmuyor.',
  ready: 'Onaylı kaynak eşlemesi hazır. Muhasebe normal fiş ekranında Kredi hareketleri kaynağını seçin; farklı muhasebe yetkilisi onaylar.',
  waiting: 'Kesin fiş oluşmadı; kaynak veya eşleme bekliyor.', reversal: 'Ters kayıt önerisi', functional: 'TL',
  reasons: { credit_approved_source_package_missing: 'Onaylı bileşen eşlemesi, gerçek hareket veya para profili doğrulanamadı.',
    credit_opening_journal_source_unavailable: 'Açılış veya ters açılış için uygun fiş kaynağı bulunmuyor; ikinci hareket oluşturulmaz.', credit_mapping_details_pending_TK30: 'Kaynak için onaylı bileşen eşleme paketi eksik.', credit_mapping_missing: 'Hesap eşlemesi eksik.',
    credit_functional_source_amount_missing: 'Kaynakta TL karşılığı eksik; sıfır veya hesaplanmış kur kullanılmaz.' },
} as const
