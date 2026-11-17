# W8 — Kimlik kullanıcı ve hesap güvenliği kapanışı

Bu rapor KIM-S6, KIM-S8 ve KIM-S9 kapsamındadır. KIM-S5/S7 ve Settings başka sahibindedir.
Kaynak/derleme tamamlanması runtime kabulü, gerçek kullanıcı ataması veya production aktivasyonu değildir.

| Dilim | Durum | Kaynak / kalan |
|---|---|---|
| KIM-S6 | DONE — kaynak; merkez entegrasyonu bekliyor | `0eab5b25c`, taze aktör çiti `e1fb4bf9c`. Gerçek kullanıcı oluşturma, parola sıfırlama, kilit açma ve pasifleştirme; hedefin bütün üyelik şirketlerinde `identity.users.manage`, son Admin koruması ve gerçek atama ekranına devam. |
| KIM-S8 | PARTIAL — kaynak tamam; merkez menü/pin ve test kanıtı bekliyor | İsteğe bağlı TOTP/kurtarma kodu akışı, taze local oturum/şirket üyeliği, aynı Serializable işlemde credential/factor/idempotency/Audit. Kaynak checkpoint'i aşağıdadır. |
| KIM-S9 | BLOCKED — açık kapsam sorusu | KIM-D27 sunucu CLI kurtarması, eski founder-only ID-2r kapsamını aşar. Sıradan kullanıcının bütün cihaz/kodları kaybolduğunda CLI hedefi henüz açık değildir. `NEEDS-SINAN` #1189 yorum 6933574705 yanıtı beklenir; sessiz founder-only veya tüm kullanıcı politikası seçilmedi. |

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
- Son dar owner-read düzeltmesi: JSON olarak geçerli fakat null/bozuk/tekrarlı recovery hash dizisi taze domain okumasında reddedilir; geçerli boş dizi yalnız bütün kodlar tüketildiğinde geçerlidir. Bozuk authenticator seed'i ve hash kanıtı yanlış kullanıcı kodu veya sıfır kalan kod gibi sunulmaz; mevcut unavailable/503 yolunu kullanır. Yeni schema veya güvenlik politikası yoktur. Unit kaynakları eklendi; bu son dar değişikliğin .NET derlemesi henüz **NOT_BUILT**.
