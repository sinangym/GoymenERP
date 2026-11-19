# W8 kaynak incelemesi sonrası düzeltmeler — 19 Kasım 2026

Bu kayıt mevcut W8 doğrulama/düzeltme kapsamındadır; yeni dalga veya yeni iş dilimi açmaz.
Kullanıcı sırası: doğrulanmış hataları birlikte düzelt, kaynağı yeniden incele, ardından düzeltilmiş
tek adaydan yeniden derleyerek test et. Eski aday için bekleyen takip başlatıcısı durduruldu.
Devam eden eski SQL koşusu ayrı tanısal kanıt olarak korunur; yeni kaynağın başarısı sayılmaz.

## Kaynak düzeltmeleri

| Bulgu | Değişiklik | Kaynak commit |
|---|---|---|
| F1 — Ayarlar provider hatası kalıcı conflict cevabına dönüşebiliyordu | 13 yönetim servisinde gerçek unique/concurrency ayrıldı; diğer SQL/EF hatası retry anahtarını tüketmeyen provider-unavailable olur. İlk receipt okuması ve catch içindeki winner okumasının SQL arızası da aynı yola gider. | `885fa0264` |
| F2 — Para profili yarış dönüşünde son yetki ölçümü eksikti | Son winner okumasından sonra şirket/aktör/yetki tekrar ölçülür; ikincil refusal kaydı öncesi de kontrol edilir. | `885fa0264` |
| F3 — Giriş başarı yazımı arada oluşmuş hesabı kilitleme kararını silebiliyordu | Normal/geçici parola başarı yazımı okunan lockout değeriyle sınırlandı. Paralel yeni kilit, parola rehash'i veya geçici parolanın tüketilmesiyle aşılmaz. Hatalı giriş sayacı değişmedi. | `d26f82de1` |
| F4 — Aynı maliyet girdisinde Pending→Recognized anahtarı çakışıyordu | İlk tarihsel anahtar korunur; sonraki geçişin kimliği beklenen Inventory sürümü, hedef durum ve girdi fingerprint'ini içerir. Eski receipt yeniden yazılmaz. | `461640edd` |
| F4 ek — Pending gözlem transit transfer kabulünü bozuyordu | Pending/Unreadable gözlem ve tersleme fiziksel etiket sürümünü artırmaz. Sürüm/ürün kontrolleri ve Serializable transaction korunur; Recognized parasal etkinin sürüm çiti sürer. | `461640edd` |
| F5 — Muhasebe store arızası sahte conflict veya kalıcı 422 üretebiliyordu | Eşleme ve katalog store'ları provider arızasını ayrı taşır; servis güncel yetkiyi tekrar ölçüp kalıcılaştırılmayan 503 döner. | `7a4498ce0` |
| F6 — Belirsiz bozdurma defter değeri komutu anahtarını kaybedebiliyordu | Exact şirket/aktör/oturum/namespace bağında yalnız bellekte anahtar ve gövde korunur. Route dönüşü ve belirsizlik sonrası erken ret aynı komutu korur; başka bağlamın geç yanıtı bırakılır. | `71b12045f` |

Ek SQL receipt-okuma regresyonu: `8a9231a4`. Altı F bulgusu ve F4'ün bağlantılı ek hatası
kaynakta düzeltildi. Bu ifade testlerin geçtiği veya bütün depoda başka hata olmadığı iddiası değildir.

Finance Q1 eşzamanlı reuse/cancel adayı değiştirilmedi: mevcut yeniden kullanım yolu yeni parasal
kalem oluşturmuyor veya iptal edileni açmıyor. Örtüşen kabulün iptalden önce sıralanması mümkün;
gerçek tüketici yanlış etkisi kanıtlanmadan politika veya kayıt davranışı değiştirilmedi.

## Yazılan doğrulama

- Sekiz yeni gerçek SQL fact: iki muhasebe provider-recovery, bir credential-lockout yarışı,
  bir gerçek transit/Pending/Recognized akışı, iki MoneyProfile winner-authority yarışı,
  bir MoneyProfile gerçek 1222 rollback/retry ve bir banka CSV receipt-okuma 1222/retry.
- Üretimde mevcut Unreadable/reopen/reclose testi fiziksel sürüm/zaman korunumuyla güçlendirildi.
- Kimlik ve muhasebe birim regresyonları ile maliyet geçiş anahtarı sözleşme testi yazıldı.
- Ekranda altı ek consumer regresyonu: erken 400/403, route dönüşü, şirket/aktör ayrımı,
  oturum sonu, eski bağlamın geç yanıtı ve doğrulanmış başarı sonrası anahtar temizliği.
- Yeni 46 hedeflik SQL listesi eski düzeltme örneklerini de içerir. Keşif/TRX kimlikleri birebir
  karşılaştırılır; eksik, fazla veya atlanan test başarı sayılmaz.

Bu belgenin kaynak kapanışı anında build/unit/SQL/Vitest **NOT_RUN**. Kaynak incelemesi ve
`git diff --check` tamamlandı. Çalıştırma kanıtı dondurulan adayın tam SHA'sına ayrı kaydedilecek;
başka SHA'nın ikilisi kullanılmayacak. Yerel SQL sürümü ile CI SQL sürümü farklı olduğundan
yerel sonuç, resmi CI veya tam K3 kabulünün yerine geçmez.

## K3 incelemesinde korunacak sınırlar

Yeni SqlClient yüzeyleri yalnız hata sınıflandırır: yeni connection, ham SQL komutu veya tenant
filtresi bypass'ı eklenmedi. Settings'teki 11 yeni sınıflandırma kaynağı ve Accounting mapping
store'u guard listesinde **K3 review pending** gerekçesiyle açıkça kayıtlıdır. Var olan CompanyPolicy,
WorkCenterCapacity ve Accounting catalog kayıtlarının gerekçeleri/pinleri de güncellendi.
Bu kayıtlar bağımsız inceleme imzası değildir; eski K3 bekleyen girdiler korunur.

Pending stok gözlemi, sahibin zorunlu Serializable transaction'ında okunan label/lineage satırlarını
korur. Kaynak dayanağı: [SQL Server SERIALIZABLE tanımı](https://learn.microsoft.com/sql/t-sql/statements/set-transaction-isolation-level-transact-sql#arguments).
Regresyon gerçek eşzamanlı label UPDATE'nin 1222 ile engellendiğini ve transaction sonrası gerçek
transfer kabulünün ilerlediğini doğrulamak üzere yazıldı; henüz çalıştırılmadı.

Ekran bellek kaydı browser storage'a yazılmaz. Sayfa oturum kapanırken unmounted ise eski kayıt
sonraki mount'ta budanır; farklı session/actor bağından geri okunamaz. Tam tarayıcı kabulü ayrıca gerekir.
Migration, işletme kararı, grant/rol/Critical, OPEN, production, Finance STOP veya workflow değişikliği yoktur.

## İlk doğrulama sonrası dar pin düzeltmesi

`b3f21f8f4` adayında derlemeler, 769 kimlik ve dört audit persistence testi geçti.
405 mimari testin biri, SQL doğrulayıcısının eski kaynak fingerprint'i nedeniyle başarısız oldu;
kuyruk SQL hedeflerini başlatmadan durdu. Bu sonuç tam kabul değildir.

Kök neden: `9077fdf4f` süre aşımında etkin global/process bütçesini raporlarken
`ReviewedSqlServerRcsiVerifierFingerprint` güncellenmemişti. Kaynak farkı ve mevcut
timeout/redaction regresyonu okundu: toplam 10800 saniye ve süreç başına 2400 saniye,
exact discovery, skip0, hata sonrası proof yasağı değişmiyor. Pin ev üreticisinin
`7982b720…` çıktısına güncellendi; başka guard koşulu veya izin listesi değiştirilmedi.

Bu ekin yazıldığı anda düzeltilmiş adayın testleri henüz çalıştırılmadı. Eski sonuçlar korunur;
yeni kanıt yeni tam SHA'ya bağlanır. Bağımsız exact-head K3 incelemesi ayrıca bekler.

## Test veritabanı bağlantı eşliği düzeltmesi

`3a51cca60` adayında derlemeler ve 405 mimari test geçti. 14 dar API sözleşme testinin
biri, aynı test veritabanının aynı bağlantıyla yeniden açılmasını yanlış reddetti.
SQL hedefleri başlamadan kuyruk durdu.

`SqlServerTestDatabase.ConnectionForHost`, yerinde değiştirilmiş builder ile serileştirilip
yeniden okunmuş builder'ı karşılaştırıyordu. Boş `AttachDBFilename` serileştirme/okuma sırasında
anahtar listesinden düşebildiğinden `EquivalentTo` aynı bağlantıyı farklı sayıyordu.
Karşılaştırmadan önce iki taraf aynı serileştirme/okuma adımından geçirilir; bütün kalan
ayarlar ve katalog adı çiti korunur. Kaynak kontrolü: [.NET 10 bağlantı builder'ı](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Data.Common/src/System/Data/Common/DbConnectionStringBuilder.cs).

Yeni regresyonlar: Mandatory/Optional/Strict şifreleme modları, boş alan ve anahtar sırası/eşadı
farkları kabul edilir; sunucu, kullanıcı/parola, Windows kimliği, TLS, sertifika adı, okuma niyeti,
pooling veya katalog adı değişikliği reddedilir. Sadece sentetik bağlantılar ayrıştırılır; bu
birim testleri veritabanına bağlanmaz. Mevcut gerçek SQL oluşturma/yeniden açma/çakışma/temizleme
testi 46 hedef içinde korunur. Üretim bağlantıları, sunucu ayarları veya migration değişmedi.
Bu ek yazılırken yeni regresyonlar henüz çalıştırılmadı; sonuçlar yeni aday SHA'sına kaydedilecek.

## Gerçek SQL'de bulunan geçici hata sarmalayıcısı

`691b65ccc` adayında derlemeler, seçilmiş 6108 backend testi, 13 ekran testi ve beş Python
doğrulayıcı paketi geçti. 46 hedeflik yerel SQL kuyruğunda ilk hedef geçti; ikinci hedef
`Reference_list_SQL_lock_timeout_is_unavailable_without_consuming_the_key_or_leaking_audit`
başarısız oldu ve kuyruk durdu. Kalan 44 hedef bu adayda çalışmadı.

SQL 1222, EF Core 10.0.8'in varsayılan yürütme stratejisinde
`InvalidOperationException -> DbUpdateException -> SqlException` olarak geliyor.
Yalnız doğrudan SQL/EF hatasını yakalayan provider filtresi bu biçimi kaçırıyordu.
Kaynak: [EF Core 10.0.8 SQL Server yürütme stratejisi](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.SqlServer/Storage/Internal/SqlServerExecutionStrategy.cs).

Aynı kalıbı kullanan 14 Ayarlar yönetim servisinin 28 save/refusal filtresi, doğrudan
SQL/EF hatasına ek olarak yalnız iç hatası SQL/EF olan bu sarmalayıcıyı tanır. Genel
InvalidOperationException veya iptal hatası provider arızası sayılmaz. Unique/concurrency
yarışı, yeniden yetki ölçümü, rollback, anahtar ve audit kuralları değiştirilmedi.
Referans listesi ve para profili gerçek kilit testleri sarmalayıcı tipini de doğrular;
başarısız komutun anahtar/audit tüketmemesi, yeniden deneme ve replay beklentileri korunur.

Guard fingerprint'leri mevcut ev üreticisiyle yenilenir; yeni bypass girdisi veya SQL
yüzeyi yoktur, K3 review bekler. Bu ek yazılırken düzeltilmiş adayın SQL sonucu **NOT_RUN**.

## Boş Audit geri alma zincirinde sıfır satırlı UPDATE

`7e97c36d9` adayında kaynak ön kontrolleri, derlemeler, seçilmiş backend/ekran testleri
ve ilk sekiz SQL hedefi geçti. Önceki Ayarlar 1222 regresyonları dahil bu sekiz sonuç
skip0'dır. Dokuzuncu SQL hedefinde boş Audit latest→zero geri alma zinciri 51000 ile
durdu; diğer 37 SQL hedefi ve sona alınan beş Python doğrulayıcı paketi çalışmadı.

Aynı eski ikiliden EF CLI ile üretilen geri alma SQL'i, `AddTypedFounderRecoverySystemAudit`
Down'ında `PermissionCode` için sıfır satır etkileyen UPDATE olduğunu gösterdi.
SQL Server UPDATE tetikleyicisini etkilenen satır olmasa da çalıştırır; ilk Audit
trigger'ı koşulsuz 51000 veriyordu. Kaynaklar:
[EF Core 10.0.8 migration SQL üreticisi](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.SqlServer/Migrations/SqlServerMigrationsSqlGenerator.cs),
[SQL Server DML trigger açıklaması](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-trigger-transact-sql?view=sql-server-ver16).

W8'de EF ile üretilmiş `ProtectAuditHistoryRollback` onarıldı; önceki tarihsel migration'lar
ve snapshot değişmedi. Down önce bütün tabloyu kilitleyerek dolu geçmişi 51900 ile reddeder,
sonra 51901 INSERT bariyerini kurar. Mevcut UPDATE/DELETE koruması yalnız inserted/deleted
boşken devam eder; gerçek satır mutasyonu yine 51000'dır. Up, bariyeri kaldırmadan önce
eski koşulsuz korumayı geri kurar. Yeni THROW numarası, veri silme veya saklama kararı yoktur.

Mevcut gerçek SQL testi boş UPDATE/DELETE'yi ve reapply sonrasında gerçek UPDATE/DELETE'nin
51000 ile reddini, payload ve migration geçmişinin aynen korunmasını da kontrol eder.
Önceki dolu tablo reddi, migration'lar arası append bariyeri ve eşzamanlı 1222 kontrolleri
korunur. Ev üreticisi fingerprint'i yenilendi; K3 inceleme kapısı sürer.
Bu ek yazılırken yeni adayın yürütme kanıtı **NOT_RUN**.

### Kasa eşzamanlı karar testinin SQL hata gözlemi — 20 Kasım

`cefb622b1152836e374edfc2f250d3a2bcb514ea` adayında dokuz derleme, 6108 backend
kontrolü, frontend tür kontrolü ve 13 hedef Vitest geçti. İlk 16 gerçek SQL hedefi
skip0 geçti; bunlar boş/dolu Audit geri alma ve Ayarlar kilit hatası regresyonlarını
da kapsar. 17. hedef `Two_racing_decisions_on_one_passivation_store_one`, beklenen
bir başarılı karar ve bir Unavailable sonucu gördükten sonra deadlock sayacında
beklenen 1 yerine 0 ile durdu. Kalan 29 SQL hedefi ve sonraki beş Python paketi
**NOT_RUN**; bu sonuç tam SQL kabulü değildir.

Test gözlemcisi yalnız komutun yürütme hatasını dinliyordu. SQL hata cevabı EF'nin
save batch sonuçlarını okuduğu aşamada da gelebilir. Gözlemci mevcut ev örneğindeki
`SaveChangesFailedAsync` sınırına taşındı; iç istisnanın gerçek SQL 1205 numarasını
sayarak EF sarmalayıcılarını kapsar. Timeout veya başka hata deadlock sayılmaz.
[EF SaveChanges hata gözlemi](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#savechanges-interception).

İki ayrı deadlock kurbanını zorlayan tur, tek başarılı karar, yetkili hata sonucu,
sürüm, tek komut ve tek audit beklentilerinin tamamı korunur. Uygulama kodu,
transaction sınırı, otomatik retry ve test kimlikleri değişmedi. Yeni adayın
gerçek SQL sonucu bu ek yazılırken **NOT_RUN**; önce bu hedef doğrulanacaktır.
