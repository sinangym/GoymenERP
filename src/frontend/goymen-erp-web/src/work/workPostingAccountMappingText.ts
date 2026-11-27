export const WORK_MAPPING_TEXT = {
  title:'Etkinleştirilecek muhasebe hesap eşlemesi', company:'Şirket', identity:'Eşleme sürümü', version:'Kayıt sürümü', effective:'Geçerlilik başlangıcı',
  status:'Durum', preparedBy:'Hazırlayan', preparedAt:'Hazırlanma zamanı', activatedBy:'Etkinleştiren', activatedAt:'Etkinleştirme zamanı',
  preparedPlan:'Eşleme hazırlanırken kullanılan hesap planı', currentPlan:'Güncel onaylı hesap planı', planVersion:'Hesap planı kaynak sürümü', planHash:'Hesap planı kaynak SHA-256',
  activeVersion:'Görülen etkin eşleme sürümü', rows:'Eşlemenin tüm satırları', accounts:'Güncel onaylı plandaki tüm hesaplar', code:'Hesap kodu', name:'Hesap adı', accountId:'Hesap kimliği',
  itemType:'Kalem türü', linePurpose:'Satır amacı', isExport:'İhracat', vatRateCode:'KDV oran kodu', absent:'Yok', yes:'Evet', no:'Hayır',
  states:{Prepared:'Hazırlandı',Active:'Etkin',Superseded:'Yerine yenisi etkinleştirildi'},
  rule:'Muhasebe hesap eşleme sürümü etkinleştirme onayı bekliyor.',
  versions:'Onayınız, bu ekranda görülen kayıt ve etkin eşleme sürümleriyle gönderilir.',
  retained:'Önceki etkinleştirmeniz yeniden doğrulanıyor. Karar verdiğiniz eşleme ve etkin sürüm korunuyor; güncel eşleme bilgileri bu isteğin yerine kullanılmıyor.',
} as const
