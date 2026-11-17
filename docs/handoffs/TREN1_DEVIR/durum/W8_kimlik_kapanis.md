# W8 — Kimlik kullanıcı ve hesap güvenliği kapanışı

Bu rapor KIM-S6, KIM-S8 ve KIM-S9 kapsamındadır. KIM-S5/S7 ve Settings başka sahibindedir.
Kaynak/derleme tamamlanması runtime kabulü, gerçek kullanıcı ataması veya production aktivasyonu değildir.

| Dilim | Durum | Kaynak / kalan |
|---|---|---|
| KIM-S6 | DONE — kaynak; merkez entegrasyonu bekliyor | `0eab5b25c`, taze aktör çiti `e1fb4bf9c`. Gerçek kullanıcı oluşturma, parola sıfırlama, kilit açma ve pasifleştirme; hedefin bütün üyelik şirketlerinde `identity.users.manage`, son Admin koruması ve gerçek atama ekranına devam. |
| KIM-S8 | PARTIAL — kaynak tamam; merkez menü/pin ve test kanıtı bekliyor | İsteğe bağlı TOTP/kurtarma kodu akışı, taze local oturum/şirket üyeliği, aynı Serializable işlemde credential/factor/idempotency/Audit. Kaynak checkpoint'i aşağıdadır. |
| KIM-S9 | PARTIAL — owner kaynak ve EF hazır; merkez CLI kompozisyonu ve kabul bekliyor | KIM-D27 için doğrudan oturum cevabı tüm kullanıcılarda açık hedefli sunucu kurtarmasını seçti; aktarım #1189 / 6951606530. Ayrı `identity recover-two-factor` komutu, global değişmez deneme, Audit ve SecurityEvents aynı Serializable işlemde. Ayrıntı aşağıdadır. |

## KIM-S6 — KIM-D9/D16/D17/D19

- Kullanıcı/grup hesabı oluşturma rol veya grant eklemez. İlk atama mevcut rol atama akışıyla yapılır. Kullanıcı şifresi ya da hash'i komut parmak izine, saklanan cevaba veya denetim kaydına girmez. Sonucu bilinmeyen istek ekran belleğinde aynı gövde/anahtarla tekrar edilir; parola web storage'a yazılmaz.
- Kullanıcı yönetimi komutları hedefin etkin üyelik şirketlerini taze okur; tüm şirketlerin yönetim yetkisini ve son Admin korumasını tek Serializable işlemde, sıralı şirket kilitleri altında doğrular. Yetkisiz okuma ve same-key replay ek yetki vermez.
- Arayüz `/ayarlar/kullanicilar`; capability `identity.users.manage`, Identity shell provider sürümü `1.3`. Menü ve merkez registry/pin üretimi entegrasyon sahibindedir.

## KIM-S8 — KIM-D3/D27

- TOTP isteğe bağlıdır; otomatik açılmaz, kimseye zorunlu olmaz. Kurulum şifreli ve aktör/stamp/sürüme bağlı hazırlık belirteci verir, doğrulayıcı kodu kanıtlanınca etkinleşir. Kurtarma kodları yüksek entropili, tek kullanımlı ve yalnız hash olarak saklıdır. Ham kodlar yalnız ilk NoStore cevabında görünür; replay kodları tekrar döndürmez.
- Açma/kapatma/yenileme mevcut parolayı ve uygun TOTP/kurtarma kanıtını doğrular, credential sürümünü ve security stamp'i yeniler. Böylece diğer oturumlar eski stamp ile geçemez. Yeni permission, grant, rol veya zorunlu 2FA politikası yoktur.
- Girişte parola doğru olsa bile ikinci faktör doğrulanmadan sayaç sıfırlanmaz, rehash veya geçici parola tüketimi yapılmaz. Faktör ve tek kullanımlık geçici parola aynı SQL işleminde tüketilir; başarısız yazı her ikisini geri alır. Yanlış parola/faktör aynı genel 401 cevabına gider; owner/crypto hatası başarı ya da yanlış kod olarak sunulmaz.
- Identity tablosu Audit olayı taşımaz. Typed Audit sözleşmesi `7e925e9d2`, gerçek Audit owner bağımlılığı `f2781f6eb` bu dalda `3acade858` karşılığıyla alınmıştır. Audit aktör=hedef, sabit `TOTP` yöntemi ve sabit Türkçe işlem gerekçesi taşır; parola/seed/token/kod/hash içermez. Identity, factor, journal ve Audit aynı owner transaction'ında commit/rollback olur.
- EF CLI üretimi: `20261117004612_AddLocalTwoFactorSecrets` + designer/snapshot. Up yalnız DDL, eski command correlation alanı nullable; geçmiş parola komutlarının gövdesi değişmez. İlk Down SQL'i **THROW 51967** ile factor satırı, etkin credential veya replay correlation kanıtı varken veri kaybını reddeder. DB apply **NOT_RUN**.
- Arayüz `/hesap/iki-asamali-dogrulama`; yeni capability türetilmez. Hesap menüsü ve merkez OpenAPI/registry/recovery/guard/ilişkisel envanter/test grubu üretimi entegrasyon sahibindedir. Varsayılan DataProtection composition kullanılır; ayrı anahtar, sağlayıcı veya environment kurulmadı.

## Yazılan kabul kaynakları ve derleme

- Unit: RFC 6238 SHA1 vektörleri, TOTP adımı/kurtarma kodu tek kullanım, hatalı faktörde geçici parolanın korunması, secret debug metninin redaksiyonu, model/snapshot/Down koruması ve kullanıcı yönetimi domain davranışı.
- Gerçek-SQL fact kaynakları: faktör ve geçici parolanın birlikte rollback/commit olması; gerçek Audit append sonrasındaki hatada credential/factor/journal/Audit sıfır kalıcı etki; aynı anahtarın tek olayla replay'i; secret içermeyen audit/journal; MFA ve replay kanıtını koruyan Down; şirketler arası yönetim izni ve son Admin.
- HTTP kaynakları: bütün kişisel 2FA yollarında anonim 401; şirket üyeliği olmayan local oturumda 403 ve kurulum/kod sızıntısı olmaması. Frontend: strict decoder, tek giriş isteğinde ikinci faktör, bilinmeyen sonuçta gövde/anahtarın sabit kalması, secret'ların storage'a yazılmaması ve kesin ret sonrası temizlenmesi.
- API, Identity.UnitTests ve Api.IntegrationTests kaynak derlemeleri `OpenApiGenerateDocumentsOnBuild=false` ile **exit 0 / 0 uyarı / 0 hata**. EF CLI üretimi exit 0. Yeni HTTP kaynakları son Integration derlemesine dahildir. OpenAPI üretimi bu derleme kanıtı değildir.
- TypeScript son kaynak derlemesi **exit 0**. Unit/SQL/HTTP/Vitest/browser/CI runtime ve DB migration uygulaması **NOT_RUN**; hiçbir test atlanmadı, silinmedi veya karantinaya alınmadı.
- **K3 review bekliyor:** global credential/factor ownership ve kişisel null-permission typed Audit yolu; yeni DbContext veya filter-bypass admission eklenmedi. Denetim/sırlar/transaction/fresh membership negatif akışları gerçek SQL kabulünde koşulmalıdır.

## Kaynak checkpoint'i ve bozuk owner kanıtı

- KIM-S8 kaynak checkpoint'i `bb82ad491`; API/Identity.Unit/Integration ve TypeScript exit 0 bu kaynağı kapsar. Yeni HTTP ve DOM test kaynakları dahildir. Runtime **NOT_RUN**.
- Son dar owner-read düzeltmesi: JSON olarak geçerli fakat null/bozuk/tekrarlı recovery hash dizisi taze domain okumasında reddedilir; geçerli boş dizi yalnız bütün kodlar tüketildiğinde geçerlidir. Bozuk authenticator seed'i ve hash kanıtı yanlış kullanıcı kodu veya sıfır kalan kod gibi sunulmaz; mevcut unavailable/503 yolunu kullanır. Yeni schema veya güvenlik politikası yoktur. Unit kaynakları eklendi; son derleme aşağıda kayıtlıdır.
- KIM-S6 komut parmak izi parola/hash içermez; oluşturmadaki kullanıcı adı, görünen ad ve gerekçe; yönetimdeki gerekçe gibi diğer gövde alanları anahtara bağlanır. Aynı anahtarla farklı kullanıcı adı ilk kullanıcıyı sessizce yeniden kullanmaz, idempotency conflict verir. Parola-only değişikliği eski password komut semantiğiyle ilk cevabı replay eder; ikinci parola uygulanmaz. Gerçek SQL test kaynağı bunu ayırır. Bu dar son kaynak değişikliği son derlemeye dahildir; test runtime **NOT_RUN**.

- Son kaynak başı `1e0f0c498` için Identity.UnitTests ve Api.IntegrationTests derlemeleri **exit 0 / 0 uyarı / 0 hata**. Komutlar `--no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false` ile sıralı çalıştı. Bu kanıt bozuk factor deposu ve parola dışı komut parmak izi düzeltmelerini içerir; runtime ve yeni OpenAPI üretimi **NOT_RUN**.

## KIM-S9 / KIM-D27 — açık hedefli operatör kurtarması

Bu kaynak değişikliği #1189 / 6933574705 sorusuna verilen doğrudan oturum cevabını uygular.
6951606530, bu cevabın ajan aktarımıdır; doğrudan insan GitHub yorumu olarak gösterilmez.
Eski `recover-founder` ve kişisel oturumun 2FA işlemleri genişletilmedi.

- Tam komut `identity recover-two-factor`; hedef kullanıcı adı, işlem anahtarı ve gerekçe terminalde sorulur.
  Fazladan komut satırı argümanı geçerli işlem sayılmaz ve argüman içeriği yazılmaz. Etkileşim ve açık hedef onayı gerekir.
- Hedef etkin veya pasif olabilir ve hiç şirket üyeliği olmayabilir. Kullanıcı etkinliği, parola ve rol/şirket atamaları değişmez.
  Parola, doğrulayıcı tohumu, kurtarma kodu, bunların özeti veya security stamp denetim/güvenlik/deneme kaydına taşınmaz.
  Gerçek işletim sistemi operatörü ve makine beyanı yalnız typed Audit provenansıdır.
- Onay kullanıcı/credential/factor sürümlerine bağlıdır. Yeni Serializable işlem bunları tekrar okur;
  onaydan sonra değişmişse doğrulayıcıya dokunmadan değişmez ret kaydeder.
- Başarı, doğrulayıcıyı pasifleştirme + yeni security stamp + değişmez deneme + iki modülün owner kaydıyla tek fiziksel işlemde yazılır.
  Audit veya SecurityEvents yazımı başarısızsa bu işlem commit edilmez. Sonucu belirsiz commit başarı diye sunulmaz;
  aynı anahtarla özgün sonuç yeniden sorgulanır.
- Anahtar global credential komutuna, hedefe, operatöre, gerekçeye ve özgün onay sürümlerine bağlıdır.
  Başka hedef/operatör/gerekçe özgün sonucu göremez. Aynı anahtarın replay'i sonradan kurulan doğrulayıcıyı kapatmaz;
  özgün sonucu döndürür ve güncel 2FA durumu iddia etmez.
- Global credential için sahte şirket oluşturulmaz ve bütün şirketlere olay yayılmaz. Yeni kapalı Audit subject'i
  `PlatformCredentialRecovery` / schema 3'tür; normal company ve Product audit constructor'ları, permission çitleri
  ve şirket okuma filtreleri korunur. SecurityEvents kapalı iki olayla mevcut No134 saklama kaynağını kullanır;
  yeni saklama süresi veya toplama politikası seçilmez.

### EF üretimi ve korumalar

Üçü de pinned `dotnet-ef` 10.0.8 ile gerçek üretildi; Designer ve snapshot dosyaları üreticiden gelir.
Veritabanı uygulaması yapılmadı.

| Migration | Koruma |
|---|---|
| `20261117160905_AddOperatorTwoFactorRecoveryAttempts` | İlk Down işlemi **51922**; global deneme geçmişi varken silmez. Up'ta dar UPDATE/DELETE tetikleyicisi **51925**; EF tracker da değiştirme/silmeyi bağlantı açmadan reddeder. |
| `20261117160915_AddPlatformCredentialRecoveryAudit` | İlk Down işlemi **51923**; platform kurtarma denetimi varken eski constraint'e dönmez. Up yalnız kapalı CHECK değişiklikleri. |
| `20261117161010_AddOperatorTwoFactorRecoverySecurityEvents` | İlk Down işlemi **51924**; yeni olaylar varken eski olay listesini geri getirmez. Up yalnız kapalı CHECK değişiklikleri. |

Numaralar ana/tren/ilgili owner kaynaklarında ve ilgili açık güvenlik PR başlarında çakışmasız doğrulandı;
üretim öncesi yalnız değişen aktif kaynaklar ve referanslar yeniden tarandı. Geçmiş migration düzenlenmedi.

### Kanıt ve kalan merkezi bağlar

- API, Identity.UnitTests, Audit.UnitTests ve SecurityEvents.UnitTests kaynak derlemeleri **exit 0 / 0 uyarı / 0 hata**.
  Üç EF üretimi de **exit 0**. IntegrationTests ilk kaynak derlemesi **exit 0**; senkron terminal yarış harness'i
  xUnit synchronization context'ini engellemeden ayrı owner işlemi çalıştıracak şekilde hizalandı ve son incremental
  kaynak derlemesi de **exit 0 / 0 uyarı / 0 hata** (43,04 saniye).
- İlk Audit.UnitTests denemesi eksik `project.assets.json` nedeniyle NETSDK1004 / exit 1 idi.
  Yalnız eksik iki owner test projesi için dar restore yapıldı, ikisi exit 0; paket/sürüm/lock değişikliği yok.
  Nullable query-filter assertion CS8602 düzeltildikten sonra Audit.UnitTests exit 0 oldu.
- SecurityEvents EF ve bir kaynak build başlangıcı RAM 2 GiB altında olduğu için **NOT_STARTED_RAM**;
  çalışan başka süreçlere dokunulmadan kaynaklar korunup yeterli RAM sonrası devam edildi. Ham log ve native exit kayıtları depodışıdır.
- Unit/model/CLI kaynakları; kapalı platform konusu, yanlış actor/permission, global anahtar, değiştirme/silme,
  yeni/özgün sürüm geçişi, argüman içeriğini yazmama ve LocalAccount marker çitini kapsar.
- Gerçek SQL için dört fact kaynağı vardır: etkin/pasif ve sıfır üyelikli hedef + yeni 2FA sonrası replay;
  iki owner'ın gerçek append'inden sonraki hata/rollback ve aynı anahtarın tekrar başarısı;
  onay sırasında gerçek factor sürüm değişimi; üç Down ve değişmez tetikleyici.
  **Unit/gerçek SQL/CLI runtime, browser, CI ve production aktivasyonu NOT_RUN.**
- Merkez: `IdentityCommandLine.RunAsync` mevcut recovery overload'una son optional
  `operatorRecoveryConsole` parametresiyle gerçek `ConsoleLocalTwoFactorOperatorRecoveryConsoleV1` bağlanmalı.
  Program ve merkezi envanter/pin dosyaları bu owner'da değiştirilmedi. Yeni izin veya grant yok.
- K3 review bekleyen yeni yüzeyler: global CompanyId'siz credential deneme tablosu; ayrı null-permission System
  Audit subject'i; Identity'nin iki fresh owner context construction noktası; ödünç aynı-transaction Audit/SecurityEvents
  owner portları; UPDATE/DELETE tetikleyicisi. Guard'ı geçirmek için sessiz allowlist genişletilmedi.
- Merkez üretici listesi: üç migration ve deneme durable-store/recovery envanteri; Audit/Identity/SecurityEvents model
  fingerprint'leri; migration mutation kaynakları (Identity Up tetikleyici + Down guard, diğer ikisinde Down guard);
  ilişkisel fact envanteri ve test grubu sabitlemeleri. Açık işletme kararı kalmadı.
