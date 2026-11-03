# Kalan Modüller Hazırlık Planı v1

Tarih: 2026-11-03 · Hazırlayan: Claude (cloud oturumu, "Kalan Modüller" şeridi) · Durum: **ÖNERİ** (plan; iş kararı
değildir) · Koordinatör: `session_015Usc2qbq4qsgayZcDY3UTb`

Kapsam (kullanıcı talimatı, 2026-11-03, bu oturum): **Ayarlar** (Organizasyon, Kullanıcı ve Yetki, Ana Veriler, Denetim
kaydı), **Cari**, **Ürün & Hizmet**, **Stok & Lojistik**, **Çalışma Merkezi (İş/Onay)**, **Belgeler ve Dijital Arşiv**,
**Muhasebe**, **Raporlar**.

Kaynaklar: `AGENTS.md`; `docs/decisions/KARAR_SICILI.md`, `ACIK_KARARLAR.md`; `SINAN_KARARLARI_2026-11-02_FINANS.md`
(FNS-D14/D15), `_SATIS_EK35.md` (SAT-D208/D209), `_ALIS_EK9.md` (ALS-D58/D59/D63/D64/D65), `_URETIM*.md`
(URT-D3/D8/D11/D17/D20…D27); `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §4, §7, §12, §27; `docs/handoffs/*.md`;
`docs/delivery/{SATIS,ALIS,FINANS,URETIM}_HAZIRLIK_PLANI_v1.md`; dört şeridin 2026-11-03 tarihli oturum mesajları
(§3); açık PR listesi (2026-11-03 07:10Z). Kod durumu: `main@2318317`.

Biçim `ALIS_HAZIRLIK_PLANI_v1.md` ile aynıdır. Yöntem "Proje modülleri" konuşmasından alındı: önce mevcut kod ve
kararlar taranır, "var/eksik" tablosu çıkarılır, sonra dilim dilim geliştirilir. **Sıfırdan yazılmaz**; Codex'in arka
plan kodu ve bayat taslak PR'ları önce donör olarak değerlendirilir (§9).

Kısaltmalar: **CL** Claude, **GP** ChatGPT (spec/kabul/golden), **SN** Sinan (karar). Seviye K1/K2/K3
(`GOYMEN_ERP_HIZLI_TESLIM_v4.md`). ✅ var ve çalışıyor · 🟡 kısmen / bağlanmamış · ❌ yok.

---

## 0. Şerit kaydı ve kurallar

- **SAT-D209 / FNS-D15 / ALS-D58:** dört şerit bu modüllerde yeni port, tablo veya ekran açmaz; bağlar "ertelendi:
  <modül> başlayınca" diye işaretlidir. SAT-D209'a göre "o modüllere başlanınca bağlantılar yeniden incelenir". Bu
  şeridin başlaması **KLN-D5** olarak kayda geçirilir (`SINAN_KARARLARI_2026-11-03_MENU.md`, PR #935); ertelenen bağlar
  artık burada (Sinan'ın açılış talimatı + koordinatör mesajı, 2026-11-03).
- **Sahiplik sınırı:** SAT-D209, ALS-D45/FNS-D1'deki "küçük ek" iznini bu modüller başlayana kadar askıya almıştı.
  2026-11-03 mesajlarında Finans ("Finans bu modüllerin dosyalarına dokunmaz; bağ yalnız sürümlü port/outbox") ve Alış
  ("Alış bu modüllerin dosyalarına dokunmaz") bu modüllerdeki işi bu şeride bıraktı. Kural: **kaynak tarafı** (Finans/
  Satış/Alış modülündeki kaynak sağlayıcı, olay, adaptör) o şeridin; **bu modüllerin içi** bu şeridin. Satır bazında
  çakışma çıkarsa ilgili şeritle yazışılır, gerekirse NEEDS-SINAN.
- **URT-D20** Stok'taki Üretim eklerini Üretim şeridine vermişti. Üretim şeridi 2026-11-03 mesajıyla bunları bu şeride
  bıraktı ("1–3'ü ben yazmayacağım"). Çakışma yok; Üretim kendi tarafını bu şeridin portuna bağlar.
- **İstisna ALS-D63:** Alış'ın üç politika kaydı (API-95 kabul toleransı, API-96 sipariş onay eşiği, API-97 fatura fiyat
  toleransı) Ayarlar'da yazılır; ALS-D63 yazar şeridi adlandırmaz, Alış şeridi 2026-11-03 mesajıyla bunları **kendisinin**
  yazacağını bildirdi. Bu şerit onları yazmaz; genel "modül politika kataloğu"
  tasarlanırsa Alış'a önceden yazılır (§6.3).
- **Emsal ALS-D1:** şeridin bütün geliştirmesi (backend, migration, OpenAPI, ekran) bu oturumda yapılır. Codex'in
  açık taslakları donördür; Codex dosyası olduğu gibi kopyalanmaz, güncel main'e göre yeniden kurulur.
- **Kod modülleri ayrı kalır.** Menü birleştirmesi yalnız ekran/navigasyon düzeyindedir (§1).
- **Review:** her PR'a bağımsız review alt ajanı; K3'te iki bağımsız exact-head review. Merge: 25 Ekim kararı
  (`SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md`) kapılarıyla.
- **Her PR açıklamasında "Modül bağlantıları" satırı** (SATIS_MODUL_BAGLANTILARI_v1.md §226 deseni): eklenen/değişen
  port, karşı modül, durum (✅/🟡/❌). Bağ yoksa "Modül bağlantıları: yok".
- **İnsan kapıları değişmez:** rol/yetki ataması, `OPEN-*` kapatma, production açma, secret/deploy Sinan'dadır.
  Kararsız nokta Issue #66'ya tek satır `NEEDS-SINAN:` olarak yazılır ve sıradaki dilime geçilir.

## 1. Menü (ekran düzeyi)

Kullanıcı talimatı (2026-11-03, bu oturum):

| Menü | İçerik | Kod modülleri |
|---|---|---|
| **Ayarlar** | Organizasyon · Kullanıcı ve Yetki · Ana Veriler · Denetim kaydı (dört alt başlık) | Organization, Identity, Settings, Audit |
| **Stok & Lojistik** | Stok pozisyonu, hareketler, etiket, sayım, transfer · yükleme, kantar, sevkiyat, irsaliye, iade, mal kabul | Inventory, Logistics |
| **Cari** | Cari kartlar, cari hesap takibi (ekstre) | Counterparty (+ Finans okuma) |
| **Muhasebe** | Kontrol, fişler, dönemler, mizan, hesap planı, **Cari Hesaplar** (ekstre) | Accounting (+ Finans okuma) |

- **Cari ekstresi iki yerden açılır (FNS-D14):** Cari > cari kart/cari hesap takibi ve Muhasebe > Cari Hesaplar. İkisi de
  aynı Finans kaynağını (`GET /api/v1/finance/party-ledger` API-05, `party-balance` API-04 #912) okur; yeni master
  açılmaz, Cari kartında hareket tutulmaz.
- **Ana rapor §4'ten sapma:** §4'te "Ana Veriler" (16) ve "Sistem Yönetimi" (17) ayrı menüdür; "Stok & Tahsis" (8) ve
  "Lojistik & Kantar" (10) ayrıdır. Kullanıcı talimatı daha yenidir ve geçerlidir. Koordinatör "karar dosyasına
  geçmeden Sinan'dan teyit al" dedi → kayıt K-1 sorusu olarak §11'de.
- Menü yetkiye göre çizilir (DEC-GOV-007); grup başlığının kendi yetkisi yoktur.

Bugünkü menü (`src/frontend/goymen-erp-web/src/shell/navigation.ts` `NAV_GROUPS`): Ayarlar grubu **var**
(Organizasyon, İş Merkezleri, Kullanıcılar ve yetkiler, İskonto limitleri, Vergi, Açılış stokları) ama alt başlıksız;
Denetim yok. Stok "Ürün ve stok" grubunda, Lojistik "Sevkiyat" grubunda, alış operasyon işleri "Satın alma"
grubunda, açılış stoku "Ayarlar"da — dağınık. Mizan rotası var, menüde yok (M-06 tarayıcı kabulü bekliyor). Raporlar
grubu bilerek yok (`navigation.ts:290`).

## 2. Bugünkü durum — "var / eksik" tabloları

### 2.1 Ayarlar › Organizasyon (AV-01)

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| Şirket okuma / bağlam | ✅ `GET /api/v1/organization/companies`, `/context` | 🟡 yalnız bağlam | Şirket yazma ❌ |
| Tesis (Site) liste/oluştur/düzenle | ✅ GET/POST/PUT `/sites` | ✅ `OrganizationSettingsPage.tsx` | |
| Lokasyon/depo kart, aktif-pasif | ✅ `/locations` (+ `/active-state`) | ✅ `LocationDetailPage.tsx` | Depo = Location (DEC-ORG-002) |
| Lokasyon operatörü | 🟡 PUT `/locations/{id}/operator`; uygunluk portu Organization'da stub | ❌ | Okuma #766, yetki kaynağı #767 (bayat taslak) |
| İş merkezi | ✅ `/work-centers` | ✅ `WorkCenterListPage/DetailPage` | |
| Vardiya / kapasite takvimi | ❌ | ❌ | URT-D11; `IOrganizationWorkCenterPlanningReferenceV1` v2 (Üretim MOD-06) |

### 2.2 Ayarlar › Kullanıcı ve Yetki (SY-01…04)

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| Kullanıcı ve rol okuma | ✅ `GET /api/v1/identity/access/users`, `/roles` (+ detay) | ✅ `IdentityAccessPage`, `IdentityRoleDetailPage` | |
| Kullanıcı oluştur/pasifleştir | ❌ (tablolar var) | ❌ | OPEN-013 (OIDC/MFA) |
| Rol-izin düzenleme, atama, kapsam | ❌ (yalnız seed + reconciler) | ❌ | **Atama Sinan'da**; ekran yalnız talep/öneri üretir |
| Erişim talebi (AccessGrantRequest) | 🟡 domain + tablo; uç yok | ❌ | |
| Onay matrisi / eşik / vekâlet (SY-04) | ❌ | ❌ | §12.3; OPEN-004/005/009 |

### 2.3 Ayarlar › Ana Veriler (Settings modülü, AV-02/03)

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| Vergi masteri | ✅ `/api/v1/settings/tax-master` | ✅ `TaxMasterPage.tsx` | |
| KDV/tevkifat oran listeleri | 🟡 `ApprovedTaxRateListCatalog` boş liste | ❌ | Satış 4d: seçim kapalı (SAT-D146) |
| İskonto limitleri | ✅ `/sales-discount-limits` | ✅ `SalesDiscountLimitsPage.tsx` | |
| Döviz kurları | ✅ `/exchange-rates` (liste, çözüm, düzeltme, TCMB içe aktarma; #906/#914) | ❌ **ekran yok** | TCMB kaynağı yapılandırmada kapalı (OPEN-012) |
| Üretim parametre / tolerans profilleri | ✅ uçlar + SQL | ❌ | #759 bayat |
| Birim / para birimi | 🟡 statik katalog (kg, TRY) | ❌ | |
| Satış/teklif politikaları, satış ayar düzenleyicileri | 🟡 statik `Approved*Catalog` | ❌ | SAT-D189; teklif kataloğu 4g verisi NEEDS-SINAN |
| Finans politika anahtarları, numara serisi | ❌ | ❌ | Finans MOD-08/API-13 |
| Varsayılan boya rengi, fire cinsi listesi | ❌ | ❌ | URT-D25, URT-D8 |
| Masraf/hizmet/işlem türü listeleri (API-98) | ❌ | ❌ | #66'da NEEDS-SINAN açık |
| Durum/iş akışı, kalite parametreleri | ❌ | ❌ | AV-03 |

### 2.4 Ayarlar › Denetim kaydı (SY-07)

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| Audit toplama (interceptor, bütünlük) | ✅ SQL | — | |
| Audit sorgulama | ✅ `GET /api/v1/audit/events` (`identity.audit.read`) | ❌ **ekran, capability, menü yok** | En ucuz eksik |
| Log/metrik/alarm, saklama | ❌ | ❌ | OPEN-017, OPEN-021 |

### 2.5 Cari

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| C-01 liste, C-02 oluştur + şirkete bağla | ✅ | ✅ `/cari`, `/cari/yeni`, `/cari/sirkete-bagla` | |
| C-02 düzenle (unvan, ticari ad, rol) | ❌ | ❌ | Yalnız iletişim ve vergi kimliği düzenlenebiliyor |
| Aktif/pasif, iletişim, vergi kimliği (talep+onay), müşteri grubu, e-belge profili | ✅ | ✅ | |
| C-03 Cari 360 | 🟡 yalnız çekirdek kart | 🟡 Bilgiler + Belgeler sekmesi | Bakiye/risk/sipariş/zaman çizgisi yok |
| C-04 Cari hesap takibi / ekstre | 🟡 Finans `party-ledger`; #912 bakiye özeti | 🟡 Cari detaydan Finans sayfasına link | Kendi rotası/menüsü yok; devir bakiyesi yok (OPEN-018) |
| C-05 açık kalem, C-08 risk | 🟡 Finans'ta | 🟡 Finans ekranlarında | Cari 360'ta sekme yok; OPEN-010 |
| Banka/IBAN, çift rol (müşteri+tedarikçi) | ❌ | ❌ | Finans MOD-06, DEC-CAR-001 |
| Tedarikçi portunda fason rolü + vade | 🟡 Cari'de `Tolling` rolü var, Alış portu açmıyor | — | Alış MOD-08 |
| C-09 mutabakat, C-11 birleştirme, C-12 takip | ❌ | ❌ | |

### 2.6 Ürün & Hizmet

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| U-01/U-02 ürün liste, kart, taslak, aktivasyon, içe aktarma | ✅ | ✅ `/urun…` | **Ürün runtime varsayılan kapalı** (`ProductRuntimeOptions.IsEnabled => Issuer is not null`) → Satış 5a–5c ve Alış 503 |
| Kalem tipi (Demir/Hurda/Hizmet) | ✅ `/product-item-types` | ✅ panel | |
| U-03/U-04 Hizmetler | ❌ ayrı katalog yok; yalnız `ItemType=Service` | ❌ | §7.4 menüsü `Ürünler \| Hizmetler \| Fiyatlar` |
| U-05 Fiyat listeleri | ✅ sürümlü, kapsam, kademe | ✅ `/fiyat-listeleri` | Onay/planlı durum, iskonto zinciri, min/max yok |
| U-07 Paket standartları | ❌ (`ProductPackageStandardStatusV2 = Unavailable`) | ❌ | Üretim URT-D27 |
| U-08 Birim çevrimi | ❌ | ❌ | Alış `IProductPurchaseUnitConversionV1` (U-A1) |
| U-09 Teknik aile / snapshot | 🟡 v1 | 🟡 | v2 (reçete-ürün bağı, kg önceliği) Üretim MOD-02; donör #749 #771 #763 #760 #744 #773 |
| Boya ürün sınıfı | ❌ | ❌ | URT-D25 |
| Tedarikçiye özel alış fiyatı, hizmet ürünleri | ❌ | ❌ | Alış MOD-09 |

### 2.7 Stok & Lojistik

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| ST-01 pozisyon, ST-02 etiket 360, ST-04 hareketler | ✅ | ✅ | "Yükleme için tutulan" ve "kalite bloğu" kırılımı yok (§7.6) |
| ST-05 sayım, hurda, etiket dönüşümü/sınıfı, açılış stoku | ✅ | ✅ | |
| AL-03/05 fiziksel tahsis (etiket bazlı) | ✅ yalnız `sales.order-line` türü | 🟡 | AL-01/02/04/06 ❌; #788 bayat |
| Üretim girdi tahsisi (`manufacturing.plan-input`) | ❌ | — | **Üretim U3-a'yı engelliyor** |
| Miktar bazlı rezervasyon | ❌ | ❌ | Satış 3h; kapsam NEEDS-SINAN (#66'da) |
| ST-07 transfer (iki bacak) | 🟡 domain/store/migration yazılı, **`AddInventoryTransfers()` çağrılmıyor**, host adaptörü/izin/rota yok | ❌ | #714/#715 karar PR'ı, #762; fason gönderimi (URT-D17) buna bağlı |
| ST-06/Q-04 kalite/sistem blokajı | ❌ (`InitialBlock="Free"` sabit) | ❌ | #750; Alış A3-KAPI |
| Kabul miktarı v2 | ❌ | ❌ | #772–776, #782 |
| Soy ağacı (üretim kenarı), 2. kalite/hurda sınıfı yazımı | 🟡 writer var, Üretim'den yazılmıyor | — | URT-D8/D14 |
| ST-08 gelecek arz v2 | 🟡 v1 | 🟡 | Alış MOD-14 |
| ST-03 depo görünümü, sarf profili | ❌ | ❌ | #779/#792 |
| L-01…L-08 yükleme, kantar, sevkiyat, teslim, irsaliye | ✅ | ✅ | Talimat stok tutmuyor; kantar yalnız satış talimatına bağlı |
| L-12 müşteri iadesi | ✅ | ✅ | Eksik/fazla/hasar farkı yok |
| L-09 beklenen alış | ✅ okuma | ✅ | |
| L-10 mal kabul (Lojistik'te tek kayıt), kantar bağı | ❌ (kabul Alış'ta, ✅ çalışıyor) | 🟡 | Alış MOD-07, B-7 |
| Tedarikçiye iade çıkışı | ❌ | ❌ | Alış MOD-06 |
| L-11 transfer çıkış belgesi | 🟡 servis var, uç yok | ❌ | |
| L-04 araç/sürücü/kapı | ❌ | ❌ | Alış API-31 şimdilik kendi kaydında |
| Sevkiyat kanıtı → Work | ❌ | — | #748 |

### 2.8 Çalışma Merkezi (İş/Onay)

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| D-05 Onaylarım (salt okuma) | ✅ `GET /api/v1/work/approvals` + detay; 4 adaptör (Finans fatura içe aktarma, Satış sipariş taslağı, Stok sayım farkı, Stok hurda) | ✅ `/onaylarim` | Karar her modülün kendi ucunda |
| Work modülü (desired-state, inbox, outbox) | 🟡 domain + SQL var, **`AddWorkModule` Program.cs'te çağrılmıyor** | — | Finans `WorkDelivery` kayıtları ve #748 havada |
| Outbox dağıtıcısı (ERP-000F) | ❌ sahibi yok | — | Satış/Alış fatura olayları, Finans olayları bekliyor |
| Eksik adaptörler | 🟡 Alış MOD-10, Satış 5j, Üretim MOD-05, Finans MOD-09 | | Adaptörü **tüketici şerit** yazar; Work'te yeni kaynak türü gerekirse bu şerit |
| Ortak onay motoru (karar türleri, vekâlet, eskalasyon, SLA) | ❌ | ❌ | §12.3; OPEN-004/005/009 |
| D-02 Benim İşlerim, D-06 istisnalar | ❌ | ❌ | |
| Kapasite/vardiya kuyruğu | ❌ | ❌ | URT-D11 → §2.1 Organizasyon |

### 2.9 Belgeler ve Dijital Arşiv

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| Kayda bağlı yükleme, liste, içerik, sürüm | ✅ `/api/v1/documents…` | ✅ `DocumentsPanel` | 4 sahip türü: satış siparişi, satın alma siparişi, cari, stok etiketi |
| Mevcut belgeyi ikinci kayda bağlama | 🟡 içerik hash'le tekil, uç yok | ❌ | "Tek dosya, çok kayıt" |
| Sahip türü: üretim emri/plan, finans (dekont, ekstre, çek, kredi) | ❌ | ❌ | Üretim MOD-10, Finans MOD-10 |
| Oluşturulan PDF'lerin kasaya yazılması | ❌ (satış sipariş PDF'i "stored nowhere") | | |
| İrsaliye/fatura/satın alma/iş emri PDF'i | ❌ | ❌ | Satın alma PDF'i Alış A2b'de |
| Dosya türleri XLSX/DOCX/e-posta | ❌ (yalnız PDF/PNG/JPEG) | | §25.7; Finans MT940/Excel (FNS-D13) |
| Metadata (tür, gizlilik, geçerlilik) | ❌ | ❌ | |
| B-06 Merkezi arşiv, B-07 Belge 360 | ❌ | ❌ | |
| OCR, AI karşılaştırma, paylaşım | ❌ | ❌ | OPEN-020 |
| S3 adaptörü | ❌ | | Kullanıcı kararı 2026-09-30: NAS/sunucu, bulut yok — gerek yok |
| Saklama/legal hold | ❌ | | OPEN-018/021 |

### 2.10 Muhasebe

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| M-07 dönem (öner/onay/kapa) | ✅ | ✅ menüde | Finans dönem kuralı kullanıyor |
| M-04 fiş liste/detay, normal fiş taslak+onay, ters kayıt | ✅ | ✅ | Tek kaynak sağlayıcı: Finans elle tahsilat |
| **Faturadan 120/320 taslak fiş** | ❌ | — | SAT-D208, ALS-D59, FNS-D14; taşıma yolu seçilmedi |
| Finans fiş kaynakları (banka masrafı, kur farkı, virman, kredi faizi) | ❌ | — | Finans MOD-07 |
| M-01 kontrol merkezi | 🟡 posting-queues | 🟡 | Belge/dönem özeti yok |
| M-06 mizan | ✅ `GET /trial-balance` | 🟡 rota var, menüde yok | |
| M-05 hesap planı | 🟡 Mikro hesap planı aynası, ekran yok | ❌ | FNS-D7: 120/320 eşlemesi mali müşavir teyidine kadar anlam eşlemesi |
| **M-03 Cari Hesaplar (ekstre)** | ❌ (Finans API-04/05 okunacak) | ❌ | FNS-D14 (§1) |
| Kur farkı / dönem sonu değerleme | 🟡 PendingValuations tablosu | | |
| M-02, M-08, M-09, KDV/vergi | ❌ | ❌ | OPEN-016, OPEN-008, §19.3 #20/#21/#27 |

### 2.11 Raporlar

| Yetenek | Backend | Frontend | Not |
|---|---|---|---|
| R-01 Rapor merkezi | ❌ | ❌ (bilerek yok) | §7.12, §13.5 |
| Satış raporları (UI-80) | ✅ `/api/v1/sales/reports/*` | ✅ `/satis/raporlar` | |
| Alış (API-90…92), Üretim (API-40…42), Finans (API-99, API-45) raporları | ❌ | ❌ | Uçlar ilgili şeritte; merkez bu şeritte |
| Stok/lojistik raporu | 🟡 yalnız operasyonel okuma | 🟡 | |
| R-04 veri kalitesi, R-05 zamanlanmış/dışa aktarım | ❌ | ❌ | |

## 3. Dört şeridin bu modüllerden beklediği bağlar (önce kapatılacak)

Kaynak: handoff dosyaları, hazırlık planları ve 2026-11-03 oturum mesajları (Satış `session_01SPADrz…`, Alış
`session_015Usc2oBT…`, Finans `session_015Usc2qRa…`, Üretim `session_015Usc2oQC…`). "Tüketici bağlar" sütunu: o şerit
kendi tarafını bu şeridin portuna bağlar.

| # | Bağ | Tüketici | Üretici modül | Port / sözleşme | Seviye | Durum |
|---|---|---|---|---|---|---|
| B-01 | Üretim girdi tahsisi: ayır / serbest bırak / tüket + okuma; talep türü `manufacturing.plan-input` (CHECK geri alınabilir migration); bir etiket miktarla çok plana; toplam ≤ serbest; müşteri malı yalnız aynı müşterinin fason planına | Üretim U3-a (URT-D3/D21/D22) | Stok | yeni `IInventoryManufacturingAllocationPortV1` (öneri) | K3 | ❌ **engelliyor** |
| B-02 | Fason sipariş cari kilidi; müşteri malı anahtarı | Satış 2e, Üretim U5-b | Stok (+Üretim) | `ISalesSubcontractOrderBindingReadPortV1` host adaptörü | K3 | ❌ bugün 503 |
| B-03 | Çıktı soy ağacı kenarı + 2. kalite/hurda etiket sınıfı | Üretim (URT-D8/D14, API-22/27) | Stok | `IInventoryManufacturingRealizationParticipantV1` v2, `IInventoryLabelGenealogyWriterV1` | K3 | 🟡 |
| B-04 | Faturadan 120/320 taslak fiş (aynı olay, onayla kesinleşir, elle ikinci kayıt yok); fatura iptali/iadesinde ters fiş (Finans F2b, SAT-D96) | Satış S5, Alış A5, Finans | Muhasebe | `sales/purchasing.invoice.finalized.v1` tüketicisi; ara dönem §6.1 | K3 | ❌ |
| B-05 | M-03 Muhasebe › Cari Hesaplar ekranı (API-04/05 okur) + Cari tarafında ekstre rotası | Finans | Muhasebe, Cari (ekran) | `party-ledger`, `party-balance` (#912) | K1/K2 | ❌ |
| B-06 | Ürün runtime'ını açma: `ProductPilotIssuerBinding` (yerel kullanıcı ↔ dış kimlik) + `CursorSigningKey` | Satış 5a–5c, Alış | Ürün | yapılandırma | K3 (kimlik/yetki + secret; **secret ve grant Sinan'da**, dev/test dahil) | 🟡 |
| B-07 | Birim çevrimi + varsayılan fiyat birimi; "çevrim yok" açıkça, varsayılan katsayı **asla** | Alış A3/A5 (ALS-D51/D55/D60) | Ürün | `IProductPurchaseUnitConversionV1` | K3 | ❌ |
| B-08 | Paket içi adet önerisi (URT-D27), boya ürün sınıfı (URT-D25) | Üretim U2-b | Ürün | `ProductPackageStandard` v2 | K3 | ❌ (#930'da elle) |
| B-09 | Teknik snapshot v2 (reçete-ürün bağı, kg önceliği) | Üretim MOD-02 | Ürün | `IProductManufacturingTechnicalSnapshotV1` v2 | K3 | ❌ |
| B-10 | KDV/tevkifat oran listeleri (veri + bakım ekranı) | Satış 4d | Ayarlar | `ISettingsTaxRateListReaderV1` | K3 (vergi) | 🟡 boş |
| B-11 | Kurlar ekranı (liste, düzeltme, yarım gün işareti, yeniden doğrulama) | Satış, Alış, Finans | Ayarlar | mevcut uçlar | K2 | ❌ ekran |
| B-12 | Satış ayar düzenleyicileri (iskonto, tolerans, eşik — SAT-D189); teklif politika kataloğu (4g, veri NEEDS-SINAN) | Satış | Ayarlar | `ISettingsSalesQuotePolicyProviderV1` | K3 | 🟡 |
| B-13 | Finans politika anahtarları (FNS-D9/D11/D12, onay eşikleri) + dekont/makbuz numara serisi (şirket bazlı) | Finans F3/F8 | Ayarlar | ADR-SET-002 | K3 | ❌ |
| B-14 | Varsayılan boya rengi, fire cinsi listesi | Üretim U2-b | Ayarlar | yeni sürümlü liste (tablo + migration) | K3 | ❌ |
| B-15 | Vardiya/kapasite takvimi (08:00, 8 saat, tek vardiya, hafta sonu/tatil kapalı, hat bazında değişebilir) | Üretim U4 | Organizasyon | `IOrganizationWorkCenterPlanningReferenceV1` v2 | K3 | ❌ |
| B-16 | Tedarikçi portunda fason rolü + ödeme vadesi | Alış MOD-08 | Cari | `IPurchasingPartyAccountPortV1` v2 | K3 | 🟡 |
| B-17 | Çift rol (müşteri+tedarikçi), tedarikçi IBAN doğrulaması | Finans MOD-06 | Cari | `IFinancePartyAccountPort` sürüm artışı | K3 | ❌ |
| B-18 | Kalite bloğu + kabul miktarı v2 (A3-KAPI) | Alış A3 | Stok, Ürün | #750, #772–776, #782 donör | K3 | 🟡 |
| B-19 | Tedarikçiye iade çıkışı (blokajlı stoktan da) | Alış MOD-06 | Stok | yeni çıkış portu | K3 | ❌ |
| B-20 | ST-08 "tedarikçide hazır" + tahsiste "aynı ton bir kez" (ALS-D30) | Alış MOD-14, Satış | Stok | Alış'ın `IPurchaseSupplyReferencePortV1` v2'sini tüketir | K3 | ❌ |
| B-21 | L-09/L-10 kabulün tek kayıt olması, kantar fişi bağı, iade sevki | Alış MOD-07, B-7 | Lojistik | yeni | K3 | ❌ |
| B-22 | Fasona giden transfer (mülkiyet şirkette) + Alış fason siparişine bağ | Üretim (URT-D17), Alış A4 | Stok | `IInventoryTransferWriterV1` (bağlanmamış) | K3 | ❌ (#714/#715 kararı önce) |
| B-23 | Miktar bazlı rezervasyon | Satış 3h | Stok | tanımsız | K3 | ❌ NEEDS-SINAN (#66'da) |
| B-24 | Work: Work runtime'ını açma + outbox dağıtıcısı (ERP-000F) sahipliği | Finans MOD-11, Satış/Alış fatura olayları | Work/platform | `AddWorkModule`, dispatcher | K3 | ❌ sahibi yok (§11 K-3) |
| B-25 | Work yeni kaynak türleri (gerekirse) | Satış 5j, Alış MOD-10, Üretim MOD-05, Finans MOD-09 | Work | `INativeApprovalReadAdapterV1` | K3 (modüller arası sözleşme) | 🟡 adaptörleri tüketici yazar |
| B-26 | Belgeler sahip türleri: üretim emri/plan, finans belgeleri; XLSX/MT940 | Üretim MOD-10, Finans MOD-10 | Belgeler | `IDocumentOwnerLinkPortV1` | K3 (dosya türü güvenliği) | ❌ |
| B-28 | Finans fiş kaynakları (banka masrafı, kur farkı, kasa/virman, kredi faizi, masraf ödemesi) + F2-OB olay tüketicisi | Finans MOD-07 | Muhasebe | `…JournalSourceProviderV1` / Finans outbox | K3 | ❌ |
| B-29 | Vergi masteri ve para profili okuması için Ayarlar portu (bugün yalnız mevcut sağlayıcı) | Alış MOD-05 | Ayarlar | `ISettingsPrerequisiteProvider` genişlemesi | K3 | ❌ ertelendi |
| B-30 | Tedarikçiye özel alış fiyatı/fiyat listesi, hizmet ürünleri | Alış MOD-09 | Ürün | N-05 | K3 (fiyat) | ❌ ertelendi |
| B-31 | Cari bağlarının yeniden incelenmesi (5d/5e/5f/5h) ve Ürün bağlarının (5a–5c) yeniden incelenmesi — "ertelendi: <modül> başlayınca" | Satış | Cari, Ürün | mevcut portlar | K2 | 🟡 SQL testleri Satış'ta |
| B-32 | Minimum stoktan satın alma talebi önerisi (MINSTOK) — "Stok başlayınca ayrıca ele alınır" | Alış (ALS-D61) | Stok | — | — | ⛔ kapsam kararı Sinan'da (§11 K-7) |
| B-27 | Maliyet izi (süre, hat, fire, lot ham verisi) | Üretim, Finans | Muhasebe | — | — | ⛔ OPEN-008 (yöntem açık karar; yapılmaz) |

Değiştirilmeyecek çalışan bağlar (dokunmadan önce sahibine haber): mal kabul → stok ortak işlemi
(`IPurchaseGoodsReceiptTransactionV1` / `IInventoryGoodsReceiptParticipantV1`), `IDocumentOwnerLinkPortV1`
(purchasing), Finans risk portu, kur çözücü `ISettingsExchangeRateResolverV1`, Cari'nin Satış/Alış/Finans/Lojistik
portları.

## 4. "Hazır" tanımı (modül başına)

Bir modül "hazır" sayılır: (1) §3'teki bağları kapanmış ya da açık karar nedeniyle bilinçli fail-closed; (2) master
§27'deki birincil ekranları gerçek API'ye bağlı, error/empty/loading/conflict durumlarıyla; (3) her komutta
CompanyId, yetki, expected version, idempotency, audit; (4) gerçek SQL kabul testi; (5) menüde §1'deki yerinde.

## 5. Kod ve teknoloji düzeltmeleri (TEK)

| ID | Konu | Not |
|---|---|---|
| TEK-01 | `AddInventoryTransfers()` ve `AddWorkModule` Program.cs'e bağlı değil | Bağlarken izin manifesti, host adaptörü, rota, SQL testi birlikte |
| TEK-02 | Ürün runtime issuer'sız kapalı | Geliştirme/test ortamında açık, production aktivasyonu Sinan |
| TEK-03 | Ekranlar elle `fetch` + decoder; generated client (Issue #122) yok | Bu şerit yeni ekranları mevcut desenle yazar; #122'ye ayrı geçiş |
| TEK-04 | Birim/para birimi statik katalog | AV-02 dalgasında SQL'e |
| TEK-05 | Organization lokasyon operatörü uygunluğu stub | #766/#767 donörüyle |

## 6. Teknik tercihler (geri alınabilir; ASSUMPTION-KLN-*)

### 6.1 Muhasebe fiş taşıması (Finans sorusu MOD-07)
**Hedef SAT-D208'deki uygulama ayrıntısıdır:** kaynak olay Satış/Alış outbox'ından yayınlanan sürümlü
`*.invoice.finalized.v1`; Muhasebe bunu inbox/dedup ile tüketir; idempotency anahtarı fatura kimliği + sürümü. Bu plan
hedefi değiştirmez.

**Ara dönem (ERP-000F dağıtıcısı yokken), öneri:** SAT-D208'in Finans için tarif ettiği ara yolun aynası — Muhasebe
sürümlü bir **kabul portu** açar (öneri ad `IAccountingSourceDocumentAdmissionPortV1`); kaynak şerit kesinleşmede
"Muhasebe kabulü bekliyor" durumunu kalıcı tutar ve portu aynı anahtarla çağırır. Muhasebe taslak fişi yalnız kendi
tablosuna yazar (modül başka tabloya yazmaz); aynı anahtar ikinci fiş üretmez. Finans'ın manuel tahsilat için bugün
kullandığı salt-okuma `…JournalSourceProviderV1` deseni Finans kaynakları (B-28) için sürer. Dağıtıcı gelince her iki ara
yol outbox tüketicisine geçer. İptal/iade ters fişi aynı anahtar ailesiyle (kaynak iptal olayı) üretilir; mevcut fiş
silinmez. Bu ara yol Satış ve Alış şeritlerine yazılarak netleşir (kaynak tarafı onların). 120/320
hesap eşlemesi FNS-D7 gereği "anlam eşlemesi" kalır; gerçek hesap kodu mali müşavir teyidine kadar etkinleştirilmez.

### 6.2 Tahsis motoru genişlemesi
Talep türü kapalı bir liste (CHECK) olarak kalır: `sales.order-line` + `manufacturing.plan-input`. Serbest miktar
formülü tek backend calculator'da; Satış ve Üretim aynı serbest miktarı okur. Müşteri malı kuralı tahsis anında
doğrulanır (UI tek güvenlik değildir).

### 6.3 Ayarlar politika kaydı
Yeni politikalar `ApprovedSalesPolicyCatalog` deseninde **sürümlü, şirket kapsamlı, auditli** kayıt (DEC-SET-001).
Ortak bir "modül politika kataloğu" çatısı tasarlanırsa önce Alış şeridine (API-95/96/97 sahibi) yazılır.
Değer seçilmez; değerler Sinan kararından gelir, gelmeyen anahtar "tanımsız → işlem kapalı" kalır.

## 7. Ekranlar (UI) — öncelikli

| UI | Ekran | Menü | Dayandığı backend |
|---|---|---|---|
| UI-K01 | Menü yeniden düzeni (Ayarlar alt başlıkları, Stok & Lojistik grubu, Muhasebe › Cari Hesaplar, Mizan) | — | — |
| UI-K02 | Denetim kaydı listesi + filtre (correlation, varlık) | Ayarlar › Denetim | ✅ hazır |
| UI-K03 | Kurlar | Ayarlar › Ana Veriler | ✅ hazır |
| UI-K04 | Muhasebe › Cari Hesaplar + Cari › Cari hesap takibi (aynı bileşen, iki giriş) | Muhasebe, Cari | Finans API-04/05 |
| UI-K05 | KDV/tevkifat listeleri, satış ayarları, Finans anahtarları, boya/fire listeleri | Ayarlar › Ana Veriler | B-10…B-14 |
| UI-K06 | Üretim parametre/tolerans profilleri | Ayarlar › Ana Veriler | ✅ (#759 sonrası) |
| UI-K07 | Transfer (ST-07/L-11) | Stok & Lojistik | B-22 |
| UI-K08 | Hesap planı (salt okuma ayna) | Muhasebe | ✅ ayna |
| UI-K09 | Cari 360 sekmeleri (cari hesap, risk, siparişler) | Cari | Finans/Satış okuma |
| UI-K10 | Hizmetler görünümü (ItemType=Service filtresi) | Ürün & Hizmet | ✅ |
| UI-K11 | Rapor merkezi iskeleti (veri tarihi/kur/filtre üst bilgisi) | Raporlar | — |
| UI-K12 | Merkezi arşiv (B-06) | Belgeler | yeni liste ucu |

## 8. Sıralı yol haritası (dalgalar)

Kural: önce §3'teki **engelleyen** bağlar, sonra kullanıcının verdiği modül sırası. Her dilim ayrı PR; K3 dilimler iki
review. Dalga içinde sıra yukarıdan aşağıya.

**Dalga K0 — engelleyen bağlar ve ucuz kazanımlar**
1. K0-1 Menü düzeni (UI-K01) — K1. *K-1 teyidi beklenmeden geri alınabilir navigasyon değişikliği olarak; teyit
   gelmezse kod tek dosyada geri alınır.*
2. K0-2 Stok: üretim girdi tahsisi (B-01) — K3, migration.
3. K0-3 Stok: fason müşteri malı + `ISalesSubcontractOrderBindingReadPortV1` adaptörü (B-02) — K3; Üretim U5-b ile.
4. K0-4 Stok: üretim çıktısı soy ağacı + etiket sınıfı (B-03) — K3.
5. K0-5 Muhasebe: satış/alış faturası 120/320 fiş kaynak sağlayıcı sözleşmesi (B-04, §6.1) — K3; fatura çekirdekleri
   (Satış S5, Alış A5) gelince tüketici bağlanır.
5b. K0-5b Ürün birim çevrimi (B-07) ve KDV/tevkifat listeleri (B-10) — K3; fatura dalgalarının (Alış A3/A5, Satış
   faturası) önündeki en yakın engeller olduğu için K0'a alındı.
6. K0-6 Muhasebe › Cari Hesaplar + Cari ekstre rotası (B-05, UI-K04) — K2.
7. K0-7 Ürün: runtime açma (B-06) — K3; issuer bağlama ve imza anahtarı secret/grant olduğu için Sinan kapısı.

**Dalga K1 — Ayarlar**
K1/K2: Denetim ekranı (UI-K02) · Kurlar (UI-K03, B-11) · üretim profili ekranları (UI-K06, #759 donör).
K3: satış ayarları (B-12) · Finans anahtarları + numara serisi (B-13) · boya/fire listeleri (B-14) · vardiya/kapasite
takvimi (B-15) · vergi/para profili portu (B-29) · birim/para birimi SQL'e (TEK-04, migration) · lokasyon operatörü
(#766/#767 donör, yetki kaynağı) · kullanıcı/rol **talep** ekranları (atama Sinan'da) · SY-04 onay matrisi
(OPEN-004/005/009 değerleri olmadan yalnız iskelet).

**Dalga K2 — Cari**
Cari bağlarının yeniden incelenmesi (B-31) · fason rolü + vade (B-16) · çift rol + IBAN (B-17) · cari düzenleme (unvan/rol; sicil değişikliğinde karşılaştırma+onay,
§8.5) · Cari 360 sekmeleri (UI-K09) · C-11 mükerrer/birleştirme ön kontrolü.

**Dalga K3 — Ürün & Hizmet**
(B-07 K0'da) · Ürün bağlarının yeniden incelenmesi (B-31) · paket standardı + boya sınıfı (B-08) · teknik snapshot v2 (B-09; #749/#771/#763 donör) · Hizmetler
görünümü (UI-K10) · alış fiyatı/hizmet ürünleri (B-30) · fiyat yaşam döngüsü tamamlama.

**Dalga K4 — Stok & Lojistik**
Kalite bloğu + kabul v2 (B-18; #750, #772–776, #782 donör) · transfer bağlama + fason gönderimi (B-22, TEK-01; #714/#715
kararı önce) · tedarikçiye iade çıkışı (B-19) · ST-08 v2 (B-20) · L-09/L-10 kabul + kantar (B-21) · miktar bazlı
rezervasyon (B-23, karar gelirse) · "yükleme için tutulan"/blok kırılımı (§7.6) · L-04, L-12 farklar, ST-03, sarf profili.

**Dalga K5 — Çalışma Merkezi**
Work runtime'ı açma + sevkiyat kanıtı (#748 donör) · outbox dispatcher (B-24, sahiplik K-3 kararıyla) · gerekli yeni
kaynak türleri (B-25) · D-02 Benim İşlerim iskeleti · ortak onay motoru (§12.3; eşik değerleri açık karar).

**Dalga K6 — Belgeler**
Sahip türleri (B-26) · ikinci kayda bağlama · oluşturulan PDF'leri kasaya yazma · dosya türleri (XLSX/DOCX; güvenlik
taraması) · metadata · merkezi arşiv (UI-K12). OCR/AI (OPEN-020) ve saklama (OPEN-018/021) kapalı kalır.

**Dalga K7 — Muhasebe (kalan)**
Finans fiş kaynakları (B-28) · mizan menüye (M-06 tarayıcı kabulü) · kontrol merkezi genişletme · hesap planı
ekranı (UI-K08) · kur farkı değerlemesi kaynağı. KDV/M-02/M-08/M-09 karar kapısı arkasında.

**Dalga K8 — Raporlar**
Rapor merkezi iskeleti (UI-K11) · Satış raporlarının merkeze bağlanması · Alış/Üretim/Finans rapor uçları geldikçe
kayıt · stok raporları · R-05 kuyrukta dışa aktarım.

## 9. Codex'in bayat taslak PR'ları (2026-10-26'dan beri)

Bu şeridin modüllerine dokunanlar donör olarak okunur; işe yarayan kısım güncel main üzerinde yeni `claude/*` dalında
yeniden kurulur, PR açıklamasında donör PR anılır. Kapatma/üstlenme kararı koordinatördedir (öneri olarak bildirilir).

| PR | Konu | Dalga |
|---|---|---|
| #766, #767 | Organizasyon lokasyon operatörü okuma / yetki kaynağı | K1 |
| #759 | Üretim tolerans profili kalıcılığı | K1 |
| #752, #795 | Cari lokasyon operatörü açıklaması | K2 |
| #749, #771, #763, #760, #744, #773 | Ürün aileleri, geometri snapshot v2, nominal boy, kabul miktarı snapshot | K3 |
| #772–#776, #782 | Kabul miktarı v2 zinciri | K4 |
| #750 | Kalite bloğu | K4 |
| #762, #714, #715 | Transfer miktar segmenti + transfer kararları | K4 |
| #788 | AL-04 tahsis okumaları | K0/K4 |
| #779, #792 | Sarf profili | K4 |
| #748 | Stok sevkiyat kanıtı → Work | K5 |

## 10. Test planı

Her dilimde: domain unit + uygulama servis testi; gerçek SQL kabul testi (CompanyId izolasyonu, idempotency replay,
expected version çakışması); HTTP yetki testi (izinsiz 403, başka şirket 404); ekranlarda vitest + a11y; K3'te başarısızlık
yolu kanıtı (fail-closed). Port değişikliğinde sözleşme sürüm testi ve tüketici şeride haber.

## 11. Sorular (NEEDS-SINAN — #66'ya tek satır)

| ID | Soru | Etkisi |
|---|---|---|
| K-1 | Menü düzeni (§1) karar dosyasına kayıt (`SINAN_KARARLARI_2026-11-03_KALAN_MODULLER.md`): Ayarlar = Organizasyon + Kullanıcı ve Yetki + Ana Veriler + Denetim; Stok & Lojistik tek menü; Cari ve Muhasebe ayrı, ekstre iki yerden. Ana rapor §4'ten sapma onayı | K0-1 geri alınabilir yapılır |
| K-2 | Muhasebe fiş taşıması §6.1: hedef SAT-D208 (outbox + inbox/dedup) aynen; ara dönemde Muhasebe kabul portu (SAT-D208'in Finans ara yolunun aynası) — teknik, ASSUMPTION olarak ilerler; Satış/Alış şeritlerine yazılır | K0-5 |
| K-3 | Outbox dağıtıcısı (ERP-000F) ve Work runtime'ı kimde? Öneri: bu şerit (Work modülü burada) | B-24 |
| K-4 | Transfer kararları #714/#715 (sahiplik sınırı, yaşam döngüsü) — merge/teyit | B-22 |
| K-5 | Miktar bazlı rezervasyon kapsamı (#66'da açık, Satış 3h) | B-23 |
| K-6 | Teklif politika verisi (kayıp nedenleri, varsayılan geçerlilik — 4g) ve API-98 listeleri (#66'da açık) | B-12 |

| K-7 | MINSTOK (ALS-D61: "Stok modülü başlayınca ayrıca ele alınır") Stok kapsamına girsin mi? | B-32 |

K-1 PR #935 ile kayda geçiyor (KLN-D1…D5; Sinan teyidi koordinatör aracılığıyla, 2026-11-03).

Rol/yetki ataması, OPEN-* kapatma, production açma bu planla seçilmez.

## 12. Durum (2026-11-03)

- Analiz tamam (bu belge). Kod değişikliği yok.
- Sıradaki dilim: K0-1 menü düzeni, ardından K0-2 Stok üretim girdi tahsisi (port biçimi önce Üretim şeridine yazılır).
