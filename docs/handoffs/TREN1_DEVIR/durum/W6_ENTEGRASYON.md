# TREN-1 — Dalga 6 entegrasyonu

Durum: **131/174 kaynak dilimi DONE (%75,3); 24 PARTIAL; 19 başlanmadı.**
Bu sayım kullanıcı kabulü değildir. Tam .NET birim/mimari, gerçek SQL, HTTP,
Vitest, tarayıcı ve CI koşumları **NOT_RUN**.

## Birleşen kapsam

Yedi ekip platform → satis → alis_cari → uretim → finans → kimlik → stok_lojistik
sırasıyla no-ff birleştirildi. Entegrasyon kaynağı 7529fe1e8; son backend davranış
birleştirmesi 65bc94e9691287eebdfd22975fe666578f087c04.

Yeni kaynak DONE: KIM-S3, KIM-S13, KIM-S18, PLT-S13, SAT-S8, SAT-SC32,
STK-S1, STK-S26, URT-S6, URT-S7, URT-S18.
Diğer kalanlar ve bağımlılıklar KALAN_PLAN.json ile ekip raporlarında açık tutuldu.
Beş DOGRULA kaydı incelendi: SAT-SC32 kaynak düzeltmesiyle DONE; KIM-SC32-a/b
PLANNED; URT-SC32 ve FNS-SC32 PARTIAL. İsim veya test stub'ı uygulama sayılmadı.

- SAT-S8 gerçek host katılımı Sales ve Inventory yazmalarını tek transaction'da tutar.
  Kilitler bütün Sales satırları, sonra bütün purchase satırları olarak sıralanır.
  Fiziksel-only kontrol, null otomatik seçim, fresh authority, transient ret önceliği,
  rollback ve replay test kaynakları yazıldı. Purchasing termin değişimi aynı kilide alındı.
- SAT-SC32 fiyat okuma yetkisi yokken tutar, iskonto oranı ve yapılandırılmış sınır
  merkezi backend view'da gizlenir; decoder ve ekran kaynakları buna uyar.
- Identity başka modülün AuditEvent mapping'ini taşımaz. Audit owner port'u caller'ın
  aynı DbTransaction'ına katılır; başarısızlık ve yabancı şirket kaynak testleri eklendi.
  EF üretimi metadata migration'ın Up/Down'u boş; Audit tablosuna DDL yoktur.
- Purchasing'deki iki yetkisiz IgnoreQueryFilters kaldırıldı. Fazla teslim izni yalnız
  company-wide kaydedildi, Critical değildir. Kredi approve Critical, propose değildir;
  grant veya rol ataması yapılmadı. Kredi alan belgesi DRAFT olarak bırakıldı.
- Stok PDF/RecordedBy kaynakları, Ayarlar listeleri ve üretim iptal/fason kaynakları
  ekip raporlarında ayrıntılıdır; browser/vault/barkod kabulü iddia edilmez.
- PLT-S13 belge/durum senkronudur; bildirim deposu uygulamasını veya karar/OPEN durumunu
  tamamlanmış saymaz. Yeni kararların 14 ek işi plan meta'sında ayrıca izlenir.

## W7'nin ilk teknik işi

STK-S17 **PARTIAL**: mal kabulü future→physical çevrimi yalnız purchase kilidi alırken
talep toplamı fiziksel ve future kümelerini iki ayrı sorguyla okur. Başka purchase
satırında yeni ayırmayla yarışıp toplam sınırını aşma karşı örneği kaynakta bulundu.
Çözüm, receipt callback'inden önce bütün ilgili Sales kilitlerini ve sonra purchase
kilitlerini almak, kümeyi yeniden doğrulamak; callback'i örtük tekrar çalıştırmamaktır.
İşletme kararı gerektirmez. Kaynak race testi gerçek SQL'de henüz koşulmadı.

STK-S19 / ALC-S6 ve üretim elle sıra kapsamındaki açık davranışlar #1189'da
NEEDS-SINAN sorularıdır. Cevap yerine tahmin etkinleştirilmez.

## Derleme ve üretim

- API, ArchitectureTests ve 18 UnitTests projesi: 20/20 derleme exit 0.
- Son Api.IntegrationTests ve DocumentGeneration.Pdf.Tests derlemeleri: exit 0,
  sıfır hata/uyarı. Bu sonuçlar test koşumu değildir.
- Frontend npx tsc -b: exit 0. Paket kilidi değiştirilmedi; Vitest NOT_RUN.
- Shell authorities gerçek registry üreticisiyle yenilendi: 15 aktif satırın 8'i
  değişti; 51 kullanılmayan tarihsel sabit elle değiştirilmedi.
- OpenAPI gerçek API üretiminden byte eş kopyalandı: 616 operation;
  SHA-256 6e3a7b59af669713a3cf069edd1768e137330e49dd98a59fa7f173ac8d725e15.
  Contract validator exit 0; 10 operation / 25 component beklentisi veri karşılaştırması
  eşleşti. .NET generation test koşumu henüz yapılmadı.
- Recovery: 19 context, 253 migration, 53 durable store. Yeni Identity metadata ve
  Settings 51940 migration'ları dahil; kaynak-only envanter restore kabulü değildir.
- 29 yeni gerçek SQL fact adı, module-of eşlemesiyle sıralı ilişkisel envantere eklendi.
  Bunlar test kaynağıdır, gerçek SQL PASS değildir.

## K3 ve insan kapıları

W5'teki 12 bypass ve 5 context construction girdisi K3 review beklemeye devam eder;
kapsamları sessiz genişletilmedi. İki yeni guard girdisi yalnız System.Data.Common
namespace kullanımı içindir: AuditTransactionWriterV1 ve SalesFutureDemandReductionHostParticipant.
Borrowed transaction/owner port gerekçeleri yorumlarda yazılıdır; bunlar da K3 review
bekler. Yeni raw SQL veya tenant bypass izni değildir.

Main henüz trene birleştirilmedi. Kaynak/test/migration'da elle main çakışması çıkarsa
kullanıcının durma kuralı uygulanacak. Son implementation PR yalnız kapanışta taslak K3;
merge ve bağımsız review bu dalga işi değildir.

EK26 ve belge takip PR'ları #1194 / #1195 taslaktır; iki bağımsız DOCS review bekler.
Karar/master/OPEN, grant/rol, production, Finance STOP ve SQL servis ayarları değişmedi.

## Hafif kontroller

Kaynak commit 7529fe1e8c2a40d2b050d9e97082b79fc716affd sabit tutularak:
delivery_backlog_validator 64/64; dotnet_bootstrap_integrity 56 test (9 mevcut
Windows symlink/case koşullu atlama); backend_test_group_verifier 28/28;
rcsi_relational_acceptance_verifier 48/48; openapi_contract_drift 16 test
(2 mevcut Windows symlink koşullu atlama); release_manifest 16/16;
recovery_surface_inventory 36/36: **exit 0**. Koşullu 11 atlama skip0 değildir.
Delivery backlog ve bootstrap bağımsız validator'ları da exit 0 verdi.
Release manifest üreticisi aynı temiz commit için exit 0 verdi; kaynak-only artifact'tır.
Son rapor/sayım commit'i bu kontrollerden sonra yalnız devir belgelerini değiştirir;
başka head için runtime veya CI kanıtı ileri sürülmez.

W6 kaynak head'i değişmesin diye W7 ekip dalları aynı 7529fe1e8 kaynağından,
recovery kontrolü tamamlanırken ayrı çalışma alanlarında başlatıldı. Tüm yedi
W6 ekibinin sabit sıradaki no-ff entegrasyonu tamamlanmıştı; W7 kodu bu rapora
ve W6 kaynak/test doğrulama sonucuna dahil değildir.
