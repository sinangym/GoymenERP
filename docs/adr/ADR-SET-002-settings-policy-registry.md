# ADR-SET-002 — Settings politika kaydı: tek saklama sahibi, tüketici sahipli aile şeması

- Tarih: 2026-10-21
- Durum: Taslak; K3 iki bağımsız exact-head review ve insan review bekliyor.
- Kapsam: EK-38 / `W1-SETTINGS` için yalnız mimari tasarım; tüketiciler EK-03, EK-08, EK-36, EK-43.
- Kaynak tabanı: `main@b6ed7ca5496f8f34314a0a4b2d3f13467bb5ffe7`.
- Kaynak izni: [Issue #66 / 6314255220](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6314255220), yalnız bu ADR.

## Bağlam ve dayanak

[DEC-SET-001](../decisions/KARAR_SICILI.md) ve
[Sinan Kararları 2026-08-31 §5](../decisions/SINAN_KARARLARI_2026-08-31.md),
politika değerlerinin şirket kapsamlı, sürümlü ve auditli tek bir Settings registry'sinden
yönetilmesini; kullanılan sürüm ve değerin işlem snapshot'ında korunmasını ister.
Gerekli politika yoksa ilgili etki fail-closed kalır. Bu karar, her ailenin değerini,
kapsam önceliğini, tarih kuralını veya yetkisini belirlemez.

Yerel uygulama paketindeki `POLITIKA_SAHIPLIGI_RV12_v2.md` bir tasarım girdisidir;
karar kaynağı değildir. Doğrulanan SHA256:
`b9d7cd60e8fd9ddf2f4b17ad2edceab95af0a20f44da8a5db41e54bc970c73b0`.
Bu ADR, girdinin T1–T7 saklama önerisini ve K1–K20 açık anlam sınırlarını daraltılmış
tasarım olarak ele alır; soruları veya merkezi durum kayıtlarını kapatmaz.

Okuma referansı [PR #759](https://github.com/sinangym/GoymenERP/pull/759)
`a76f24517a6bfc10df1ab407806fff0e9b34a10d` head'idir; merge edilmiş taban değildir.
Bu head'deki
[`SettingsDbContext.cs`](https://github.com/sinangym/GoymenERP/blob/a76f24517a6bfc10df1ab407806fff0e9b34a10d/src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsDbContext.cs)
216 satırdır; Git blob baytlarının SHA256'sı
`c84fdb64ec4d6392a43241f0eade5f3a70e2a569ed0502f4ede95081ccac06bc`.
Aşağıdaki entegrasyon sırası bu korumalı yüzeye göredir; başka bir yerel HEAD'in
veya raporun tam dosya metninin bunun yerine geçirilmesini önermez.

## Önerilen saklama ve sahiplik

1. Yeni politika ailelerinin değer/sürüm kayıtları tek Settings registry'sinde tutulur.
   RV12 v2'deki `settings.PolicyVersions` bu çekirdeğin tasarım adayıdır; bu ADR bir
   tablo, migration veya çalışan servis eklemez. Diğer modüller Settings tablolarına
   doğrudan yazmaz; sürümlü uygulama sözleşmesini tüketir.
2. Tipli master'lar politika ailesine çevrilmez. KDV, kur, manuel kur istisnası,
   iş takvimi ve vade profili tasarımları kendi tipli kayıtlarını korur. Olay/aggregate
   niteliğindeki `OperationalYears`, `LegalHolds`, `RecordDisposalRequests` de politika
   değeri değildir. Bu ayrım, bu kayıtların uygulanmış veya kabul edilmiş olduğunu
   söylemez ve veri sahipliğini başka modülden Settings'e devretmez.
3. Mevcut `ManufacturingTolerancePolicies` ve #759'daki
   `ManufacturingToleranceProfileLifecycles` korunur. Registry tasarımı bunları taşımaz,
   çift yazma başlatmaz veya mevcut sağlayıcının kaynağını değiştirmez.
4. Registry saklama mekanizmasının sahibi Settings'tir. Aile alanlarının anlamı,
   birim/karşılaştırma kuralları ve tüketici kapsamı, ilgili tüketici owner'ının kaynaklı
   sözleşmesiyle gelir. Şema ve işletme anlamı hazır olmayan aile kaydedilmez;
   tüketicide ilgili sonuç `Unavailable` kalır. Rapor içi varsayım iki yerde tekrar
   edildiği için kabul edilmiş sayılmaz.

## Exact-scope okuma ve snapshot

Önerilen registry okuyucusu, server'dan çözülmüş şirket, onaylı aile kimliği,
ailenin onaylı tam kapsamı ve tüketici sözleşmesince belirlenmiş iş tarihiyle okur.
Adaptör şirket, ürün, banka, vade veya işlem boyutunu kendiliğinden tamamlamaz;
alias, kapsam önceliği, geri düşüm veya yeni tarih dönüşümü eklemez.

Ailenin yaşam döngüsü ve yürürlük anlamı ayrıca onaylandığında okuyucu, tam kapsam
ve onaylı tarih anlamına uyan tek yayımlanmış sürümü çözmelidir. Sıfır veya birden
fazla eşleşme, eksik/değişik kapsam, onaysız şema ya da gerekli kararın yokluğu ilgili
etki için `Unavailable`/default-deny sonucudur. İlk/en son sürüm, şirket geneli satır,
sıfır değer veya kod sabitiyle devam edilmez. `businessDate` adaptörde bugünden ya da
`OccurredAtUtc`'nin UTC gününden türetilmez; tarihi belirleyen tüketici sözleşmesi yoksa
o aile açılmaz. Bu, mevcut Manufacturing sağlayıcısının bugün bu algoritmayı
uyguladığı iddiası veya onun algoritmasını bu ADR ile değiştirme izni değildir.

Tüketici mevcut sürümlü sağlayıcı arayüzünü korur. Sonraki yetkili dilimde gerekirse
registry okuyucusuna dar adaptör yazılır; wire DTO veya izin adı burada seçilmez.
[Mevcut Manufacturing sözleşmesi](../../src/backend/Modules/Settings/GoymenERP.Modules.Settings.Application/Contracts/V1/ISettingsManufacturingToleranceProviderV1.cs)
kimlik, sürüm, değer ve pencerenin birlikte saklanmasını ister. DEC-SET-001 ve
DEC-PRDCT-008 uyarınca tüketici, kullanılan gerçek değeri ve sürümü kendi işlem
snapshot'ına yazar; sonraki politika değişikliği geçmişi yeniden hesaplamaz.
`PolicyId + VersionNo + ValuesHash` olası teknik referans biçimidir; hash gerçek
değer snapshot'ının yerine geçmez ve mevcut contract'a bu ADR ile alan eklenmez.
Override yalnız zaten yetkilendirilmiş işlem sözleşmesiyle ve kullanılan değer/sürüm
kanıtıyla yapılabilir; registry okuması override yetkisi üretmez.

## Yaşam döngüsü, yazma çiti ve şirket izolasyonu

Mevcut Manufacturing yaşam döngüsü `Draft → Approved → Published` ve immutable
yayın kuralını korur. Onaylayan hazırlayandan, yayımlayan da hazırlayandan farklıdır;
onaylayanın yayımlaması yasak değildir. Üç ayrı kişi şartı eklenmez.
[Lifecycle sözleşmesindeki](../../src/backend/Modules/Settings/GoymenERP.Modules.Settings.Application/Contracts/V1/ManufacturingToleranceProfileLifecycleV1.cs)
eylem adları birer bildirimdir; izin kaydı veya grant değildir. Bu yaşam döngüsünün
diğer ailelere uygulanması, onların onay ve yürürlük kararlarını ikame etmez (K15).
#759'da lifecycle yayını mevcut runtime sağlayıcısını etkinleştirmez; bu ayrım korunur.

#759 referansındaki bütün public `SaveChanges*` overload'ları kapalı kalır.
`SaveLifecycleCommandChangesAsync` / `ApplyLifecycleCommandWriteGuards`, mevcut
geçiş alan listeleri, immutable yayın ve silme reddi korunur. Yeni yazma yolu,
ayrı yetkili dilimde yalnız kendi adlandırılmış save ve dar entity/alan guard'ıyla
tasarlanır; registry eklenmesi genel save'i açma veya mevcut guard'ın bütün yeni
entity'leri yazmasına izin verme gerekçesi değildir.

Lifecycle, `CommandExecution` ve `AuditEvent` için
`companyContext.IsResolved && x.CompanyId == companyContext.CompanyId` filtreleri
korunur. Şirket kontrolü mutation anında da uygulanır.
`SettingsAuditEventTenantGuard` aktif şirkete append-only audit sınırını korur;
`CommandExecution` kayıtları mevcut aktif şirket ve append-only kurallarında kalır.
Audit/Idempotency mapping'leri `excludeFromMigrations: true` kalır; Settings bu
modüllerin şema sahipliğini üstlenmez.

Gelecek registry komutları mevcut authorization, expected version, idempotency,
correlation/causation ve audit sözleşmelerine uymalıdır. İlgili durum değişikliği,
command kaydı ve audit mevcut atomik transaction sınırında ele alınır; sonradan
audit tamamlama veya ikinci iş hareketi üretilmez. Dış etki gerekiyorsa mevcut outbox
sözleşmesi ve inbox/dedup sınırı ayrı yetkili kaynak diliminde korunur. Bu ADR yeni
retry, transaction izolasyonu veya genel lock politikası seçmez.

## Açık anlamlar: RV12 v2 K1–K20

Aşağıdaki tablo RV12 v2'nin tasarım incelemesindeki belirsizliklerini taşır; yeni
işletme kararı değildir. Sonraki dilim, kendi güncel karar/sahip sözleşmesini doğrular.
Review, eksik insan kararının yerine geçmez. Dayanağı olmayan davranış açılmaz.

| Kimlik | Korunan sınır |
|---|---|
| K1 | Kantar ürün/işlem boyutlarının varlığı, aralarında öncelik veya ürün → işlem geri düşümü belirlemez; exact scope dışı okuma yok. |
| K2 | Kantar yüzdesi için ne 100 üst sınırı ne sınırsızlık seçilir; aile şeması owner/karar kontrolünü bekler. |
| K3 | Örnek başlangıç değerlerinin sıralı olması `Warn ≤ Approval ≤ Block` kuralının kararı değildir; dayanak doğrulanmadan aile açılmaz. |
| K4 | Sınır karşılaştırması `Exceeds`/`AtOrAbove` varsayılmaz; onaylı sürüm/şema yoksa `Unavailable`. |
| K5 | Kantar iş tarihi `OccurredAtUtc`'den türetilmez; tüketici owner'ının tarih sözleşmesi gerekir. |
| K6 | Risk azaltımı şirket kapsamına daraltılmaz; tür/banka/vade ve çakışma önceliği kararı bekler. |
| K7 | Risk yüzdelerine 100 veya başka bir üst sınır atanmaz; Finance anlam kontrolü ve mevcut STOP korunur. |
| K8 | `NetOfVat`/`WithVat` maruziyet bazı seçilmez; onaylı Finance sözleşmesi olmadan `Unavailable`. |
| K9 | Yeni saklama revizyonu eski kayıtlara topluca uygulanmaz; kayıt/değerlendirme tarihi seçimi açık kalır, imha uygunluğu üretilmez. |
| K10 | AI geri bildirim saklaması genel saklama ailesiyle birleştirilmez; farklı tarih anlamı ve AI A-contract-only sınırı korunur. |
| K11 | Saklama başlangıcı olarak takvim yıl sonu/31 Aralık seçilmez; OPEN-018/021 açık kalır. |
| K12 | Yasal alt sınır veya onu zorlayan onay kancası etkinleştirilmez; süre matrisi ve insan sorusu korunur. |
| K13 | Tahsis “yaklaşıyor” eşiği veya `warningDays ≥ 1` seçilmez; ilgili gösterim `Unavailable` kalır, mevcut tahsis kuralı değişmez. |
| K14 | Manuel kur istisnasına şirket aç/kapa anahtarı eklenmez veya gereksiz diye kaldırılmaz; mevcut gerekçe/yetki/audit sınırı korunur. |
| K15 | Manufacturing'in üç adımlı yaşam döngüsü bütün ailelere genellenmez; aile onay/yürürlük dayanağı yoksa yayın açılmaz. |
| K16 | Aile başına/genel izin adı, rol veya grant seçilmez; katalog kaydı, assignment ve runtime aktivasyonu ayrı kapılardır. |
| K17 | Tarih penceresi “onay = yürürlük” veya “en yüksek revizyon” modeline varsayımla giydirilmez; aile başına tarih kararı gerekir. |
| K18 | Mal kabul kantar etkisi, sayım takvimi, saha veri toplama ve üçlü eşleme politikaları kendi açık anlamlarını korur; saklama dönüşümü bunları kapatmaz. |
| K19 | Gerekli politika yoksa fail-closed davranış DEC-SET-001 ile kilitlidir; kod varsayılanı eklenmez. |
| K20 | Kullanılan sürüm ve gerçek değer işlem snapshot'ında korunur; politika güncellemesi geçmiş anlamı değiştirmez. |

`OPEN-004`, `OPEN-010`, `OPEN-012`, `OPEN-018`, `OPEN-021` ve ilgili mevcut insan
soruları [açık karar sicilindeki](../decisions/ACIK_KARARLAR.md) statülerini korur.
Bu ADR rapor içi `ASSUMPTION-*`, `APPROVAL-01` veya soru numarasını kabul edilmiş
otoriteye dönüştürmez; merkezi sayaç ve kabul durumlarını değiştirmez.

## #759 sonrasında dar entegrasyon sırası

Bu sıra ilerideki kaynak dilimlerine teknik öneridir; START-GO veya merge yetkisi
değildir. #759'un güncel exact head, kabul ve owner entegrasyon kapıları önce doğrulanır.
Her sonraki diff gerçek kabul edilmiş tabana yeniden çapalanır; rapor satır numarası
uygulanabilir hunk sayılmaz.

| Adım | Tasarım farkı ve korunacak kapı |
|---|---|
| 0 | #759 lifecycle yüzeyini ve mevcut provider sınırını koru; `SettingsDbContext`'i rapordaki tam metinle değiştirme. |
| 1 | EK-38 için tek registry version kaydı ve mapping'ini dar fark olarak tasarla; entity başına yazma yetkisini ayrıca sınırla. |
| 2 | Gereken Settings outbox eklemesini kendi sözleşmesiyle sırala; aile sınıflarını topluca kaydetme. |
| 3 | EK-36 tipli master/registry proposal workflow eklemelerini kendi adlandırılmış save/guard yoluyla hazırla. Partial gerekiyorsa bildirimi dar değiştir; Manufacturing mapping, audit/idempotency sahipliği ve `IsResolved` filtrelerini koru. |
| 4 | İş takvimi/vade/kur gibi tipli master partial'larını aynı gerçek tabana ekle; politika adaylarını ikinci bağımsız typed policy deposu olarak çoğaltma. |
| 5 | EK-08 ve diğer tüketici adaptörleri yalnız kendi onaylı aile şemasıyla gelsin. Açık aileler kayıt edilmez; mevcut sağlayıcıları topluca değiştirme. |
| 6 | EK-43 dönem/legal-hold ve imha talebi aggregate tasarımlarını kendi kayıtları olarak tut; saklama süresi/alt sınır/AI saklaması/saha politikası dönüşümlerini K9–K18 kapılarından ayrı geçir. |

Her uygulama dilimi kendi exact allowlist'ini, migration/geri alma veya forward-fix
planını ve davranış testlerini getirir. Model snapshot'ı, migration envanteri ve
merkezi reviewed-source/fingerprint dosyaları gerçek üretici komutlarıyla, kendi
tek yazarlarında güncellenir; hash elle pinlenmez. Bu belgede o dosyalar değiştirilmez.

## Sonuç, doğrulama ve geri alma

Tek registry, aynı politika için paralel değer deposu oluşmasını önler; tipli master
ve aggregate ayrımı korunur. Aileleri topluca taşımak yerine sahip sözleşmeleriyle
sıraya almak, açık anlamlar için `Unavailable` durumunu görünür bırakır.

Bu değişiklik yalnız bu ADR dosyasıdır. Uygulama kodu, migration, route, grant,
gerçek değer, çalışma zamanı, Finance posting/persistence, AI fazı veya production
değişikliği içermez. Finance STOP ve AI A-contract-only sürer.

Bu kaynak tesliminde belge referansları/hash'leri, tek dosyalık diff ve whitespace
kontrolü doğrulanır. Build/restore, unit/integration, SQL, browser, scanner ve CI
çalıştırması `NOT_RUN`; iki bağımsız exact-head K3 review ve insan review açıktır.
ADR kabulü runtime/SQL kabulü değildir. Sonraki uygulama diliminde kendi slotunda
şirket izolasyonu, yetki/replay, atomik audit/idempotency, sürüm yarışı, exact-scope
bulunamama/çakışma ve yayın–provider ayrımı kanıtlanır; ilişkisel kanıt gerçek SQL
Server'da skip0 olmalıdır. Zorunlu CI #765/LOCAL_FULL kabulüne bağlı kalır.

Belgeyi geri almak çalışan veri veya şema değiştirmez. Sonraki uygulamanın geri
alma planı kendi diliminde incelenir; bu ADR geçmiş snapshot'ları, audit'i veya
idempotency kanıtını silme yetkisi vermez. Merge mevcut koordinatörün expected-head
kapısındadır; bu taslak nihai kabul veya PROGRAM-128 sayacında artış değildir.
