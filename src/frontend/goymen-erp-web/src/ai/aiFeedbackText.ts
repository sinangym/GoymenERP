export const AI_FEEDBACK_TEXT = {
 heading:'Yanıt geri bildirimi', intro:'Bu geri bildirim yalnız yapay zekâ yanıtını değerlendirir; ERP kaydını onaylamaz veya değiştirmez.',
 kind:'Yanıt değerlendirmesi', choose:'Seçiniz', reason:'Geri bildirim nedeni', optionalReason:'Kabul için neden isteğe bağlıdır; diğer değerlendirmelerde bir neden seçin.',
 kinds:{Accepted:'Yanıtı kabul ettim',Corrected:'Yanıtı düzelttim',Rejected:'Yanıtı reddettim',Reverted:'Önceki kabulü geri aldım'},
 reasons:{incorrect_information:'Yanlış bilgi',incorrect_or_missing_source:'Yanlış veya eksik kaynak',incomplete_answer:'Eksik yanıt',outdated_information:'Güncelliğini yitirmiş bilgi',other:'Diğer'},
 save:'Yanıt geri bildirimini kaydet', retry:'Aynı geri bildirim isteğini yeniden gönder', unknown:'Geri bildirimin sonucu doğrulanamadı. İlk değerlendirme ve istek korunuyor.',
 done:'Yanıt geri bildirimi kaydedildi.', refused:'Geri bildirim kaydedilemedi. Güncel yanıt durumunu ve yetkinizi kontrol edin.',
 current:'Sunucudaki son geri bildirim', none:'Henüz geri bildirim yok.', version:'Geri bildirim sürümü', receipt:'Kaydedilen isteğin makbuz sürümü',
 revertHelp:'Geri alma yalnız son değerlendirme kabul ise kullanılabilir. Geri alma ERP işlemini değiştirmez.', reload:'Geri bildirimi yenile',
} as const
