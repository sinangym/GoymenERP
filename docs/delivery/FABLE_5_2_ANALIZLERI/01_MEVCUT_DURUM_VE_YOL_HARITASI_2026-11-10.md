# GöymenERP — Mevcut Durum, Modül Analizi, Çözüm Yol Haritası ve Opus Devri

> **Belge türü:** Fable 5.2 analizi — 1. aşama (salt-okunur inceleme, 10 Kasım 2026).
> **İncelenen sürüm:** `main` = `9091e7d4ca553389fc152bde2cf9e4903a538545`.
> **Durum:** Bilgi amaçlı analizdir. Karar, onay, görev ataması ya da `OPEN-*` kapatma değildir. `KARAR_SICILI.md`, tarihli `SINAN_KARARLARI_*` dosyaları ve feature spec'ler bu belgenin üstündedir. Production, migration, seed, gerçek grant, finans aktivasyonu, merge veya deployment onayı değildir.
> **Kayıt:** Rapor sohbette teslim edildi; 11 Kasım 2026'da kullanıcının isteğiyle gövdesi değiştirilmeden depoya işlendi.
>
> **⚠ Bu raporun bazı tespitleri 2. aşamada düzeltildi.** Uygulamadan önce [02_DOGRULANMIS_GELISTIRME_PLANI_2026-11-11.md](02_DOGRULANMIS_GELISTIRME_PLANI_2026-11-11.md) §1'e bakın. Çelişkide 2. aşama geçerlidir. Başlıca düzeltmeler:
>
> - **F-001 / OP-01:** Mevcut kararlarla yapılamaz; rol atamanın kim tarafından yapılacağı ve ilk yönetici kararsız (E3/E4, OPEN-013). Bölüm J'deki "ilk iş" önerisi bu nedenle 2. aşamada değişti.
> - **F-006 / OP-02:** Lojistik pay kaydında aktör sütunu yok; migration gerekir.
> - **F-008 / OP-03:** Efor büyüktür; soy ağacı modeli yalnız Böl/Birleştir (`[Kind] IN (1, 2)`), model seçimi gerekir.
> - **F-011 / OP-05:** Hesaplayıcı, defter tablosu ve okuma portu var; eksik olan defter yazıcısı, Alış fiyat portu ve Satış bağlantısı.
> - **F-015:** ALS-D3 "hazırlayan ≠ onaylayan" hükmünü açıkça içerir. Aşağıdaki "olası risk" ve "görev ayrılığı hükmü görmedim" ifadeleri yanlıştır; boşluk doğrudan doğrulandı.
> - **F-019 / OP-08:** Taslak PR #1059'da sürüyor.
> - **F-021 / OP-06:** Dağıtıcı çekirdeği merge edildi (#999); Work modülü host'a bağlı değil, kaynak ve tüketici yok.
> - **F-022 / OP-04:** Eksik bağlam üçtür (Ayarlar, Belgeler, Fiyat Listesi); Work dahil değildir.
> - **F-028 / OP-07:** Yalnız host adaptörü (B-04c) eksik; F-005'e bağlı.
> - **F-009:** Plan ret/iptal var (#1023, #1030); üretim emri iptali yeniden doğrulanmadı.

**İncelenen sürüm:** `sinangym/GoymenERP`, `main` = `9091e7d4` (10 Kasım 2026). İnceleme salt-okunurdu; dosya, dal, veri ya da ayar değişmedi, derleme/test/uygulama çalıştırılmadı.

---

## A) YÖNETİCİ ÖZETİ

**Ne yapmak istiyorsunuz.** Demir-çelikte kendi üretimini, ticari malı ve fason hizmeti sipariş merkezli tek sistemde yürüten bir ERP. Grup içinde cari ve ürün kimliği ortak; stok, sahiplik, hesap ve hareket şirket bazında ayrı. Beş üretim motoru ayrı işliyor. Depodaki kararlar (`KARAR_SICILI`, tarihli `SINAN_KARARLARI_*`) bu hedefi taşıyor; hedefle kararlar arasında çelişki görmedim.

**Proje bu hedefin neresinde.** Yatırım sağlam ve korunmaya değer:

- 16 modüllü modüler monolit; şirket filtresi, tekrar koruması (idempotency), sürüm kontrolü ve denetim kaydı tutarlı uygulanmış.
- Bu commit'te CI'ın 22 işinin tamamı başarılı; gerçek SQL Server testleri bunun içinde.
- Ancak **bugün gerçek bir kullanıcı sistemi kullanamaz.** Tarayıcıda gerçek giriş yok. Yaklaşık 160 iznin yalnız 14'ü bir rolde ve rol atayacak komut ya da ekran yok.
- Dolayısıyla "çalışıyor" dediğim her şey test kimliği ve test yetkisiyle çalışıyor.

**Backend'de gerçekten çalışan kısım (bu sürümde testle kanıtlı):**

- Cari açma ve listeleme.
- Satış: talep → teklif → sipariş → onay → revizyon → iptal.
- Fiziksel stoktan tahsis.
- Satış siparişi için yükleme → tartı → irsaliye → sevk → stok çıkışı.
- Satın alma: talep → sipariş → mal kabul (lot, etiket ve tek stok hareketi).
- Elle banka tahsilatı, tahsilatın açık kaleme dağıtımı, cari ekstre, muhasebe dönemi.
- Üretim: beş motorun hesaplayıcıları ve tek proses için plan → onay → emir → girdi → çıktı → kütle dengesi kapanışı.

**Zincirin koptuğu altı yer:**

1. **Erişim:** giriş ve rol atama yok (F-001, F-002).
2. **Satış → finans:** satış faturası hiçbir koşulda kesinleşemiyor, alacak doğmuyor (F-005).
3. **Sevk → satış:** kimin sevk ettiği Satış'a dönmüyor; satır kapatma ve fazla teslim onayı her zaman reddediliyor (F-006).
4. **Üretim:** çıktı etiketinde soy ağacı yok, prosesler zincirlenmiyor, iptal/düzeltme ve termin hesabı yok (F-007…F-010).
5. **Fason:** müşteri malı işlenebiliyor ama müşteriye geri sevk edilemiyor; hizmet bedeli bağı yok (F-012).
6. **Finans ve maliyet:** kasa, borç tarafı ve açılış bakiyesi yok; alış ortalama maliyet defterini yazan kod yok (F-011, F-014).

**Korunacaklar.** Modül sınırları ve sürümlü portlar, şirket filtresi, komut iskeleti, sunucu tarafı yetki ve ekran görünürlüğü, sürümlü üretim motorları, "karar yoksa kapalı kal" yaklaşımı, test envanteri. Yeniden yazma, çatı değişikliği ya da mikroservis için somut bir gerekçe bulmadım.

**İlk iş.** Erişim temeli: Ayarlar → "Kullanıcılar ve yetkiler" ekranı ve arkasındaki rol/atama komutu. Son kararlarınızın tümü yetki vermeyi bu ekrana bırakıyor, ama ekran yok.

**Sizden beklenen kararlar** (ayrıntı bölüm I):

- Giriş sağlayıcısı.
- Rol onay usulü ve ilk yönetici.
- Resmî fatura verisinin kaynağı.
- Fason malın çıkışı ve hizmet bedeli.
- Mamul maliyeti yöntemi.

---

## B) İNCELEME KAPSAMI VE KAYNAKLAR

| Konu | Tespit |
|---|---|
| Depo / dal | `sinangym/GoymenERP`; çalışma dalı `claude/goymen-erp-analysis-1dlpw7` |
| Commit | `HEAD` = `origin/main` = `9091e7d4ca553389fc152bde2cf9e4903a538545` |
| Çalışma ağacı | Temiz; commit edilmemiş değişiklik yok |
| Açık PR'lar | #797, #782, #760 (eski taslaklar); incelenen kod `main` |
| CI kanıtı | "CI" koşusu `42914783507`, bu commit: 22 iş, hepsi başarılı, atlanan yok |
| Diğer koşular (aynı commit) | Cari C-01 gerçek tarayıcı E2E `42914783446`, UI katalog `42914783327`, Cari C006 `42914783586`: başarılı |
| Teknoloji | .NET 10, ASP.NET Core minimal API, EF Core, SQL Server; React 19 + TypeScript, Vite, Vitest, Playwright |
| Yapı | 16 backend modülü + `BuildingBlocks` + `GoymenERP.Api`; ayrı bir AI modülü yok |
| Test envanteri (kaynak sayımı) | 22 xUnit projesi, 5.709 test; 820 gerçek-SQL testi; 355 Vitest dosyası; 88 Playwright dosyası |

CI koşusunun 22 işi şunları kapsıyor: backend derleme ve migration, backend test grupları, üç parça gerçek SQL testi ve RCSI, dört parça frontend testi, OpenAPI kayma kontrolü, recovery/release kanıtları, container derlemesi.

**Yol kısaltmaları**

- `API/` = `src/backend/GoymenERP.Api/`
- `MOD/X/…Katman/` = `src/backend/Modules/X/GoymenERP.Modules.X.Katman/`
- `BB/` = `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/`
- `FE/` = `src/frontend/goymen-erp-web/src/`

**Kanıt düzeyi**

- **[D]** = dosyayı bu incelemede doğrudan okudum.
- **[A]** = alt inceleme raporundan ya da arama (grep) düzeyinde; satır aralıkları yaklaşık olabilir, Opus uygulamadan önce doğrulamalı.

**Yapmadıklarım ve sınırlar**

- Derleme, test, uygulama başlatma, migration ve seed çalıştırmadım. "Çalışıyor" kanıtı, aynı commit'teki CI koşusu ve kod okumasıdır.
- CI testleri yetkiyi test düzeneğiyle veriyor. Gerçek giriş ve gerçek rolle uçtan uca çalıştırma kanıtı yok.
- Cari C-01 dışındaki tarayıcı takımlarının (satış, finans, üretim, lojistik) bu commit'te koştuğunu doğrulayamadım.
- Depo sığ klonlanmış (50 commit); geçmiş analizi sınırlı.
- Okumadıklarım: bazı karar dosyaları (`SATIS_EK44…EK50`, `ALIS_EK11…13`, `K3_DELTA`), Issue #66'nın tamamı, test gövdelerinin çoğu, ekranların tek tek davranışı.
- Yerel Windows/SQL ortamınıza ve production sunucusuna erişimim yok.
- "Bulamadım" dediğim yerler arama düzeyindedir; kesin yokluk iddiası değildir.

---

## C) HEDEF SİSTEM VE KAPSAM

**Kararlardaki hedef** (öncelik: en yeni kararınız > tarihli `SINAN_KARARLARI` > `KARAR_SICILI` > master/spec)

- Modüler monolit; her kritik kayıtta `CompanyId`; bir modül başka modülün tablosuna yazmaz (`AGENTS.md`).
- Stok: sahiplik, emanet, lokasyon ve ticari durum ayrı eksenler; fiziksel stok ile gelecek tedarik ayrı (DEC-STK-001…009).
- Tahsis fiziksel hareket değildir; atomik ve tekrar korumalıdır.
- Üretim: beş motor; tam soy ağacı ve MTC referansı; merkezi, sürümlü, test edilmiş formüller (DEC-PRDCT-001…009).
- Finans: ERP resmî fatura kesmez; resmî belgeyle eşleşince kesinleşir (DEC-FIN-003, SAT-D89).
- Ana menü 13 başlık (KLN-D1); Ayarlar altında Ana Veriler ve Sistem Yönetimi (KLN-D2).

**Sizin bağlamınızla projedeki kararların eşleşmesi**

| Konu | Projedeki son karar | Not |
|---|---|---|
| Temel maliyet | Alış hareketli ortalaması, depo bazında; sahibi Stok (SAT-D256…D265) | Mamul marjı şimdilik "kullanılamıyor" (SAT-D258); temel maliyet hedefinizle aradaki boşluk Soru 5 |
| Yetki verme | İş rolleri kodla tanımlanmaz; kurulumda Ayarlar rol ekranından verilir (SAT-D282, SAT-D287) | Ekran henüz yok (F-001) |
| Entegrasyonlar | İlk sürümde e-belge, banka ve Mikro yok; sonraki sıra belli (OPEN-016) | İfadenizle uyumlu |
| Modül sırası | 3 Kasım'da "Kalan Modüller" şeridi açıldı (KLN-D5) | Cari, Ürün, Stok & Lojistik, Ayarlar artık geliştirilebilir |
| SaaS | Şirket üstü kiracı düzeyi yok | Bugün sorun değil; ileride ciddi iş (F-029) |

**Bilinçli ertelenenler** (eksik saymadım): ileri maliyet/genel gider motoru (OPEN-008), çek kırdırma optimizasyonu, ~15 günlük üretim döngüsü optimizasyonu, İK/bordro, bakım, ileri kalite, sabit kıymet, geniş CRM, tam banka/e-belge/Mikro entegrasyonu.

**Açık kararlar** (kodları aynen; hepsi "KISMEN"): OPEN-004, OPEN-005, OPEN-007, OPEN-008, OPEN-009, OPEN-010, OPEN-013, OPEN-016, OPEN-017, OPEN-018, OPEN-019, OPEN-020.

---

## D) MODÜL DURUM TABLOSU

"Doğrulanmış" etiketi backend düzeyindedir: bu commit'te CI'da geçen birim ve gerçek-SQL testleri. Gerçek kullanıcıyla uçtan uca doğrulama değildir.

| Modül | Hedef | Mevcut durum | Eksik bağlantı | Öncelik | Kanıt |
|---|---|---|---|---|---|
| A1 Organizasyon | Şirket, tesis, depo, lokasyon, iş merkezi | **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış** (ana veri) | İş merkezinde kapasite, takvim, maliyet yok | P1 | `API/Endpoints/OrganizationEndpoints.cs:23-441` [A]; F-010 |
| A2 Kimlik / giriş | Kurumsal giriş, MFA | Backend: **Entegre görünüyor, güncel çalışırlık doğrulanamadı**. Tarayıcı girişi: **Yok / yalnız tasarım** | Giriş ekranı, production kurulum yolu | P0 | F-002 |
| A3 Yetki / rol | Rol ekranından atama, görev ayrılığı | **Kısmi uygulama**: izin denetimi çalışıyor; atama yok | Rol/atama komutu ve ekranı | P0 | F-001 |
| A4 Onay / Çalışma Merkezi | Onay kutusu, iş listesi | Onay kutusu salt-okunur: **Kısmi uygulama**. Work modülü: **İskelet veya mock** | Work çalışma zamanı ve olay dağıtıcısı | P2 | F-021 |
| B Cari | Grup ortak kimlik, şirket bazlı hesap | Açma/listeleme/detay: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış** (C-01 tarayıcı E2E dahil). Düzenleme, pasif, birleştirme, limit, vade: **Yok / yalnız tasarım** | Limit/teminat kaynağı, çift rol, IBAN | P2 | `MOD/Counterparty/…` [A]; F-018, F-027 |
| C Ürün, kalite, ölçü, birim | Ürün/hizmet kartı, kalite, ölçü, birim çevrimi | **Kısmi uygulama**; çalışma zamanı varsayılan kapalı; beş yassı aile, tek kalite, yalnız kg | Çalışma zamanını açma, birim çevrimi, boru/profil ailesi | P1 | F-003, F-004 |
| D Satış | Talep, teklif, fiyat, iskonto, sipariş, onay | **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış** | Risk kontrolü kaynaksız; sevk gerçekleri dönmüyor | P1 | `API/Endpoints/SalesEndpoints*.cs` [A]; F-006, F-018 |
| D Satış faturası | Resmî belgeyle eşleşip kesinleşme → alacak | Taslak/onay: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış**. Kesinleşme: ulaşılamıyor | Resmî belge verisi ve portu | P1 | F-005 |
| E Satın alma | Talep, sipariş, kabul, fatura, iade | Talep/sipariş/mal kabul: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış**. Alış faturası, borç, üç yönlü eşleşme, iade: **Yok / yalnız tasarım** | Finans borç kalemi; satış talebine bağ | P1 | F-014, F-015, F-017 |
| F Stok, etiket, izlenebilirlik | Lot/etiket, sahiplik, soy ağacı, transfer, blok | Lot/etiket/hareket, eksi bakiye engeli: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış**. Transfer ve blok: **İskelet veya mock**. Soy ağacı: **Kısmi uygulama** (yalnız bölme/birleştirme) | Üretim çıktısı soy ağacı, transfer, etiket çıktısı | P1 | F-008, F-013, F-026 |
| G Tahsis | Fiziksel stok ve gelecek tedarikten tahsis | Fiziksel stoktan: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış**. Gelecek tedarikten: **Yok / yalnız tasarım** | Alış satırı ve üretim planı kaynağı | P2 | `…Inventory.Infrastructure/Persistence/InventoryStore.Allocations.cs:70-156` [A]; F-017 |
| H Üretim planlama (ortak) | Plan, onay, emir, fiil, kapanış, termin | Tek proses zinciri: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış**. Kapasite/termin, iptal/düzeltme: **Yok / yalnız tasarım** | Sipariş bağı, takvim, soy ağacı | P1 | F-007…F-010 |
| H1 Dilme ve sarım | Dilme planı ve sarım hesabı | **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış** (hesap + onay kapısı) | Soy ağacı; ikinci kalite/hurda etiket sınıfı | P1 | `…Manufacturing.Domain/Engines/Slitting/SlittingEngineV1.cs`, `Winding/WindingEngineV1.cs` [D] |
| H2 Soğuk hadde | Hadde hesabı | **Kısmi uygulama**: tek paso hesap; motora özel kapı yok | Kapı, zincirleme | P1 | `…Engines/ColdRolling/ColdRollingEngineV1.cs` [D] |
| H3 Boru/profil | Boru/profil hesabı | **Kısmi uygulama**: motor var; ürün ailesi ve üretim profili yok, hesap "kullanılamıyor" döner | Ürün ailesi, birim, profil | P1 | `…Engines/TubeProfile/TubeProfileEngineV1.cs` [D]; F-004 |
| H4 Boyama | Boya hesabı | **Kısmi uygulama**: motor var; boya ürün sınıfı ve renk listesi yok | Ürün sınıfı, liste | P1 | `…Engines/Painting/PaintingEngineV1.cs` [D] |
| H5 Boy kesme | Boy kesme hesabı | **Kısmi uygulama** (gövdeyi doğrudan okumadım) | Kapı, zincirleme | P1 | `…Engines/CutToLength/CutToLengthEngineV1.cs` [A] |
| I Fason ve müşteri malı | Müşteri malı girişi, işleme, iade, hizmet bedeli; dış fason | Üretime giriş koruması: **Kısmi uygulama**. Müşteriye sevk: bilinçli kapalı. Hizmet bedeli bağı, dış fason: **Yok / yalnız tasarım** | Sevk kuralı, sipariş bağı, bedel | P1 | F-012 |
| J Sevkiyat | Yükleme, tartı, irsaliye, teslim | Satış sipariş satırı için: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış** | Tartı hükmü, talimat iptali, diğer sevk türleri | P2 | F-020 |
| K Maliyet ve kârlılık | Temel maliyet, marj | Alış ortalaması: **Kısmi uygulama** (defter ve port var; yazan/okuyan yok). İleri maliyet: **Bilinçli ertelenmiş / ilk sürüm dışı** | Defter yazıcısı, marj, mamul maliyeti | P1 | F-011 |
| L Finans | Cari hesap, tahsilat, ödeme, çek, kasa | Banka tahsilatı/POS, dağıtım, ekstre: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış**. Çek/senet, tedarikçi ödemesi: **Kısmi uygulama**. Kasa, virman, açılış: **Yok / yalnız tasarım** | Borç tarafı, ters kayıt genişliği | P1 | F-014, F-019 |
| L Muhasebe sınırı | Dönem, fiş, mizan | **Kısmi uygulama**: yalnız elle tahsilat fişinden besleniyor | Fatura fişi | P2 | F-028 |
| M Doküman / arşiv | Kayda bağlı belge, merkezi arşiv, çıktılar | Belge kasası: **Entegre görünüyor, güncel çalışırlık doğrulanamadı**. Çıktı: yalnız sipariş PDF'i | Etiket/irsaliye çıktısı, sahip türleri | P2 | `API/Endpoints/DocumentEndpoints.cs:46-202` [A]; F-026 |
| M Dış entegrasyonlar | Kur, e-belge, banka, Mikro | Yalnız TCMB kur istemcisi (kapalı). Diğerleri: **Bilinçli ertelenmiş / ilk sürüm dışı** | Kur ekranı | P2 | F-025 |
| N Pano ve raporlar | Yönetim raporları | **Kısmi uygulama**: üç satış raporu, kontrol merkezi, mizan, ekstre; pano yok | Rapor merkezi | P2 | `API/Endpoints/SalesEndpoints.Reports.cs:31,70,111` [A] |
| O AI | Yetki içinde yardımcı, taslak üretici | **Yok / yalnız tasarım**: sözleşme tipleri ve kapalı panel | Tümü (OPEN-020) | P3 | F-030 |
| P Ortak altyapı | Test, hata yönetimi, işletilebilirlik | Tekrar koruması, denetim, şirket filtresi, CI: **İlgili sürümde test/çalıştırma kanıtıyla doğrulanmış**. Yedek: **Erişim veya kanıt yetersiz** | Olay dağıtıcısı, hazır olma kontrolü, yedek, izleme | P1 | F-016, F-021, F-022, F-032 |

---

## E) UÇTAN UCA AKIŞLAR VE KOPUKLUKLAR

İşaretler: ✅ çalışıyor (backend, testli) · 🟡 kısmi · ❌ yok ya da ulaşılamıyor.

**Akış 1 — Cari + ürün → satış siparişi → onay → tahsis → sevk → finansal takip**

1. Cari ✅.
2. Ürün ve fiyat listesi 🟡: çalışma zamanı kapalıyken fiyatlama 503 döner (F-003).
3. Teklif, sipariş, onay ✅. Risk kontrolü her zaman "kaynak yok" döner (F-018).
4. Fiziksel stoktan tahsis ✅ (yalnız şirket malı).
5. Yükleme → tartı → irsaliye → sevk → stok çıkışı ✅.
6. ❌ Sevk eden bilgisi Satış'a dönmüyor; satır kapatma ve fazla teslim onayı reddediliyor (F-006).
7. Fatura taslağı ve onayı ✅; ❌ resmî eşleşme 503, kesinleşme ve alacak yok (F-005).
8. Tahsilat ✅ girilir ama satış faturasına dağıtılamaz. Açık kalem bugün yalnız Finans'ın CSV içe aktarımıyla oluşur.
9. Sipariş yalnız "zorla kapatma" ile kapanır.

**Akış 2 — Satış ihtiyacı → tedarik → mal kabul → fiziksel stok → tahsis**

- Stoktan karşılama ✅.
- Üretimden karşılama ❌: plan ile satış siparişi arasında bağ yok (F-007).
- Alıştan karşılama: talep/sipariş ✅ → mal kabul ✅ → fiziksel stok ✅.
- ❌ Mal kabul satış talebine bağlanmıyor; gelecek tedarikten tahsis yok. Tahsis ancak mal geldikten sonra elle yapılır (F-017).

**Akış 3 — Ham bobin → beş motor → mamul/yarı mamul → sevk**

- Tek proses içinde plan → onay (hazırlayan onaylayamaz) → emir → girdi → çıktı etiketi → kütle dengesi kapanışı ✅.
- ❌ Çıktı etiketi girdi etiketine bağlanmıyor (F-008).
- ❌ Prosesler arası zincir yok: dilme çıktısı hadde girdisi olarak ancak elle yeni planla kullanılır (F-007).
- ❌ Yanlış fiil düzeltilemiyor, emir iptal edilemiyor (F-009).
- 🟡 Üretim çıktısı etiketinin sevk edildiğini gösteren test bulamadım; lojistik testi alış etiketi kullanıyor (`tests/e2e/logistics-l04/specs/l04-loading-journey.spec.ts:452` [A]).

**Akış 4 — Müşteri malı → fason → üretim fiili → iade/sevk → hizmet bedeli**

- Müşteri malının ayrı bir kabul yolunu göremedim: **Erişim veya kanıt yetersiz** [A].
- Müşteri malının üretime girişi 🟡: "yalnız aynı müşterinin fason planı" koruması var; fason sipariş bağı portu uygulanmamış.
- ❌ Müşteriye geri sevk: 409 `OwnerNotCompany`. Bu bilinçli bir kapıdır; kural kararı bekliyor.
- ❌ Hizmet bedeli üretim fiiline bağlı değil. ❌ Dış fason (bizim malın fasona gitmesi) yok. (F-012)

**Akış 5 — Tahsilat/ödeme → cari etkisi → dağıtım → kalan → düzeltme**

- Banka tahsilatı/POS ✅ → cari ekstre ✅ → açık kaleme dağıtım ✅ → kalan bakiye ve serbest bırakma ✅.
- Dağıtımın ikinci bir para hareketi üretmediği kuralı kodda var (`…Finance.Infrastructure/FinanceAllocationPolicy.cs:51-111` [A]).
- 🟡 Ters kayıt dar kapsamlı. ❌ Kasa, virman, açılış bakiyesi yok. 🟡 Borç tarafı kısmi (F-014, F-019).

**Akış 6 — İptal, revizyon, kısmi gerçekleşme, iade, geri alma**

- Satış: revizyon, iptal, zorla kapatma ✅. Fatura iptali yalnız taslak/onay bekleyen durumda.
- Müşteri iadesi (lojistik) 🟡. Müşteri iade faturası kararlı ama kodda yok (F-019).
- Tahsis serbest bırakma ✅. ❌ Üretim emri iptali. ❌ Tedarikçiye iade. ❌ Yükleme talimatı iptali.

**Kontrol edilen kesişen konular**

| Konu | Sonuç |
|---|---|
| Birim çevrimi | Yalnız kg; çevrim yok (F-004). "Varsayılan katsayı asla" kuralı plan B-07'de yazılı |
| Teorik ve tartılan ağırlık | İki tartı kaydediliyor; farka hüküm verilmiyor (OPEN-004, DEC-STK-009; F-020) |
| Kütle dengesi, hurda, ikinci kalite | Kapanışta denge var; tolerans kataloğu boş (OPEN-005); etiket sınıfı kısmi |
| Soy ağacı | Bölme/birleştirmede var; üretim çıktısında yok (F-008) |
| Şirket ve sahiplik sınırı | Sorgu filtresi ve mimari test var; müşteri malı satışa tahsis edilemiyor (422) |
| Çift işlem | Benzersiz kaynak indeksi ve tekrar koruması var (`…Inventory.Infrastructure/Persistence/InventoryDbContext.cs:348-351` [A]) |
| Eşzamanlılık | Sürüm kontrolü ve etiket sürüm belirteci var |
| Para hassasiyeti ve kur | Yuvarlama kararları kodda (SAT-D261, SAT-D292); yalnız TRY; kur ekranı yok (F-025) |
| Sevk personelinden fiyat gizleme | Sunucu tarafı izin denetimi var; API ve rapor düzeyinde ayrıca doğrulayamadım: **Erişim veya kanıt yetersiz** |
| Yetkisiz onay | Üretim planında ve Satış Destek'te kendi kaydını onaylama engelli; satın almada engel göremedim (F-015) |

---

## F) ÖNCELİKLENDİRİLMİŞ BULGULAR VE ÇÖZÜMLER

Öncelik ölçütüm: **P0** pilot dahil gerçek kullanım mümkün değil · **P1** bütünleşik kullanım için ana akışı kesiyor · **P2** önemli, geçici yolla yürür · **P3** borç/iyileştirme.

### P0

**F-001 — Rol atama yolu yok; izinlerin çoğu hiçbir rolde değil** (A · kapsam eksiği · P0)

- **Kanıt [D]:**
  - `BB/Security/ErpPermissions.cs`: yaklaşık 160 izin.
  - `API/Seed/identity-seed.json`: beş sistem rolü, toplam 14 farklı izin; 33 satış izninden hiçbiri yok.
  - `API/Endpoints/IdentityEndpoints.cs:18,57,72,90,105`: yalnız okuma uçları.
  - `FE/identity/capabilities.ts`: yazma yeteneği bilinçli olarak yok.
  - `MOD/Identity/…Domain/Approvals/AccessGrantRequest.cs`: erişim talebi modeli var, ucu yok.
  - `MOD/Identity/…Infrastructure/Seeding/SystemRolePermissionReconciler.cs`: kayıtlı, çağıran yok.
- **Beklenen / mevcut:** Yetkiler kurulumda Ayarlar rol ekranından verilir (SAT-D282, SAT-D287, KLN-D2). Ekran salt-okunur; atama ancak test düzeneğiyle yapılabiliyor.
- **İş etkisi:** Hiçbir iş kullanıcısı satış, alış, finans ya da üretim ekranını açamaz.
- **Kök neden:** Yetki verme bilinçli olarak insan kapısında tutuldu; kapıyı işletecek araç yazılmadı.
- **Çözüm:** OP-01. Bağımlılık: Soru 2.
- **Kabul:** Yalnız yeni komutla verilen yetkiyle bir kullanıcı Satış ekranını açar; kendi talebini onaylama reddedilir; yetkisiz 403; tekrar tek kayıt; eşzamanlı iki onayın biri 409.
- **İnsan kararı:** Evet. Gerçek atamalar her zaman sizde kalır.
- **Güven:** Yüksek. Sayılar kaynak sayımıdır.

**F-002 — Tarayıcıda gerçek giriş ve production kurulum yolu yok** (A, P · kapsam eksiği · P0)

- **Kanıt:**
  - `FE/identity/api.ts` (~22–30) [D]: yalnız `X-Development-User` başlığı; bu başlık yalnız geliştirme/test ortamında kabul ediliyor.
  - Frontend'de OIDC akışı bulunamadı [A].
  - `API/Security/ErpAuthenticationExtensions.cs:22-121` [A]: backend OIDC/JWT hazır.
  - `API/Program.cs:482-514` [D]: migration ve seed yalnız geliştirme/test ortamında.
  - `docs/operations/CANLI_ORTAM_KURULUM_REHBERI_v1.md:37-50` [D]: E1–E3 engelleri.
- **Beklenen / mevcut:** Kullanıcı kurumsal hesabıyla girer. Bugün production'da kimse giriş yapamaz; şema, ilk yönetici ve ilk şirketi kuracak yol da yok.
- **Kök neden:** OPEN-013 kapanmadı.
- **Çözüm:** Frontend'e standart OIDC girişi; production için elle çalıştırılan migration adımı; ilk yönetici/şirket kurulum komutu. Büyüklük: büyük.
- **Kabul:** Gerçek sağlayıcıyla giriş → şirket seçimi → yetkili ekran; süresi dolan oturum girişe döner; geliştirme başlığı production'da reddedilir.
- **İnsan kararı:** Evet (Soru 1, Soru 2).
- **Güven:** Yüksek.

### P1

**F-003 — Ürün çalışma zamanı varsayılan kapalı** (C · bilinçli kapı, karar/işlem bekliyor)

- **Kanıt [D]:** `API/ProductRuntime/ProductRuntimeExtensions.cs:25-50` iki ayar yoksa kapalı; `:101-135` ürün uçları 403/503; `:160-169` ürün migration'ları uygulanmıyor. Plan B-06: "secret ve grant Sinan'da".
- **İş etkisi:** Ürün kartı ve fiyat listesi olmadan satış fiyatlaması 503 döner; Akış 1 ilk adımda durur.
- **Çözüm:** Kimlik bağlama ve imza anahtarı adımı sizde; teknik iş küçük.
- **Kabul:** Ayarlar verildiğinde ürün oluşturulur, fiyat listesi satışta okunur.
- **Güven:** Yüksek.

**F-004 — Ürün kapsamı dar: yassı aileler, tek kalite, yalnız kg** (C · kapsam eksiği)

- **Kanıt:** `MOD/Product/…Infrastructure/Persistence/ProductInitialTechnicalDefinitionV1.cs:27-102` (beş yassı aile, `S235JR`, kaplamasız) [A]; birim listesinde yalnız kilogram [A]; plan B-07, B-08, B-09 ❌.
- **İş etkisi:** Boru/profil (metre, adet, paket), boyalı ürün ve başka kaliteler tanımlanamaz; üçüncü motor fiilen çalıştırılamaz.
- **Çözüm:** Birim çevrimi portu, boru/profil ailesi, paket standardı. Değerler uydurulmaz.
- **Kabul:** Çevrimi tanımlı üründe kg ↔ metre/adet tutarlı; çevrim yoksa açıkça "çevrim yok".
- **İnsan kararı:** Evet (OPEN-007 verileri).
- **Güven:** Orta.

**F-005 — Satış faturası kesinleşemiyor; alacak oluşmuyor** (D, L · eksik entegrasyon + karar eksikliği)

- **Kanıt [D]:**
  - `…Sales.Application/Contracts/SalesInvoiceMatchContractsV1.cs:55`: resmî fatura okuma portu tanımlı, uygulayan sınıf yok.
  - `…Sales.Application/SalesInvoiceService.Match.cs:163-198`: port yoksa 503 `OfficialInvoiceProviderUnavailable`.
  - `…Sales.Domain/Invoices/SalesInvoiceFinalization.cs:52-55`: eşleşme olmadan kesinleşme yok.
  - `API/Shell/SalesFinanceAdmissionHost.cs:20-70`: kesinleşme → Finans açık kalemi bağı hazır ama ulaşılamıyor.
  - `…Finance.Infrastructure/FinanceOfficialInvoiceImportParserV1.cs`: içe aktarılan CSV'de resmî numara, ETTN, KDV, tevkifat sütunu yok.
- **Beklenen / mevcut:** Resmî belgeyle eşleşen fatura kesinleşir ve alacak doğar (SAT-D89, DEC-FIN-003). Eşleşmenin karşılaştıracağı veriyi hiçbir modül tutmuyor.
- **İş etkisi:** Satış faturası cari ekstreye düşmez; tahsilat faturaya dağıtılamaz.
- **Çözüm:** Karar sonrası resmî belge kaydı, okuma portu ve çift alacak koruması.
- **Kabul:** Eşleşen fatura tek açık kalem üretir; tutar farkı toleransı (SAT-D229, SAT-D232) dışında ret; tekrar ikinci kalem üretmez.
- **İnsan kararı:** Evet (Soru 3).
- **Güven:** Yüksek. Çift alacak olasılığını (CSV yolu ile satış kesinleşmesi aynı faturaya) yeniden üretmedim; statik bir olasılık.

**F-006 — Sevk gerçekleri Satış'a dönmüyor** (D, J · eksik entegrasyon; bilinçli kapalı)

- **Kanıt [D]:**
  - `…Sales.Application/Contracts/ISalesOrderDeliveryReadPortV1.cs:62,70`: `ShippedBy` alanı isteğe bağlı.
  - `…Logistics.Infrastructure/Persistence/LogisticsSalesOrderDeliveryReadPort.cs`: yanıt `ShippedBy` olmadan kuruluyor.
  - `…Sales.Application/SalesDraftService.LineClosure.cs:205-238`: sevk eden bilinmiyorsa ret.
  - `…Logistics.Infrastructure/Persistence/LogisticsRows.cs:73`: aktör yalnız komut deneme satırında (`LoadingAttemptRow.ActorId`).
  - Risk istisnası okuma portunun Satış dışında tüketicisi yok.
  - `…Sales.Application/Contracts/SalesDraftUserCloseContractsV1.cs:1-60`: kullanıcı kapatmasının üç ekseni uygulanmamış.
- **Beklenen / mevcut:** Sevk eden kendi fazla teslimini onaylayamaz (SAT-D216). Lojistik bildirene kadar onay kapalı kalır (SAT-D222); bugün hep kapalı.
- **İş etkisi:** Eksik/fazla teslim edilen satır kapatılamaz; onaylı risk istisnası hiçbir sevkte tüketilmez.
- **Çözüm:** OP-02 (plan B-33).
- **Kabul:** Sevk eden A, onaylayan B → onay geçer; A kendi onayında ret; eski kayıtlarda bilgi yoksa ret.
- **İnsan kararı:** Hayır (kararlar mevcut).
- **Güven:** Yüksek.

**F-007 — Üretim: motora özel kapı yalnız dilmede; zincirleme ve sipariş bağı yok** (H · kapsam eksiği)

- **Kanıt [A]:** `…Manufacturing.Domain/Plans/SlittingApproval.cs:98-161`; `Plans/ProductionPlanCalculatorV1.cs:259-378` (dilme dışındaki dört motor yalnız hesap sonucu üretir); boru/profil üretim profili yok; tolerans kataloğu boş; Üretim satış siparişi okumuyor.
- **İş etkisi:** "Bu sipariş için şu bobinden şu planla üretim" izlenemez; kısmi hammadde uyarı/onayı (DEC-PRDCT-002) dört motorda işlemez.
- **Çözüm:** Dört motor için kapı, plan ↔ sipariş satırı bağı, ardışık proses bağı.
- **Kabul:** Siparişten açılan plan sipariş satırını taşır; dilme çıktısı hadde planına girdi seçilir.
- **İnsan kararı:** Kısmen (OPEN-004, OPEN-005 değerleri).
- **Güven:** Orta.

**F-008 — Üretim çıktısında soy ağacı yok** (F, H · eksik entegrasyon)

- **Kanıt:** `…Inventory.Infrastructure/Persistence/InventoryManufacturingRealizationParticipant.cs:193-292` çıktı etiketi oluşturur [A]; soy ağacı yazıcısı yalnız etiket bölme/birleştirme komutunda kullanılıyor [D, arama]; plan B-03 "kısmi".
- **Beklenen / mevcut:** Tam çoktan-çoğa soy ağacı (DEC-PRDCT-003). Çıktı etiketi girdi etiketine bağlanmıyor.
- **İş etkisi:** "Bu rulo hangi ana bobinden, hangi dökümden çıktı" sorusu sistemden yanıtlanamaz.
- **Çözüm:** OP-03.
- **Kabul:** Her çıktı etiketinden ana bobine iz sürülür; tekrar tek kenar; başka şirket girdisi reddedilir.
- **İnsan kararı:** Hayır.
- **Güven:** Orta-yüksek.

**F-009 — Üretim emrinde iptal, düzeltme, ters kayıt yok; ham olay verisi toplanmıyor** (H · kapsam eksiği)

- **Kanıt [A]:** `…Manufacturing.Domain/Orders/ProductionOrder.cs:216-295` (durum yalnız açık/kapalı); `API/Endpoints/ManufacturingEndpoints.cs:149-551` iptal/düzeltme ucu yok; `…Domain/Common/CostResult.cs:3-34` hesaplanmadı.
- **İş etkisi:** Yanlış girilen kg düzeltilemez; stok ve kütle dengesi yanlış kalır. Ham veri toplanmadığı için maliyet ileride geriye dönük hesaplanamaz (DEC-PRDCT-009).
- **Çözüm:** Ters kayıtla düzeltme, emir iptali, süre/hat/fire ham kaydı.
- **Kabul:** Düzeltme yeni hareketle yapılır, eski hareket silinmez; kapalı emirde ret.
- **Güven:** Orta.

**F-010 — Kapasite, takvim ve termin hesabı yok** (H, A1 · kapsam eksiği)

- **Kanıt [A]:** İş merkezinde yalnız lokasyon ve tür var; plan B-15 ❌; Üretim'de sıralama/takvim kodu bulunamadı.
- **İş etkisi:** Müşteriye sistemden teslim tarihi verilemez. Bu kapsam ertelenmiş değil.
- **Çözüm:** Organizasyon'da vardiya/takvim; Üretim'de basit ileri yükleme ile termin.
- **Kabul:** Aynı girdiyle aynı termin; tatil günü atlanır.
- **İnsan kararı:** Hat hızları için evet (OPEN-007).
- **Güven:** Orta.

**F-011 — Maliyet: alış ortalama defterini yazan ve okuyan yok; mamul maliyeti kararsız** (K · yarım iş + karar eksikliği)

- **Kanıt:** `…Inventory.Application/InventoryAverageCostReadContractsV1.cs` (port satır 159) ve `…Infrastructure/Persistence/InventoryAverageCostReadPort.cs:38` [D]; migration `20261110065424_AddInventoryAverageCostLedger.cs` [D]. Deftere yazan kod ve Stok dışında tüketici bulunamadı [A]. Mal kabulde maliyet "hesaplanmadı".
- **Beklenen / mevcut:** Her stok girişinde ortalama yeniden hesaplanır; Satış sipariş anında satıra kopyalar (SAT-D256, SAT-D262). Defter dün geldi; yazıcı ve tüketici henüz yok.
- **İş etkisi:** Ticari malda bile marj görünmez.
- **Çözüm:** Mal kabulde defter yazımı, Satış'ta okuma ve kopya, marj raporu. Önce yazıcının gerçekten olmadığı doğrulanmalı.
- **Kabul:** İki farklı fiyatlı girişten sonra ortalama 0,01 TL hassasiyetle doğru; kaynak yoksa "kullanılamıyor".
- **İnsan kararı:** Mamul için evet (Soru 5).
- **Güven:** Orta.

**F-012 — Fason: müşteri malı geri sevk edilemiyor; sipariş ve bedel bağı yok** (I · bilinçli kapı + kapsam eksiği)

- **Kanıt [D]:** `…Inventory.Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs:164-176` (karar bekleyen not, 409 `OwnerNotCompany`); `…Inventory.Application/InventorySalesOwnershipGuardV1.cs:1-31` ("kapı, politika değil"). [A]: fason sipariş bağı portunun uygulaması yok (plan B-02: "bugün 503"); dış fason plan B-22.
- **İş etkisi:** Fason iş kolu sistemde baştan sona yürütülemez.
- **Çözüm:** Karar sonrası müşteri malı teslim akışı, fason sipariş bağı, hizmet bedeli satırı.
- **Kabul:** Müşteri malı yalnız sahibi cariye çıkar; mülkiyet değişmez; bedel işlenen miktara bağlanır.
- **İnsan kararı:** Evet (Soru 4). Kapı kaldırılmamalı, kuralla değiştirilmeli.
- **Güven:** Yüksek.

**F-013 — Depo transferi ve blok bağlı değil** (F · iskelet)

- **Kanıt [A]:** `…Inventory.Infrastructure/InventoryTransferRegistration.cs:13` kaydı çağıran yok; blok/serbest bırakma iskelet; transfer çıkış belgesinin ucu yok; plan B-22 ("kararlar kilitli, uygulanabilir"), B-18.
- **İş etkisi:** Depolar arası hareket ve kalite bloğu sistemde yapılamaz; transfer maliyeti kuralı (SAT-D260) da buna bağlı.
- **Kabul:** İki ayaklı transferde sahiplik değişmez; yarım kalan transfer görünür; bloklu etiket tahsis edilemez.
- **Güven:** Orta.

**F-014 — Kasa/virman, borç tarafı ve açılış bakiyesi yok** (L, E · kapsam eksiği)

- **Kanıt [A]:** `docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md:121-133`; `…Finance.Contracts/PartyLedgerV1.cs:73` (açılış yok, OPEN-018); `docs/handoffs/ALIS_FINANS_DEVIR_v1.md` (alış faturası yok).
- **İş etkisi:** Nakit işlem, tedarikçi borcu ve devir bakiyesi olmadan cari ekstre gerçeği göstermez.
- **Kabul:** Açılış bakiyesi tek kaynaklı kayıtla girilir; kasa bakiyesi eksiye düşmez.
- **İnsan kararı:** Evet (OPEN-018, OPEN-009).
- **Güven:** Orta.

**F-016 — Yedek ve geri yükleme kanıtsız** (P · kanıt eksikliği)

- **Kanıt:** Kurulum rehberi yedek için "NO-GO" diyor; OPEN-017 hedefleri varsayım (15 dakika / 4 saat) ve tatbikat yok.
- **İş etkisi:** Veri kaybı riski; pilotun da ön koşulu.
- **Çözüm:** Ayrı bir ortamda geri yükleme tatbikatı ve süre ölçümü. Ortam erişimi ve sizin onayınız gerekir.
- **Güven:** Yüksek.

### P2

| ID | Başlık (modül · tür) | Kanıt | Beklenen / mevcut ve etki | Çözüm ve test | Karar | Güven |
|---|---|---|---|---|---|---|
| F-015 | Satın alma siparişinde hazırlayan ≠ onaylayan zorlanmıyor (E · olası risk) | `…Purchasing.Domain/PurchaseOrder.cs:186-197` [A] | ALS-D3 yalnız eşikten söz ediyor; görev ayrılığı hükmü görmedim. Kişi kendi siparişini onaylayabilir | Karar teyidi; gerekirse üretimdeki kuralın aynısı. Test: kendi siparişinde ret | Evet (küçük) | Orta |
| F-017 | Mal kabul satış talebine bağlı değil; gelecek tedarikten tahsis yok (E, G · eksik entegrasyon) | `IPurchaseSupplyReferencePortV1` tüketicisi yok [A]; plan B-20, B-23 | DEC-STK-002/003: tahsis alış satırından da yapılabilir. Bugün yalnız fiziksel stoktan | Alış satırı kaynağı + "aynı ton bir kez" kuralı. Test: aynı miktar iki siparişe verilemez | Evet (miktar bazlı rezervasyon, #66'da açık) | Orta |
| F-018 | Kredi limiti ve teminat kaynağı yok; risk kontrolü hiç çalışmıyor (D, L · karar eksikliği) | `…Finance.Infrastructure/FinanceSalesPartyRiskReadPortV1.cs:57-61` [D] | Risk hep "kaynak yok". Limit aşımı durdurulmuyor; risk istisnası akışı boşta | Karar sonrası limit kaynağı. Test: limit aşımında sevk durur | Evet (OPEN-010) | Yüksek |
| F-019 | Ters kayıt dar; fatura iptali bağlı değil; iade faturası yok (L, D · kapsam eksiği) | `…Finance.Infrastructure/ReversalProposalPolicy.cs:31-143` [A]; Satış kodunda iade faturası yok [D, arama] | SAT-D288…D294 iade faturasını tanımlıyor; kodda yok. İadede alacak düşürülemiyor | İade faturası + ters kalem portu. Test: özgün kalem değişmez, ters kalem ayrı | Hayır | Orta |
| F-020 | Sevk yalnız satış satırı için; tartı hükümsüz; talimat iptali yok (J · kapsam eksiği) | `API/LogisticsRuntime/LogisticsRealizationTransactionRunner.cs:141-155` [A]; `…Logistics.Domain/Loading/VehicleWeighing.cs:19-89` [A] | Fason iade, transfer, numune sevki yapılamaz; tartı farkı yorumlanmaz | Sevk türleri; tolerans profili. Test: tür dışı sevk 4xx | Evet (OPEN-004) | Orta |
| F-021 | Olay dağıtıcısı ve Work bağlı değil (P, A4 · teknik borç) | `MOD/Work/…Infrastructure/WorkModuleExtensions.cs:14,53,80` çağıran yok [A]; plan B-24 | Modüller arası olaylar yazılıyor ama dağıtılmıyor; iş listesi yok | Dağıtıcı + tekrar süzme. Test: olay bir kez işlenir | Hayır | Orta |
| F-022 | Hazır olma kontrolü dört veritabanı bağlamını kapsamıyor (P · doğrulanmış eksik) | `API/Startup/DatabaseSchemaCompatibilityHealthCheck.cs:31-47` [D] | Ayarlar, Belgeler, fiyat listesi ve Work şeması eksikken sistem "hazır" der | Dört bağlamı ekle. Test: eksik migration'da "hazır değil" | Hayır | Yüksek |
| F-023 | Merkezi denetim kaydı yalnız başarılı işlemleri alıyor (P · olası risk) | `MOD/Audit/…Infrastructure/Services/AuditEventFactory.cs:66-82` [D] | Reddedilen denemeler modül tablolarında; denetim ekranında görünmez | Ret kayıtlarını denetim görünümüne bağla | Hayır | Orta |
| F-024 | Görev ayrılığı varsayımları teyitsiz (D, J · karar eksikliği) | `…Logistics.Domain/Loading/LoadingInstruction.cs:329-345` (irsaliye onayı varsayım notu) [A] | İrsaliyeyi hazırlayan onaylayabiliyor; satışta kural yalnız Satış Destek için | Varsayımı karara çevir | Evet (küçük) | Orta |
| F-025 | Kurlar: ekran yok, kaynak kapalı (M · eksik entegrasyon) | `MOD/Settings/…Infrastructure/ExchangeRates/ExchangeRateSources.cs:9-68` [A]; plan B-11 | Dövizli işlem yapılamaz; yalnız TRY | Kur ekranı. Test: kur yoksa işlem durur | Hayır | Orta |
| F-026 | Etiket/barkod/irsaliye çıktısı ve pano yok (M, N · kapsam eksiği) | Yalnız sipariş PDF'i (`API/Endpoints/SalesEndpoints.OrderPdf.cs:14-15,32`) [A] | Depoda etiket basılamaz; irsaliye çıktısı alınamaz | Çıktı şablonları (OPEN-019) | Kısmen | Düşük-orta |
| F-027 | Cari ve Satın alma kapsam eksikleri (B, E) | Cari düzenleme/pasif/birleştirme, tedarikçi iadesi, fason alımı bulunamadı [A]; plan B-16, B-17, B-19 | Günlük kullanımda cari güncellenemez | Plan dalgaları K2, K4 | Hayır | Orta |
| F-028 | Muhasebe yalnız elle tahsilat fişinden besleniyor (L · eksik entegrasyon) | `MOD/Accounting/…Infrastructure/AccountingModuleExtensions.cs:68-69,92-93` [A]; plan B-04, B-28 | Faturadan 120/320 fişi oluşmuyor | Kararlar mevcut (KLN-D6…D19). Test: aynı faturadan tek fiş | Hayır | Orta |

### P3

| ID | Başlık | Kanıt | Etki ve öneri | Karar |
|---|---|---|---|---|
| F-029 | Politika katalogları ve şirket kimlikleri derlenmiş kodda; şirket üstü kiracı düzeyi yok | `ApprovedSalesPolicyCatalog.cs:60-110`, `ApprovedUnitProfileCatalog.cs:34-50` [A] | Kendi kullanımınız için sorun değil. Lisanslı ürüne geçişte ciddi iş; yeni tablolarda bu ayrımı zorlaştıracak kısayollardan kaçının. Şimdi kiracı katmanı eklemeyi önermem | Hayır |
| F-030 | AI: sözleşme tipleri ve kapalı panel | `BB/Ai/AiExternalEgressV1.cs`, `FE/ai/AiPanel.tsx:70-74` [D] | Tasarım doğru yönde (yalnız şemalı araçlar); sağlayıcı ve KVKK kararı bekliyor | Evet (OPEN-020) |
| F-031 | Belge ve durum kayması | `docs/acceptance/ACCEPTANCE_MATRIX.csv`, `README.md:5-7,105-107`, `docs/backlog/*` [A] | Durum belgeleri kodu yansıtmıyor; yanlış karar riski | Hayır |
| F-032 | Gözlemlenebilirlik yalnız konsol günlüğü | Kurulum rehberi; izleme kütüphanesi bulunamadı [A] | Canlıda sorun teşhisi zor; pilot öncesi dosya günlüğü ve temel ölçüm | Hayır |

---

## G) AŞAMALI TAMAMLAMA PLANI

Depodaki `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md` planını (K0…K8 dalgaları, B-01…B-33 bağları) esas aldım; yeni bir örgütlenme önermiyorum. Büyüklük: K = küçük, O = orta, B = büyük.

| Faz | İçerik | Bulgular | Büyüklük | Sonunda ne mümkün olur |
|---|---|---|---|---|
| 0 | Hazır olma kontrolü; durum belgelerinin güncellenmesi; maliyet defteri yazıcısının doğrulanması | F-022, F-031, F-011 | K | Yanlış "hazır" sinyali kalkar |
| 1 | Rol ekranı ve atama komutu; giriş; production kurulum yolu; yedek tatbikatı | F-001, F-002, F-016 | B | Gerçek kullanıcı giriş yapar ve yetkili ekranı açar |
| 2 | Ürün çalışma zamanı; sevk gerçekleri → Satış; fatura → alacak; fatura fişi; alış maliyeti ve marj; iade faturası | F-003, F-005, F-006, F-011, F-019, F-028 | B | Ticari malda sipariş → sevk → fatura → tahsilat zinciri kapanır |
| 3 | Birim çevrimi; gelecek tedarikten tahsis; transfer ve blok; etiket çıktısı; alış faturası/borç | F-004, F-013, F-014, F-017, F-026 | B | Tedarik ve depo işlemleri sistemde yürür |
| 4 | Çıktı soy ağacı; dört motor kapısı; zincirleme; sipariş bağı; iptal/düzeltme; takvim ve termin | F-007…F-010 | B | Beş motor siparişe bağlı ve izlenebilir çalışır |
| 5 | Fason: müşteri malı teslimi, sipariş bağı, bedel; dış fason | F-012 | B | Fason iş kolu baştan sona yürür |
| 6 | Kasa/virman, açılış, ters kayıt genişliği, finans fiş kaynakları | F-014, F-019 | B | Cari ekstre ve muhasebe gerçeği yansıtır |
| Sonra | AI, pano ve rapor merkezi, e-belge, banka, Mikro, SaaS katmanı | F-029, F-030 | — | — |

**Dört kategori**

- **Mevcut kararlarla yapılabilir:** F-001 (ekran ve komut), F-006, F-008, F-011 (alış maliyeti kısmı), F-013, F-019 (iade faturası), F-021, F-022, F-025, F-028, F-031.
- **Kararınızı bekleyen:** F-002 (OPEN-013), F-005, F-012, F-011 (mamul maliyeti), F-004 ve F-010 (OPEN-007 verileri), F-014 (OPEN-018), F-018 (OPEN-010), F-020 (OPEN-004), F-015, F-024, F-030 (OPEN-020).
- **Teknik doğrulama ya da ortam erişimi bekleyen:** F-016; F-003 (anahtar ve bağlama işlemi sizde); diğer tarayıcı takımlarının bu sürümde koşturulması; [A] işaretli bulguların doğrulanması.
- **Bilinçli sonraki kapsam:** ileri maliyet, çek kırdırma optimizasyonu, üretim döngüsü optimizasyonu, İK, bakım, ileri kalite, sabit kıymet, CRM, tam entegrasyonlar.

**Kontrollü pilot ile bütünleşik canlı kullanımın farkı** (öneridir, onay değildir)

- **Kontrollü pilot:** Tek şirket, yalnız ticari mal (yassı ürün), az sayıda kullanıcı, mevcut sisteminizle paralel, finans aktivasyonu yok. Ön koşul: Faz 1, ürün çalışma zamanının açılması ve yedek tatbikatı. Faz 2'nin tamamı gerekmez; fatura ve alacak eski sistemde kalabilir.
- **Bütünleşik canlı kullanım:** Faz 1–6 tamam; açık kararlar kapalı ya da açıkça kapsam dışı; tarayıcı kabul testleri gerçek giriş ve gerçek rollerle geçmiş.

---

## H) OPUS DEVİR PAKETİ

**Çalışma kuralı (aynen):**

> "Uygulamaya başlamadan önce mevcut HEAD ve çalışma ağacını raporun başlangıç sürümüyle karşılaştır. İlgili kod değişmişse yalnız etkilenen bulguları yeniden doğrula. Fable raporunu tartışılmaz gerçek kabul etme. Yalnız kullanıcı tarafından uygulama için seçilen paketi gerçekleştir; diğer paketlere kendiliğinden geçme. Bu rapor production, migration, seed, gerçek grant, finans aktivasyonu, merge veya deployment onayı değildir."

**Ek uyarı.** Depoda birkaç ajan şeridi aynı anda çalışıyor ve aşağıdaki üç paket de şerit planında zaten sırada (K1, B-33, K0-4). Paketi vermeden önce Issue #66 ve açık PR listesinden o dilime başlanmadığını teyit edin; yoksa çift iş çıkar. İnceleme anında bu işler için açık PR yoktu.

**Sıralı iş listesi**

| Sıra | Paket | Bulgular | Büyüklük | Durum |
|---|---|---|---|---|
| 1 | OP-01 Rol ekranı ve erişim talebi/onayı | F-001 | B | Yapılabilir (Soru 2 ayrıntıyı etkiler) |
| 2 | OP-02 Lojistik → Satış sevk gerçekleri | F-006 | O–B | Yapılabilir |
| 3 | OP-03 Üretim çıktısı soy ağacı | F-008 | O | Yapılabilir |
| 4 | OP-04 Hazır olma kontrolü kapsamı | F-022 | K | Yapılabilir |
| 5 | OP-05 Alış maliyet defteri yazıcısı ve satış marjı | F-011 | O | Yapılabilir; önce doğrulama |
| 6 | OP-06 Olay dağıtıcısı ve Work | F-021 | B | Yapılabilir |
| 7 | OP-07 Faturadan 120/320 taslak fiş | F-028 | O | Yapılabilir |
| 8 | OP-08 Müşteri iade faturası | F-019 | B | Yapılabilir |
| 9 | OP-09 Tarayıcı girişi ve production kurulum yolu | F-002 | B | OPEN-013 bekliyor |
| 10 | OP-10 Resmî fatura eşleşmesi → alacak | F-005 | O–B | Soru 3 bekliyor |
| 11 | OP-11 Ürün çalışma zamanı, birim çevrimi, boru/profil ailesi | F-003, F-004 | B | Sizin işleminiz + OPEN-007 |
| 12 | OP-12 Transfer ve blok | F-013 | B | Yapılabilir |
| 13 | OP-13 Üretim kapıları, zincirleme, iptal/düzeltme | F-007, F-009 | B | Kısmen karar |
| 14 | OP-14 Takvim, kapasite, termin | F-010 | B | Kısmen karar |
| 15 | OP-15 Fason akışı | F-012 | B | Soru 4 bekliyor |
| 16 | OP-16 Kasa, borç, açılış | F-014 | B | OPEN-018 bekliyor |

Üç paket için ortak bilgiler:

- **Başlangıç commit'i:** `9091e7d4`.
- **Kanıt düzeyi:** K3; iki bağımsız inceleme gerekir.
- **Yerel doğrulama komutları ve yan etkileri:** bu bölümün sonunda.

### OP-01 — Ayarlar "Kullanıcılar ve yetkiler": rol tanımı ve erişim talebi/onayı

- **Bulgular:** F-001.
- **İş amacı:** Yetkili kişi, karar dosyalarındaki rol setlerini ekrandan tanımlar ve kullanıcılara şirket kapsamında atar. Atama, talep eden dışında biri onaylayınca yürürlüğe girer. Kodda rol tanımı, atama ya da seed yapılmaz (SAT-D282, SAT-D287). Plan notu: "atama Sinan'da; ekran talep üretir".
- **Ön koşul:** Soru 2 yanıtı tasarımı etkiler. Yanıt gelmeden geliştirme/test ortamında iki ayrı kullanıcıyla kurulabilir.
- **Bağımlı paketler:** OP-09 ve tüm pilot.
- **Önce okunacak mevcut dosyalar:**
  - `MOD/Identity/…Domain/Approvals/AccessGrantRequest.cs`
  - `MOD/Identity/…Infrastructure/Seeding/SystemRolePermissionReconciler.cs`
  - `MOD/Identity/…Infrastructure/Services/ErpAuthorizationService.cs`
  - `API/Endpoints/IdentityEndpoints.cs`, `API/Security/ErpPermissionAuthorizationHandler.cs`, `API/Seed/identity-seed.json`
  - `BB/Security/ErpPermissions.cs`
  - `FE/identity/capabilities.ts`, `FE/identity/api.ts`, `FE/identity/IdentityAccessPage.tsx`, `FE/identity/IdentityRoleDetailPage.tsx`, `FE/shell/navigation.ts:355-382`
  - `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`, `docs/security/GOREV_AYRILIGI_ONAY.md`, `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md`
  - `docs/operations/CANLI_ORTAM_KURULUM_REHBERI_v1.md` (E4 ve §4.6), `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md` §2.2
  - Depodaki beceriler: `k3-command-skeleton`, `permission-capability-registration`, `openapi-regenerate`, `recovery-inventory-repin`, `ef-migration-forward-fix`, `frontend-screen-states`
- **Olası değişiklik kapsamı (önerilen yeni):** Identity uygulama katmanında rol oluşturma/izin seti değiştirme, erişim talebi açma, onaylama/reddetme ve atamayı pasifleştirme komutları; bunların uçları; ekran için yeni yetenek kimlikleri; ekranda talep ve onay görünümleri; gerekirse bir migration.
- **Dokunulmayacak alanlar:** Gerçek rol atamaları ve seed içeriği; sistem rollerinin izin setleri; diğer modüllerin izin adları; `.claude/settings.json`; OPEN-013 kapsamı (giriş sağlayıcısı).
- **Adımlar:**
  1. Mevcut alan modelini ve yetki değerlendirmesini oku; eksik durum geçişlerini listele.
  2. Komutları ev iskeletiyle yaz: izin çiti, beklenen sürüm, tekrar koruması, tek kayıtta deneme ve denetim.
  3. Uçları ekle; OpenAPI'yi yeniden üret.
  4. Yeni izin ve yetenekleri kaydet; yetki listesini yeniden türet.
  5. Ekranı tüm durumlarıyla yaz (yükleniyor, boş, yetkisiz, çakışma, hata).
  6. Recovery envanterini yeniden sabitle.
- **Testler:**
  - Olumlu: talep → başka kullanıcının onayı → atanan kullanıcı ilgili ucu çağırabilir.
  - Olumsuz: var olmayan rol; pasif kullanıcı; başka şirket kapsamı.
  - Yetkisiz: izni olmayan 403; kendi talebini onaylama ret.
  - Tekrar: aynı anahtarla ikinci çağrı tek kayıt.
  - Eşzamanlılık: aynı talebe iki onay → biri 409.
- **Ölçülebilir kabul:** Bir gerçek-SQL testi ve bir tarayıcı testi, test rol yazıcısı kullanmadan, yalnız yeni komutla verilen yetkiyle bir Satış ekranını açar. Pasifleştirme sonrası aynı çağrı 403 döner.
- **Geri alma:** PR'ın geri alınması; migration varsa ileri düzeltme kuralıyla.
- **Gereken onaylarınız:** Soru 2; yeni izin adları; migration. Gerçek atamalar sizin elinizle yapılır.
- **Opus'un sunacağı kanıt:** Test adları ve sonuçları, komut çıktıları ve çıkış kodları, OpenAPI farkı, envanter yeniden sabitleme çıktısı, iki inceleme bağlantısı.

### OP-02 — Lojistik → Satış: sevk eden bilgisi ve risk istisnası tüketimi

- **Bulgular:** F-006.
- **İş amacı:** Lojistik her sevkte sevk eden kullanıcıyı kalıcı kaydeder ve mevcut teslim portunda `ShippedBy` olarak döndürür; Satış'ta kod değişmeden SAT-D216 denetimi başlar (SAT-D222, plan B-33). İkinci dilimde tek-sevk risk istisnası yükleme talimatına bağlanıp sevkte tüketilir, sevk iptalinde düşer (SAT-D283, SAT-D285).
- **Not:** İkinci dilimin iş değeri OPEN-010 kapanınca ortaya çıkar; risk bugün hep "kaynak yok". Sözleşme şimdi kurulabilir.
- **Ön koşul:** Yok. Kullanıcı kapatmasının üç ekseni bu paketin dışındadır.
- **Önce okunacak mevcut dosyalar:**
  - `…Sales.Application/Contracts/ISalesOrderDeliveryReadPortV1.cs`
  - `…Logistics.Infrastructure/Persistence/LogisticsSalesOrderDeliveryReadPort.cs`, `LogisticsRows.cs`, `LogisticsStore.cs`, `LogisticsDbContext.cs`
  - `…Sales.Application/SalesDraftService.LineClosure.cs`, `SalesDraftService.OverDeliveryApproval.cs`
  - `…Logistics.Application/LogisticsInstructionService.Realization.cs`, `LogisticsInstructionService.DispatchNotes.cs`
  - `…Logistics.Domain/Loading/LoadingInstruction.cs`
  - `API/LogisticsRuntime/LogisticsRealizationTransactionRunner.cs`
  - `…Sales.Application/Contracts/SalesShipmentRiskExceptionContractsV1.cs`, `…Sales.Infrastructure/Persistence/SalesShipmentRiskExceptionReadPort.cs`, `…Sales.Domain/RiskExceptions/SalesShipmentRiskException.cs`
  - `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json`
  - Kararlar: `SINAN_KARARLARI_2026-11-03_SATIS_EK37.md`, `…11-07_SATIS_EK41.md`, `…11-10_SATIS_EK55.md`, `…EK56.md`, `…11-03_MENU.md`; Issue #66'daki ilgili yanıt.
- **Olası değişiklik kapsamı:** Lojistik'te sevk kaydına aktör alanı ve migration (önerilen yeni); okuma portunda alanın doldurulması; ikinci dilimde Lojistik'in tüketim/iptal bildirimi portu (önerilen yeni).
- **Dokunulmayacak alanlar:** Satış'taki kural mantığı; teslim toleransı değerleri (SAT-D5); risk hesabı; Finans. Yeni bir teslim portu açılmaz; mevcut port kullanılır.
- **Adımlar:**
  1. Aktörün bugün nerede saklandığını doğrula (deneme satırı); sevk kaydına açık alan gerekip gerekmediğine karar ver.
  2. Alanı ekle; eski kayıtlar için değer türetilemiyorsa boş bırak (boş değer onayı kapalı tutar).
  3. Okuma portunu doldur; karşılaştırma Satış aktör kimliği biçiminde ve birebir olmalı.
  4. İkinci dilim: tüketim ve iptal bildirimi.
- **Testler:**
  - Olumlu: A sevk eder, B fazla teslimi onaylar.
  - Olumsuz: A kendi sevkini onaylayamaz; aktörü olmayan eski sevkte ret.
  - Yetkisiz: onay izni olmayan 403.
  - Tekrar: aynı sevk komutu tek aktör kaydı.
  - Eşzamanlılık: aynı satıra iki onay → biri 409; aynı istisnayı iki sevk tüketemez.
- **Ölçülebilir kabul:** `sales_over_delivery_shipper_unverified` yalnız aktör gerçekten yokken döner; çok parçalı sevkte tüm sevk edenler listelenir.
- **Geri alma:** PR geri alınır; alan boş kalırsa sistem bugünkü kapalı davranışa döner.
- **Gereken onaylarınız:** Migration; modüller arası sözleşme değişikliği.
- **Opus'un sunacağı kanıt:** Gerçek-SQL test sonuçları, mimari bağımlılık testinin geçtiği, karar maddeleriyle eşleme tablosu.

### OP-03 — Üretim çıktısı soy ağacı kenarı ve uçtan uca iz

- **Bulgular:** F-008 (plan B-03, K0-4).
- **İş amacı:** Her üretim çıktısı etiketi, aynı emirde tüketilen girdi etiketine soy ağacı kenarıyla bağlanır; kenar çıktı kabulüyle aynı işlemde yazılır. Sonuç: mamul etiketinden ana bobine iz sürülebilir (DEC-PRDCT-003).
- **Ön koşul:** Yok.
- **Önce okunacak mevcut dosyalar:**
  - `…Inventory.Infrastructure/Persistence/InventoryManufacturingRealizationParticipant.cs`, `InventoryStore.Genealogy.cs`
  - `…Inventory.Infrastructure/InventoryLabelGenealogyRegistration.cs`, `InventoryLabelTransformationCommandService.cs`
  - `…Inventory.Application/InventoryLabelGenealogyContractsV1.cs`
  - `…Manufacturing.Domain/Orders/ProductionOrder.cs` (çıktı beyanındaki ana rulo kimliği)
  - `API/ManufacturingRuntime/ManufacturingInventoryOutputAdmission.cs`, `ManufacturingInventoryInputIssueHost.cs`
  - `tools/relational_inventory/inventory.txt`, `manufacturing.txt`; `tests/e2e/manufacturing-mfg`, `tests/e2e/logistics-l04`
- **Olası değişiklik kapsamı:** Katılımcı sözleşmesinin yeni sürümü (önerilen yeni); kenar yazımı; gerekirse migration; soy ağacı okuma ucunun üretim çıktısını göstermesi.
- **Dokunulmayacak alanlar:** Motor formülleri, kütle dengesi kuralları, tolerans değerleri (OPEN-004, OPEN-005), maliyet. İkinci kalite/hurda etiket sınıfında yalnız mevcut kararlar (URT-D8, URT-D14) uygulanır.
- **Adımlar:**
  1. Çıktı beyanındaki ana rulo kimliğinin stoktaki girdi etiketine nasıl eşlendiğini doğrula.
  2. Sözleşmeyi sürümle; kenarı Stok yazsın (soy ağacının sahibi Stok).
  3. Kenarı çıktı kabulüyle aynı işlemde yaz.
  4. Üretim çıktısı etiketinin sevk edilebildiğini gösteren testi ekle.
- **Testler:**
  - Olumlu: bir girdiden üç çıktı → üç kenar; iki girdili emirde doğru eşleşme.
  - Olumsuz: emirde tüketilmemiş girdiye kenar reddedilir; başka şirket etiketi reddedilir.
  - Yetkisiz: çıktı beyanı izni olmayan 403.
  - Tekrar: aynı çıktı beyanı tek etiket ve tek kenar.
  - Eşzamanlılık: aynı ana rulodan eşzamanlı iki çıktı tutarlı kalır.
- **Ölçülebilir kabul:** Soy ağacı sorgusu mamul etiketinden alış etiketine kesintisiz yol döner; üretim çıktısı etiketi yükleme talimatıyla sevk edilir.
- **Geri alma:** PR geri alınır; kenarlar ek kayıttır, mevcut hareketleri değiştirmez.
- **Gereken onaylarınız:** Migration; sözleşme sürüm artışı.
- **Opus'un sunacağı kanıt:** Gerçek-SQL ve tarayıcı test sonuçları, örnek soy ağacı sorgu çıktısı, mimari ve envanter kontrollerinin çıktıları.

### Önerilen yerel doğrulama komutları (bu incelemede çalıştırılmadı)

```bash
# Backend (yan etkisiz derleme ve birim testleri)
dotnet restore GoymenERP.slnx
dotnet build GoymenERP.slnx
dotnet test GoymenERP.slnx

# Frontend (src/frontend/goymen-erp-web içinde)
npm ci && npm run lint && npm test && npm run build

# Push öncesi toplu kontrol (artifacts/openapi.json dosyasını yazar)
python tools/pre_push_check.py
```

- **Gerçek-SQL testleri:** `GOYMEN_ERP_TEST_SQLSERVER_CONNECTION` ortam değişkeni ister ve o sunucuda **geçici veritabanı oluşturup siler**. Mevcut SQL servislerinize yöneltilmemeli; ayrı, onayladığınız bir sunucu gerekir.
- **Tarayıcı takımları** (`tests/e2e` içinde `npm run browser:<takım>:ci`): **geçici Docker yığını başlatır** (SQL 11433, API 15080, web 15173/15174). Mevcut Docker ortamınıza dokunmama kuralınız nedeniyle açık onayınız gerekir.
- `tools/ci/local_check.py` kirli çalışma ağacında çalışmayı reddeder.

---

## I) BENDEN GEREKEN GERÇEK KARARLAR

Zaten Issue #66'da ya da açık karar listesinde duran konuları (miktar bazlı rezervasyon, teklif politika verisi, MINSTOK, OPEN-004/005/007/009/010/017/018/019/020) yeniden sormuyorum.

**Soru 1 — Giriş sağlayıcısı (OPEN-013).** Kullanıcılar sisteme hangi kurumsal hesapla girecek; MFA zorunlu mu; şirket dışından erişim olacak mı?

- **Önerim:** Şirkette zaten kullanılan bir kurumsal hesap sistemi varsa (ör. Microsoft 365) onu kullanın; MFA zorunlu; dış erişim yalnız VPN üzerinden.
- **Alternatif:** Kendi sunucunuzda çalışan açık kaynak bir OIDC sağlayıcısı. Dışa bağımlılık olmaz; kurulum ve bakım yükü size kalır.
- **Etki:** F-002'yi ve pilotu açar.

**Soru 2 — Rol ekranında onay usulü ve ilk yönetici.** Rol içeriği değişikliği ve rol ataması iki kişiyle mi (talep eden ≠ onaylayan) yürüsün; tek yöneticinin olduğu ilk kurulumda ilk yönetici nasıl oluşsun?

- **Önerim:** İki kişi kuralı kalsın; koddaki erişim talebi modeli buna göre kurulmuş. İlk kurulumda sizin elinizle çalıştırılan, kayıt altına alınan tek seferlik bir "kurucu yönetici" adımı olsun; ilk iş ikinci onaylayıcıyı tanımlamak olsun.
- **Alternatif:** Tek kişi (yalnız siz) onayı. Daha hızlı; görev ayrılığı ve denetim izi zayıflar.
- **Etki:** OP-01 tasarımı ve kurulum rehberindeki E3/E4.

**Soru 3 — Resmî fatura verisi sisteme nasıl girecek?** Satış faturasının kesinleşmesi resmî belgeyle eşleşmeye bağlı; bugün bu veriyi tutan yer yok.

- **Önerim:** Muhasebe, e-fatura portalından ya da Mikro'dan aldığı listeyi (numara, ETTN, tarih, matrah, KDV, tevkifat, toplam) kontrollü dosya olarak içe aktarsın; veri Finans'ta dursun; Satış buna karşı eşleştirsin. OPEN-016 ile uyumlu.
- **Alternatif:** Muhasebe kullanıcısı resmî numara, ETTN ve tutarı eşleştirme ekranında elle girsin, ikinci kişi onaylasın. Geliştirmesi küçük; yazım hatasına açık.
- **Etki:** F-005; satış → finans zinciri.

**Soru 4 — Fason: müşteri malı nasıl çıkar, hizmet bedeli neye bağlanır?**

- **Önerim:** Müşteri malı satış sevkinden ayrı bir "fason teslim" türüyle, yalnız malın sahibi cariye çıksın; mülkiyet değişmesin. Hizmet bedeli satış siparişinde ayrı hizmet satırı olsun ve işlenen miktara bağlansın.
- **Alternatif:** Mevcut satış sevk akışında sahiplik istisnası. Daha az geliştirme; "satılan mal" ile "iade edilen müşteri malı" raporlarda karışır.
- **Etki:** F-012; beş motorun fason kullanımı.

**Soru 5 — İlk sürümde mamul maliyeti.** Alış maliyeti kararlı; üretilen malda marj bugün "kullanılamıyor" (SAT-D258).

- **Önerim:** Mamul maliyeti, tüketilen girdi etiketlerinin alış ortalama maliyetinin iyi çıktı kilosuna dağıtılmasıyla hesaplansın; işçilik, enerji ve genel gider eklenmesin (ileri motor ertelenmiş). Hurda ve fire payının iyi ürüne yüklenip yüklenmeyeceğini siz belirleyin.
- **Alternatif:** İlk sürümde mamulde marj göstermemek, yalnız ham veriyi toplamak. Bugünkü kararla uyumlu; "temel maliyet" hedefiniz ertelenir.
- **Etki:** F-011, F-009 (ham veri) ve yönetim raporları.

---

## J) SON KARAR

**"GöymenERP'de şimdi ilk olarak şu iş yapılmalı:** Ayarlar → "Kullanıcılar ve yetkiler" ekranı ile arkasındaki rol tanımı ve erişim talebi/onayı komutu (OP-01), hemen ardından tarayıcı girişi; **çünkü** yaklaşık 160 iznin yalnız 14'ü bir rolde, rol atayabilecek hiçbir komut ya da ekran yok ve son kararlarınızın tümü yetki vermeyi bu ekrana bırakıyor. Bu yapılmadan, test edilmiş hiçbir modül gerçek bir kullanıcıya açılamaz."

**"Bu iş bittiğinde kullanıcı şu işlemi gerçekten yapabilecek:** yetkili kişi karar dosyalarındaki rol setlerini ekrandan tanımlayıp bir satış çalışanına atayacak; o çalışan test düzeneği olmadan Satış ekranını açıp sipariş girebilecek."

**"Tamamlandığını şu kanıt gösterecek:** yalnız yeni komutla verilen yetkiyle çalışan bir gerçek-SQL testi ve bir tarayıcı testi; kendi talebini onaylamanın reddedildiği, yetkisiz çağrının 403, tekrar çağrının tek kayıt, eşzamanlı iki onaydan birinin 409 döndüğü test sonuçları; hepsi aynı commit üzerinde ve iki bağımsız incelemeyle."