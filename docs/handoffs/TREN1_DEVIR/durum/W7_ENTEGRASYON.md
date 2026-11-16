# TREN-1 — Dalga 7 entegrasyonu

Kaynak sayımı: **135/174 DONE (%77,6); 23 PARTIAL; 16 başlanmadı.**
Yeni DONE: **KIM-SC32-a, KIM-SC32-b, STK-S17, STK-S18**.
Bu sayım kullanılabilirlik veya kabul değildir. Tam .NET birim/mimari, gerçek SQL,
HTTP, Vitest, browser ve CI koşumları **NOT_RUN**; eski W5 kırmızıları yeşil sayılmaz.

## Birleşen kapsam

W7 kaynakları üretim → finans → kimlik → stok_lojistik sırasında no-ff birleştirildi;
diğer üç ekipten bu dalgada yeni kaynak yoktu. Son dar Settings replay düzeltmesi
`a427e1e9fbb05ab4215914aab1e81f1df80415bb` ve raporu ayrıca no-ff alındı.
Backend davranış/sözleşme checkpoint'i `6edcbf09266c5d9f6038cadb8bd5fdcc725a8284`.
Ekip raporları W7_kimlik, W7_stok_lojistik, W7_uretim ve W7_finans dosyalarındadır.

- Settings gerçek teminat türü/sürüm kataloğu ve ayrı sözleşme iskonto sınırı;
  exact oran/missing/zero, owner Audit, immutable history ve taze yetkili replay.
  Beş unique-race dalında son kazanan okumasından sonra özgün şirket/aktör ve taze
  izin tekrar ölçülür. Gerçek SQL yarış fact kaynakları yazılmıştır, koşulmamıştır.
- Kabulde Sales kilitleri önce, purchase kilidi sonra; taze allocation altkümesi,
  yalnız yazma öncesi bounded hazırlık tekrarı ve tek business callback.
  F017-d kabul ekranı gerçek owner toplamını okur; okunamayan veri sıfır sayılmaz.
- Manufacturing gerçek input availability, Settings çalışma günü tamponu ve ayrı
  append-only öncelik revizyonu. Rollback sonrasında yalnız aynı şirket/plan için
  istenen/kullanılabilir/eksik kg bilgisi HTTP/UI'a açılır; replay ayrıntıyı taşımaz.
- Finance iç defteri ve bakiye kontrolü aynı Serializable transaction içindedir.
  Taslaklar bakiye sayılmaz; POS net, virman bacakları, açılış ve tersleri tek olgudur.
  MARS/save failure/borrowed transaction/audit-batch kaynakları eklenmiştir.
  FIN-SC36 bütün paketi bitmediğinden PARTIAL kalır.
- KIM-S5 SetupPending/lifecycle/inbox/outbox kaynağı PARTIAL'dır. Kullanıcının daha
  yeni açık ilk yönetici seçimi cevabı eski otomatik opener→Admin yolunun değişmesini
  gerektirir; bu eski yol tamamlanmış sayılmaz. Açma akışı/UI ve gerçek grup yetki
  kaynağı eksiktir; dispatcher kapalıdır. Gerçek kullanıcıya rol/grant yapılmadı.
- `60751490a569e05c86147dac729d76a3cd5b2c6f`: normal şirket isteği lifecycle sahibini
  okumadan önce erişim çitinden geçer; yabancı pending/active/unavailable ayrımı sızmaz.
  Mevcut first-admin istisnası ve await sonrası erişim çiti korunur. Altı test metodu/
  sekiz senaryo kaynak olarak eklendi; gerçek host/SQL kabulü değildir.
- `6edcbf09266c5d9f6038cadb8bd5fdcc725a8284`: ortak şirket 409/503 sözleşmesi handler'ın
  JSON mediasını korur. Ürün read/preview 409'u idempotency diye etiketlenmez.
  503, gerçek platform writer'ın `service_unavailable` koduyla belgelenir; yeni
  code-less varsayımı yapılmaz. 626 operasyon ve 20 yeni/değişen component beklentisi
  kaynak/gerçek üretim JSON'u ile statik karşılaştırıldı; test koşumu değildir.

## Main birleştirmesi

Kullanıcının [dar istisna cevabı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926992238)
ile main `8b680d1c7a3886fba2766de9b2443e2c6b916c71`, no-ff merge
`8ac175881a78f955e9f3f49576a746451cc58824` ile trene alındı.
ManufacturingSalesOrderSupplyReadPort.cs tren tarafıyla byte olarak aynı tutuldu;
main #1120 güncel revizyon kg düzeltmesi zaten vardı, iptal/çoklu nesil ekleri korundu.
Üç pin/envanter çatışmasında bütün fact'ler korundu; fingerprint'ler üreticiyle yeniden
alındı. Bu elle kaynak çözümü mekanik DELTA değildir; final head'de iki tam bağımsız K3
incelemesi gerekir. K3 öncesi main tekrar taze doğrulanacaktır.

## Yeni cevaplar ve belge adayları

Kaynaklar ajan aktarımıdır; insanın GitHub'a bizzat yazdığı yorum olarak sunulmaz:
[#1189/6926090415](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926090415),
[6926238225](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926238225),
[6926298700](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926298700),
[6926992238](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926992238).

Kur önceki iş günü/arife algoritması; fatura teorik kg oranı ve irsaliye eski ağır satır
kuralı; alış siparişinde önek/yıl/sıra; bildirimin değiştirilebilir 90 gün başlangıcı;
açık ürün ticari mal/mamul sınıfı; satır SMM düzeltmesi; kalıcı elle üretim sırası ve
C15→B20→A10 anchor; 7B yalnız sığanı yeniden açma; taslakta fiziksel toplam kg; açık
ilk yönetici seçimi yanıtlandı. Karar engeli yerine eksik owner uygulaması izlenir.

Sonraki iki cevap: [6927182217](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6927182217)
muhasebede ters/düzeltme yetkilisi hazırlar, farklı muhasebe yetkilisi onaylar;
[6927195150](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6927195150)
muhasebe yetkilisi açık dönem/tarih seçer, dönem kendiliğinden açılmaz.
Şu anda bu sorulara bekleyen cevap yoktur. Fiziksel stok/tutar/miktar/vergi değişmez.

#1194 EK26 head `2d0b5f40eb14e9107c4429f8ed812a9f04d11ef3` korunur.
#1195 P2 hatalı EK22/FNS-D73 düzeltme satırı yalnız yeni belgede kaldırıldı;
head `30d73d0e23eeab09d4a0832e87995547c17b80cd`, iki yeni DOCS incelemesi gerekir.
#1196 EK27 head `f894221aa0db8fab8639141b7c09f3192933739b`, iki DOCS incelemesi bekler.
Eski karar dosyaları yerinde değiştirilmedi; belge PR'ları kod kabulü değildir.

## Üretim ve K3 listesi

- Shell: gerçek ComputeCandidateRevision üreticisinden 15 aktif authority; dört değer
  değişti, tarihsel satırlar korundu. Settings 1.11, Inventory 1.9.
- OpenAPI: gerçek .NET üretimi **626 operasyon**, baseline byte-equal;
  SHA256 `97695739fc94339ca23de4c5e137ac0595bf1389aea781528611cb95bd9f45cd`.
- Recovery: ilk üretici koşumu üç yeni mesaj tablosunun eksikliğinde exit 2 verdi.
  Identity/Organization inbox ve Organization outbox fiziksel kaynakları kaydedildi;
  beklenen toplam **19 context, 258 migration, 56 durable store**. Organization outbox
  için üretim yazıcısı henüz yoktur; DDL/model/okuyucu kanıtı yazıcı diye gösterilmedi,
  KIM-S5 PARTIAL kaldı. Risky source sayısı 1049; Audit 31 dosya.
  DispatchSourceAsync, eski altı blob ve gerçek yeni inbox/outbox yazıcıları üreticiyle
  yeniden ölçüldü. Envanter kaydı K3 onayı veya çalışma kabulü değildir.
- Mimari guard: 28 mevcut fingerprint, iki mevcut GetDbConnection sayımı yeniden ölçüldü.
  Tek kabul 3→6, batch 1→5; hepsi aynı caller bağlantısı ve owner port enlist'idir,
  yeni bağlantı açma değildir. K3 incelemesi bekler.
- 38 yeni SQL fact adı ordinal envantere eklendi; main'in fact'leri korundu.
  SQL çalıştırılmadı; envanterde bulunmak PASS değildir.
- EF ile üretilen beş migration: AddCollateralTypeCatalog **51941**,
  AddSalesContractDiscountLimit **51942**, AddManufacturingPlanPriorities **51943**,
  AddCompanySetupInboxAndOutboxPosition **51944**, AddCompanySetupLifecycleAndMessaging
  **51945**. Down ilk SQL'de owner geçmişi varken veri kaybını reddeder. K3 review bekler.

Yeni beş runtime kaynak gerekçesi guard'a açıkça **K3 review pending** yazıldı;
inceleme verilmiş sayılmaz. Yeni tenant filtresi bypass'ı açılmadı:

1. FinanceTreasuryTransaction: System.Data.Common yalnız DbException/ödünç transaction
   isolation/rollback türü; bağlantı açmaz, SQL komut metni üretmez.
2. SettingsCollateralTypeAdministration ve SettingsCompanyPolicyAdministration:
   Microsoft.Data.SqlClient yalnız owner SaveChanges sonrası 2601/2627 tanıması.
3. IdentityCompanySetupOutboxSource ve OrganizationCompanySetupOutboxSource:
   tek sabit parametresiz MIN_ACTIVE_ROWVERSION horizon okuması; event satırları
   ambient CompanyId filtresinde kalır, yazma yoktur. Aktif transaction altında erken
   rowversion cursor ilerletilip event atlanmasını önler. Dispatcher etkinleştirilmedi.

W5'teki 12 runtime + 5 context-construction K3 listesi ve W6'nın iki namespace/type
kaynağı final K3 gövdesine taşınacaktır; review durumu yükseltilmedi.

## Derleme ve hafif doğrulama

Api/OpenAPI üretimi, Api.IntegrationTests ve 18 UnitTests projesi: **exit 0, 0 hata/uyarı**.
TSC `npx tsc -b` exit 0; frontend kaynakları bu ölçümden sonra değişmedi.
Yeni guard kaynakları için ArchitectureTests derlemesi ve üretici/hafif Python kontrolleri
henüz tamamlanmadı; sonuçları ayrı doğrulama kaydında tamamlanmadan trene push yapılmaz.
Tam uygulama/SQL/test/CI koşumları yapılmadı. Grant/rol, activation, OPEN ve Finance STOP
değişmedi. Kalan 39 dilim KALAN_PLAN.json'da açık tutulur.
