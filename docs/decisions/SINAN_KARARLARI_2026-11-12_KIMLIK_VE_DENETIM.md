# Sinan Kararları — 2026-11-12 (yerel hesap, rol atama, reddedilen denemelerin denetim kaydı)

Bu belge önceki karar dosyalarını silmez; `KIM-D1`…`KIM-D18` kararlarını ekler.
`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` BS-6 (tek seferlik kurucu admin) geçerli kalır.
BS-7'deki "production'da MFA zorunlu" yönü, 2FA zorunluluğu bakımından `KIM-D3` ile şimdilik yerini
"herkese isteğe bağlı" kararına bırakır (Sinan: "12 Kasım kararı geçerli"); BS-7'nin OIDC/VPN yönü ve
`OPEN-013` açık kalır.

**Kaynak:** Sinan (`sinangym`), Issue #66; kararlar koordinatör oturumunda AskUserQuestion ile alınıp
koordinatör tarafından yazıldı:

- [`6818566934`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6818566934)
  (2026-11-12 08:23Z, 1. tur) madde 1-2 — "önerilen seçeneklerin hepsi seçildi". Madde 3-4 (ürün modülü,
  fire maliyeti) bu dosyanın konusu değildir; ilgili şeridin dosyasına aittir.
- [`6819201876`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819201876)
  (2026-11-12 08:55Z, 4. tur) madde 2 — "önerilen seçeneklerin hepsi seçildi". Diğer maddeler bu dosyanın
  konusu değildir.
- [`6820046672`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820046672)
  (2026-11-12 09:36Z, 8. tur) madde 1-4 — AI Merkezi NEEDS-SINAN
  [`6819541595`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819541595) ve spec #1112
  üzerine. Serbest metin cevaplar aynen: "Gevşek ama ilerde ayarlardan değiştirilebilir" ve
  "Kurulumcu admin her zaman yapabilsin,".
- [`6820161788`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820161788)
  (2026-11-12 09:42Z, 9. tur) madde 4 — AI Merkezi F023-1 üzerine; "(Önerilen)". Madde 1-3 (F-029
  politika yönetimi) bu dosyanın konusu değildir.
- [`6820237902`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820237902)
  (2026-11-12 09:46Z, 10. tur) madde 1, 3, 4. Serbest metin cevap aynen: "Bilemiyorum ama bence şuanda
  hiçbiri". Madde 2 (F-029 N4) bu dosyanın konusu değildir.
- [`6820838938`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820838938)
  (2026-11-12 10:15Z, 14.–15. tur) madde 1-5 — AI Merkezi NEEDS-SINAN
  [`6820649209`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820649209) üzerine; altı
  maddenin hepsinde önerilen seçenek seçildi. Madde 6 (F-029 N6) bu dosyanın konusu değildir.
- [`6821105608`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821105608)
  (2026-11-12 10:27Z, 16. tur) madde 4 — #1112 `NEEDS-SINAN-ID-10` (son Admin). Cevap aynen: "Sistem
  engellesin." Madde 1-3 (transfer, fason tartı) bu dosyanın konusu değildir.
- [`6821262098`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821262098)
  (2026-11-12 10:35Z, 17. tur) madde 4 — #1112 (çok şirkette ilk Admin). Cevap aynen: "Kurucu hepsinde
  Admin." Madde 1-3 ve 5-8 bu dosyanın konusu değildir.

AskUserQuestion seçeneklerinin tam metni koordinatör oturumundadır; aşağıdaki "seçilmeyen" bilgisi #66
yorumlarında ve `6819541595` önerilerinde yazılı olanla sınırlıdır. 12-16. satırlardaki "Seçilmeyen"
hücreleri yazar özetidir: soru metni ve öneri `6820649209`'dadır, seçilmeyen seçeneklerin metni koordinatör
oturumundadır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | F-001 rol atama nasıl olsun? (1. tur) | Önerilen | Admin ekrandan rol atar; Critical rollerde (finans onayı, yetki yönetimi) ikinci kişi onayı zorunlu; ilk Admin kurulumda bir kez tanımlanır; atamalar denetim kaydına yazılır. |
| 2 | F-002 / `OPEN-013` giriş yöntemi? (1. tur) | Önerilen | Şimdilik yerel hesap (ERP içi kullanıcı adı/şifre, güçlü şifre politikası + isteğe bağlı 2FA); Microsoft/Google (OIDC) sonra ayar olarak eklenebilecek esnek yapı. |
| 3 | F-023 hangi reddedilen denemeler denetim kaydına yazılır? (4. tur) | Önerilen | Yetki retleri ve iş kuralı retleri dahil reddedilen tüm işlem denemeleri (kim, ne zaman, hangi işlem, ret nedeni); kişisel veri/şifre/secret yazılmaz; saklama süresi ayrı politika. |
| 4 | ID-01 2FA isteğe bağlı mı, zorunlu mu? (8. tur) | "Şimdilik herkese isteğe bağlı" | Kimseye zorunlu değil, pilot sonrası yeniden bakılır. Seçilmeyen: önerilen "Critical rol taşıyanlara zorunlu". |
| 5 | ID-02/03/06 parola, kilit, oturum değerleri? (8. tur) | Serbest metin: "Gevşek ama ilerde ayarlardan değiştirilebilir" | Koordinatörün yazdığı başlangıç değerleri: parola en az 8 karakter, 10 hatalı denemede kilit, boşta 8 saat; hepsi Ayarlar'dan değişir; sızmış/yasaklı parola listesi ve admin/admin yasağı korunur. Seçilmeyen öneri: 12 karakter, 5 hata/15 dk kilit, boşta 30 dk/mutlak 10 saat. |
| 6 | ID-04/07/08 Critical liste ve ilk Critical onaylayıcı? (8. tur) | Serbest metin: "Kurulumcu admin her zaman yapabilsin," | Koordinatörün okuması ("geri alınabilir" diye işaretli): kurulumu yapan Admin Critical rol atamasını ikinci kişi onayı olmadan her zaman yapabilir; diğer atayanlarda ikinci onay sürer. Critical işlem listesi seçilmedi. Seçilmeyen öneri (ID-08): kurulumda iki kişi tanımlansın. |
| 7 | ID-09 admin/admin nerede kabul edilir? (8. tur) | Yalnız bu bilgisayardaki geliştirme ortamı | Repoya, örnek dosyaya, seed'e, appsettings'e yazılmaz; test/canlı ortamda hiç kabul edilmez; başka kullanıcı bağlanmadan önce değiştirilir. |
| 8 | F023-1 reddedilen deneme kayıtları ne kadar saklanır? (9. tur) | Önerilen | 2 yıl; süre Ayarlar'dan değiştirilebilir; süre dolunca otomatik imha ayrı onayla açılır. |
| 9 | ID-04/07 Critical işlem listesi? (10. tur) | Serbest metin: "Bilemiyorum ama bence şuanda hiçbiri" | Koordinatörün okuması ("geri alınabilir" diye işaretli): şimdilik yeni işlem Critical'e eklenmez; ödeme/tahsilat onayı, `identity.users.manage`, resmî fatura içe aktarma, manuel makbuz Critical sayılmaz; mevcut Critical sınıflandırmalar kaldırılmaz. Seçilmeyen öneri: bu dördünün Critical olması. |
| 10 | F023-2/3 yetki retleri nerede tutulur, kim görür? (10. tur) | Önerilen | Tek kayıt güvenlik olay kanalında; yalnız güvenlik olayı okuma yetkisi olan görür. |
| 11 | F023-4 tekrarlayan 400/422 retleri? (10. tur) | Önerilen | Başta her ret ayrı yazılır; gerçek kullanım ölçüldükten sonra toplulaştırma açılır, eşikleri Sinan girer. F023-5 ve F023-6 teknik varsayılan olarak AI önerisiyle kalır. |
| 12 | ID-02b parola kuralı? (14.–15. tur) | Önerilen | Karakter sınıfı zorunluluğu yok, düzenli zorunlu değişim yok; yalnız en az 8 karakter (8. tur); değişim yalnız sızıntı şüphesinde; iki kural Ayarlar'dan açılabilir. Hash maliyeti teknik ayrıntı: framework PBKDF2 varsayılanı (spec önerisi A). Seçilmeyen: karakter sınıfı ve/veya dönemsel değişim zorunlu; Argon2id (öneri B). |
| 13 | ID-03b kilit süresi ve kilidi kim açar? (14.–15. tur) | Önerilen | Kilit 15 dk sonra kendiliğinden açılır; `identity.users.manage` sahibi daha erken açabilir; süre Ayarlar'dan değişir. |
| 14 | ID-06b oturum üst süresi? (14.–15. tur) | Önerilen | 12 saat mutlak; boşta kalma 8 saat (8. tur); değer Ayarlar'dan değişir. |
| 15 | ID-05 Critical rolün geri alınması? (14.–15. tur) | Önerilen | Tek adımda, ikinci onay yok; denetim kaydı yazılır. Seçilmeyen: geri almada da ikinci kişi onayı. |
| 16 | ID-07b grup geneli parola sıfırlama ve kilit açma? (14.–15. tur) | Önerilen | Yalnız, kullanıcının üye olduğu her şirkette `identity.users.manage` yetkisini şirket genelinde taşıyan kişi yapabilir. Seçilmeyen (yazar özeti): tek şirkette yetki yeterli; yalnız Admin. |
| 17 | ID-10 şirketteki son Admin kendi Admin rolünü kaldırmaya çalışırsa? (16. tur) | "Sistem engellesin." | Şirkette en az bir aktif Admin kalmak zorunda; son Admin'in rolü kaldırılamaz ve pasifleştirilemez; önce başka birine Admin verilir. Seçilmeyen seçeneklerin metni koordinatör oturumundadır. |
| 18 | Çok şirkette ilk Admin? (17. tur) | "Kurucu hepsinde Admin." | İlk kurulumda kurucuya o anda tanımlı bütün şirketlerde Admin atanır; sonradan açılan şirkette şirketi açan kişi ilk Admin olur; sonra başkalarına devredilebilir. Seçilmeyen seçeneklerin metni koordinatör oturumundadır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KIM-D1 | F-001 rol atama | Admin rolleri ekrandan atar; Critical rollerde ikinci kişi onayı zorunlu (istisnası KIM-D5); ilk Admin kurulumda bir kez tanımlanır; her atama audit'e yazılır. | `docs/features/IDENTITY_LOCAL_ACCOUNT_AND_ROLE_ASSIGNMENT_F001_F002_SPEC_v1.md` |
| KIM-D2 | F-002 giriş | Şimdilik yerel hesap + güçlü parola politikası + isteğe bağlı 2FA; OIDC sonra ayar olarak eklenebilir. `OPEN-013` kapanmaz. | Aynı spec |
| KIM-D3 | 2FA zorunluluğu | Şimdilik herkese isteğe bağlı, kimseye zorunlu değil; pilot sonrası yeniden bakılır. BS-7 ile çelişki bu kararla çözülür. Yöntem (TOTP önerisi) uygulama ayrıntısıdır (geri alınabilir). | Aynı spec, ID-5 dilimi |
| KIM-D4 | Parola/kilit/oturum başlangıç değerleri | En az 8 karakter, 10 hatalı denemede kilit, boşta 8 saat oturum; hepsi Ayarlar'dan değiştirilebilir. Sızmış/yasaklı parola listesi ve admin/admin yasağı korunur. Kilit süresi, kilidi kim açar, mutlak oturum süresi, karakter sınıfı, periyodik değişim ve hash maliyeti 8. turda seçilmedi (KIM-D12…D14 ile seçildi). | Aynı spec, politika tablosu |
| KIM-D5 | Kurucu Admin istisnası | Kurulumu yapan Admin, Critical rol atamasını ikinci kişi onayı olmadan her zaman yapabilir; diğer atayanlarda ikinci onay sürer. Kaynakta koordinatörün okuması olarak "geri alınabilir" diye işaretlidir. Critical işlem listesi 8. turda seçilmedi; KIM-D9 (10. tur) ile kapandı. | Aynı spec, maker-checker bölümü |
| KIM-D6 | admin/admin | Yalnız Sinan'ın bilgisayarındaki Development ortamında kabul edilir; repoya, örnek dosyaya, seed'e, `appsettings*`'e yazılmaz; test ve canlı ortamda hiç kabul edilmez; başka kullanıcı bağlanmadan önce değiştirilir. Açan ayarın yalnız yerel kullanıcı-gizli dosyasında durması uygulama ayrıntısıdır (geri alınabilir). | Aynı spec, "Varsayılan kimlik bilgisi yasağı" |
| KIM-D7 | F-023 reddedilen denemeler | Yetki ve iş kuralı retleri dahil reddedilen tüm işlem denemeleri denetim kaydına yazılır (kim, ne zaman, hangi işlem, ret nedeni); kişisel veri/şifre/secret yazılmaz; saklama süresi ayrı politikadır (KIM-D8). | `docs/features/AUDIT_REFUSED_ATTEMPTS_F023_SPEC_v1.md` |
| KIM-D8 | F-023 saklama | Reddedilen işlem denemesi kayıtları 2 yıl saklanır; süre Ayarlar'dan değiştirilebilir; süre dolunca otomatik imha ayrı onayla açılır (bu karar imhayı açmaz). Ayar adları uygulama ayrıntısıdır (geri alınabilir). | Aynı spec, §Saklama |
| KIM-D9 | Critical işlem listesi | Şimdilik yeni işlem Critical'e eklenmez: ödeme/tahsilat onayı, `identity.users.manage`, resmî fatura içe aktarma ve manuel makbuz Critical değildir. Bugünkü `ErpPermissions.Critical` sınıflandırmaları kaldırılmaz (ayrı karar ister). Kaynakta koordinatörün okuması olarak "geri alınabilir" diye işaretlidir; liste ileride karar ile genişletilebilir. | Kimlik spec'i, maker-checker bölümü |
| KIM-D10 | F-023 yetki retleri | Yetki retleri tek kayıt olarak güvenlik olay kanalında tutulur (denetim kaydına ikinci satır yazılmaz); yalnız güvenlik olayı okuma yetkisi olan görür. Yetki adı kaydı grant değildir. | F-023 spec, §Sınır ve ekran |
| KIM-D11 | F-023 hacim, replay, yazım hatası | Başta her ret ayrı satır; toplulaştırma ölçümden sonra, eşikleri Sinan girerek açılır. Aynı idempotency anahtarıyla gelen ret tekrarı yeni kayıt değildir, yalnız metrik. Ret kaydı yazılamazsa ret geçerli kalır ve alarm metriği üretilir. Son ikisi kaynağa göre teknik varsayılandır. | F-023 spec, §Hacim kontrolü ve §Yazım hatası |
| KIM-D12 | Parola kuralı ve hash | Karakter sınıfı zorunluluğu ve düzenli zorunlu değişim yok (ikisi de varsayılan kapalı, Ayarlar'dan açılabilir); değişim yalnız sızıntı şüphesinde. Hash: ASP.NET Core Identity `PasswordHasher<T>` (PBKDF2) framework varsayılanı; iterasyon ayarı uygulama ayrıntısıdır (geri alınabilir). | Kimlik spec'i, politika tablosu |
| KIM-D13 | Kilit | Kilit 15 dk sonra kendiliğinden açılır (başlangıç değeri, Ayarlar'dan değişir); `identity.users.manage` sahibi daha erken açabilir (KIM-D16 kapsamıyla); her açma denetim kaydına yazılır. | Aynı spec, politika tablosu ve `/unlock` |
| KIM-D14 | Oturum mutlak süresi | 12 saat (başlangıç değeri, Ayarlar'dan değişir); boşta kalma 8 saat (KIM-D4). | Aynı spec, politika tablosu |
| KIM-D15 | Critical rol geri alma | Tek adımda, ikinci onay yok; denetim kaydı ve `identity.permission.changed` olayı yazılır. Atama tarafındaki iki kişi kuralı (KIM-D1/D5) değişmez. | Aynı spec, atama durumları ve `/revoke` |
| KIM-D16 | Grup geneli parola sıfırlama/kilit açma kapsamı | Yalnız hedef kullanıcının üye olduğu **her** şirkette `identity.users.manage` yetkisini şirket genelinde taşıyan kişi yapabilir; tek bir şirkette eksikse istek reddedilir (fail-closed). |  Aynı spec, hedef kapsam ve izin tablosu |
| KIM-D17 | Son Admin koruması | Her şirkette en az bir aktif Admin kalır. Şirketteki son aktif Admin'in Admin ataması geri alınamaz ve o kullanıcı pasifleştirilemez (kendisi veya başkası yapsa da); sistem reddeder, önce başka birine Admin verilir. "Admin" = mevcut Admin persona tanımına eşlenen rol; kodda rol adı sabiti yok. Problem kodu ve eşzamanlılık kilidi uygulama ayrıntısıdır (geri alınabilir). | Kimlik spec'i, görev ayrılığı, `/revoke`, `/deactivate` |
| KIM-D18 | Çok şirkette ilk Admin | Bootstrap, kurucuya o anda tanımlı **bütün** şirketlerde Admin atar. Sonradan açılan şirkette şirketi açan kişi o şirketin ilk Admin'i olur. Admin sonra başkalarına devredilebilir (KIM-D17 sınırıyla). Şirket açma akışının Identity'ye nasıl bildirildiği uygulama ayrıntısıdır (cross-module sözleşme, K3). | Kimlik spec'i, bootstrap ve görev ayrılığı |

**Yazar okumaları (Sinan kararı değildir; kimlik spec'inde uygulanır, review'da düzeltilebilir):**

- KIM-D16: "üye" = bugün geçerli (başlamış, süresi dolmamış) ataması olan şirket; site/lokasyon kapsamlı atama
  o şirkette üyeliktir. Hiç geçerli ataması olmayan kullanıcıda boş küme "herkes" diye okunmaz: işlemi yalnız
  **bütün tanımlı** (pasifler dahil) şirketlerde şirket geneli `identity.users.manage` taşıyan yapabilir;
  kurucuya ayrı yol açılmaz (KIM-D5 yalnız Critical rol atamasıdır). Aynı kapsam pasifleştirmeye de uygulanır.
- KIM-D17: "aktif Admin" = aktif kullanıcının, aktif rol tanımındaki, başlamış ve süresiz şirket geneli Admin
  ataması; süreli atama sayılmaz. Problem kodu ve kilit uygulama ayrıntısıdır.
- KIM-D18: "bütün şirketler" pasif şirketleri de kapsar.

## Sınırlar

- Karara bağlanmayanlar `NEEDS-SINAN` olarak açık kalır: otomatik imhanın açılması (ayrı onay ve ADR) ve
  toplulaştırma eşik değerleri (ölçümden sonra). ID-02b/03b/05/06b/07b 14.–15. turda (KIM-D12…D16),
  ID-10 16. turda (KIM-D17), çok şirkette ilk Admin 17. turda (KIM-D18) karara bağlandı.
- Şirket açma yetkisinin kimde olduğu bu dosyanın konusu değildir (Organization modülü); KIM-D18 yalnız
  açan kişinin ilk Admin olmasını söyler, şirket açma izni vermez. Bu iznin Critical sınıflandırması
  `NEEDS-SINAN-ID-12`, şirket açılış olayı işlenemezse kurtarma yolu `NEEDS-SINAN-ID-11` olarak açıktır.
- ADR-SEC-001 E8 (başarısız girişte kullanıcı adı hash'inin anahtarı) bu dosyanın konusu değildir: anahtar
  bir secret olduğundan insan kapısıdır; o gelene kadar hash yazılmaz.
- Bu dosya `OPEN-013`'ü veya başka bir `OPEN-*` kaydını kapatmaz; grant/rol ataması, ilk Admin'in
  oluşturulması, Critical küme değişikliği veya production aktivasyonu değildir.
- İnsan kapıları değişmez: deployment, environment, secret, Settings etkinleştirmesi, grant/rol/Critical,
  production, `OPEN-*` kapatma ve `.claude/settings.json`.
