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
