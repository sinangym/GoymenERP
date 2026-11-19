# W8 runtime kapanış ara durumu — 18 Kasım

Sabit kapsam **174/174 kaynak DONE**, W8 son pakettir. Yeni dalga veya dilim açılmaz.
Bu belge bir ara koşum kaydıdır; final exact-head kabulü, SQL, tarayıcı, CI veya K3
onayı değildir. Yayımlama öncesi tarihsel tren checkpoint'i 135/174 idi; aşağıdaki önceki
koşumlar tarihçedir. Final sonuç ve yayın makbuzu ayrı kaydedilir.

| Kanıt | Gerçek sonuç / sınır |
|---|---|
| İlk tam 18 UnitTests koşumu, `a8f4440ec` | 13.168 test: 13.091 PASS, 77 FAIL, 0 skip; 18 build exit0. İlk proje dağılımı 5 PASS / 13 FAIL. |
| Son modül tekrarları | 18 projenin tamamı kendi son koşusunda yeşil. Bunlar farklı kaynak checkpoint'leridir; final birleşik koşum değildir. |
| Purchasing, `1b55f5e10` | 884/884 PASS, 0 skip, build0/test0. İlk iki assertion hatası ve native kanıtları korundu. |
| Manufacturing, `203d0536b` | 1.424/1.424 PASS, 0 skip. |
| PDF, `e83c0cf62` | İzole kaynakta build0 ve 82/82 PASS, skip0; aynı kaynak birleşti. Eski 2/4 failure kanıtları korundu. Font ve iki kendi-catalog XMP UUID farkı giderildi; exact-byte, font, uzunluk/xref/trailer kontrolleri korundu. Final birleşik tekrar ayrıca gerekir. |
| RCSI CLI araç testleri, `edab5c3c9` | Build0 ve 72/72 PASS, skip0. İlk fixture ve sonraki xUnit2009 hataları silinmedi. Aynı path-bound kontrolleri `Assert.StartsWith` kullanır; gerçek junction ve pre-secret ret kontrolü çalıştı. Gerçek SQL/RCSI kabulü değildir. |
| Inventory takipli maliyet izolasyonu, `031728b6f` | Integration projesinde 4/4 PASS, 0 skip; mevcut InMemory sağlayıcısı, gerçek SQL değildir. |
| Mimari bypass guard grubu, `031728b6f` | 338/340 PASS, 2 FAIL. Recovery dalındaki iki gerçek Purchasing kaynak düzeltmesi henüz birleşmediği için iki fingerprint farklı; ayrıca Product price-list tasarım factory girdisi `203d0536b` ile gerekçeli eklendi. Final tekrar QUEUED. |
| Frontend | İlk tam `203d0536b`: TSC0; 614 dosya, 550 PASS / 64 FAIL; 9.813 test, 9.571 PASS / 242 FAIL, exit1, 2061.96 saniye. Hatalı/değişmiş 70 dosyanın `edab5c3c9` tekrarı: TSC0, 1.181/1.185 test PASS, 4 FAIL; 66/70 dosya PASS, exit1, 391.37 saniye. Tam JSON/loglar korundu; final tam koşum değildir. |
| Recovery | Eski sabit `c8d7d7c3e` gerçek üretici0 ve 36/36 Python PASS. Birleşik ölçüm99 ve yedi typed-constructor metadata hizalaması sonrası temiz `3123ef92f` gerçek üretici exit0; recovery ve release tam paketleri ayrı donuk çalışma alanında sırayla çalışıyor. Final kabul değildir. |
| Hafif doğrulayıcılar | Backlog ve bootstrap exit0; backlog 64 test/skip0, bootstrap 56 test/9 mevcut koşullu skip, backend grup 28 test/skip0, OpenAPI drift 16 test/2 mevcut koşullu skip. |
| İlişkisel envanter Python paketi | İlk koşumda Work'un dört yeni gerçek bildirim fact'ini eski exact beklenti saymadığı için 1 FAIL. Kaynak/envanter doğrulanan `586176b32` beklenti düzeltmesi birleşti; kök tekrar 48/48 PASS, skip0, exit0. Gate gevşetilmedi. |
| Gerçek SQL, tarayıcı, final CI, iki bağımsız K3 | Henüz tamamlanmadı. Yerel SQL yapılandırılmamıştır; yeni DB/container/service oluşturulmadı. |

İlk başarısız native log/exit/TRX dosyaları korundu; yeni denemeler ayrı dosyalara
yazılır. Test atlama, silme, karantina veya SQL yokluğunu başarı sayma uygulanmaz.
Sıradaki sıra: mevcut hataları düzeltme ve tekrar → final üreticiler/pinler → birleşik
kapanış kanıtı → normal tren fast-forward → tek taslak K3 PR. Yazar merge etmez.

## Son kapanış checkpoint'i

- Frontend `c6168c8e2`: lint0, üretim build0 (TSC + Vite), **614/614 dosya ve
  9.854/9.854 test PASS, 0 FAIL, 0 skip**, native Vitest exit0; 1942.652 saniye.
  İlk 242 FAIL ve sonraki hedefli tekrarların native kanıtları korundu. Bu tam
  koşum sonradan yapılan yalnız docs/pin commitleriyle değişmeyen frontend kaynağına
  aittir. Lint/build uyarıları loglarda kalır; tarayıcı kabulü değildir.
- Recovery/release donuk `3123ef92f`: gerçek üretici0, recovery36/36 ve release16/16
  PASS, skip0. Son birleşik backend `321201b2b` için gerçek üretici ayrıca exit0:
  19 context,339 migration,66 durable store;3116 kaynak ve1459 risky kaynak.
  Risk fingerprint `e64d43adbbd335ce7c60db5f39fb1359b837d0f4c5e1c8237e7f619fc759cd19`.
  Tam eski paket sonucu ile son kaynak üretimi ayrı kanıttır.
- `b5465825a` hafif tekrarları: iki validator0, delivery64/skip0,
  bootstrap56/Windows koşullu9skip, backend grup28/skip0,
  ilişkisel verifier48/skip0, OpenAPI drift16/koşullu2skip; hepsi native exit0.
- İlk tam mimari `380901177`:398/403 PASS,5 FAIL,skip0. Dört kaynak nedeni ve
  `c6168c8e2` düzeltmesi [mimari raporda](W8_ARCHITECTURE_KAPANIS.md); birleşik
  tekrar henüz QUEUED. Son 20 modül grubunun birleşik tekrarları da QUEUED.
- Toplu base diff kontrolü eski W6 raporunda bir fazla EOF boş satırı buldu;
  yalnız bu boş satır düzeltildi. Uygulama veya karar metni değişmedi.

Kaynak174/174 korunur. Tren henüz ilerletilmedi; SQL, son CI, tarayıcı ve bağımsız
K3 kapıları bu checkpoint ile kapanmaz.

## Tam modül protokolü tekrarları

- `914bf9938`:20 Release prebuild exit0;13 projenin8.020 testi PASS/skip0.
  Organization'ın kültüre bağlı dokuz deferred kimliği yüzünden verifier exit1;
  [dokuz native scalar kimlik düzeltmesi](W8_ARCHITECTURE_KAPANIS.md) bunu ele alır.
- `33fee7571`:20 prebuild exit0; Organization dahil16 proje koşuldu.12.192 PASS,
  1 FAIL,skip0; verifier exit1. Hata Satış SAT-D406 sayfalama fixture'ındaydı:
  önceki onay okumalarının `Answered` kayıtları dururken yalnız `Batches` temizlenmiş.
  Rastgele sipariş kimliğine göre eski yanıtla yanlış karşılaştırma oluşabiliyordu.
- Aynı fixture artık kontrollü sayfalama okumasından önce iki listeyi birlikte
  sıfırlar; iki istek ve iki yanıt sayısını, gerçek ikinci sayfanın ilk yanıtta
  bulunmamasını aynen doğrular. Üretim kodu/iş kuralı değişmedi. Gerçek Release
  build0 ve hedef1/1 PASS/skip0; tam20 proje tekrar alınacaktır.

İki ilk native TRX/verifier çıkışı korunur. Eksik kalan proje çalışmaları PASS
sayılmaz; sonraki tam grup mevcut verifier ile yeni/boş kanıt dizininde başlar.

## Son yerel kapanış sonucu

Sabit kapsam **174/174 DONE (%100 kaynak)**; W8 son pakettir, yeni dalga yoktur.

| Doğrulama | Kaynak ve gerçek sonuç |
|---|---|
| Tam modül grubu | `e41360340dc3be1bca13a66c48ade37aaa537bda`: mevcut grup doğrulayıcısı, 20 proje, **13.325/13.325 PASS, 0 FAIL, 0 skip**, exit0. 18 UnitTests toplamı 13.171; PDF 82 ve RCSI CLI 72 bu toplamın içindedir. |
| Tam mimari grup | `25b7477b0e93de39ac8d5de08f508a69fb2eb996`: **405/405 PASS, 0 FAIL, 0 skip**, exit0. |
| Tam frontend | `c6168c8e23afa323af003dc79fdab3c9fbc00872`: **614 dosya, 9.854/9.854 PASS, 0 FAIL, 0 skip**; lint/build exit0. Sonraki değişiklikler frontend kaynağını değiştirmedi. |
| OpenAPI / Integration | `25b7477b0e93de39ac8d5de08f508a69fb2eb996`: Release build exit0, 0 hata/uyarı; gerçek üretim ve byte drift exit0; **10/10 PASS, 0 skip**. 9.746.060 byte, SHA256 `432c013a9dd5b992d91536c89aca1ab8805563524dc1b9cb5238a84bbb71056a`. |
| Recovery / release | Donuk `3123ef92f`: 36/36 ve 16/16 PASS, skip0. Son gerçek üretici `321201b2b` exit0; 19 context, 339 migration, 66 durable store. Sonraki değişiklikler backend/build-control girdisini değiştirmedi; bu ayrım final CI exact-head kanıtının yerine geçmez. |
| Hafif kontroller | Backlog 64/64; bootstrap 56 testte mevcut 9 Windows koşullu skip; backend-group 28/28; RCSI envanter 48/48; OpenAPI drift 16 testte mevcut 2 koşullu skip. Native exit0; skip sayıları gizlenmez. |
| Bekleyen kabul | Yerel gerçek SQL NOT_CONFIGURED; tam API/gerçek SQL mevcut final CI'da koşacak. Tarayıcı/E2E, iki bağımsız exact-head K3 review ve genel analiz ayrı kapılardır. |

İlk başarısız koşumların log/exit/TRX kayıtları ve önceki tarihsel bölümler korunur.
`artifacts/local-final-e41360340/proofs/modules.json` ve
`artifacts/local-final-25b7477b0/proofs/architecture.json` yerel native grup kanıtlarıdır;
bunlar GitHub CI artifact'ı olarak sunulmaz. Grant/rol, production, OPEN veya Finance STOP değişmedi.

## İlk final CI kapısı ve dar ön kontrol düzeltmesi

Yayın `e838ca9079eda6c52d0d31cd909e8541f121f976`, tek taslak K3 PR #1201.
CI `44237822849` ilk ön kontrolde üç açıklama/sanal fixture yolunu gerçek
okunan prose girdisi sandı. İki sanal JSON/YAML dosya adı `docs/fixtures/`
altına taşındı (test gerçek dosya okumaz, aynı Markdown dışlama ve credential
reddini sınar). Backup verifier insan runbook atfı Python yorumuna taşındı;
çalıştırılabilir bütün AST düğümleri birebir aynı. Filtre/scanner/workflow
ve test assertları değiştirilmedi. Backup test yükleyicisi, dataclass için
modülü exec öncesinde standart `sys.modules` kaydına alır.

Yerel credential guard 24/24 PASS, skip0; backup verifier 50 testte 49 PASS ve
mevcut sembolik bağlantı erişimi koşullu 1 skip. CI safe-summary paketi 64 testte
63 PASS, 1 FAIL: Windows Bash ortamında Faz0 geçici git fixture komut sonucu;
bu kalan hata atlanmadı. Linux ilk CI koşumunda Faz0 testi zaten PASS idi;
CI'nin kaynak prose testi düzeltme sonrası yeniden doğrulanmalıdır.

Ayrı güvenlik engeli: `GHSA-fc5q-39xc-vjpf`, Picomatch <=4.0.7 yüksek önem.
18 Kasım canlı npm registry latest4.0.7; advisory patched=None. Upstream
#267/#259 birleştirilmemiştir. Vite/Vitest zorla düşürülmedi, override veya
audit-ignore eklenmedi, audit kapısı gevşetilmedi. Güvenli upstream düzeltmesi
olmadan bu CI güvenlik kapısı PASS sayılamaz.
Kaynak: https://github.com/advisories/GHSA-fc5q-39xc-vjpf ve
https://registry.npmjs.org/picomatch . Kaynak %100 bu engeli kapatmaz.

### Windows Faz0 tekrar sonucu

Geçici Git fixture'ında çıplak `bash` Windows System32 WSL aracını seçiyordu;
WSL RPC hata koduyla çıkıyordu. Yalnız Windows test harness'i kurulu Git'in
`bin/bash.exe` dosyasını açıkça seçer; Linux çağrısı, shell scripti ve bütün
assertlar aynıdır. Tam `ci_safe_trx_summary` **64/64 PASS, 0 skip**, native exit0,
48.895 saniye. Önceki başarısız kayıtlar korunur. Ayrı salt okuma tanısında
162 shell çağrısı özgün prose/kod/geçersiz-head sonuçlarıyla doğrulandı.

### İlk API ve gerçek SQL denemelerinden kapanış girdisi

`94e0e88e98ca3374cb3149d943a9d4e9db5d6bd7`, CI `44238350645`:
backend build/vulnerability/migration üretimi, modül grubu, mimari grup ve dört
frontend shard PASS. API-1 PASS; API-2 ve API-3 mevcut fixture hizalama hatalarıyla
FAIL. Cari C006 378 testte 377 PASS/1 FAIL: gerçek frontend üç alan gönderirken
fixture yeni opsiyonel false alanını da serialleştiriyordu. Düzeltme yalnız açık
üç alanlı fixture projection'dır. Gelecek tedarik HTTP fixture'ına eksik mevcut
fresh authority double kaydı eklendi; production permission/seed genişletilmedi.

Accounting lineage testinin bağımsız native tekrarı, ikinci gerçek CompanyContext.Set
çağrısının immutable context tarafından InvalidOperationException ile reddedildiğini
kanıtladı; beklenen AccountingStoreException üretilen kaynağa ulaşmıyordu. Bu yalnız
hata enjeksiyon fixture'ında mutable ICompanyContext ile düzeltilir; gerçek
CompanyContext değişmez. Aynı kesin hata/yanlış olumlu durumlar için mevcut
Accounting/Counterparty/Manufacturing test fixture'ları dar biçimde hizalandı;
gerçek scoped DbContext şirket filtresi ve bütün state/rollback/Denied kontrolleri
korundu. Bu yeni kaynakların native tekrarı henüz tamamlanmamıştır.

İlk SQL shard A: 638 FAIL/0 PASS; B: 623 FAIL/4 PASS; C: 486 FAIL/11 PASS;
üçünde skip0. RCSI 14 test: 9 PASS/5 FAIL/skip0. Bunlar başarı sayılmaz.
RCSI ve C01 başlangıç stack'inde Sales iptal trigger'ı migration aşamasında
SQL468 collation hatası görüldü: ActorId BIN2, CancelledBy DB-default.
İki operandı aynı açık binary collation ile karşılaştıran bootstrap repair ve
uygulanmış geliştirme DB'leri için forward migration hazırlanır. Yeni politika
seçilmez; CompanyId/sürüm/tarih ve exact actor kimliği şartları korunur.

## İlk CI düzeltmelerinin yerel kapanışı

Kaynak `54bec41070da4f8c2e42c5e40c64d847094df8e4`; sabit 174/174 kaynak DONE (%100), W8 son paket. Önceki başarısız koşumlar korunur.

- Birleşik Integration Release build `edf25498d`: 0 hata/0 uyarı, exit0. OpenAPI, Accounting lineage, future-supply HTTP, Counterparty ve Manufacturing değişen fixture hedefleri **69/69 PASS, 0 FAIL, 0 skip**, native exit0. Gerçek scoped DbContext şirket filtresi korunur; mutable double yalnız drift enjeksiyonundadır.
- Tam Cari C006 Windows native koşumu `54bec4107`: restore0; **378/378 PASS, 0 FAIL, 0 skip**, test exit0. Bu test paketi gerçek tarayıcı kabulünün yerine geçmez.
- Sales repair persistence agent koşumu: **128/128 PASS, 0 FAIL, 0 skip**, Release build0. Gerçek SQL yerelde NOT_CONFIGURED/NOT_RUN; yeni CI sonucu gereklidir.
- OpenAPI 11/11 PASS bu 69 hedef içindedir. Gerçek belge 9.746.060 byte, 826 operasyon/1721 schema; SHA256 `091cc64a9e71f90ddc12b7c2ab9784b3f22fd8e181234f7f7b50603dc30dec04`. Schema serialization V3/V31 için ordinal ve kültür bağımsızdır; JSON iş değerleri değişmez. Önceki agent birleşik 12 koşumunda Accounting fixture hatasıyla exit1 korunur; root69 tekrarı bu fixture'ı ayrıca doğrular.
- Sales actor karşılaştırmasının iki tarafı açık BIN2 olur. EF üretimli `20261118050006_RepairSalesOfficialInvoiceCancellationActorCollation` Up/Down güvenli ALTER trigger'ı tekrar uygular; veri/model/snapshot değişimi ve yeni THROW yoktur. K3 review bekler. Toplam340 migration, main'e göre177 yeni migration; 313 toplam/199 yeni THROW ve118 yeni izin kaydı değişmedi.
- Gerçek recovery üreticisi `54bec4107`: exit0,19 context/340 migration/66 durable store; kaynak3119, riskli1461, riskli SHA `9af854faccd37fccca382a68dd8d6316d4d00824fbd438ec91b3784243ac9854`. İlk CRLF dosya-byte ve ardından index-stat nedeniyle temiz-worktree kontrolü exit2 tekrarları korunur. Çalışan üretici committed blob ile byte eşitlenip aynı içerik Git index'i yenilendikten sonra yeni boş kanıt dizininde gerçek exit0 alındı; filtre/üretici kapısı gevşetilmedi.
- Güncel hafif kontroller: iki validator0; backlog64/64; bootstrap56 test/9 mevcut Windows koşullu skip; backend-group28/28; RCSI48/48; OpenAPI-drift16 test/2 mevcut koşullu skip; hepsi native0. Windows Faz0 safe-summary64/64,skip0. Tam recovery36/release16 önceki donuk kaynağa aittir; güncel340 exact-source CI kanıtı ayrıca beklenir.
- Yayındaki `94e0e88e` CI `44238350645` artık tamamlandı: recovery exact-source ve release manifest PASS; modül/mimari/API-1/dört frontend shard PASS. Diğer API/SQL/OpenAPI ve güvenlik hataları önceki bölümdeki gibi FAIL'dir. Yerel düzeltmeler henüz bu CI head'inde yoktur; yeni normal FF yayını yeni koşumu başlatacaktır.

İki bağımsız tam exact-head K3 incelemesi için tek istem: [K3 bağımsız inceleme istemi](../K3_BAGIMSIZ_INCELEME_ISTEMI.md). Review/CI/SQL/browser açıkken genel kabul veya production tamamlandı denmez. Upstream Picomatch güvenlik engeli korunur; audit kapısı gevşetilmez.

### Güncel tam mimari ve SQL fixture kapanışı

- `c07255c4970ea3cbdae8f0a2f8b334b0b57f431f`: tam mimari405 testte404 PASS/1 FAIL/skip0, native1. Build-security negatif testinde CRLF raw-string nedeniyle paket silme mutantı unchanged kalıyordu. LF normalizasyonu ve her mutantın canonical'dan farklı olduğunu açıkça doğrulayan ek assert sorunu çözer; guard gevşetilmez. İlk TRX korunur.
- `fce8792f515bf8263e753dc2fb6b20b96d057288`: mevcut tam grup doğrulayıcısı **405/405 PASS, 0 FAIL, 0 skip**, native0; proof `artifacts/local-final-fce8792f5/proofs/architecture.json`.
- `e994d1df21207db654ee16a8a37395e0bfd1d032`: Organization SQL fixture eski Version13 yerine KIM-D48 staging gereği12 bekler; onay gelmeden gerçek şirket değeri/sürümü ilerlemez. İki eski history count4 yerine11 tam migration kimliği ve sıra doğrulanır. Counterparty Down testi ilk SQL'den önce mevcut client ile lazy host'u başlatır; bütün Down/rollback/constraint assertları korunur.
- `47429c2a576bfa3d1c2c07291083ca5244cbc680`: iki mevcut SQL Run.StartAsync, initializer hata verdiğinde caller'a geçemeyen disposable factory'yi temizler; asıl exception ve stack yeniden fırlatılır. Önceki üç SQL shard'ında1377 doğrudan inotify IOException görüldü; dar cleanup'ın tamamını çözdüğü henüz kanıtlanmadı. Bu SQL fixture değişiklikleri yerelde SQL PASS sayılmaz; gerçek CI tekrarı gereklidir.

Birleşik kaynak `e35cd8cb4ecf502b0cb77cf81a334dd92077adff`; yeni kaynak/migration/işletme politikası eklenmez, yalnız sabit W8 kabul hataları kapatılır. Eski başarısız kanıtlar korunur.

Son birleşik SQL-fixture kaynağı `e35cd8cb4ecf502b0cb77cf81a334dd92077adff`: Integration Release build **exit0, 0 hata/0 uyarı**,365.88 saniye. Gerçek OpenAPI yeniden üretimi aynı `091cc64a9e71f90ddc12b7c2ab9784b3f22fd8e181234f7f7b50603dc30dec04` byte SHA değerini verdi. Bu derleme gerçek SQL koşumu değildir.

## Son sınırlı doğrulama kapanışı — 18 Kasım 2026

**174/174 kaynak DONE (%100); W8 son paket, yeni dalga yok.** Bu ek kaynak/test düzeltmesi ve gerçek koşum kaydıdır; final CI veya bağımsız K3 kabulü değildir. Önceki başarısız kanıtlar korunur.

- Üretim kaynak checkpoint'i `581d01e7807bdb4dcbfcf7b9a5965ddb664b3924`: tam Release Integration derlemesi exit0. Gerçek sağlayıcı testiyle üretilen azaltılmış Shell authority değeri `sha256:pTSIaODoh7NURxieNmmuzSy5Zal6xRKy0dgPcML2PvE`; Shell 10/10 PASS, 0 skip.
- Aynı kaynakta 76 API metodu/138 SDK vakası: 137 PASS, 1 FAIL, exit1. Tek kalan AuthenticationProviders login fixture hatası silinmedi. InMemory fixture gerçek SQL ExecuteUpdate mağazası yerine mevcut tracked credential fixture'ını kullanacak şekilde düzeltildi; üretim giriş/SQL politikası değişmedi.
- `6f9ca88db4cb71e46d098ee44cae2972ef840f49`: AuthenticationProviders sınıfının 10 metot/15 SDK vakası **15/15 PASS, 0 skip**; değişen runtime korumaları **3/3 PASS, 0 skip**. Fresh SDK discovery ve özgün exact TRX doğrulayıcısı PASS; fixture derlemeleri exit0. Bu head'de tam backend tekrar derlenmedi; üretim ağacı 581d01e78 ile aynıdır. Bu kanıt 138 API vakasının veya bütün API gruplarının yeni head'de tam yeşil olduğu iddiası değildir.
- `581d01e78`: InventoryFutureAllocationContractTests 15 metot/45 vaka **45/45 PASS, 0 skip**, kanonik sürüm negatif vektörleri dahil. RCSI/discovery mimari koruması **1/1 PASS, 0 skip**. `03` gibi kanonik olmayan sürüm kilit/owner çağrısından önce reddedilir; mevcut sözleşme ve bypass yüzeyi genişlemez.
- Aynı gerçek API derlemesinin OpenAPI drift kontrolü exit0: 826 operasyon, 1721 şema, 9.746.060 byte, SHA256 `091cc64a9e71f90ddc12b7c2ab9784b3f22fd8e181234f7f7b50603dc30dec04`.
- `6f9ca88db`: strict recovery ve release manifest gerçek üreticileri exit0; SOURCE_ONLY. 19 context, 340 migration, 66 durable store, 3119 kaynak/1461 riskli kaynak. Riskli SHA256 `f83a4a44766f40bb6666735273a586720fb90fc9762b9dfb790e6d25873ba93d`; backend kaynak SHA256 `3e481305b77de7a12cdd6315b30fc91c2e5404ca63c7406adf171db395d9206e`. Recovery36/36 ve release16/16 tam paket PASS/skip0 kanıtı önceki exact `8eb2668d0` içindir.
- SQL verifier canlı ilerleme çıktısı: 68/68 Python PASS, 0 skip; aynı 2400 saniye son tarih ve native hata/timeout/skip0/discovery/TRX kapıları korunur. Windows API filter sınırı: 38/38 Python PASS, 0 skip; yalnız fazla uzun argv için bütün SDK kimliklerini eşitleyen sınıf filtresi; test atlama yok. Workflow değişmedi.
- Önceki yayımlı `8eb2668d0` CI `44244586757` tamamlandı, overall FAIL; test edilen merge `44a2749f8fb28061e24cefdb3a6e7e6e8ca69763`. Build/modül/mimari/API1, dört frontend shard, OpenAPI/recovery/release PASS; gerçek RCSI14/14 PASS, 0 skip. API2/3 fixture hataları yerelde düzeltildi; tam yeni CI bekler. Geniş SQL A/B/C ayrı ayrı 2400 saniye timeout: tamamlanan test sayısı UNKNOWN, başarılı sayılmaz. C014 tarayıcı10/10 PASS; network evidence FAIL ve activation SKIPPED; dar allowlist/evidence düzeltmesi yeni CI bekler.
- Picomatch `GHSA-fc5q-39xc-vjpf` için resmi advisory'de düzeltilmiş sürüm bulunmaması ve registry latest4.0.7 doğrulandı. Güvenlik kapısı bypass edilmez; audit kırmızısı ve final CI/SQL/browser/K3 kapıları açık kalır. Kaynak %100, kabul/production %100 değildir.
- Son plan üstü sevk kararı önceki kaynakta uygulanmıştır: plan100 korunur, ek20 ayrı satış müdürü istisnasıdır; gerçek yüklenenden fazlası sevk edilmez. Gerçek grant/rol verilmedi. Yeni karar sorusu yok; mevcut OPEN ve insan kapıları kapanmadı.

Son kayıt üzerinde iki hafif validator exit0; backlog64/64 PASS/skip0; bootstrap56 test/9 mevcut Windows koşullu skip; OpenAPI drift16 test/2 mevcut koşullu skip; paket native exitleri0 ve git diff --check0. Koşullu Python skipleri gerçek SQL/test kabulünde skip0 yerine sayılmaz.

## K3 REQUEST-CHANGES sonrası sonlu düzeltme — 18 Kasım

**174/174 kaynak DONE; W8 son pakettir. Runtime kabulü tamamlanmadı.** İki COMMENT incelemesi `4d2a620972d1cdb55a317c76531d80cdc12bfa9a` için REQUEST-CHANGES: [A](https://github.com/sinangym/GoymenERP/pull/1201#pullrequestreview-5953258185), [B](https://github.com/sinangym/GoymenERP/pull/1201#pullrequestreview-5953265437). Bunlar kırmızı-CI üzerinde kısmi ön incelemelerdir; tam K3 onayı veya bütün migration/izin/muhasebe/UI kapsamının okunması değildir.

- Yayımlı4d2 ana CI44267292714 **tamamlandı, FAIL**; test edilen merge `c645a6e84d116dfb111587e49ad7f8e3a0f7e417`. Recovery/release, backend build, modül, mimari, API1/API2, OpenAPI ve dört frontend test shard'ı PASS. Cari44267292685, C-01 run44267292744 ve UI katalog44267292698 SUCCESS. API2 exact proof2488 sonuç/1687 PASS/801 ayrı SQL işlerine bırakılan izinli skip; bu SQL kabulü değildir.
- API3 native test sonucu değil exact sayım uyuşmazlığıyla11/51 batch'te durdu: `Found_without_actual_bound_active_customer_evidence_is_unavailable` serileştirilemeyen record MemberData discovery1/koşum12 üretiyordu. Aynı12 negatif kayıt serileştirilebilir sabit vaka adlarıyla korundu; doğrulayıcı gevşetilmedi. `eb279bf6230357de77cc6d7f7564dc687b0b62aa` dar Integration fixture build0; fresh discovery12 ve bütün ilgili sınıf **24/24 PASS, skip0**, özgün exact TRX kontrolü PASS.
- Aynı ara adayda isteğe bağlı tam API3 tanısı19/55 batch'e kadar595 PASS/220 izinli SQL skip ve exact kontrollerle ilerledi. Yeni SQL test kodunun derlemesine geçmek için yalnız yazarın yerel tanı süreç ağacı20. batch sürerken sonlandırıldı; native exit1/author-stop kanıtı korunur. **Bu koşum tamamlanmış API3 veya resmi grup proof'u değildir.** Hiçbir GitHub koşumu iptal edilmedi; zorunlu tam grup yeni head CI'ında koşacaktır.
- SQL A/B/C her biri2400 saniyede timeout, final TRX/proof yok. Canlı kayıtlar A168 PASS/115 FAIL, B295 PASS/51 FAIL, C287 PASS/49 FAIL gösterir: bunlar yalnız **gözlenen olaylar**, doğrulanmış nihai toplam değildir. Sorun yalnız süre değildir; gerçek başarısızlıklar da vardır ve nedenleri henüz kesinleşmedi.
- `Concurrent_customer_returns_leave_only_one_bounded_effect` testinde şirket kilidinden sonraki iki-taraflı bariyer somut bekleme döngüsü oluşturuyordu. Bariyer Stage/kilitten önceye taşındı; bounded cancellation/finally bırakması, tek bakiye/sürüm/hareket/komut/audit/outbox ve değişikliksiz replay kontrolleri eklendi. Üretim kilidi korunur; ham sağlayıcı hataları başarı sayılmaz. `e24388892` Integration fixture derlemesi exit0/0 hata/0 uyarı. Yerel SQL NOT_CONFIGURED; gerçek kabul yeni CI bekler.
- Collation repair için önceki head'de zaten davranışsal gerçek-SQL fact'i vardı; eksik olan başarılı koşum kanıtıydı. Fact Up ve Down'ı ayrı ayrı ikişer kez uygulayıp iki yönlü actor harf farkını51223 ile reddeder, exact actor'u kabul edip rollback sonrası veri/pay/JSON korunmasını doğrular. Yeni fact kimliği, migration veya iş politikası eklenmedi. Bu test derlendi; SQL PASS iddiası yok. P3 iptal-nedeni binary karşılaştırma notu değiştirilmedi.
- TRX özeti artık JSON/konsolda yalnız observed_trx_results_only, groupCompleteness=unknown, jobAcceptance=not_evaluated der; failed0 yalnız görülen sonuçları sayar. Başarısız son TRX için kapalı SQL-provider hata kodu yansıtılır; raw mesaj/tablo/yol/bağlantı/client kimliği yayımlanmaz. Tam Python paketi **76/76 PASS, skip0**. Bu kod tek başına kök neden veya kabul kanıtı değildir.
- Broad/shard SQL çalıştırıcısı teoriyi bölmeyen8 yöntem/16 execution sınırında, sınıflar arasında deterministik round-robin grupları ve numaralı TRX kullanır. **Tek toplam2400 saniye** bütün gruplar/preflight/pipe tamamlanmasını kapsar; her batch'e yeni40 dakika verilmez. İlk native hata korunur, tamamlanan TRX tanı için kalır. Canonical nonzero UUID/duplicate alias, normal dosya, kullanılan tanımlar, exact yöntem/teori sayımı, tam union ve skip0 zorunludur; eksik/başarısız union'dan proof yazılmaz. RCSI14 tek süreç yolu ve proof şeması aynı kalır; workflow değiştirilmedi. Süreç/static ömür değişikliği K3 review bekler; bu değişiklik hız veya yeşil koşum garantisi değildir. Tam Python paketi **91/91 PASS, skip0**.
- Ev üreticisi SQL verifier fingerprint `8adf932dec0fee8f16e121d74392b863530b76c96cb375269bdf529a1aa297e9`; mevcut verifier-test fingerprint `f81256442c584997842457d45f57e383ee6af10c13dbd4a22c7e23c4bf8d4162`. `5064a06ddbb3409a24a36170af76d5e3785f0f7b` mimari fixture build0, fresh SDK/exact TRX ile ilgili koruma **1/1 PASS, skip0**. Tam backend yeniden derlenmedi; üretim kaynak ağacı581d01e78 ile aynıdır.
- Picomatch advisory GHSA-fc5q-39xc-vjpf düzeltilmiş sürümNone; güvenlik kapısı açık. Audit-ignore/force downgrade uygulanmadı. Yeni exact-head CI, gerçek geniş SQL ve iki bağımsız **tam** K3 review gereklidir; yeni karar sorusu veya yeni dalga yok, production/grant/OPEN/Finance STOP değişmedi.

Son hafif kontroller: backlog64/64 ve backend-group38/38 PASS/skip0; iki validator0. Bootstrap ilk koşumunda iki kesin şablon sayısı testi2 beklerken yeni üçüncü komut nedeniyle başarısızdı; keşif/RCSI/batch komutlarının üçünde de noAutoResponse ve no-restore zorunlu kalacak şekilde tam beklenti3 yapıldı. Tekrar56 test/native0,9 mevcut Windows koşullu skip. Önceki başarısız log korunur; diff-check0. Workflow, kilitli restore girdisi veya kalite kapısı gevşetilmedi.


## Yeni SQL tanısı ve sınırlı düzeltme — 18 Kasım

**174/174 kaynak DONE (%100), W8 son paket; runtime kabulü açık.**
Yayımlı kaynak `9ddaa2e5cb7d249e2a7771c1912cadcd5befda48`,
[CI 44276883719](https://github.com/sinangym/GoymenERP/actions/runs/44276883719),
test edilen birleşik ağaç `5fabb3397d4cc10f5dcf893d47b71f3579c80b96`.
Önceki başarısız koşumlar ve iki kısmi REQUEST-CHANGES incelemesi korunur.

- Derleme, modül, mimari, API1, OpenAPI, RCSI, release manifest ve dört frontend shard PASS.
  C01 gerçek tarayıcı, Cari C006 ve UI catalog ayrı koşumları PASS.
  API2, API3 ve recovery bu kayıt anında sürüyor; başarı sayılmaz.
  Picomatch audit FAIL, ardından lint/build SKIPPED; güvenlik kapısı gevşetilmedi.
- Yeni SQL batch yolu gerçek TRX bıraktı: A ilk8 testte7 PASS/1 FAIL, B iki batch16 testte15 PASS/1 FAIL,
  C ilk8 testte7 PASS/1 FAIL; üçünde skip0. Bunlar yalnız tamamlanan32 sonucun29 PASS/3 FAIL
  kaydıdır; tam shard veya kabul proof'u değildir.
  Başarısız yöntemler:
  `ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests.Concurrent_approvals_leave_one_journal_and_one_proposal_decision`,
  `FinanceCommandApiSqlServerRelationalAcceptanceTests.A_customer_POS_collection_is_saved_reopened_and_recorded_once_over_HTTP`,
  `CounterpartyAuditEventTenantGuardSqlServerRelationalAcceptanceTests.AuditSidecar_IsTenantFilteredAndAllWritePathsFailClosed`.
- Counterparty testindeki eski7 migration beklentisi, sahibin güncel11 migration kimliğini
  gerçek SQL geçmişindeki tam kimlik listesiyle ordinal eşitleyen kontrolle değişti.
  Tenant/yazma/atomicity kontrolleri ve audit sayısı2 korunur.
  `1eeac20ba980dc9ac5e0ed6a083e5cb8ea4f3236` Integration fixture build exit0,
  0 hata/0 uyarı,118.05 saniye. Bu yerel derleme SQL PASS değildir.
- `66a97595785b9b08cd23d750b19e89023d0b4796`: güvenli özet kapalı assertionKind ve
  yalnız tek tam FQN frame'inden sayısal sourceLine yansıtır. Ham mesaj, expected/actual veya
  yerel yol yayımlanmaz. Satır untrusted TRX metaverisidir; kaynakla doğrulanmış kök neden değildir.
  Taşma/yinelenen/nested/foreign alanlar kapalıdır. Python **86/86 PASS, skip0**.
  Üretim eşzamanlı onay ve Finans POS hatalarının kesin kök nedeni hâlâ bilinmiyor;
  beklenti veya para/yetki davranışı tahminle değiştirilmedi.
- `e73727bc4d7c134b1238f08cfd890bd0ad47f8ff`: yalnız gerçek native1 ve eksiksiz,
  exact kimlik/sayım/counter doğrulaması geçen Failed TRX'ten sonra kalan SQL batch'leri
  tanı için birer kez sürer. İlk hata kalıcıdır, nihai komut başarısızdır, kabul proof'u yazılmaz.
  Altyapı/eksik/bozuk/skip/diğer native hata hemen durdurur.
  Tek toplam2400 saniye, aynı envanter ve RCSI tek süreç yolu korunur; workflow değişmedi.
  Python **99/99 PASS, skip0**. Ev üreticisi verifier fingerprint:
  `8fbae0393b3510886508bb6b0af594b572a6bbd149b2f3df6f9494f6a35d8958`.
  Aynı head'de mimari fixture build0, fresh discovery ve strict TRX ile ilgili guard **1/1 PASS, skip0**;
  tam backend yeniden derlenmedi, üretim ağacı581d01e78 ile aynıdır.
- İki hafif validator exit0; backlog64/64 PASS/skip0; bootstrap56 test/9 mevcut Windows koşullu skip,
  native0; diff-check0. Yerel SQL NOT_CONFIGURED/NOT_RUN.
  Mevcut CI işlerini otomatik iptal etmemek için yeni tren push'u işlerin bitmesini bekler.
  Yeni exact-head CI, geniş gerçek SQL/collation kanıtı ve iki bağımsız tam K3 review hâlâ gerekir.
  Yeni dalga/karar sorusu, grant/rol/production/OPEN/Finance STOP değişikliği yok.


## Normal çalıştırıcı uyarısı sonrası tanı kapanışı — 18 Kasım

Kaynak174/174 DONE (%100), W8 son paket; kabul açık. Önceki20bfbc750
[CI44285710026](https://github.com/sinangym/GoymenERP/actions/runs/44285710026)
tamamlandı: API2/API3 dahil bütün backend test grupları, build/OpenAPI/RCSI/recovery/release
ve dört frontend shard PASS; C01/Cari/UI ayrı koşumları PASS. Geniş SQL A/B/C ve frontend audit FAIL.
Test edilen merge609323a9a6192bb9d971da349be2d4af4adad128. Tam SQL proof yok.

- SQL A7 PASS/1 FAIL, B15 PASS/1 FAIL, C15 PASS/1 FAIL; yalnız tamamlanmış batch sonuçları, skip0.
  Counterparty audit migration-history fact'i bu kez PASS; C'nin sonraki hatası e-belge VKN değişimi
  fact'idir. Üretim maliyet farkı eşzamanlı onay ve Finans POS başlangıç hataları sürer.
  Bunların kesin kök nedeni henüz bilinmiyor; beklenti veya iş politikası tahminle değiştirilmedi.
- Bütün shard'lar normal RunInfo uyarısını altyapı hatası sayan tanısal devam kontrolünde durdu.
  Başarılı yerel TRX'lerde de Warning metaverisi bulundu. b4fd38b43 yalnız bu normal outcome'u
  tanısal devamda kabul eder; Error/Aborted/unknown/eksik outcome, bozuk/eksik sonuç veya skip yine durdurur.
  İlk native1 kalıcıdır ve hiçbir hata kabul proof'u üretemez; tek2400s sınırı değişmedi.
  Tam Python paketi101/101 PASS/skip0.
- 158c7dc83: kapalı exceptionKind ve aynı test sınıfının en yakın yardımcı frame satırı
  (en çok8 geçerli benzersiz frame) eklendi. Ham mesaj/yol/yöntem adı yayımlanmaz;
  sourceLine ve sourceFrameScope untrusted TRX metaverisidir, kaynakla doğrulanmış kök neden değildir.
  Exact test frame önceliği ve taşma/belirsizlik kapıları korunur. Python96/96 PASS/skip0.
- 4192f1531: Finans SQL fixture başlangıcında health/migrate/document_numbers kapalı fazları;
  başarısız başlangıçta dispose denemesi ve asıl hatayı inner exception olarak koruyan tanı eklendi.
  Integration fixture Release build0/0 hata/0 uyarı145.07s. Yerel gerçek SQL koşulmadı.
- 158c7dc83 native mimari fixture build0/fresh discovery/strict TRX: ilgili guard1/1 PASS/skip0.
  Ev üreticisi verifier fingerprint c5650132ff237c072a1f8c1c28ae33333dbb6125bceb8a232ad4d8e43afc0cb1.
  Tam backend tekrar derlenmedi; üretim kaynak ağacı581d01e78 ile aynıdır.
- Resmî Picomatch advisory patched=None, registry latest4.0.7 tekrar doğrulandı.
  Audit-ignore/downgrade/vendor override yok. Yeni migration/THROW/izin/bypass/context/dalga/işletme politikası yok.
  Yeni doğal exact-head CI, tam gerçek SQL ve iki bağımsız tam K3 review beklenir; production/grant/OPEN değişmedi.


## W8 son SQL teşhis eki — kaynak 9bd94c2187c0

- Önceki yayımlanmış kaynak `c00c4726e210c89d2362257f8832bd912574064a`, CI `44299253899`, test edilen merge `81df7e173784a72056305539a6a83fc0b20edada`: tamamlandı, FAIL. API-1/2/3, modüller, mimari, OpenAPI, RCSI, recovery/release ve dört frontend test parçası PASS; geniş SQL A/B/C ve frontend güvenlik kapısı FAIL. C-01 browser, Cari ve UI catalog ayrı koşuları PASS. Toplu kabul yok.
- Tamamlanan SQL batch kayıtları: A 7 PASS/1 FAIL, B 15 PASS/1 FAIL, C 15 PASS/1 FAIL; skip 0. Bunlar tam shard sayıları değildir. A: `argument_out_of_range`; B: `finance_command_startup_document_numbers` aşamasında `invalid_operation` sarmalayıcısı; C: `Equal`. Gerçek kaynak satırı henüz bilinmiyor; iş kuralı veya test beklentisi tahminle değiştirilmedi. Counterparty audit migration-history fact'i PASS.
- `d12a853aa`: yalnız eksiksiz, exact kimlikli Failed TRX + native exit 1 teşhis yolunda RunInfo veto edilmez. İlk hata korunur, son exit başarısızdır ve kabul proof'u yazılmaz. Başarılı kabul yolu ve yapısal/kimlik/çokluk/sayaç/skip/timeout kontrolleri değişmez; global süre sınırı korunur. Önceki RunInfo veto açıklamalarının bu teşhis yolu için yerini bu kural alır. K3 incelemesi bekler.
- `9bd94c2187c041ca5e800f1a775b88e67060ab34`: hata özeti, sınırlı ve doğrulanmış Git'te izlenen C# dosyasının depo göreli yolunu/satırını gösterebilir. Özel yol öneki, yöntem ve ham mesaj yayımlanmaz. İç exception kaynak frame'i önceliklidir; bu konum TRX metadatasıdır, kök neden veya kabul kanıtı değildir.
- Gerçek yerel kanıt: safe summary 103/103 PASS, SQL verifier 101/101 PASS, skip 0. Mimari fixture build/discovery exit 0, strict TRX guard 1/1 PASS, skip 0. Tam backend yeniden derlenmedi; production kaynak değişmedi. İki hafif doğrulayıcı exit 0; backlog 64/64 PASS, bootstrap 56 test exit 0 (mevcut Windows koşullu 9 skip, SQL skip0 kanıtı değildir). Yerel SQL NOT_RUN.
- 174/174 kaynak DONE (%100), W8 son paket. Yeni dalga/dilim, migration, izin, grant, OPEN/production/Finance STOP değişikliği yok. Tam SQL kabulü, frontend güvenlik kapısı ve iki yeni exact-head tam bağımsız K3 review hâlâ bekler.


## W8 SQL fixture ve cari ekstre düzeltmesi — kaynak 853f1ea01095

- Yayımlanmış `4ffd8cd3976df1dfa34d10fdb5173aa2918c24bc`, CI `44317154064`, tested merge `ca4fe7eec935d195d3d201c4a8978aed971d55bd`: tamamlandı, FAIL. Bütün backend/API grupları, OpenAPI/RCSI/recovery/release ve dört frontend test shard'ı PASS; C01/Cari/UI ayrı koşuları SUCCESS. Geniş SQL ve frontend audit FAIL. SQL A23 tamamlanmış TRX/119PASS65FAIL; B37/245PASS51FAIL; C29/218PASS21FAIL; toplam719 gözlenen sonuç=582PASS137FAIL, skip0. Üç shard global2400s sınırında durdu; tam proof yok. A15/B1 hata grubu ayrıntısı kırpıldı, toplam sayaçlar kırpılmadı.
- Somut ortak fixture nedeni: gerçek Audit fabrikası kapalı SourceChannel sözleşmesinde Test/UnitTest kabul etmiyor. `383b125f521dc8edc87ab8c8643a1ea96cb50ddb` dokuz fixture dosyasındaki on bu tür metadata değerini mevcut Web sabitiyle düzeltir. Audit izin listesi, negatif testler ve iş beklentileri değişmedi. Manufacturing maliyet farkı, Finance/Settings belge numaraları ve Purchasing ortak setup/ithal/fatura akışları gerçek Audit yazımına bağlıdır. Bu kaynak bulgusu bütün137 SQL hatasının kapandığı iddiası değildir.
- Organization audit guard `c248c6fae` ve Counterparty ana SQL fixture `c13e3d5b8`: eskimiş sabit migration sayısı yerine owner'ın bütün sıralı migration kimlikleri gerçek SQL geçmişiyle eşitlenir; tenant/write/constraint/seed kontrolleri korunur. Diğer Counterparty ilk vergi kimliği PUT hatalarının gerçek HTTP cevabı hâlâ bilinmiyor; onay/yetki davranışı değiştirilmedi.
- Gerçek Finance provider regresyonu `6f64099a26fb84d6bf203804fd8b9e44ed954c79`: ilk build NETSDK1004 eksik assets nedeniyle FAIL; locked restore0 ve build0 sonrasında test native1, InvalidOperationException/could-not-be-translated hatasıyla mevcut açılış sorgusunu yeniden üretti. `3a7f4caa896070424e0863988a443520f3003b3c` yalnız OpeningFact projeksiyonunu üye başlatma biçimine çevirir; sekiz alan, şirket/cari/hesap filtresi, tarih, sıra ve limit aynı, istemcide filtreleme yok. Aynı test build0/test0; ilgili tüm PartyLedger hedefleri39/39 PASS, skip0, native0. Bağlantı açılmadı; gerçek SQL kabulü değildir. Allocation hata önceliği ve reversal yarış hatası bu düzeltmeye katılmadı.
- Son kaynak `853f1ea010953e331601f6c2bd20d2ca3533056b`: Integration fixture build0/0hata/0uyarı; tam backend yeniden derlenmedi. Gerçek recovery ve release üreticileri exit0/SOURCE_ONLY. 19context/340migration/66store/3119kaynak/1461riskli kaynak değişmedi. Üretici çıktısından risk SHA `a0912a048e044efd925d4d75cb3d8d7fc650ea28534f871b2df5ec6e64651a8a`, tam backend kaynak SHA `0528dbd291847ffd91fb5c637f1957a0444d7ab8f631697dcfba420b37025f2a`. Önceki581d üretim ankrajından sonra yalnız bu Finance kaynak düzeltmesi geldi; eski kanıtın yeni head'de yeniden koştuğu iddia edilmez.
- İki hafif validator exit0, backlog64/64PASSskip0, bootstrap56test/9mevcutWindowskoşulluskip/native0, diff0. Yeni tam CI ve iki tam bağımsız exact-head K3 bekler; yerel gerçek SQL NOT_RUN. 174/174 kaynak DONE (%100), W8 son paket; yeni dalga/izin/migration/THROW/bypass/grant/OPEN/production/Finance STOP değişikliği yok.

## W8 SQL kapanış düzeltmeleri — 18 Kasım, kaynak 1dcfa3328

- Önceki yayımlanmış kaynak `b41e2ba3dc51762cc7f9d47eb7b2917d08fa1c6c`, CI `44332161172`, tested merge `b7a41e8c587e4f8aec7921a3215e22a36c66ebbb`: tamamlandı, FAIL. Backend build/modüller/mimari/API-1/2/3, OpenAPI, RCSI, recovery/release ve dört frontend test shard'ı PASS; C01/Cari/UI ayrı koşuları SUCCESS. Geniş SQL A/B/C ve frontend audit FAIL; toplu kabul yok.
- Tamamlanmış SQL batch sonuçları A240=195PASS45FAIL, B192=176PASS16FAIL, C231=213PASS18FAIL; toplam663 gözlenen sonuç=584PASS79FAIL, skip0. Bunlar tam shard kapsamı değildir; önceki koşumla kapsam değiştiği için hata azalması yüzdesi çıkarılmaz.
- `3f3df76b0`: alış iptalinde belge numarası bırakma, owner'ın açtığı Serializable işlemde yürür. Ödünç alınmış işlem sahipliği korunur; rollback/retry/gap/replay SQL senaryosu güçlendirildi. Settings işlem çiti gevşetilmedi.
- `6db22567b` gerçek RED: ödeme eşleştirme hizmetinde dört senaryodan ikisi yanlış hata önceliğiyle FAIL. `91f4b9891` saf politika kontrolünü bakiye çitlerinden önce çalıştırır; iki kullanılabilirlik çiti yazımlardan önce kalır. Son hedefli native build0/test0, 48/48 PASS, skip0 (`357e7b8a8`). Terminal komut/replay kaydı korunur; ekonomik durum değişmez. InMemory kanıtıdır, gerçek SQL değildir.
- `5cc655ec5`: muhasebe dönem SQL fixture'ı geliştirme kullanıcı metnini GUID'e çevirmek yerine mevcut kimlik eşlemesinden gerçek actor GUID alır. `0306a07f7`: temel dört owner'ın eski sabit migration sayıları yerine bütün sıralı migration kimlikleri gerçek SQL geçmişiyle karşılaştırılır. İş beklentileri/tenant/seed/constraint kontrolleri korunur.
- `b5591d56d`, EF üretimi `6fbd0252a`, ayrı Down guard düzeltmesi `357e7b8a8`, testler `cf31c1715`: Unicode revision sütununda ASCII10/11 vergi numarası için DATALENGTH=2*LEN; normal varchar denetimleri değişmez. Gerçek EF10.0.8 üretimi `20261118165020_RepairPartyIdentityRevisionTaxNumberFormat` yalnız CHECK Drop/Add yapar. Down, vergi revision geçmişi varsa önce THROW51799 ile reddeder; mevcut veri/sütun değiştirilmez. İlk üretilmiş committe eksik guard sonraki normal committe, yayımlamadan önce eklendi; tarihçe korunur. Model/snapshot/migration unitleri native34/34 PASS, skip0 (`8f7f39087`). Gerçek SQL geçerli/geçersiz Unicode ve Down geçmiş senaryoları yazıldı; yerelde NOT_RUN.
- `6f7268cf7`: gerçek recovery ve release üreticileri exit0/SOURCE_ONLY. 19context/341migration/66store/3121kaynak/1463riskli kaynak. Risk SHA `7f7ecd3b78c28a94ba49b7f4ca075ceb2e0fa6467c21bd4c760ec7576b3376bc`; tam backend SHA `b7525d134264c539cacd904141ea166bdf3f6f58df49409d1710269f831dfce9`. Yeni mutation guard yalnız Down korumasını gerekçesiyle kaydeder; bağımsız K3 incelemesi bekler.
- Kullanıcının 18 Kasım açık onayı, `1dcfa3328`: geniş SQL shard başına tek toplam10800s (180dk); her native süreç/setup/RCSI sınırı2400s kalır. Süre yenileme/retry/skip/kapsam düşürme/partial proof yok; workflow, hesap/kota, ücretli hizmet değişikliği yok. Verifier103/103 PASS, skip0; iki yeni regresyon uzun toplam bütçenin tek süreç sınırını büyütmediğini doğrular. Önceki tek2400s açıklamaları tarihsel koşumlara aittir.
- Hafif validatorlar exit0; backlog64/64PASSskip0; bootstrap56test/native0, mevcutWindowskoşullu9skip. Bu skipler SQL kabulü değildir.
- Kaynak174/174 DONE (%100), W8 son paket; yeni dalga/dilim yok. Tam yeni-head CI/gerçek SQL, frontend güvenlik kapısı ve iki bağımsız tam K3 incelemesi bekler. Yerel SQL bağlantısı/DB/container/service oluşturulmadı; grant/rol/OPEN/production/Finance STOP değişmedi.

Canlı kaynak notu (18 Kasım): [GHSA-fc5q-39xc-vjpf](https://github.com/advisories/GHSA-fc5q-39xc-vjpf) bugün Withdrawn olarak işaretlenmiştir. Önceki CI npm audit FAIL sonucu tarihsel olarak korunur; yeni npm audit koşmadan kapı PASS sayılmaz. Audit istisnası, paket veya gate değişikliği yapılmadı.

Son native doğrulama: temiz `6f7268cf7` üzerinde IntegrationTests Release ve bütün proje bağımlılıkları build exit0, 0hata/0uyarı (411.02s). Bu, yukarıdaki backend/migration/SQL-fixture değişikliklerini derler; gerçek SQL çalıştırmaz. Aynı frontend lock dosyasının yeni `npm audit --package-lock-only --audit-level=high` koşumu exit1: aynı GHSA üzerinden6high. Resmî Withdrawn kaydı npm denetimine henüz yansımamış görünüyor; güvenlik kapısı açık tutuldu, istisna/downgrade uygulanmadı.

Yayımlama öncesi mimari checkpoint: temiz6f7268cf7 fixture build0/discovery0, strict hedef koşum native1:4test/2PASS2FAIL/skip0. İki neden, eksik Finance allocation fingerprint'i ve Counterparty'nin genel taramadan dışlanmasına rağmen yeni migration'ın genel listeye eklenmesidir. `2d7d7c700` gerçek producer Finance SHA d2aaf08c47463649bd1cf41cc4c05b816c3c29ee7b5c7fb362f987c98de5bbe1 ve değişmeyen tek SqlClient race-classification sayısını kaydeder; yalnız yeni deployment migration'ını mutation doğrulamasının kaynaklarına açıkça dahil eder. Diğer Counterparty runtime dışlama/ayrı guard sınırı korunur. Bu düzeltmenin yeni-head yerel mimari tekrarı henüz NOT_RUN; fresh CI gereklidir. Recovery/release birim paketlerinin ilk denemesi kirli çalışma ağacını setup'ta reddetti (0test,exit1); temiz6f tekrarının kaynak çözümlemesi sürüyor, PASS sayılmaz. Önceki gerçek iki üretici exit0 kanıtları ayrı korunur. Taslakta normal yayımlama, bu eksik test kanıtlarını kabul veya merge yetkisine dönüştürmez.

## W8 SQL kapanis onarimi — 19 Kasim, kaynak d395b5eae55e

- Sabit kapsam174/174 kaynak DONE (%100); W8 son paket, yeni dalga/dilim yok. Kabul tamamlanmadi.
- Onceki a6bcc67553a09b909e2583f50870450574301038, CI44357816788, tested merge d83a065d8c3118f52d2fc7096a90698152373442: tamamlandi FAIL. Backend/API/moduller/mimari/OpenAPI/RCSI/recovery-release ve frontend audit/lint/build/test kapilari PASS; genis SQL A/B/C FAIL. Frontend guvenlik engeli bu eski head'in yeni gercek audit sonucunda yesildir; audit-ignore/paket/gate degisikligi yok.
- SQL'de gozlenen1762 sonuc=1536PASS226FAIL,skip0 (A638=493/145; B627=580/47; C497=463/34). A'da95 failure-group ayrintisi ozet siniri nedeniyle yoktur. Bunlar kabul proof'u degildir; farkli kapsamli eski kosumla hata yuzdesi karsilastirilmaz.
- DEC-FIN-006: odeme eslestirmesi tenant-filtered gercek original OutgoingMoneyEvent ister; Recorded status tek basina para kaydi kaniti degildir. Ilk gercek RED11testte5PASS6FAIL; owner duzeltmesi375d11b0 sonrasi hedef11/11 ve tam Finance1556/1556PASS,skip0,native0. Tahsilat saf politika hata onceligi iki bakiye citinden once, iki cit de yazimdan once korunur. InMemory/unit kanitidir, SQL degildir.
- EF10.0.8 gercek uretimi c015d872f:20261118211933_RejectNullAccessGrantApprovalModes, yalniz iki mevcut CHECK Drop/Add. Approved+NULL, SQL UNKNOWN ile ikinci kisi denetiminden gecemez. Down Approved gecmiste once THROW51798 ile reddeder. Yeni grant/rol/izin/sinif veya veri rewrite yok. Mevcut SQL fact; NULL/oz-onay retleri, bos ve onayli gecmiste exact Down ilk guard'i ve rollback testiyle genisletildi; yerel SQL NOT_RUN.
- Guncel Identity tam unit 767/767PASS,skip0; degisen mimari guardlar 4/4PASS,skip0; native build/discovery/test0 ve mevcut exact SDK/TRX verifier PASS. Integration ve butun backend proje bagimliliklari Release derleme0; ilk iki xUnit2031 hatasi ve duzeltme tekrarlarinin kayitlari korunur.
- Ilk Identity native test baslatma, Windows komut uzunlugu sinirinda test baslamadan reddedildi. Son tam proje filtresiz kosumu767 exact SDK/TRX kimligini aynen dogruladi; eski baslatma hatasi PASS sayilmadi.
- SQL fixture onarimlari: gercek Settings numaralama owner'i; guncel owner migration/table envanteri; eski Down guard'inin tarihsel konumunda sinanmasi; gercek customer-return owner baglantisi; izinli test actor/kanal; tam immutable COGS graph esitligi. Test silme, atlama, karantina veya fiziksel/finansal beklenti gevsetme yok. Yeni SQL sonucu olmadan kac kirmizinin kapandigi soylenmez.
- Gercek recovery/release ureticileri native0/SOURCE_ONLY:19context342migration66store,3123backend/1465riskli kaynak. Main farki179 yeni timestamped migration;315 tekil THROW numarasi (main migration numaralarindan201 fazla), yeni51798 tekil. Risk SHA1095737f09bc9700fa105767e62c0f9871d9498aa4e4392ce6dab487e83dcaf8; migration SHA7c94aea3b1d34e1427569c13ad3b9be8c71ea8fd957ceebd45f617668aac5f21. Guard hashleri ev ureticisinden; bagimsiz K3 review bekler.
- API build uretimi OpenAPI byte drift exit0; canonical SHA091cc64a9e71f90ddc12b7c2ab9784b3f22fd8e181234f7f7b50603dc30dec04,9746060byte. Public contract degismedi.
- Iki hafif validator0; backlog64/64PASSskip0; bootstrap56test/native0 ve mevcut9Windows kosullu skip (SQLskip0 degildir). Recovery ilk yerel denemesi calisan dosya/commit byte esitligi nedeniyle exit2; yalniz satir sonu exact commit byte'ina esitlendi, kapi degismedi. Gercek uretici tekrari ayri kanittir.
- Acik tani: iki resmi fatura/fason iptal yarisi ve iki son revizyon onayi. Guvenli ozet ve job logu gercek inner exception/HTTP sonucu vermiyor; failed TRX artifact'i yok. Kaynak daraltildi fakat neden tahmin edilmedi ve citlar gevsetilmedi. Yeni SQL kaniti gerekir.
- Siradaki kapi: normal FF push ile yeni dogal CI, genis SQL'in ayni kapsamla tamamlanmasi; sonra iki yeni bagimsiz tam exact-head K3. Taslak#1201 hazir/merge yapilmaz; mevcut yardimci ajanlar yazardir, bagimsiz reviewer degildir. Yerel SQL/DB/container/service, workflow, grant/rol/OPEN/production/Finance STOP degismedi.


## 2026-11-19 - mevcut yerel SQL ile hedefli tani

- Kullanici mevcut yerel SQL kullanimini ve yalitilmis test kurulumunu acikca onayladi. Mevcut sunucu zaten calisiyordu; servis yeniden baslatilmadi. Test dosyalari ayrilmis bir alanda, baglanti/konum yalniz test surecinde; instance varsayilanlari ve mevcut sirket DB'leri degismedi. Onceki NOT_CONFIGURED/NOT_RUN satirlari tarihseldir.
- Sabit174/174 kaynak DONE (%100); W8 son paket, yeni dalga/dilim yok. Kabul tamamlanmadi.
- Yayinli71e8dd2c CI44404510203 tamamlandi FAIL: gozlenen1762 SQL sonucu1610PASS152FAILskip0 (A638=531/107, B627=594/33, C497=485/12). A'nin ozetindeki57eksik hata adi mevcut guvenli job tani satirlarindan kurtarildi; toplam107. Bu sayilar basarili grup proof'u degildir.
- Ilk yerel SQL turu0279d4b47f7189adae5cfefcd1bcf01d07345382:11 hedef=9PASS2FAILskip0. Resmi iptal yarisi, iptal sonrasi yeni fason faturasi, belge uretimi/branding, iki customer-return Down, draft approval, invoiced quantity ve temel SQL fact PASS. Iki revizyon onayi503kaldi; her ilk kirmizi kayit korundu.
- Kaynak 9fa1588031c770a4ac40700ea90d8e7eb789725c: tam Integration+backend bagimlilik derlemesi native0; Documents tam263/263PASSskip0, depolama guard3/3PASSskip0, iki revizyon onayi2/2 gercekSQL PASSskip0. SDK discovery ve TRX kimlikleri ev dogrulayicisiyle kontrol edildi. A6+B12 tani hedefi bu kayitta RUNNING; PASS sayilmadi.
- Revizyon503: hostta kayitli futureDemandReduction, SalesModuleExtensions explicitfactory'sinde verilmemisti; yalniz mevcut participant baglantisi eklendi (SAT-S8/KLN-D105). Citleme/transaction sahibi korunur. Iptal edilmis fason fatura state7, aktif claim sayilmadan immutable history'de korunur.
- Muhasebe/Finans yarislarinda gercek SQL1205'in EF InvalidOperation/DbUpdate wrapper'i olculdu. Yalniz bu zincir mevcut ConcurrentChange veya Unavailable reddine cevrilir; otomatik retry/EnableRetryOnFailure veya basari cevabi eklenmedi. Duzeltme sonrasi genis SQL sonucu henuz yoktur.
- Fixture onarimlari: mevcut Audit Integration kanali, real Settings numara formati, real banka kimligi (acilis/bakiye icat edilmeden), SELECT-projection collation ve SQL keyword escaping; guard/yetki/para beklentisi gevsetilmedi. Documents realmapper unit kurulumu mevcut sentetik Audit ayarlariyla263yesil.
- Yerel SQL major17, CI SQLServer2022 major16: yerel tani son CI kabulu yerine gecmez. OpenAPI byte drift0, SHA091cc64a9e71f90ddc12b7c2ab9784b3f22fd8e181234f7f7b50603dc30dec04.
- Mevcut kaynak yuzeylerinin hashleri ev aracindan olculdu; 3a399af25f62 pin checkpoint'i K3 review bekler. Yeni migration/izin/grant/rol, instance policy, OPEN, production veya Finance STOP degisikligi yok.
- Hafif kontroller: iki validator0; backlog64/64PASS; bootstrap56/native0 ve9Windows kosullu skip; backend verifier38/38PASS; RCSI verifier103/103PASS; OpenAPI validator16/native0 ve2Windows kosullu skip. Bunlar SQLskip0 kanitindan ayridir.
- Siradaki kapi: yerel tani ve kaynak duzeltmeleri bitirilecek, pinler son kaynakta yeniden dogrulanacak, sonra normal FF push ile yeni dogal tam CI. Taslak#1201 ready/merge yapilmaz; iki yeni bagimsiz tam exact-head K3 review gerekir. Yazara yardimci ajanlar bagimsiz reviewer sayilmaz.


### 2026-11-19 - yerel SQL hedeflerinin ikinci checkpoint'i

- 9fa1588031c770a4ac40700ea90d8e7eb789725c turu tamamlandi:20 hedef12PASS8FAILskip0; bunun A6+B12 altkumesi18=10PASS8FAIL, iki revizyon2PASS. Onceki RUNNING satiri bu tamamlanmis kayitla guncellenir; genis CI kaniti degildir.
- a1ac08d7abb60229ea436d2d207ad59807a4d066: tam Integration+backend derlemesi0hata0uyari (401.281s); depolama3/3PASSskip0; yedi SQL hedef5PASS2FAILskip0. Transfer iptali, batch revival, iki kabulde tek ilk-fatura kurusu, order-cancellation Down ve Work dispatch gercek SQL'de PASS. Paint eksiksecim ve Identity Sales403 kaldi; ilk hatalar saklandi.
- Paint HTTP422'nin kapali kodu manufacturing_paint_selection_required: test gercek Product Excel uyeligi ve gercek Cari tedarikci surumleriyle tamamlandi. Identity port/adapter gecerken Sales403, yalniz Identity'de bulunan sentetik sirketin Organization'da olmamasiydi; gercek owner bootstrap'a yalitilmis sirketler eklendi. Uygulama yetki/uygunluk citlari gevsetilmedi.
- Manufacturing unit1428/1428 nativePASSskip0 fakat SDK1425 vaka: custom-object MemberData kesifte1, kosumda4gorunuyordu. Dort ayni vaka primitive InlineData olarak korundu (972f75493); onceki exactproof NOT_PASS, yeniden kosum gerekir.
- Manufacturing iptal write-guard fingerprint'i ev ureticisinden olculdu; recovery1465kaynak pin'i yeniden olculup3b0f53244'te gercek source-only consumer exit0 alindi:19context342migration66store. Yeni migration/izin veya tenantbypass eklenmedi. OpenAPI byte drift0.
- cc53a6363'te16 mevcut SQL hedefi ve tam Manufacturing unit kosumu baslatildi: RUNNING, PASS sayilmaz. Yalniz fixture/test/pin degisikligi oldugu git diff ile dogrulandi; hedefli derleme a1'de derlenmis degismemis backend bagimliliklarini kullanir. Son tam taze CI ayri kapidir.
- Kaynak174/174 (%100); W8 son paket. Yayinli71e8dd2c tam SQL CI halen FAIL152 ve taslakK3#1201 kabul bekler. Yerel SQL17 kaniti CI16, iki tam bagimsiz exact-head K3 ve insan kapilarinin yerine gecmez.

### 2026-11-19 — hedefli SQL onarımı ve alış outbox sözleşmesi

- Sabit 174/174 kaynak DONE (%100), W8 son paket; yeni dalga veya dilim yok. Yayımlı `71e8dd2c` CI sonucu halen 1.762 SQL = 1.610 PASS / 152 FAIL / skip0; yeni geniş CI kabulü alınmadı.
- `cc53a6363fe941c8dfcb2f51a9f68df2f6dafab4` yerel turu tamamlandı: 16 SQL hedefi 4 PASS / 12 FAIL / skip0. Aynı kaynakta tam Manufacturing birim testleri 1.428/1.428 PASS, skip0 ve exact SDK/TRX eşleşmesi PASS. Önceki RUNNING kayıtları bu sonuçla tamamlanır.
- `4358d76ef5cc391837815a73aa157105afe51f6e` hedefli derlemeler exit0; tam Purchasing birim testleri 884/884 PASS, skip0 ve exact SDK/TRX PASS. Seçili 17 gerçek SQL hedefi 13 PASS / 4 FAIL / skip0. Kimlik dizini, bozdurma muhasebesi, cari ekstre HTTP, alış faturası kesinleştirme/iade maliyeti ve yeni outbox Down guard fact'i geçenler arasındadır. Başarılı altküme toplu kabul değildir.
- Gerçek alış faturası kesinleştirme, modeldeki eski iki olaylı CHECK yüzünden reddediliyordu. EK24 FNS-D105'in mevcut `purchasing.invoice.finalized.v1` olayını kapalı üçüncü değer olarak ekleyen `20261119012425_AllowPurchaseIncomingInvoiceFinalizedOutboxEvents`, pinli dotnet-ef 10.0.8 ile üretildi; yalnız CHECK Drop/Add, veri yazımı yok. Down ilk adımda bu olay varsa THROW51815 ile durur. Numara canlı main ve diğer açık PR head'leriyle çakışma taramasından geçti. Gerçek SQL koruma testi ve model/DDL birim testleri PASS; tam boş Down/up kanıtı güçlendirilmiş yeni fact ile sıradaki turdadır. K3 incelemesi bekler.
- Dört kalan hedef için dar düzeltmeler: şirket açma deadlock'unda sadece daha önce commit edilmiş kazananın mevcut replay yolundan okunması; receipt-prelock fixture'ında gerçek scope/şirket/aktör ve kayıtlı maliyet portları; sıfır banka bakiyesinde mevcut FNS-D87 hata önceliği; profile-weight fixture SQL sütununun kaçışlanması. İş politikası, otomatik retry, para hesabı, grant veya aktivasyon eklenmedi. `4878fcafc90955cf186451e58b66b35945d09e2b` kaynağında beş SQL fact'i ve tam mimari test turu RUNNING; henüz PASS sayılmaz.
- Ev recovery üreticisi `4878fcafc90955cf186451e58b66b35945d09e2b` için exit0: 19 context / 343 migration / 66 durable store, SOURCE_ONLY. Yerel SQL17, CI SQL2022/16 kabulünün yerine geçmez; son taze CI ve iki bağımsız tam exact-head K3 incelemesi bekler. Taslak #1201 ready/merge yapılmaz.

### 2026-11-19 — kalan SQL ailelerinin ölçümü ve dar onarım adayı

- `4878fcafc90955cf186451e58b66b35945d09e2b`: beş onarılmış gerçek SQL fact'i 5/5 PASS, skip0, exact SDK/TRX PASS; yeni alış outbox migration'ı boş tam Down/up ve dolu guard korumasını geçti. Mimari 405 test:403 PASS/2 FAIL/skip0; sebepler beş EF OriginalValues.GetValue çağrısı ve iki yanlış eşlenmiş dosya fingerprint'iydi. Guard gevşetilmedi; tipli Property.OriginalValue erişimi ve ev aracından ölçülen doğru dosya pinleri hazırlandı. Yeni yerel OpenAPI byte karşılaştırması exit0, kanonik SHA değişmedi.
- `118aa796dfcec1dfd628539ba0aa09226b82c4fc`: başka 26 eski kırmızı SQL hedefi tamamlandı, 8 PASS/18 FAIL/skip0. Bu ayrı kümedir; önceki turdan çıkarılıp yeni geniş CI toplamı üretilmez. İlk kırmızılar ve özel test kanıtları korundu.
- Dar kaynak onarımları: kaynak fatura iptalinin ölçülen exact EF-wrapped1205 reddi; source-voucher onay DTO'sunda bilinmeyen JSON alanlarının reddi; transfer mal kabulünde mevcut owner/borrowed işlem sarmalayıcısı; kesirli exact maliyet havuzu değişmeden eski projeksiyon gerekçesinin eşlenmesi. Para/formül/izin/iş politikası, otomatik retry veya dış aktivasyon eklenmedi.
- Test onarımları: iki tarihsel Down guard'ı yeni koruma kayıtlarını silmeden kendi ilk SQL adımıyla sınanıyor; boş tam Down/up yolları korunuyor. Yarışlarda nonterminal anahtarın saklanmaması, aynı aktör/anahtarla yeni scope'taki açık isteğin ikinci ekonomik olay üretmemesi güçlendirildi. Source-grain yarış bariyeri fiziksel mutex öncesine, süre sınırıyla taşındı. Gerçek banka ve numaralama aktörü önkoşulları tamamlandı; bigint ve nullable rezervasyon beklentileri mevcut sözleşmeyle hizalandı. ALS-D92'nin geçerli kapalı+iptal payı pozitif sınanırken negatif/toplam sınırları korunuyor. Üretim output denetimi yedi kapalı olaylık tam listeyle doğrulanıyor.
- `31b90670b` kaynak kodunda dört değişen backend hedef derlemesi exit0; API/fixture, 20 SQL fact'i ve tam Manufacturing/Inventory/Accounting birim testleri RUNNING. Sonuçlar tamamlanana kadar PASS sayılmaz. Son mimari ve tam taze CI ayrıca gereklidir. Yeni dalga yok; kaynak174/174 (%100), runtime/release kabulü tamamlanmadı; taslak #1201 ready/merge yapılmaz.

- Sözleşme devam kaydı: `31b90670b` değişen backend ve API derlemeleri exit0. OpenAPI byte kontrolü exit2 ile durdu; fixture/birim/SQL aşamaları bu ilk denemede NOT_RUN. Gerçek üretilen JSON farkı yalnız `ApproveAccountingSourceVoucherV1.additionalProperties=false`: yeni bilinmeyen alan reddinin sözleşme karşılığı. Baseline gerçek API artifact'ından kopyalandı, şema semantik testi bunu açıkça doğrular. Backend kaynağının derlenmiş kimlikle eşitliği kontrol edilerek geçen derlemeler tekrar edilmeden yeni fixture ve test turuna devam edilecek; eski hata kanıtı korunur.

### 2026-11-19 — yerel 20 SQL sonucu ve son üç hedef

- `de140ea9aa19c72adad3ad3279bbdd90f37741f7`: 20 gerçek SQL fact'i 17 PASS/3 FAIL/skip0; exact SDK/TRX eşleşmesi başarısız fact'lerde NOT_PASS olarak korundu. Önceki 18 hata ve iki regresyon hedefidir, geniş CI kabulü değildir.
- Aynı head'de tam Manufacturing 1428/1428, Inventory 1967/1967, Accounting 679/679 PASS, skip0 ve exact SDK/TRX PASS. OpenAPI byte kontrolü exit0 ve üretim sözleşmesi fact'i 1/1 PASS, skip0. Fixture derlemesi exit0; backend derleme kanıtı kaynak eşitliği doğrulanarak 31b90670b üzerinden devralındı, taze derleme diye sunulmaz.
- Üç kalan: SQL17 sıralı aggregate hatası nedeniyle Üretim audit sorgusu; şirketin teknik fiziksel doğrulama guard'ında ilk INSERT PK yarışının ham sağlayıcı hatası; sıfır bakiye yerinin eski null test beklentisi. İlkinde aggregate yerine parametreli, binary sıralı satır okuma; ikincisinde yalnız guard PK2627 ve optimistic update yarışının mevcut ConcurrentChange reddine çevrilmesi; üçüncüsünde kayıtlı sıfır bakiyenin yeri ve 0 kg kanıtı eklendi. İş politikası/otomatik retry/commit/para hesabı eklenmedi. Guard kaynağı ve tipli sağlayıcı hata sınıflandırması K3 review bekler.
- `118aa796dfcec1dfd628539ba0aa09226b82c4fc` hafif doğrulama zincirinin tamamı exit0: backlog64, bootstrap56 (mevcut9 Windows koşullu skip), recovery36, release16, backend verifier38, RCSI103, OpenAPI drift16 (mevcut Windows koşullu skip); SQL skip0 bu koşullu birim skip'lerinden ayrıdır. Recovery/release eski head kanıtıdır; yeni riskli kaynak pin'i ev üreticisinden ölçüldü, yeni consumer kontrolü ve tam mimari koşusu bekler.
- Kaynak sabit174/174 (%100), W8 son paket; yeni dalga/dilim yok. Runtime/release tamamlanmadı. Üç SQL hedefi + tam Inventory ve mimari doğrulaması sırada; daha önce yerelde ölçülmemiş81 eski SQL A hatasının tanısı ardından alınacak. Yeni tam GitHub CI ve iki bağımsız tam exact-head K3 review olmadan #1201 ready/merge yapılmaz.

### 2026-11-19 — üç SQL düzeltmesi ve tam mimari sonucu

- Kaynak `698d9e82ff646ec40a5d3e141289bcf952b9a7c6`: son üç gerçek SQL hedefi 3/3 PASS, skip0, exact SDK/TRX PASS. Üretim audit olaylarının yedi elemanlık listesi, source-grain yarışında tek ekonomik etki ve sıfır bakiye ret ayrıntısı doğrulandı. Önceki17/20 sonucu kendi head'inde tutulur; bu iki koşum taze geniş CI toplamı değildir.
- Aynı head'de Inventory tam1967/1967 PASS, tam mimari405/405 PASS, skip0; exact SDK/TRX PASS. API, Inventory altyapısı, Integration ve mimari fixture derlemeleri exit0. OpenAPI byte kontrolü exit0, üretim sözleşmesi1/1 ve yerel test depolama3/3 PASS, skip0.
- Recovery gerçek consumer exit0/SOURCE_ONLY:19 context343 migration66 durable store, riskli kaynak1467/fingerprint4b5054dd87f1dac93a69c404db85b28c17228082de92845264943067c48f5588. Bu runtime/restore kabulü değildir. İlk producer yürütümü yalnız çalışma dosyası satır sonu/exact-commit eşitliği yüzünden durdu; commit byte'ı ve Git index aynı blob'a eşitlenip temiz worktree doğrulandıktan sonraki consumer sonucu ayrıdır. Kapı değişmedi.
- Daha önce yerelde ölçülmemiş81 eski SQL A hatası aynı derlenmiş kaynakta ayrı tanı kuyruğuna alındı, RUNNING; her süreç40 dakika/toplam180 dakika sınırında, SQL skip0 ve exact kimlik kapıları korunuyor. Bir testi atlamak, başarısız sonucu silmek veya kısmi sonuçtan başarı kanıtı üretmek yok.
- Kaynak incelemesi, aynı teknik guard yarışının bazı participant girişlerinde mevcut typed409 dönüşümünden önce oluşabildiğini gösterdi. Mevcut ConcurrentChange hata sözleşmesini koruyan dar eşleme ve anlamlı birim testleri hazırlanıyor; henüz derleme/SQL sonucu değildir. Otomatik retry/para hesabı/yeni iş politikası eklenmez.
- Kapsam174/174 kaynak DONE (%100), W8 son paket; runtime/release tamamlanmadı. Son taze GitHub CI ve iki bağımsız tam exact-head K3 review bekler; #1201 taslak kalır.

### 2026-11-19 — 81 eski SQL A hedefinin tanısı ve dar düzeltmeler

- `698d9e82ff646ec40a5d3e141289bcf952b9a7c6` üzerinde81 gerçek SQL fact'i tamamlandı:23 PASS/58 FAIL/skip0. SQL aşamaları49,36 dakika, ortanca35,54 saniye. Başarısız TRX ve native sonuçlar korunur; bu taze geniş CI kabulü veya yeni genel başarı yüzdesi değildir.
- Düzeltme kaynağı `34556dea80897a38a038ad8578b037fe54053cf9`: teknik fiziksel guard yarışları mevcut typed409 sözleşmesine eşlendi; muhasebe ve üretim kuyruğunda yalnız ölçülen EF-wrapped SQL1205, mevcut rollback/tracker temizliği sonrasında ConcurrentChange olur. Otomatik retry/yeni iş politikası yok. Üretim çıktı düzeltmesinin130 karakterlik iç anahtarı tam şirket/emir/fact kimlikleri korunarak120 karaktere indirildi; gerçek metadata factory'sine saf regresyon fact'i eklendi.
- Fixture'lar gerçek boya sahiplerine, şirket köküne, termin revizyon0 sözleşmesine ve fiziksel toplam/taslak ile serbest kg/onay ayrımına bağlandı. Eski endpoint/tablo/sütun beklentileri düzeltildi. Tarihsel Down guard'ları sonraki bağımsız guard'ları veya kalıcı kanıtı silmeden kendi gerçek SQL işlemleriyle sınanır; boş tam Down/Up ve tam geçmiş/bütünlük kanıtları korunur.
- ALS-D85/KLN-D105 satır kilidi altında iki alış termin yazması testi, imkânsız iki-save bariyeri yerine kontrollü gerçek owner kilidiyle tek revision ve diğer yazarda yalnız mevcut LineBusy reddini sınar; kaybedenin attempt/audit üretmediği ayrıca doğrulanır. Fact yeniden adlandırıldı; sayı aynı, envanter sıralı, fingerprint ev üreticisinden ölçüldü.
- Recovery gerçek consumer `190f58b1dd78ade4dbc1a6e08eb5be1457bf262c` üzerinde exit0/SOURCE_ONLY:19 context343 migration66 durable store; riskli1467 kaynak fingerprint9904f4c24f490df7095508e707fed56c1ec2d8a5bb48e0e293ec7a7552bbf8c6. Sonraki farklar yalnız fixture/envanter/rapor; backend ve generator byte'ları aynı. Bu restore/runtime kabulü değildir.
- Sırada taze API/fixture derlemesi, tam Inventory/Accounting/Manufacturing birimleri,58 düzeltilmiş SQL hedefi+3 regresyon, OpenAPI ve tam mimari var: QUEUED/NOT_RUN. SQL motoru yerelde17, CI'da16 olduğu için yerel kanıt son CI yerine geçmez. Native işler tek kuyrukta; süreç40 dakika, toplam SQL180 dakika kapıları korunur.
- Sabit kapsam174/174 kaynak DONE (%100), W8 son paket; yeni dalga/dilim yok. Runtime/release tamamlanmadı. Henüz yeni train push/tam CI veya yeni bağımsız tam exact-head K3 onayı yok; #1201 taslak kalır.

### 2026-11-19 — yerel onarılmış A turu ve dar takip

- `7004b0dcc4d759709d5a32662128c8c278fac7bb` üzerinde61 gerçek SQL fact'i tamamlandı: 54 PASS/7 FAIL/skip0. SQL süresi37.42 dakika; başarısız TRX ve exact kimlik sonuçları korunur. Tam Inventory1987/1987, Accounting679/679, Manufacturing1428/1428 PASS; tümünde skip0 ve exact SDK/TRX PASS. Yeni iç anahtar regresyonu1/1, OpenAPI üretim sözleşmesi1/1 ve yerel depolama3/3 PASS. Taze fixture derlemesi exit0; API tam referanslı derlemesi f134166b1a4c üzerinde exit0, yalnız test farkı kaynak eşitliğiyle devralındı, taze diye sunulmaz. OpenAPI byte kontrolü exit0. Mimari süreç exit0; ayrıntılı SDK/TRX kanıtı yerel rapordadır.
- Takip kaynağı `3ff63092b8be99c5f001828b993d312d43c0b1bc`: başarılı DDL'de yalnız SQL rowversion değişimi ayrılır, iş Version alanı korunur; reddedilen guard bütün satırı aynen korur. Transfer kaybı kararı mevcut owned/borrowed işlem örüntüsüne bağlandı; gerçek geç-save hata rollback'i, host rollback'i ve aynı anahtarla retry/replay kanıtı eklendi. Fazla teslim CHECK ve unique index testleri birbirinden ayrıldı. URT-D111 serbest stok onayı mevcut409 sözleşmesiyle hizalandı; taslak/sürüm değişmeme kontrolleri durur. Alış batch Down koruması kendi SQL adımıyla ayrıldı; boş tam Down/up eklendi. Outbox zaman aşımında RCSI ve gerçek okuyucu izolasyonunu ölçen tanı kontrolleri eklendi; kök neden henüz kapanmadı. Alış participant kanıtında LineNo kaçışlandı ve bütün JSON sonuçları scalar sarmalayıcıyla kesilmeden karşılaştırılır. İş politikası, otomatik retry, grant veya yeni dalga yok.
- Recovery consumer df86ded540fd üzerinde exit0/SOURCE_ONLY:19 context343 migration66 durable store, riskli1467 kaynak fingerprintf67f2a589d7df947280c2cf491a08beb2e0413a838d65ad539bbab0fabe666ca. Sonraki fark yalnız testlerdir; restore/runtime kabulü değildir. Takipte taze API/fixture, tam Inventory ve yalnız kalan7 SQL hedefi ile mimari doğrulaması QUEUED/NOT_RUN.
- Sabit174/174 kaynak DONE (%100), W8 son paket; runtime/release tamamlanmadı. Yayımlı71e8dd2c geniş CI sonucu değişmedi; henüz taze train push/tam CI veya iki yeni bağımsız tam exact-head K3 onayı yok. Yerel SQL17 hedefCI SQL2022/16 yerine geçmez; #1201 taslak kalır.

### 2026-11-19 — yedi hedefin sonucu ve iki hedeflik takip

- `94479216856fe83563e3fc7ba8afe0e5046636a3` taze API tam referanslı derleme ve fixture exit0; OpenAPI byte kontrolü exit0 ve üretim sözleşmesi1/1 PASS. Tam Inventory1987/1987 ve mimari405/405 PASS, skip0 ve exact SDK/TRX PASS. Yedi gerçek SQL hedefi5 PASS/2 FAIL/skip0; başarılı beşin exact kimliği PASS, iki başarısız kanıt korunur. Satır sürümü, kayıp/fazla teslim işlem sınırı, serbest stok hata sözleşmesi ve numaralı batch guard+boş tam Down/up geçmiştir.
- Outbox tanısı: gerçek veritabanında RCSI açık; ambient/EF transaction yok, fakat gerçek okuyucu oturum izolasyonu READ COMMITTED değil. Süre artırmak yerine kaynak yalnız kendi READ COMMITTED okuma sınırını açacak; uyumlu host işlemini commit/rollback/dispose etmeyecek, uyumsuz/ambient sınırı sorgudan önce reddedecek. Horizon, şirket filtresi, sıralama ve cursor aynı; test gerçek komut izolasyonunu ve host sahipliğini ölçecek. K3 review bekler, henüz runtime PASS değildir. İkinci hata boş JSON scalar'ın DBNull dönmesidir; kanıt sorguları tam JSON'u ve boş dizi karşılığını açıkça korur.
- Takip kaynağı `111f1ebfe47cb791a5bc224151bb59f09083b2e7`: yalnız bu iki SQL hedefi, değişen Purchasing birimleri ve mimari doğrulaması QUEUED/NOT_RUN. Diğer backend/build-control kaynaklarının944792168 ile eşitliği doğrulanarak yalnız değişen Purchasing bileşeni yeniden derlenecek; API/fixture yine güncel çıktıyla derlenir. Riskli1467 kaynak ölçümü190fd826f5f863fa0d87ae2c80e67e05fe79cb527a4ce536d1493ae63351ad93; consumer doğrulaması sıradadır. Backlog/bootstrap/test-grubu/RCSI/OpenAPI hafif doğrulayıcıları944792168 üzerinde exit0;9 bootstrap ve2 OpenAPI Windows koşullu skip SQL skip0 yerine geçmez.
- Sabit174/174 kaynak DONE (%100), W8 son paket; yeni dalga veya iş politikası yok. Runtime/release tamamlanmadı; yayımlı71e8dd2c CI kaydı değişmedi. Taze tam CI ve iki bağımsız tam exact-head K3 onayı bekler; #1201 taslak kalır.

### 2026-11-19 — son iki SQL hedefi yerelde kapandı; taze CI sırada

- `e728607c921ad51ef96fdc71ea576ff40cd35a9f`: iki gerçek SQL fact'i2/2 PASS, skip0 ve exact SDK/TRX PASS. Gerçek outbox komutları, SERIALIZABLE test oturumu önkoşuluna rağmen kendi READ COMMITTED işleminde yürütülür; uyumlu host işlemi açık kalır, uyumsuz/ambient işlem sorgudan önce reddedilir. Önceki açık A olayı atlanmadan A→B ve cursor/şirket sınırları korunur. Alış kabul participant tam geçmiş/constraint/iş kaydı kanıtı ve gerçek host rollback'i geçti.
- Aynı kaynakta tam Purchasing884/884 ve mimari405/405 PASS, skip0 ve exact SDK/TRX PASS. Değişen Purchasing bileşeni9,765s/API34,903s exit0; değişmeyen backend/build-control kaynaklarının944792168 ile eşitliği doğrulanarak önceki tam referans derlemesi açıkça devralındı. Yeni fixture113,016s exit0. OpenAPI byte kontrolü exit0, üretim sözleşmesi1/1 ve yerel depolama3/3 PASS. Recovery gerçek consumer exit0/SOURCE_ONLY:19 context343 migration66 durable store; riskli1467 kaynak190fd826f5f863fa0d87ae2c80e67e05fe79cb527a4ce536d1493ae63351ad93. Bu restore kanıtı değildir. Hafif doğrulayıcıların tamamı exit0; Windows koşullu9 bootstrap/2 OpenAPI atlaması SQL skip0'dan ayrıdır.
- Yayımlı71e8dd2c CI'sındaki152 başarısız methodun tamamı, korunmuş yerel exact SDK/TRX sonuçlarında son ölçümü PASS olarak bulundu; açık/ölçülmemiş0. A güvenli özetindeki57 kesilmiş grup, korunmuş tam107 FQN listesiyle tamamlandı. Güvenli method/kaynak-head matrisi W8_CI71_YEREL_HATA_KAPSAMI.json içindedir. Kanıtlar farklı onarım head'lerindedir;152/152 yerel izleme tek head'de1762 testin PASS olduğu anlamına gelmez.
- Kaynak174/174 DONE (%100), W8 son paket; yeni dalga yok. Bilinen yerel kırmızılar giderildi; runtime/release kabulü hâlâ tamamlanmadı. Son normal fast-forward train push'u ile taze GitHub SQL2022/16 tam CI alınacak; sonra iki bağımsız tam exact-head K3 incelemesi gerekir. Önceki kırmızı CI/ilk hata kanıtları korunur. #1201 taslak kalır, yazar merge etmez.

### 2026-11-19 — CI87 keşif sınıflaması düzeltildi

- `87c12ac994eae9a51612972b29b97b2c938dc553`, CI run44463834664: FAILURE. Backend api1/api2/api3/modules/architecture, frontend audit/lint/build ve dört shard, OpenAPI, RCSI, recovery/release ile ayrı gerçek tarayıcı/Cari/UI golden işleri SUCCESS. Üç geniş SQL shard'ı, beklenen1756 yerine1757 yöntem bulduğu için geniş test yürütümüne başlamadı: NOT_RUN; eski152 hatanın tekrar ettiği veya SQL kabulünün geçtiği söylenmez.
- Fazla yöntem, yazarın SQL adlı partial sınıfa eklediği saf metadata Fact'idir. `43bfd3c3d9d35fe2d587c0700133e8fd0b599f2a` testin bütün gövdesini değiştirmeden ayrı ManufacturingOutputCorrectionMetadataTests sınıfına taşır. Test silinmedi/atlanmadı; hiçbir SQL fact'i, kayıtlı envanter veya kapı değiştirilmedi.
- Aynı43bfd3c3d kaynağında taze fixture derlemesi exit0; gerçek SDK/console keşfiyle mevcut broad doğrulayıcı tam1756 yöntem, eksik0/fazla0 PASS. Taşınan Fact1/1 PASS, skip0 ve exact SDK/TRX PASS. Backend test-grubu ve RCSI Python doğrulayıcıları exit0. Backend/build-control kaynaklarının e728607c921a ile eşitliği doğrulandı; önceki backend derlemesi açıkça devralındı. Bu yeni tam SQL koşumu değildir.
- Sabit174/174 kaynak DONE (%100), W8 son paket; runtime/release tamamlanmadı. Normal fast-forward train push ile taze tam CI, ardından iki bağımsız tam exact-head K3 review gerekir. #1201 taslak kalır; manuel rerun, workflow değişikliği, yeni dalga veya yazar merge'ü yok.
