# Sinan Kararları — 2026-11-03 (fazla teslim onayı, satır/sipariş kapatma, geri açma)

Bu belge önceki karar dosyalarını silmez; SAT-D213…SAT-D218'i ekler. EK24 SAT-D168 (fazla teslim onayı, onay bandındaki eksik
teslimle satır kapatma, "kapanmaya hazır" siparişin kullanıcı kapatması) uygulamasından önce açık kalan soruları cevaplar.
SAT-D17, SAT-D30, SAT-D32, SAT-D168, SAT-D187/D188/D190 ve EK36 SAT-D210 geçerlidir.

**Kaynak:** Sinan'ın 3 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: iki tur, altı soru. 3. sorunun cevabı Sinan'ın kendi metnidir ve aynen aktarılmıştır. Sinan'a her seçeneğin adı
**ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Toleransı aşan fazla teslim onaylanınca, sonradan aynı satıra daha fazla sevk olursa (fazlalık büyürse) ne olsun? | Yeni onay gereksin (Önerilen) | "Onay, onay anındaki sevk miktarını kapsar; fazlalık artarsa yeniden onay gerekir." (seçilmeyen: "Onay geçerli kalsın" — "Bir kez onaylanan satır için sonraki fazlalıklar da onaylı sayılır.") |
| 2 | Eksik teslimle satırı kapatma: "uyarı" bandındaki (küçük) eksikle kapatmayı kim yapsın? | Aynı kişiler (Önerilen) | "Uyarı bandında da kapatma bir kullanıcı işlemidir; onay bandındaki gibi Satış Uzmanı, Satış Müdürü, GM, Admin yapar (onay gerekmez, yalnız işlem)." (seçilmeyenler: "Sevk eden de kapatabilsin" — "Uyarı bandında Sevkiyatçı da satırı kapatabilir."; "Otomatik kapansın" — "Uyarı bandındaki eksik sevk tamamlanınca satır kendiliğinden kapanır.") |
| 3 | Satır kapatma ve sipariş kapatmada gerekçe yazılsın mı? (Kapatılan satıra artık sevk yapılamaz.) | Kendi metni: "Her ikisinde de zorunlu değil ama kapatılan satır veya sipariş onayla geri açılabilmeli" | — (seçenekler: "Satırda zorunlu, siparişte değil (Önerilen)" — "Eksikle satır kapatırken gerekçe zorunlu; 'kapanmaya hazır' siparişi kapatırken gerekçe istenmez (her şey tamam)."; "İkisinde de zorunlu" — "Her kapatmada gerekçe yazılır."; "İkisinde de isteğe bağlı" — "Gerekçe alanı var ama boş bırakılabilir.") |
| 4 | Fazla teslim onayı ve eksikle satır kapatmada: sevki yapan ya da talebi açan kişi aynı işlemi kendisi onaylayabilsin mi? | Hayır, başkası onaylasın (Önerilen) | "Sevki yapan kişi kendi fazla tesliminin onayını veremez (hazırlayan ≠ onaylayan)." (seçilmeyen: "Evet, kendisi yapabilir" — "Yetkisi olan kişi kendi işlemini de onaylayabilir; ayrım aranmaz.") |
| 5 | Kapatılan satır veya sipariş "onayla geri açılabilsin" dedin. Geri açmayı kim istesin, kim onaylasın? | Uzman ister, müdür onaylar (Önerilen) | "Satış Uzmanı (veya Destek) geri açma ister; Satış Müdürü, GM veya Admin onaylar. İsteyen onaylayamaz." (seçilmeyen: "Yalnız müdür açar" — "Satış Müdürü, GM veya Admin tek adımda geri açar; ayrı onay yok.") |
| 6 | Zaten faturası kesilmiş ya da finansal olarak kapanmış bir sipariş de geri açılabilsin mi? | Evet | "Her kapalı sipariş onayla geri açılabilir." (seçilmeyen: "Hayır (Önerilen)" — "Faturası kesilmiş/finansal kapanmış sipariş geri açılamaz; yalnız henüz faturalanmamış kapatılmış satır/sipariş açılır.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D213 | Fazla teslim onayının kapsamı | Fazla teslim onayı **onay anındaki sevk miktarını** kapsar; aynı satırda fazlalık sonradan artarsa **yeniden onay** gerekir (soru 1) | SAT-D30, SAT-D168 uygulaması |
| SAT-D214 | Uyarı bandında satır kapatma | Uyarı bandındaki eksik teslimle satırı kapatmak da bir **kullanıcı işlemidir**; onay bandındaki gibi **Satış Uzmanı, Satış Müdürü, Genel Müdür ve Admin** yapar; ayrıca onay gerekmez (soru 2). Otomatik kapanma ve Sevkiyatçı'nın kapatması seçilmedi | SAT-D17, SAT-D168 uygulaması |
| SAT-D215 | Kapatmada gerekçe | Satır kapatmada ve sipariş kapatmada gerekçe **zorunlu değildir** (soru 3). Kapatılan satıra artık sevk yapılamaz (soru 3'ün metni) | SAT-D17, SAT-D32 uygulaması |
| SAT-D216 | Görev ayrımı | Fazla teslim onayında ve eksikle satır kapatmada **sevki yapan ya da talebi açan kişi kendi işlemini onaylayamaz**; başkası onaylar (soru 4) | SAT-D30, SAT-D17 uygulaması |
| SAT-D217 | Geri açma | Kapatılan **satır veya sipariş onayla geri açılabilir** (soru 3). Geri açmayı **Satış Uzmanı veya Satış Destek ister**; **Satış Müdürü, Genel Müdür veya Admin onaylar**; isteyen onaylayamaz (soru 5). Uygulama ayrıntısı (geri alınabilir): geri açma kapatmayı silmez; ters kayıt olarak işlenir ve audit'lidir | SAT-D168 uygulaması |
| SAT-D218 | Faturalı/finansal kapanmış siparişin geri açılması | Faturası kesilmiş ya da finansal olarak kapanmış sipariş de **onayla geri açılabilir** (soru 6). Uygulama ayrıntısı (geri alınabilir): geri açma kesilmiş faturayı, Finans açık kalemini ya da muhasebe kaydını değiştirmez veya silmez (DEC-FIN-001, SAT-D172); yalnız siparişin satış durumunu açar. Geri açılan siparişte faturalama ve finansal kapanış ekseninin nasıl yeniden hesaplanacağı fatura dalgasında belirlenir; seçim gerekirse `NEEDS-SINAN` | SAT-D168 uygulaması, fatura dalgası |

## Sınırlar

- "Kullanıcı kapatmasıyla kapanan sipariş" ile "zorla kapatılan sipariş" (#807) aynı yaşam döngüsü durumunda mı, farklı etiketle mi
  gösterileceği bu kararla seçilmez; uygulama PR'ında geri alınabilir uygulama ayrıntısı olarak işaretlenir.
- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Gerçek grant ataması insan kapısıdır.
