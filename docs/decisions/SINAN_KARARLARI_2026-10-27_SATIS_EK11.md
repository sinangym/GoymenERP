# Sinan Kararları — 2026-10-27 (Satış: iskonto limitlerini kim düzenler, grup bağının kapsamı, tek Müdür)

Bu belge önceki karar dosyalarını silmez; SAT-D65…SAT-D67'yi ekler. `SINAN_KARARLARI_2026-10-27_SATIS_EK10.md`'nin
(SAT-D61…D64) "Açık kalanlar" listesindeki üç maddeyi cevaplar.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; iki soruda Sinan kendi metnini yazdı, o metin aynen
verilir.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| İskonto limitlerini, limit gruplarını ve kişi → grup bağlarını kim düzenleyebilsin? | Kendi metni: "Yönetim kurulu üyeleri gibi olabilir bunu mantıklı şekilde araştır" | — (seçenekler: "Yalnız Admin (Önerilen)" — "Ayarlar'daki diğer politikalar gibi yalnız Admin personası değiştirir; Müdür kendi limitini artıramaz."; "Admin ve Müdür"; "Yalnız ben (Sinan)") |
| Bir kişinin limit grubu üç şirkette ortak mı olsun, şirket şirket mi? | Kendi metni: "Normalde şirket şirket ayrı olabilmeli ama biz şuan bunu varsayılan üç şirkette ortak yapalım" | — (seçenekler: "Şirket şirket (Önerilen)"; "Üç şirkette ortak") |
| Tek Müdür varsa ve kendi limitini aşan iskonto girerse onayı kim versin? | Başka onay yetkilisi beklesin | "Kural değişmez; onay yetkisi olan ikinci bir kişi (örn. sen) onaylayana kadar satır bekler." (seçilmeyenler: "Sinan onaylasın", "Şimdilik sorun değil") |

## Araştırma notu (SAT-D65 için; bağlayıcı değil, gerekçe)

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
| SAT-D65 | İskonto limitlerini kim düzenler | İskonto limitleri, limit grupları, bağlı grup/ek puan ve kişi → limit grubu bağları **Yönetim Kurulu** düzeyinde düzenlenir (Sinan'ın cevabı; araştırma notu gerekçedir). Bunun için **ayrı bir izin** kullanılır (adı uygulama PR'ında permission manifest'e eklenir, hiçbir role atanmaz); izin Satış Destek ve Müdür personalarına verilmez, böylece limiti sınırlanan kişi kendi limitini değiştiremez. Her değişiklik **zorunlu gerekçe** ve audit kaydıyla tutulur. İznin hangi kişilere (yönetim kurulu üyeleri) verileceği ve "Yönetim Kurulu" personasının persona/grant kartına eklenmesi insan kapısıdır | Ayarlar limit kaydı, kişi → grup bağı, permission manifest, `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` (öneri) |
| SAT-D66 | Kişi → limit grubu bağının kapsamı | Sistem bağı **şirket bazında** tutabilmelidir (kişi bir şirkette Müdür, ötekinde Satış Destek olabilir). **Varsayılan:** bir bağ girildiğinde **üç şirkette (GDC, GSP, GIN) ortak** uygulanır; şirkete özel ayrım ileride açılabilir. Bu, EK10'daki "kişi → grup bağı şirket bazında mı" açık maddesini kapatır | Kişi → grup bağı kaydı ve ekranı, API-12 `ISalesDiscountLimitGroupSourceV1` (şirket bazında okur) |
| SAT-D67 | Tek Müdür durumu | Müdür'ün kendi limitini aşan iskontosu, **onay yetkisi olan başka bir kişi** onaylayana kadar bekler; kural değişmez (SAT-D37 ve P6 aynen). Ayrı bir istisna veya acil yol açılmaz | API-12 (#834, mevcut davranış) |

## Uygulama notları

- SAT-D66 için önerilen teknik biçim (geri alınabilir): bağ kaydı şirket kapsamlı tutulur; ekran varsayılan olarak aynı
  bağı üç şirkete birlikte yazar. Böylece port bugünkü gibi şirket bazında okur ve ileride şirkete özel ayrım için
  şema değişmez.
- EK10 düzeltmesi: EK10 "Açık kalanlar"daki "(SAT-D38 gereği acil yol yoktur)" ifadesi SAT-D38'in kapsamını (No 160 talep
  yolu) aşar; doğru okuma SAT-D67'dir: karar verilen kural gereği satır başka bir onay yetkilisini bekler.

## Açık kalanlar

- Limit değişikliklerinde **çift onay** (bir yönetim kurulu üyesinin girdiği değişikliği bir başkasının onaylaması)
  sorulmadı; uygulama tek kişi + zorunlu gerekçe + audit ile başlar.
- İzni taşıyan bir kişinin **kendi** grup bağını değiştirip değiştiremeyeceği sorulmadı; uygulama görevler ayrılığı
  gereği bunu reddeder (fail-closed, geri alınabilir).
- Müdür'ün ek puanının ilk değeri (EK10) hâlâ açıktır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
