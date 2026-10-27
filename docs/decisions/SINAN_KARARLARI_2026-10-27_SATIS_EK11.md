# Sinan Kararları — 2026-10-27 (Satış: iskonto limitlerini kim düzenler, grup bağının kapsamı, tek Müdür)

Bu belge önceki karar dosyalarını silmez; SAT-D65…SAT-D70'i ekler. `SINAN_KARARLARI_2026-10-27_SATIS_EK10.md`'nin
(SAT-D61…D64) "Açık kalanlar" listesindeki üç maddeyi cevaplar ve SAT-D36'daki "değerleri Sinan Ayarlar ekranından
girer" ifadesini yönetim kurulu düzeyine genişletir (SAT-D65, SAT-D70).

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları (iki tur: ilk üç soru, ardından #848 review'unun açtığı dört soru). Sinan'a seçeneğin adı **ve açıklaması**
birlikte gösterildi; iki soruda Sinan kendi metnini yazdı, o metin aynen verilir. İkinci turda Sinan dört sorunun üçünde
"(Önerilen)" olarak sunulan seçeneği **seçmedi**; seçtiği seçenek aynen kayda alınır.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| İskonto limitlerini, limit gruplarını ve kişi → grup bağlarını kim düzenleyebilsin? | Kendi metni: "Yönetim kurulu üyeleri gibi olabilir bunu mantıklı şekilde araştır" | — (seçenekler: "Yalnız Admin (Önerilen)" — "Ayarlar'daki diğer politikalar gibi yalnız Admin personası değiştirir; Müdür kendi limitini artıramaz."; "Admin ve Müdür"; "Yalnız ben (Sinan)") |
| Bir kişinin limit grubu üç şirkette ortak mı olsun, şirket şirket mi? | Kendi metni: "Normalde şirket şirket ayrı olabilmeli ama biz şuan bunu varsayılan üç şirkette ortak yapalım" | — (seçenekler: "Şirket şirket (Önerilen)"; "Üç şirkette ortak") |
| Tek Müdür varsa ve kendi limitini aşan iskonto girerse onayı kim versin? | Başka onay yetkilisi beklesin | "Kural değişmez; onay yetkisi olan ikinci bir kişi (örn. sen) onaylayana kadar satır bekler." (seçilmeyenler: "Sinan onaylasın", "Şimdilik sorun değil") |
| İskonto limiti veya limit grubu değişikliği tek kişiyle mi yapılsın, iki kişiyle mi? | Tek kişi + gerekçe | "Yetkili kişi tek başına değiştirir; gerekçe zorunlu, değişiklik kayıt altında." (seçilmeyen: "Çift onay (Önerilen)" — "Bir yönetim kurulu üyesi değişikliği hazırlar, başka bir üye onaylayınca geçerli olur. Sistemdeki tolerans profilleri de böyle çalışıyor.") |
| Sistem Admin'i bu limit düzenleme iznini otomatik alsın mı? | Evet, Admin de | "Admin diğer ayarlar gibi bunu da düzenleyebilir." (seçilmeyen: "Hayır, yalnız Yönetim Kurulu (Önerilen)" — "İzin kritik (Critical) sayılır; Admin'in genel yetkisine girmez, yalnız açıkça verilen kişilerde olur.") |
| Yönetim kurulu üyesi aynı zamanda bir limit grubundaysa (örn. Müdür grubunda), o grubun limitini kendisi değiştirebilsin mi? | Evet, değiştirebilir | "Yönetim kurulu üyesi kendi grubunun limitini de değiştirebilir." (seçilmeyen: "Hayır, başka üye (Önerilen)" — "Kendi içinde bulunduğu grubun limitini ve kendi grup bağını başka bir üye değiştirir veya onaylar.") |
| Bu izni ileride kimler alacak (canlıya geçişte açılır)? | Sinan + diğer Y.K. üyeleri | "Sen ve yönetim kurulundaki diğer kişiler; isimleri canlıya geçişte verirsin." (seçilmeyen: "Yalnız Sinan") |

## Araştırma notu (SAT-D65 için; bağlayıcı değil, gerekçe — ikinci turda Sinan çift onay ve kendi grubunu değiştirme konularında araştırmanın önerisinden farklı seçti)

Sinan "mantıklı şekilde araştır" dedi. Kısa araştırmanın özeti:

- Yetki devri / onay matrisleri (Delegation of Authority) genellikle **yönetim kurulu veya üst yönetim kararına**
  dayanır; matrisin satırları rol × işlem türü × tutar/oran bandıdır.
- **Görevler ayrılığı:** onaylama, uygulama ve kayıt görevleri farklı kişilerde olmalıdır; limiti sınırlanan kişi kendi
  limitini koyamamalı ve kendi işlemini onaylayamamalıdır. Kendi kendine onay engellenir; yüksek riskli değişikliklerde
  çift onay önerilir.
- İstisnalar ve değişiklikler **gerekçe ve sahibiyle** kayda geçirilir; kurallar sistemde zorlanır.

Kaynaklar: [Umbrex — DoA frameworks](https://umbrex.com/resources/frameworks/organization-frameworks/delegation-of-authority-doa-framework/),
[FitSmallBusiness — Approval matrix](https://fitsmallbusiness.com/accounting/approval-matrix/),
[FitGap — fee approvals, thresholds and segregation of duties](https://us.fitgap.com/stack-guides/managing-fee-approvals-with-policy-thresholds-and-segregation-of-duties),
[Bayburt Üniversitesi iç kontrol (görevler ayrılığı)](https://arsiv.bayburt.edu.tr/uploads/.global/fd1268b2c68138fab611d846b771848b.pdf),
[KTÜN — Görevler Ayrılığı ve Erişim Yönetimi Politikası](https://www.ktun.edu.tr/Dosyalar/1070/files/Bgys/G%C3%B6revler%20Ayr%C4%B1l%C4%B1%C4%9F%C4%B1%20ve%20Eri%C5%9Fim%20Y%C3%B6netimi%20Politikas%C4%B1.pdf).

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D65 | İskonto limitlerini kim düzenler | **Sinan'ın yönü:** İskonto limitleri, limit grupları, bağlı grup/ek puan ve kişi → limit grubu bağları **yönetim kurulu üyeleri** düzeyinde düzenlenir; izni **Sinan ve diğer yönetim kurulu üyeleri** alır (ikinci tur) ve Admin de alır (SAT-D69). **Araştırmaya dayalı uygulama ayrıntısı (geri alınabilir):** bunun için **ayrı bir izin** kullanılır (adı uygulama PR'ında permission manifest'e eklenir; bu belgeyle hiçbir role atanmaz, persona kartı eşlemesi için bkz. SAT-D69); izin Satış Destek ve Müdür personalarına verilmez. İznin kişilere verilmesi ve "Yönetim Kurulu" personasının persona/grant kartına eklenmesi insan kapısıdır (canlıya geçişte) | Ayarlar limit kaydı, kişi → grup bağı, permission manifest, `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` (öneri) |
| SAT-D66 | Kişi → limit grubu bağının kapsamı | Sistem bağı **şirket bazında** tutabilmelidir (kişi bir şirkette Müdür, ötekinde Satış Destek olabilir). **Varsayılan:** bir bağ girildiğinde **üç şirkette (GDC, GSP, GIN) ortak** uygulanır; şirkete özel ayrım ileride açılabilir. Bu, EK10'daki "kişi → grup bağı şirket bazında mı" açık maddesini kapatır | Kişi → grup bağı kaydı ve ekranı, API-12 `ISalesDiscountLimitGroupSourceV1` (şirket bazında okur) |
| SAT-D67 | Tek Müdür durumu | Müdür'ün kendi limitini aşan iskontosu, **onay yetkisi olan başka bir kişi** onaylayana kadar bekler; kural değişmez (SAT-D37 ve P6 aynen). Ayrı bir istisna veya acil yol açılmaz | API-12 (#834, mevcut davranış) |
| SAT-D68 | Limit değişikliğinde onay | Limit, grup, bağlı grup/ek puan ve kişi → grup bağı değişikliği **tek kişiyle** yapılır; ikinci kişinin onayı aranmaz. **Gerekçe zorunludur** ve her değişiklik audit kaydıyla tutulur. Bu, bu ayarlar için `SINAN_KARARLARI_2026-10-21.md` P6'daki "hazırlayan ≠ onaylayan" ilkesine açık bir istisnadır (Sinan'ın seçimi; önerilen çift onay seçilmedi); iskonto **onayı** için P6 aynen geçerlidir (SAT-D37, SAT-D67) | Ayarlar limit kaydı, kişi → grup bağı |
| SAT-D69 | Admin | Limit düzenleme izni **Critical değildir**; Admin personası, kartın genel kuralıyla (Critical olmayan tüm kayıtlı izinler) bu izni de kapsar | Permission manifest, persona/grant kartı |
| SAT-D70 | Kendi grubunu değiştirme | İzni taşıyan kişi (yönetim kurulu üyesi), kendisinin de içinde bulunduğu grubun limitini **değiştirebilir**; ayrı bir engel konmaz. **Yorum (geri alınabilir):** soru yalnız grubun limitini sordu; aynı kişinin **kendi grup bağını** değiştirebilmesi bu cevaptan çıkarılmıştır. Değişiklik SAT-D68 gereği gerekçe ve audit ile kayda geçer | Ayarlar limit kaydı, kişi → grup bağı |

## Uygulama notları

- SAT-D66 için önerilen teknik biçim (geri alınabilir): bağ kaydı şirket kapsamlı tutulur; ekran varsayılan olarak aynı
  bağı üç şirkete birlikte yazar. Böylece port bugünkü gibi şirket bazında okur ve ileride şirkete özel ayrım için
  şema değişmez. Üç şirkete birlikte yazan komut her şirkette yetkiyi doğrular, tek transaction'da ya hepsini ya hiçbirini
  yazar (kısmi yazma yok), idempotency ve expected version taşır; kişinin üyesi olmadığı şirket ve şirkete özel bir
  kaydın üzerine ortak yazma kuralı uygulama PR'ında belirlenir (fail-closed).
- İznin adı, sahibi olan modül ve kişi → grup bağının sahibi (öneri: Identity) uygulama PR'ında belirlenir (K3).
- EK10 düzeltmesi: EK10 "Açık kalanlar"daki "(SAT-D38 gereği acil yol yoktur)" ifadesi SAT-D38'in kapsamını (No 160 talep
  yolu) aşar; doğru okuma SAT-D67'dir: karar verilen kural gereği satır başka bir onay yetkilisini bekler.

## Açık kalanlar

- EK10'dan devam edenler: kişi → grup bağının hangi ekrandan yönetileceği; Müdür'ün ek puanının ilk değeri; bağlı
  grubun onaylı limiti yokken "0 + ek puan" mı yoksa onaya gönderme mi (uygulama onaya gönderir, fail-closed); limit
  değişikliğinin ne zaman etkili olacağı.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
