# GöymenERP — uygulanabilir paket planı, 26 Kasım 2026

**Durum:** hedefli kaynak eşleştirmesi ve yerel plan. Yeni bir backlog veya bütün ERP için yeniden tasarım değildir. İşletme kabulü değildir.

Ana kayıt [DELIVERY_BACKLOG_v1.json](../backlog/DELIVERY_BACKLOG_v1.json) olarak kalır. 41 alt paket, mevcut iş kimliklerine bağlanmıştır; ana backlog durumları, sorumluları ve bağımlılıkları değiştirilmedi. Kesin kapsam, mevcut/önerilen dosyalar, referanslar, kaynak bulguları, bağımlılık, sahiplik, risk, teknik/işletme kabulü ve açık kararlar [paket JSON dosyasındadır](UYGULAMA_PAKETLERI_2026-11-26.json).

**Kaynak anı:** main `ddc6cba4e0e172cca60a2dc3d4e339972a08e5f6`; tren-2 `c24706af4de4300d4a171eca11459098d87cd14f`. Bu tarih/sürümdeki bulgular sonraki commitlere otomatik taşınmaz.

## Kesinleştirilen yön ve sınırlar

[26 Kasım kararları](../decisions/SINAN_KARARLARI_2026-11-26_UYGULAMA.md): ROL-D26, ALS-D129, ALS-D130. Kesin fiş düzeltmesinde farklı MUHM; karma alışta mevcut gerekli sipariş onayından sonra bağımsız kısmi gerçekleşmeler; siparişsiz/acil alış ilk kullanımda. Önceki Mikro, mülkiyet, maliyet ve görev ayrılığı kararları açıkça değiştirilmedikçe sürer.

Bu yönler rol ataması, hesap/vergi seçimi, deployment veya AI açma yetkisi değildir. Tartışma metnindeki öneriler otomatik işletme kararı yapılmaz.

## İlk incelemedeki kaynak farkları (26 Kasım)

Bu bölüm başlangıç anıdır; güncel teslim ve kalan kabul kanıtları aşağıdaki tarihli notlarda ve paket JSON'undadır.

- İzin kaydı 311, Critical 17; önceki matris 306 satırdı. Kodda bulunan beş izin yalnız rol **önerisi** olarak eklendi. Grant/seed değişmedi. MD ve CSV eşleştirildi.
- [311 izin kaynak haritası](YETKI_KAYNAK_ESLESMESI_v1.json) backend, capability, frontend ve test kullanım yerlerini ayrı gösterir. Leksik kullanım listesi, tüm ekranların efektif erişimi veya hassas alan kontrolü incelenmiş demek değildir; bu semantik kabul ROLE-UX paketindedir.
- Alış backend’i çok satır kabul ediyor; oluşturma ekranı tek satır gönderiyor. Önce mevcut servisi kullanmak gerekir. Mal/hizmet karma belge ve siparişsiz fatura/kabul ayrı sözleşme işidir.
- Üretim stok sözleşmesinde adet mevcutken üretim giriş portu/host zinciri adedi taşımıyor. Beş motoru yeniden yazmak yerine bu sınır tamamlanmalı ve ardışık proses kabulü yapılmalı.
- Normal satış kapanışında owner axes kaynağının uygulama/kaydı bulunmadı. Kısmi sevkte risk ve çift açık kalem iddiaları ise yeniden üretim gerektirir; mevcut runtime hatası diye sunulmaz.
- AI hattı Unconfigured ledger; kredi kapısı NoneApproved. Kredi grupları için mevcut onay tekrar sorulmaz; alanlar ve bağlantılar tamamlanmadan kapı körlemesine açılmaz.
- Reporting’de beş rapor, dışa aktarım, kayıtlı rapor görünümü ve denetçi paketi var. Genel operasyonel liste dışa aktarımı/görünümü bunlarla aynı değildir; WP14 kapsam kabulü ayrıca gereklidir.

## İş sırası ve sahiplik

1. Bu yerel referans/izin eşliği ve plan doğrulaması; bağımsız inceleme sonrası entegrasyon.
2. İlk sınırları açık işler: çok satırlı alış ekranı (Claude), üretim giriş adedi (Codex/K3), satış kapanış owner portları (Codex/K3), risk/açık kalem yeniden üretimi. Paket içi kaynak okuması ve dosya lease ön koşuldur.
3. İlk kullanım için mali sınıflama, kesin fiş düzeltme, karma ve siparişsiz alış: mevcut altyapı kullanılır; aşağıdaki dar eşleme/yetki kararları tamamlanır. Tamamı ileri sürüme atılmaz.
4. Tren-2 kalan paketleri ayrı hat olarak mevcut taahhütle biter; bu yeni ilk kullanım kapsamı trene eklenmez.
5. İki veri provası, gerçek rol ve şirketlerle uçtan uca kabul, mutabakat ve kurtarma kanıtı; ardından insan aktivasyon kapıları.

26 Kasım geçici kullanıcı devriyle Codex backend ve frontend'i yürütür; mevcut koordinatör entegrasyonu yönetir. Aynı dosyada iki yazar yok. Work migration sırası WP3 → WP8 → YOL-D40; AI defteri ayrı şema, C2/C3 ondan sonra. Muhasebe ve Finans şemaları kendi tek yazar kuyruğunda. Pinler en sonda. Bu plan yeni ajan veya çalışma servisi başlatmaz.

## Paket dizini

| Kimlik | Öncelik / risk | Durum | Bağımlılık |
|---|---|---|---|
| W1-YETKI/ROL-D24-PARITY | P0 / K2 | IMPLEMENTED_LOCAL | — |
| W1-YETKI/ROLE-UX | P0 / K3 | SOURCE_PARTIALLY_IMPLEMENTED_LOCAL | W1-YETKI/ROL-D24-PARITY |
| W1-ORG/BOOTSTRAP | P0 / K3 | SOURCE_REVIEW_REQUIRED | — |
| W1-CARI/QUICK-CREATE | P0 / K2 | SOURCE_PARTIALLY_IMPLEMENTED_LOCAL | — |
| W1-URUN/UNIT-SCOPE | P0 / K3 | SOURCE_PARTIALLY_IMPLEMENTED_LOCAL | — |
| W2-SATINALMA/MULTILINE | P0 / K2 | SOURCE_IMPLEMENTED_LOCAL | — |
| W2-SATINALMA/MIXED-MIN | P0 / K3 | SOURCE_REVIEW_REQUIRED | W2-SATINALMA/MULTILINE, W1-URUN/UNIT-SCOPE |
| W2-SATINALMA/NOPO-MIN | P0 / K3 | CONTRACT_REQUIRED | — |
| W4-MUHASEBE/CLASSIFY-MIN | P0 / K3 | CONTRACT_REQUIRED | — |
| W4-MUHASEBE/CORRECTION-MIN | P0 / K3 | SOURCE_REVIEW_REQUIRED | W4-MUHASEBE/CLASSIFY-MIN |
| W4-FINANS/EXPENSE-CATALOG | P0 / K3 | CONTRACT_REQUIRED | — |
| W2-SATIS/CLOSURE-AXES | P0 / K3 | SOURCE_PARTIALLY_IMPLEMENTED_LOCAL | — |
| W4-RISK/SECOND-SHIPMENT | P0 / K3 | SOURCE_PARTIALLY_IMPLEMENTED_LOCAL | — |
| W2-ACIKKALEM/LEGAL-IDENTITY | P0 / K3 | REPRODUCTION_REQUIRED | — |
| W3-URETIM/INPUT-PIECES | P0 / K3 | SOURCE_IMPLEMENTED_LOCAL | — |
| W3-URETIM/FIVE-ENGINES | P0 / K3 | ACCEPTANCE_REQUIRED | W3-URETIM/INPUT-PIECES |
| W3-URETIM/SUBCONTRACT | P0 / K3 | ACCEPTANCE_REQUIRED | — |
| W3-LOJISTIK/EXIT-EVIDENCE | P0 / K3 | SOURCE_REVIEW_REQUIRED | — |
| W2-BELGE/MIKRO-EDOC | P0 / K3 | SOURCE_REVIEW_REQUIRED | — |
| W5-RAPOR/WP14-COVERAGE | P0 / K2 | ACCEPTANCE_REQUIRED | — |
| W5-MIGRASYON/CUTOVER | P0 / K3 | HUMAN_ACTIVATION_REQUIRED | W1-YETKI/ROLE-UX, W1-ORG/BOOTSTRAP, W1-CARI/QUICK-CREATE, W1-URUN/UNIT-SCOPE, W2-SATINALMA/MULTILINE, W2-SATINALMA/MIXED-MIN, W2-SATINALMA/NOPO-MIN, W4-MUHASEBE/CLASSIFY-MIN, W4-MUHASEBE/CORRECTION-MIN, W4-FINANS/EXPENSE-CATALOG, W2-SATIS/CLOSURE-AXES, W4-RISK/SECOND-SHIPMENT, W2-ACIKKALEM/LEGAL-IDENTITY, W3-URETIM/FIVE-ENGINES, W3-URETIM/SUBCONTRACT, W3-LOJISTIK/EXIT-EVIDENCE, W2-BELGE/MIKRO-EDOC, T2/WP8 |
| T2/WP3 | P1 / K3 | SOURCE_IMPLEMENTED_LOCAL | — |
| T2/WP8 | P1 / K3 | SOURCE_PARTIALLY_IMPLEMENTED_LOCAL | T2/WP3 |
| T2/WP10 | P1 / K2 | DECISION_REQUIRED | — |
| T2/WP-B1 | P1 / K2 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP-B2 | P1 / K2 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP-B4 | P1 / K2 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP-B5 | P1 / K2 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP-C1-LEDGER | P1 / K3 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP-C2 | P1 / K3 | DECISION_REQUIRED | T2/WP-C1-LEDGER |
| T2/WP-C3 | P1 / K3 | SOURCE_REVIEW_REQUIRED | T2/WP-C1-LEDGER |
| T2/WP-C5 | P1 / K2 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP-C7 | P1 / K2 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP-C8 | P1 / K2 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP-C9 | P1 / K3 | CONTRACT_REQUIRED | T2/WP-B2 |
| T2/YOL-D40 | P1 / K3 | SOURCE_PARTIALLY_IMPLEMENTED_LOCAL | T2/WP8 |
| T2/CREDIT-GROUPS | P1 / K3 | SOURCE_REVIEW_REQUIRED | — |
| T2/SMALL-GAPS | P1 / K3 | SOURCE_PARTIALLY_IMPLEMENTED_LOCAL | — |
| T2/RELEASE | P1 / K3 | DEPENDENCIES_PENDING | T2/WP3, T2/WP8, T2/WP10, T2/WP-B1, T2/WP-B2, T2/WP-B4, T2/WP-B5, T2/WP-C1-LEDGER, T2/WP-C2, T2/WP-C3, T2/WP-C5, T2/WP-C7, T2/WP-C8, T2/WP-C9, T2/YOL-D40, T2/CREDIT-GROUPS, T2/SMALL-GAPS, W5-RAPOR/WP14-COVERAGE |
| W5-RAPOR/PROACTIVE | P1 / K3 | DECISION_REQUIRED | — |
| W4-MUHASEBE/EQUIPMENT-LINK | P2 / K3 | DEFERRED_BY_DECISION | — |

`IMPLEMENTED_LOCAL` inceleme/merge anlamına gelmez. `SOURCE_REVIEW_REQUIRED`, kaynak giriş noktaları bulunmuş fakat paket uygulama incelemesi/kabulü bitmemiş demektir. `REPRODUCTION_REQUIRED` doğrulanmış canlı hata değildir. Bağımlılık ve karar çözülmeden READY yapılmaz.

## Açık kararlar: tam ERP’yi bekletmeyen dar kapılar

İlk uygulama sözleşmelerini açan sorular:

1. **Siparişsiz/acil alış:** tür bazında sonradan kontrolü hangi mevcut görev yapacak? Öneri: teslim sahibi fiziksel gerçekleşmeyi kaydetsin; ticari kontrol ALMM, fatura farkı MUHM; gider/yatırım için ayrıca somut tür eşlemesi verilsin. Bu rol ataması değildir.
2. **Mali kullanım amacı:** ilk canlı kapsamındaki amaçlar ve gerçek Mikro hesap eşlemeleri hangileri? Öneri: şirket bazlı dar MUHM onaylı liste; bilinmeyen eşleme fiş oluşturmasın. PDF’deki örnek hesaplar sabit alınmasın.
3. **Birim kapsamı:** çelik kg/adet dışındaki sarf, varlık ve hizmet birimleri ilk kullanımda hangileri? Öneri: mal kendi onaylı birimiyle, hizmet fiziksel stok dışında; çelik motorunun birim kuralları korunarak.
4. **Mikro:** ürün/sürüm ve kullanılacak örnek aktarım/sonuç dosyası nedir? Öneri: önce kontrollü dosya ve çift kayıt üretmeyen mutabakat; bağımsız adapter gerçek sürümle doğrulansın.

Kişi–şirket–rol listesi ve gerçek kurulum verisi UAT/aktivasyon kapısında gerekir. Leasing KDV eşlemesi, pilot komut/ölçütler, yeni proaktif eşikler ve geçiş tarihi/RPO/RTO ilgili paket zamanı topluca kesinleştirilir; mevcut kilitli süreler ve onaylı kredi grupları yeniden sorulmaz. ShareId tekilliği önce kaynak/karar araştırmasıdır; insan sorusu diye peşinen kabul edilmez. Tüm kapılar JSON `open_questions` içinde aşamasıyla bulunur.

## Work/AI ve Tren-2 kabulü

Work olayları owner modülün deterministik sorgu/olaylarından gelir; aynı kayda ikinci görev sistemi kurulmaz. AI kapalıyken kutu, hatırlatma ve istisna takibi çalışır. AI özet/analiz/taslak hazırlar; AI-D25 kapalı listesine dokunulmaz. Yeni proaktif tür, mevcut tüketici/zamanlayıcıdan genişletilir; eşik, sorumlu, tekilleştirme anahtarı, kapanış olayı ve başarı/durdurma ölçütü olmadan açılmaz.

Tren-2 devir belgesindeki “yazıldı” sınıfı mevcut entegrasyon kabulü sayılmaz. WP3/8/10, B1/2/4/5, C1 kalıcı defter, C2/3/5/7/8/9, YOL-D40, kredi ve küçük açıklar ayrı paketlerde tutulur. Yazılmış WP1/2/4–7/9/11–16 ve AI temel/B3/C6/C10 tekrar geliştirilmez; kaynak bağlantısı ve ilgili kabul kapanışta doğrulanır.

**Kapanış kanıtı eksikleri:** 64 onay türünün her birinde listeleme+owner karar+yetki+SoD+replay matrisi; gerçek ana sayfa sayaçları; tüm PDF ve rapor çeşitleri; genel liste dışa aktarımı; 311 izin için olumlu/olumsuz ekran ve hassas veri kabulü. Bunlar tamamlandı diye işaretlenmedi. C4, Y-D ve donanım kapsam dışı kalır.

[64 akışın kaynak/test gezinme haritası](WORK_ONAY_KAPSAM_HARITASI_v1.json) bu incelemenin başlangıcıdır. Satırlardaki rol ve süre hücreleri eski taslağın aktarımıdır; yürürlükteki owner kuralı veya güncel süre politikası yerine kullanılamaz. `NOT_VERIFIED` alanları tek tek kapatılmadan bütün akışlar tamamlandı denmez.

## Doğrulama ve yeniden kullanım

[50 referans giriş noktası](REFERANS_KOD_KATALOGU_v1.md) yalnız yaklaşan paketlerin ihtiyacını karşılar. Her paket önce ne kadarının zaten var olduğunu okur; farkı geliştirir. Değişmemiş algoritmanın testleri yeniden yazılmaz; yeni kullanımın şirket, yetki, port, sürüm ve olay tekilliği test edilir.

Bu çalışmada ERP derleme/SQL/tarayıcı veya yeni CI koşulmadı. Eski CI `45164205956`, `9e6107ff3…` üzerinde 22/22 başarılıydı; bu tren head’inin CI kanıtı değildir. Kaynak/plan/parite doğrulayıcısı ve hafif doküman kontrolleri ayrıca raporlanır.

Teslim kapıları değişmez: ilgili SQL/tarayıcı/başarısızlık senaryosu, exact-head incelemeler, zorunlu CI, main tazeliği. Açık P0/P1/P2 olan K3 onaylanmaz. Aktivasyon ve işletme kabulü insandadır.

Yerel ilk kontrol: kaynak/plan/parite/64 akış haritası doğrulayıcısı exit 0; kendi 6 negatif testi başarılı; backlog doğrulayıcısı ve 64 testi başarılı; bootstrap doğrulayıcısı başarılı, 56 testin 47’si geçti ve 9’u mevcut dosya sistemi/symlink koşulları nedeniyle atlandı. Yeni skip eklenmedi; bu sonuç `skip0` kabulünün yerine geçmez. ERP işlev kodu, migration, pin veya workflow değiştirilmedi; yalnız doküman ve referans denetleme aracı hazırlandı. Bağımsız inceleme ve entegrasyon bekliyor.

## Geliştirme devri — 26 Kasım

Sinan’ın açık talimatıyla Claude kapasitesi yokken frontend dahil geliştirme Codex tarafından yürütülür. Mevcut 41 paket korunur; dosya adayları ile gerçek `change_files` lease’i JSON’da ayrıdır. Ana backlog’a ve grant/aktivasyona dokunulmaz.

İlk teslimler: `W3-URETIM/INPUT-PIECES` (`c48f96c91`) ve `W2-SATINALMA/MULTILINE` (`c98ede21d`). Yerel hedefli kanıtlar JSON `execution` alanındadır; gerçek SQL, browser, pin, hosted CI ve bağımsız review bekler. Bunlar üretim kabulü veya tren kapanışı değildir. Son kontrolün CUTOVER, Work şema sırası, açık karar ayrımı ve sonraki kapsam bağlantıları aynı 41 pakete işlendi.

`T2/WP-C1-LEDGER` kaynak teslimi: `b3742be08`. Mevcut AI bağlamında ayrı `ai` şeması, atomik durum/olay ve sürüm CAS bağlandı. 94 hedefli test geçti (0 skip); EF model farkı yok. Dört SQL kabulü yazıldı, koşulmadı. Pin/tam CI/K3 kapanışta bekler; AI kapalıdır.

`T2/CREDIT-GROUPS` kısmi teslimi: `267d08ec1` / `f45d0ce05`. Dört onaylı alan kapısı ve leasing kaynak KDV’si mevcut plan/hareket/muhasebe zincirine bağlandı. Finans 1876, muhasebe kredi 9, frontend kredi 22 test yeşil; SQL/CI/K3 ve kalan grup ayrıntıları bekler. WP3 yönetim kararı ROL-D27 ile kaydedildi; ek onay ve otomatik grant yok.

`T2/WP3` kaynak teslimi: `4d39b9287`. ROL-D27 kendi vekâleti ve ayrı şirket yönetim izni, süre/zincir/SoD ve atomik audit ile uygulandı. Work 169, Identity 866, ilgili API 18 test geçti (0 skip). Yeni izinle güncel sayı 312/17 Critical; GM/SYS satırı yalnız öneridir. Dört SQL testi yazılı ve derlenmiş, NOT_RUN. Vekâlet kapalı; WP8 ekran/owner komutu bağlantısı, SQL/pin/CI/K3 bekler.


`T2/WP8` ilk kaynak teslimi: `bb453a75f`. YOL-D41'in dört ekransız türü mevcut Inventory/Finance karar komutlarına bağlandı. Work yönetiminde mevcut kural tablosu ve geciken/sahipsiz bileşenleri, WP3 oluşturma/iptal ekranıyla birlikte kullanılır. Vekâlet yeni yetki kazandırmaz; owner yetki kontrolünde tekrar ölçülür. Kasa sahibi, mevcut transaction helper'ında yazım sonrası/commit öncesi yetki kaybında karar/receipt/audit'i geri alır.

Yerel kanıt: Finans 1876, Inventory 29, API 13, frontend 452 test; toplam 2370, atlanan 0. `tsc -b`, lint, sınırlı staged secret scan ve referans/parite kontrolü temiz. İki gerçek SQL testi yazıldı ve derlendi, **NOT_RUN**. Sayı 312 izin / 17 Critical; rol/grant ataması yok. Yeni migration yok. Shell authority/OpenAPI/guard/recovery pinleri kapanışa bırakıldı; genel host/runtime kabulü bu nedenle ileri sürülmez.

WP8 **kısmi kaynak teslimidir**: diğer owner türleri ve 64 akış kanıtı, vekâlet geçmişinde okunur kişi adları ve gelen vekâletlerin tam sayfalama deneyimi devam eder. SQL, gerçek API ile tarayıcı, tam CI ve iki K3 incelemesi bekler. Satış kaynak/finans kapanış soruları SAT-D478/479 ile cevaplandı; owner bağlantılarının uygulaması sürer.


27 Kasım ek teslimi: SAT-D478/479 ve DEC-SAL-001 sicil eşliği, normal satış kapanışının kaynak payı ve geçerli mahsup koşullarını kaydeder. Banka mutabakatı bağımsızdır; bütün eksenler ve insanın kapatma komutu korunur.

`T2/WP8` kural düzenleme kaynak teslimi: `ea68692d1` / `99e7e7aed`. Mevcut tür bazlı PUT komutu, strict decoder ve durable işlem günlüğü kullanıldı. Owner onay izni salt okunur; süre sınıfı, yükseltme izni ve gerekçe beklenen sürümle kaydedilir. Belirsiz sonuçta aynı anahtar/payload, şirket/oturum ayrımı ve stale conflict korunur. Son frontend ağacı üzerinde 53/53 test, TypeScript ve scoped lint geçti; mevcut sayfanın Fast Refresh uyarısı sürer. Yeni izin veya atama yok; 312/17 eşliği korunur.

`W2-SATIS/CLOSURE-AXES` kısmi backend teslimi: `01697d4f7`. Finance, bütün fatura taksitlerinin geçerli kayıt/mahsup/ters kayıt/iade ve mevcut bakiye devri kanıtını salt okunur porttan sunar; yeni para hareketi üretmez. Finans 1898/1898 test geçti, atlanan 0. İki SQL testi yazıldı ve API integration projesi 0 hata/0 uyarıyla derlendi; gerçek SQL **NOT_RUN**. Sales fatura/sevk kaynak kümesi ve eksen kompozisyonu henüz bağlı değildir; `NoSource` kapanış engeli korunur. Kaynak ekseninde henüz siparişleşmemiş talep ve ortak belgenin sipariş payı özellikle kapsanacaktır.

SQL, gerçek API ile tarayıcı, pinler, tam hosted CI ve iki bağımsız K3 incelemesi bekler. Bu teslimler tren/main kabulü değildir. 41 paket ve 50 referans korunmuştur; yeni paralel backlog oluşturulmamıştır.

27 Kasım devamı — satış kapanışı: `8e4d4cb12`, `0577612d4` ve `345c427b6`. Yukarıdaki önceki kısmi teslimin aksine normal siparişin kaynak/fatura/finans kompozisyonu artık bağlıdır. Mevcut Sales fatura/sevk claim'leri, Logistics fiziksel sevk kanıtı, Inventory kaynak hareketi/tahsis, Manufacturing açık plan ve Finance mahsup portu yeniden kullanılır. Purchasing portu, henüz siparişleşmemiş talep payını da okur; ortak alış belgesinin başka sipariş payı kapanışı bekletmez. Kaynak kompozisyonunun 21 ve Purchasing portunun 12 hedefli testi geçti (0 skip). Fason hizmet faturasının ayrı kanıt ailesi hâlâ `NoSource`; paket bütünü kabul edilmiş değildir. Dört ek kapanış ve iki Purchasing SQL senaryosu kaynakta yazıldı; SQL **NOT_RUN**. Kapanış SQL kaynağında gerçek Sales/Finance/Purchasing ile açıkça sentetik downstream sınırları ayrılır; tam uçtan uca kabul iddiası yoktur.

WP8 vekâlet görünümü: `19cec31b5`, mevcut yetkili sayfadaki kişilere sınırlı Identity ad çözümü ve gelen vekâletlerde tam sayfalama ekler. Agent'ın `b61a351b0` frontend kaynağında 66/66 test, TypeScript ve scoped lint geçti; gerçek tarayıcı kabulü değildir. Altı HTTP testi denendi ancak shell authority pinleri henüz kapanışta yenilenmediğinden host başlayamadı. Gate gevşetilmedi; bu testler **kabul edilmedi**. Satışın mevcut onay/ret komutlarına WP8 bağlantısı devam ediyor; YOL-D23 maker/submitter ayrılığı korunuyor.

Cari bakiye devri replay: `69ffd6ccb` ve `264975943`. Özgün correlation kimliği mevcut immutable komut makbuzunun JSON'una eklendi; replay başlığı buradan döner. Yeni tablo, migration, ikinci finansal hareket veya frontend response alanı yoktur. Eski makbuzda olmayan kimlik tahmin edilmez. Hedefli 5/5 test geçti (0 skip); HTTP/SQL kabulü ve diğer SMALL-GAPS işleri bekler. Eski “migration gerekir” varsayımı bu somut uygulama için geçerli değildir.

C: dolu olduğu için yeni çalışma ve build çıktıları D: üzerindeki ayrı checkout'ta sürer. C: çalışma alanları ve birleşmemiş WIP silinmedi. Ağır işler tek kaynak kilidiyle yürütülür; pinler, gerçek SQL, hosted CI, tarayıcı ve bağımsız K3 kabulü kapanış kapısında kalır.

CUTOVER mevcut bağımlılıklarına ilk kullanım Work kabulünü temsil eden `T2/WP8` eklendi (`WP3` zaten onun ön koşulu). Genel sohbet ve ileri AI, canlı geçiş ön koşulu değildir.

WP8 Sales kaynak eklemesi: `8882e4ecb` (`6405e21db` agent kaynağı). Sipariş ve fatura native onay/ret, terminal kayıt metadata/replay ve approve-only purpose okuma bağlıdır. YOL-D23 creator/submitter yasağı eski native istisnanın yerine uygulanır; yalnız editöre ek dışlama yoktur. Yeni izin/grant yok. Agent frontend kaynağında 25/25 test, tsc ve scoped lint geçti; shell mount kayıt düzeltmesi ve backend geniş Sales regresyonu bekler. Bu kaynak eklemesi 64 akışın runtime kabulü değildir.

27 Kasım ek kabul ayrımı: Sales mevcut birim testlerinin tamamı `f4ccda30f` kaynağında 2440/2440 geçti (0 skip). WP8 native karar ve shell mount bağlantısı yazıldı; yetkili gerçek API/tarayıcı, SQL, pin ve CI kabulü bekler. YOL-D40 `a30b3ee9f`/`cb9119f7d`/`481fc3e72`: eski öneri Bayatladı olarak immutable kapanır, aynı işlemde outbox/audit/komut makbuzu oluşur, mevcut Work özetinde hazırlayana türü belli bildirim gider. 62 hedefli frontend testi (0 skip), tsc/lint agent kaynağında geçti; yeni hazırlama UI eki sürüyor. EF migration’ları araçla üretildi; gerçek SQL/CI/K3 hâlâ NOT_RUN/PENDING.

LOJ-D21 kısmi kaynak teslimi `e719f413f`/`7e3faaaa3`: şirket+ShareId filtresiz SQL tekilliği, iptal sonrası kimliğin korunması ve terminal replay yarışı mevcut altyapıya bağlandı. EF10.0.8 ile üretilen migration, eski çakışmada 52350 ile durur; geçmiş hareket varken Down 52351 ile reddedilir. Model testleri 24/24 geçti (0 skip); beş SQL senaryosu kaynakta, gerçek SQL ve geçmiş kayıt kontrolü NOT_RUN. YOL-D40 yenileme UI eki `92bdc40fb`: mevcut komutla yeni öneri, eski kapanışın korunması, aynı belirsiz isteğin güvenli tekrarı; değişen ekran dosyasının tüm 29 testi agent kaynağında geçti (0 skip).

Yerel birleşik kontrol: API test projesi yeni SQL kaynaklarıyla derlendi; owner/Work/stale tüketici hedefinde 44/44, Work model/kayıt hedefinde 17/17, Muhasebe model hedefinde 39/39 geçti (tümü 0 skip). Logistics/Work/Accounting için EF pending-model kontrolü exit0. Bunlar gerçek SQL veya HTTP/tarayıcı kabulü değildir. `db40e1fe2` küçük UI eki: eski WP6 açılış maliyeti metinleri kaldırıldı; aktif WP5 owner/metin korunuyor; 25/25 test ve tsc/lint agent kaynağında geçti. Owner ayrı ret kodu vermediği için plan üstü sevk uyarısı gerçekte söylediği onaylı/yeterli ek miktar eksiğini açıklar.


İkinci sevkiyat risk kaynağı `c54147e79`/`51a59cf5d`: Finance açık alacağı ve işlenmiş fatura kümesi aynı Serializable okumada, Sales native kesinleşmiş fatura/pay geçmişiyle bağlandı. Mevcut hesap motoru ve resmi ters kayıt yolu korundu; tamamen mahsup edilmiş fatura tekrar faturalanmamış sevk sayılmaz. Agent `4c3945058` kaynağında tam Sales 2447/2447 geçti (0 skip); bütün Sales/Finance kaynakları ve Sales test ağacı birleşik dalda aynı. API integration projesi yeni SQL kaynağıyla derlendi. Gerçek SQL, normal Logistics→Sales→Finance zinciri, tarayıcı, tam CI ve K3 bekler; paket bütünü kabul edilmedi. Değişen test blobu nedeniyle REF-OPENITEM-01 eski CI ağacı eşliğinden kaynak/test kanıtına indirildi; eski kanıt yeni kullanım kabulü sayılmadı.

Risk SQL kaynak eki `e3d7c806a`: mevcut Finance native kayıt/mahsup/mahsup geri alma/tahsilat ters kayıt fixture’ı kullanılarak iki senaryo daha eklendi. Tam mahsupta alacak sıfır olsa da fatura işlenmiş kalır. Mahsup geri alma tek başına mevcut tahsilatı yok etmez (SAT-D52); yetkili onaylı tahsilat ters kaydı borcu tekrar riskte gösterir. Toplam üç risk SQL senaryosu derlendi (0 hata/0 uyarı), gerçek SQL **NOT_RUN**; tam ikinci sevk zinciri kabulü açık.

Hızlı cari kısmi kaynak teslimi `0dc6182ab`: alış, satış ve manuel banka ödeme başlangıcı, mevcut Cari formu/komutu ve yetkili owner sorgularını kullanır. Şirket hesabı yalnız tam kimlik eşleşmesiyle seçilir; belirsiz işlem aynı anahtarla korunur ve ana formun gönderilmesi engellenir. Beş etkilenen dosyada 101/101 test (0 skip), tsc ve scoped lint geçti (12 mevcut uyarı). Native DTO’da VKN alanı, ayrı oluşturma talebi servisi ve hazır bağımsız gelen belge/tahsilat girişi bulunmadığı için bunların kabulü tamamlanmadı. Yeni rol/grant, API veya migration yok; gerçek tarayıcı/CI kabulü bekler.

WP8 satınalma talebi backend eki `25835fe7a`: owner native onay/ret ve amaçlı snapshot bağlandı. Native maker/gönderen yasağı, 4000 karakter ret ve terminal replay korunur; save sonrası/commit öncesi yetki veya vekâlet kaybında karar/attempt/audit rollback olur. Agent `84b4d2343` kaynağında Purchasing 1241/1241 ve Work host kompozisyonu 27/27 geçti (0 skip). Üç SQL kaynağı derlendi, gerçek SQL **NOT_RUN**; authority/delegation sınırı sentetik olduğundan gerçek kullanıcı HTTP/tarayıcı kabulü sayılmaz. Purpose route ve shell bağlantısı ayrı artımda; yeni permission/grant veya migration yok.

WP8 satınalma talebi purpose UI eki `4dc4136b3`: Work kutusu ve yeni amaçlı shell route yalnız mevcut taleponay izniyle aynı generic ekrana bağlandı. Genel satınalma liste/create/price yetkisi verilmez. Native zorunlu ret ve 4000 karakter sınırı, sürüm/aynı anahtarla retry korunur. Altı dosyada 55/55 test (0 skip), tsc ve scoped lint (0 uyarı) agent kaynağında geçti; tüm frontend ağacı birleşik dalda aynı. Yeni C# source-link/provider/declaration testleri bu artım için henüz koşulmadı; önceki1241/27 sonucu yeni declaration kabulü değildir. Hash/pin, gerçek SQL/HTTP/tarayıcı, CI ve K3 bekler.

27 Kasım devamı: fason owner kapanış bağlantısı `5f60d107e`, ayrı Finance `OfficiallyReversed` kanıtı `0d2324dc6`–`9edcbed9f` ve tarihsel iptal eşlemesi `5a1644b2a` bağlandı. Native para/hareket motoru yeniden yazılmadı; iptal ödeme sayılmaz. Finance 1934/1934, normal fatura karar kapısıyla Sales 2450/2450, satınalma purpose kaynak değişiklikleriyle Purchasing 1241/1241 yerel test geçti (0 skip, kaynak/head kapsamları JSON’da). Birleşik 83 host testinin 82’si geçti; değişken yanlış-scope fixture’ı bir testi kırdı. `119191f70` bütün eski iddiaları koruyarak düzeltir ve iki negatif vaka ekler; yeniden koşu bekler. İlk hata makbuzu saklandı. Yeni native SQL kaynakları derlendi, gerçek SQL **NOT_RUN**.

WP8 fason fatura purpose/lifecycle kapısı `27bce08f2`, ordinary invoice replay `a02def7b2` ve ambient transaction reddi `ef8e363c5` mevcut komut, izin ve journal’dan genişletildi. Fason frontend 47/47; son transport dosyası 9/9, tsc/lint temiz. Bu C# artımları henüz doğrulanmadı. `89296ec85`/`d28d94520` onaylı nakdi kredi kaynak-plan/vade farkını uygular; Finance 1951/1951 geçti, grup bütünü tamamlanmadı. Evrakın belgeli serbest bırakma sonrası yeniden kullanımı için dar FNS-D126 sorusu bekliyor; bağımsız işler sürüyor. 41 paket/50 referans korunur; SQL, gerçek HTTP/tarayıcı, pinler, tam CI ve K3 kabulü bekler.

27 Kasım sonraki doğrulama: `6a8480eff` üzerinde Purchasing **1260/1260**, kaynak/Work kompozisyonu **121/121** geçti (skip0); ikincisi 58 satış kapanış vakasını da içerir. Önceki yanlış-scope fixture ve service snapshot fixture hataları korumalar gevşetilmeden düzeltildi, başarısız loglar saklandı. Normal HTTP host authority pin hatası kapanmadı; pinler tren kapanışında üretileceğinden **NOT_ACCEPTED** kalır. Finance **1986/1986** (`aa10817d3`) ve Sales **2469/2469** (`324b9da8c`) ayrı kaynak anlarının yerel kanıtıdır; güncel tam CI veya SQL sonucu değildir.

WP8'e kredi, alış siparişi ve hizmet siparişi amaçlı kararları eklendi. Alış ve hizmet siparişinde native ret yoktur; hizmet onayı kullanıcının gerçek gerekçesini taşır (native sınır 1000). `0d0197c3b` hizmet arayüzü, agent `3d89fe017` kaynağındaki 11 dosyayla aynıdır: **98/98** test, tsc/lint temiz. Önceki alış siparişi arayüzü **85/85** geçti. Native sipariş/servis/kredi komutlarının yetki, vekâlet, işlem ve audit korumaları genişletildi; SQL senaryoları yazılı/derlenmiş, **NOT_RUN**. On bir bağlı kind, 64 akışın kabulü demek değildir. Gelen fatura farkı ve mal kabul karar koruması sıradaki artımlardır.

ROLE-UX `303f9ba4a`–`6a8480eff`: mevcut rol kataloğundan çoklu seçim taslağı ve izin önizlemesi, ilk rol atamasında da aynı native tek-rol formuyla çalışır. Her rol ayrı insan işlemi; Critical talep etkin yetki değildir. Okunamayan efektif izinler boş varsayılmaz. **35/35** test, tsc/lint temiz; altı dosya agent `1edea19c9` ile aynıdır. Gerçek şablon/kişi ataması ve tarayıcı kabulü bekler; 312/17 paritesi atama kanıtı değildir. C9 için A5 sayım portu hazırdır, fakat doğrulanabilir model çıktı kimliği gerekir; bu nedenle mevcut B2 bağı korunur. Saklama zaten K10/No159'da ayrı 90 gün ailesidir; Work notu/hafıza/komut defteri yerine kullanılamaz. Kalıcı sözleşme ve migration kapsamı K3 olarak düzeltilmiştir; yeni paket veya işletme kararı oluşturulmadı.

Bootstrap kontrolü (`b067dac5f`): 56 vakadan 47'si geçti, 9'u atlandı, hata yok; skip0 kabulü değildir. Beş dosya ve üç dizin sembolik bağlantı testi bu Windows oturumunda bağlantı oluşturamıyor; bir test aynı dizinde iki farklı harf büyüklüğüne sahip dosya adı gerektiriyor. Mevcut CI bu grubu Ubuntu'da çalıştırıyor; yeni terminal CI kanıtı **NOT_RUN**. Uygulama/SQL kabul testleriyle karıştırılmaz, hiçbir test veya gate değiştirilmedi.

27 Kasım karar/owner artımı: gelen fatura farkı ve mal kabul partisi Work kaynak/arayüzleri native komutlara bağlandı. 13 bağlı kind, 64 akışın kabul sayısı değildir. Purchasing **1296/1296** (f1e7d5269), Manufacturing **1551/1551** ve hedefli Work owner kompozisyonu **115/115** (c56d32a45) geçti, skip0. Yeni SQL kaynakları derlendi; gerçek SQL **NOT_RUN**. Önceki hatalı fixture ve authority-pin koşuları saklandı; normal HTTP pin kabul eksiği kapanmadı. Manufacturing purpose/native ret hazırlık kaynağı vardır; capability=false, Work onay/yeniden onay ve gerekli açık etiket seçimi henüz bağlı değildir. Tam CI, pinler, tarayıcı ve iki K3 bekler.

Hızlı cari vergi kimliği artımı f3ebf4963: mevcut ayrı EnterTaxIdentity komutu ve native mükerrer uyarı/teyidi kullanılır; yeni atomic create-with-tax veya kesin tekillik iddiası yoktur. Yedi dosyada **120/120**, tsc/scoped lint temiz; altı mevcut uyarı. Önceden mükerrer kontrolü, yabancı kimlik ve yetkisiz oluşturma talebi dahil paket bütünü açıktır. UNIT-SCOPE dar incelemesi kg’siz stok/kabul bağlantısının henüz tamamlanmadığını doğruladı; birim kimlikleri, şirket profili ve fiziksel stok desteği ayrı kabul edilir. Gayrinakdi tazmin/geri ödeme olay yönü için dar kullanıcı sorusu beklenir; Drawdown banka girişi bu amaçla körlemesine kullanılmaz. Yeni paket veya paralel backlog oluşturulmadı.

UNIT-SCOPE ilk kaynak artımı `ee9b40393`: mevcut Settings kataloğuna litre, takım, set, saat, gün ve hizmet kimlikleri eklendi. Eski dört kimlik ve şirket profilleri korundu. Yeni kimlikler fiziksel adet dönüşümüne düşmez; desteklenmeyen fiziksel anlam açıkça Unavailable kalır. Şirket profili, ölçek, aile varsayılanı veya dönüşüm etkinleştirilmedi. Settings **886/886** yerel test geçti (skip0); 34 yeni senaryo dahildir. Product/Purchasing/Inventory sahip bağlantıları, gerçek aile/birim kurulum verileri ve ilk kullanım kabulü hâlâ bekler; bu artım kg’siz fiziksel stok desteğinin tamamı değildir.

27 Kasım WP8 üretim artımı: `6d3af96b4`/`3a85da179` native ilk onay ve açık etiket seçimiyle yeniden onayı bağlar; `b593dec13`/`f72ef515e`/`94331601f`/`dcd641d6a` purpose ve arayüzü tamamlar. Önceki staged/capability=false notu bu kaynak artımıyla aşılmıştır; ortam aktivasyonu yapılmadı. **14 bağlı kaynak türü, 14 kabul edilmiş akış değildir.** Manufacturing **1560/1560**, Work owner kompozisyonu **157/157** (3a85da179, skip0); frontend **128/128**, tsc/lint exit0 (b0272a054 ile bütün frontend ağacı aynı). Yayımın son C# kontrolleri bekler. `874a78e54` stok kayıp kararında owned transaction, native replay ve audit/yetki güvenliğini güçlendirir; Work purpose henüz bağlı değil, testleri bekler. İki üretim ve sekiz stok SQL kaynağı envantere alındı; gerçek SQL, HTTP/tarayıcı, tam CI ve K3 **NOT_RUN/PENDING**.

27 Kasım son yerel kontrol: `9bef612e9` üzerinde Inventory **2266/2266**, Manufacturing **1560/1560**; `255453f72` üzerinde API owner kompozisyonu ve üretim declaration **158/158**, tamamı skip0. SQL kaynakları derlendi; SQL çalışmadı. Üretimde gerçek capability adının çoğul olduğu düzeltildi (`255453f72`), açılış maliyeti shell kataloğu tamamlandı (`bff583b9c`). Frontend bütün ağaç agent `0ec55126e` ile aynı: **160/160**, tsc/lint exit0; bir mevcut lint uyarısı. İlk başarısız koşular saklandı. `2c35e5842` stok kayıp talebini numara/tarih ve gerçek native komut makbuzu kanıtıyla bağlayan staged backend; `fb1312d62` typed UI adayıdır. Yeni backend testleri bekler, capability=false/eski bağlantı sürer. Locator ve geçmiş okumasının 1000 kaydı aşması açık503 verir; ölçek ve gerçek kullanıcı kabulü bekler. Bunlar 64 akışın kabulünü kapatmaz.

27 Kasım Work kaynak artımı: stok kayıp purpose bağlantısı `c1152cbc2`/`1349d982c` ile kaynakta hazır; önceki staged notu aşıldı, ortam/rol aktivasyonu yok. **15 kaynak türü, 64 akışın kabulü değildir.** `60380ae37` eski talebin doğru maker ve canlı vekâletiyle yalnız gerçek makbuz tekrarını korur; güncel kendi talebine karar ve diğer dışlamalar kapalı kalır. Önceki Inventory **2275/2275** (30b81a53a), son frontend **170/170**, tsc/lint exit0 (c900c9997; bütün ağaç aynı). Son `2ba0032c8` kaynağında Inventory **2275/2275**, Counterparty **386/386** geçti (0 skip). API test derlemesi mevcut olmayan SqlServerTheory nedeniyle durdu; `19a6351ee` aynı 12 senaryoyu mevcut SqlServerFact ve ortak test gövdelerine bağladı, API tekrar kontrolü bekler. `cdc4aac0c` cari vergi kimliği/unvan native kararlarında işlem/audit/yetki güvenliğini güçlendirir; 10 unit ve 12 SQL vaka kaynağı yazıldı, iki Work adaptörü henüz bağlı değil. Gerçek SQL/HTTP/tarayıcı, pinler/tam CI ve iki K3 bekler.
