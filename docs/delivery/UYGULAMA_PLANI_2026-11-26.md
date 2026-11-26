# GöymenERP — uygulanabilir paket planı, 26 Kasım 2026

**Durum:** hedefli kaynak eşleştirmesi ve yerel plan. Yeni bir backlog veya bütün ERP için yeniden tasarım değildir. İşletme kabulü değildir.

Ana kayıt [DELIVERY_BACKLOG_v1.json](../backlog/DELIVERY_BACKLOG_v1.json) olarak kalır. 41 alt paket, mevcut iş kimliklerine bağlanmıştır; ana backlog durumları, sorumluları ve bağımlılıkları değiştirilmedi. Kesin kapsam, mevcut/önerilen dosyalar, referanslar, kaynak bulguları, bağımlılık, sahiplik, risk, teknik/işletme kabulü ve açık kararlar [paket JSON dosyasındadır](UYGULAMA_PAKETLERI_2026-11-26.json).

**Kaynak anı:** main `ddc6cba4e0e172cca60a2dc3d4e339972a08e5f6`; tren-2 `c24706af4de4300d4a171eca11459098d87cd14f`. Bu tarih/sürümdeki bulgular sonraki commitlere otomatik taşınmaz.

## Kesinleştirilen yön ve sınırlar

[26 Kasım kararları](../decisions/SINAN_KARARLARI_2026-11-26_UYGULAMA.md): ROL-D26, ALS-D129, ALS-D130. Kesin fiş düzeltmesinde farklı MUHM; karma alışta mevcut gerekli sipariş onayından sonra bağımsız kısmi gerçekleşmeler; siparişsiz/acil alış ilk kullanımda. Önceki Mikro, mülkiyet, maliyet ve görev ayrılığı kararları açıkça değiştirilmedikçe sürer.

Bu yönler rol ataması, hesap/vergi seçimi, deployment veya AI açma yetkisi değildir. Tartışma metnindeki öneriler otomatik işletme kararı yapılmaz.

## Kaynakta tespit edilen uygulanabilir farklar

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

Codex backend/DB/port ve sözleşme; Claude frontend/tarayıcı; mevcut koordinatör entegrasyon. Aynı dosyada iki yazar yok. Work migration sırası WP3 → WP8 → YOL-D40; AI defteri ayrı şema, C2/C3 ondan sonra. Muhasebe ve Finans şemaları kendi tek yazar kuyruğunda. Pinler en sonda. Bu plan yeni ajan veya çalışma servisi başlatmaz.

## Paket dizini

| Kimlik | Öncelik / risk | Durum | Bağımlılık |
|---|---|---|---|
| W1-YETKI/ROL-D24-PARITY | P0 / K2 | IMPLEMENTED_LOCAL | — |
| W1-YETKI/ROLE-UX | P0 / K3 | SOURCE_REVIEW_REQUIRED | W1-YETKI/ROL-D24-PARITY |
| W1-ORG/BOOTSTRAP | P0 / K3 | SOURCE_REVIEW_REQUIRED | — |
| W1-CARI/QUICK-CREATE | P0 / K2 | SOURCE_REVIEW_REQUIRED | — |
| W1-URUN/UNIT-SCOPE | P0 / K3 | SOURCE_REVIEW_REQUIRED | — |
| W2-SATINALMA/MULTILINE | P0 / K2 | SOURCE_IMPLEMENTED_LOCAL | — |
| W2-SATINALMA/MIXED-MIN | P0 / K3 | SOURCE_REVIEW_REQUIRED | W2-SATINALMA/MULTILINE, W1-URUN/UNIT-SCOPE |
| W2-SATINALMA/NOPO-MIN | P0 / K3 | CONTRACT_REQUIRED | — |
| W4-MUHASEBE/CLASSIFY-MIN | P0 / K3 | CONTRACT_REQUIRED | — |
| W4-MUHASEBE/CORRECTION-MIN | P0 / K3 | SOURCE_REVIEW_REQUIRED | W4-MUHASEBE/CLASSIFY-MIN |
| W4-FINANS/EXPENSE-CATALOG | P0 / K3 | CONTRACT_REQUIRED | — |
| W2-SATIS/CLOSURE-AXES | P0 / K3 | SOURCE_REVIEW_REQUIRED | — |
| W4-RISK/SECOND-SHIPMENT | P0 / K3 | REPRODUCTION_REQUIRED | — |
| W2-ACIKKALEM/LEGAL-IDENTITY | P0 / K3 | REPRODUCTION_REQUIRED | — |
| W3-URETIM/INPUT-PIECES | P0 / K3 | SOURCE_IMPLEMENTED_LOCAL | — |
| W3-URETIM/FIVE-ENGINES | P0 / K3 | ACCEPTANCE_REQUIRED | W3-URETIM/INPUT-PIECES |
| W3-URETIM/SUBCONTRACT | P0 / K3 | ACCEPTANCE_REQUIRED | — |
| W3-LOJISTIK/EXIT-EVIDENCE | P0 / K3 | SOURCE_REVIEW_REQUIRED | — |
| W2-BELGE/MIKRO-EDOC | P0 / K3 | SOURCE_REVIEW_REQUIRED | — |
| W5-RAPOR/WP14-COVERAGE | P0 / K2 | ACCEPTANCE_REQUIRED | — |
| W5-MIGRASYON/CUTOVER | P0 / K3 | HUMAN_ACTIVATION_REQUIRED | W1-YETKI/ROLE-UX, W1-ORG/BOOTSTRAP, W1-CARI/QUICK-CREATE, W1-URUN/UNIT-SCOPE, W2-SATINALMA/MULTILINE, W2-SATINALMA/MIXED-MIN, W2-SATINALMA/NOPO-MIN, W4-MUHASEBE/CLASSIFY-MIN, W4-MUHASEBE/CORRECTION-MIN, W4-FINANS/EXPENSE-CATALOG, W2-SATIS/CLOSURE-AXES, W4-RISK/SECOND-SHIPMENT, W2-ACIKKALEM/LEGAL-IDENTITY, W3-URETIM/FIVE-ENGINES, W3-URETIM/SUBCONTRACT, W3-LOJISTIK/EXIT-EVIDENCE, W2-BELGE/MIKRO-EDOC |
| T2/WP3 | P1 / K3 | SOURCE_REVIEW_REQUIRED | — |
| T2/WP8 | P1 / K3 | SOURCE_REVIEW_REQUIRED | T2/WP3 |
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
| T2/WP-C9 | P1 / K2 | SOURCE_REVIEW_REQUIRED | T2/WP-B2 |
| T2/YOL-D40 | P1 / K3 | SOURCE_REVIEW_REQUIRED | T2/WP8 |
| T2/CREDIT-GROUPS | P1 / K3 | SOURCE_REVIEW_REQUIRED | — |
| T2/SMALL-GAPS | P1 / K3 | SOURCE_REVIEW_REQUIRED | — |
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
