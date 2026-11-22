# Rol–Yetki Taslağı v1.2 (bütün modüller)

- **Durum:** TASLAK v1.2. Sinan'ın S-01–S-20 cevaplarıyla (ROL-D1–ROL-D20,
  `docs/decisions/SINAN_KARARLARI_2026-11-22_ROL_YETKI.md`) ve Y-01–Y-05 cevaplarıyla (ROL-D21–ROL-D23, AI-D36,
  `docs/decisions/SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`) güncellendi. Bu belge **karar değildir**; yeni karar kimliği
  üretmez, `KARAR_SICILI.md`'yi, `docs/decisions/**`'ı, seed'i, manifesti veya kodu değiştirmez.
- **Dayanak:** YOL-D2, YOL-D20, ROL-D1–ROL-D23 ve AI-D36. Dalga 1, dilim W1-D.
- **İnsan kapısı:** Hiçbir rol, izin veya grant bu belgeyle etkinleşmez. Rol tanımı, rol seed'i, grant, Critical sınıfı
  ve gerçek kişi ataması AGENTS.md "Değişmeyen insan kapıları" gereği Sinan'ın somut kararını ister. Taslak atama öncesi
  kişi listesiyle Sinan'a yeniden sunulur (ROL kararları "Sınırlar").
- **Makine-okunur eş:** [`ROL_YETKI_TASLAGI_v1.csv`](ROL_YETKI_TASLAGI_v1.csv) — her izin için bir satır; §2.3 matrisi o
  dosyadan üretilmiştir. Çelişki olursa CSV'deki hücre esas alınır ve belge yeniden üretilir.

## v1.2 değişiklik özeti

| Karar | Taslağa etkisi |
|---|---|
| ROL-D21 | `sales.orders.margin.read` = GM + Satış Müdürü (SAT-D250/SAT-D276 (2) "GM + Admin" yerine); Sistem Yöneticisi görmez. |
| ROL-D22 | `settings.policies.publish-critical` = Sistem Yöneticisi. |
| ROL-D23 | Sistem Yöneticisi acil erişimi: süreli, gerekçeli, başka yetkilinin onayladığı, denetim kayıtlı, süre bitince kalkan erişim ataması; kalıcı iş izni değil. |
| Y-02 (ROL-D19 sonucu) | `settings.sales-discount-limits.manage` = GM + Sistem Yöneticisi. |
| Y-05 (ROL-D14 sonucu) | Kantar isteğe bağlı rol olarak kalır. |
| AI-D36 | `ai.decisions.read` bütün kullanıcı rollerine önerilir. İzin bugün `ErpPermissions`'ta **kayıtlı değildir**; matrise satır olarak girmez, kaydedildiğinde `ai.panel.use` ile aynı sütunlara eklenir (§4). |

## v1.1 değişiklik özeti

| Karar | Taslağa etkisi |
|---|---|
| ROL-D1 | Muhasebe Müdürü (MUHM) standart rol. |
| ROL-D2 | Satış Destek, Satış Temsilcisi (SAT) rolüne katıldı. |
| ROL-D3 | Uygun onaycı kalmazsa onay **ilgili modül müdürü ve Genel Müdür**'e düşer; yoksa sahipsiz onay (YOL-D17). Küçük kadro istisnası yok. |
| ROL-D4 | GM vekili, ilgili izne sahip modül müdürü; Sistem Yöneticisi vekil olamaz. |
| ROL-D5 | **Yeni rol: Operasyon Müdürü (OPM)** — sevk, depo ve üretim müdürlüğü tek rolde. Sayım, hurda, transfer, irsaliye, plan üstü sevk, tartım farkı ve üretim planı onayları OPM'ye geçti; GM yedek. SAT-D191 ara dönem kayıt hücreleri (açılış stoğu, üretim fiilîsi, yükleme talimatı, yükleme) GM'den OPM'ye geçti. |
| ROL-D6 | Sistem Yöneticisi (SYS) iş onaylarını ve iş kayıtlarını **taşımaz**; 27 izne indi (v1.2'de ROL-D22 ile 28) (temel okumalar, kurulum ana verisi, sistem ayarları, kullanıcı yönetimi, AI paneli). Bu, SAT-D187'nin Admin satırlarının yerine geçer. |
| ROL-D7 | Satış Müdürü kredi limitini ve cari ekstreyi (tutarlar dahil) okur. |
| ROL-D8 | Muhasebe dönem ve yevmiyeyi salt okur. |
| ROL-D9 | Üretim emri kapatma OPM; tahsilat kaydı Finans. |
| ROL-D10 | Gelen fatura farkını Muhasebe Müdürü onaylar; Satınalma Müdürü onaylamaz. |
| ROL-D11 | Tek adımlı finans kayıtları (virman, cari bakiye virmanı, teminat, çek/senet) tek adımda kalır; risk kabul edildi. |
| ROL-D12 | Dönem yeniden açma ayrı izin, yalnız GM (izin adı henüz kayıtlı değil; Accounting sahibinin teknik işi). |
| ROL-D13 | **Salt Okunur Denetçi rolü kaldırıldı**: denetçiye sistem erişimi yok; rapor paketi dışa aktarılır (YOL-D13). |
| ROL-D14 | Tartımı sevk/üretimi hazırlayan girer (Kantar rolü isteğe bağlı); tartım farkı onayı OPM; irsaliye onayı OPM **veya** Satış Müdürü. Maker ≠ checker sürer. |
| ROL-D15 | Grup içi döküm/konsolide görünüm: GM + SYS + MUHM (izin adı yok; insan kapısı). |
| ROL-D16 | `ai.panel.use` bütün kullanıcı rollerine önerilir (AI-D8 başlangıcının yerine). |
| ROL-D17 | Şimdilik Sistem Yöneticisi hem rol kurgular hem erişim onaylar (geçici istisna; risk kabul edildi). |
| ROL-D18 | Satış Müdürü fiyat listesini hem hazırlar hem yayımlar (risk kabul edildi). |
| ROL-D19 | Satış onay eşikleri ve sözleşme iskonto limiti GM + SYS; teslim toleransı Satış Müdürü (SAT-D189). |
| ROL-D20 | Kalite rolü pasif; kalite modülüyle (Dalga 3) açılır. |

## 0. Kaynaklar ve sayılar

| Kaynak | Ne alındı |
|---|---|
| `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs` | Bütün izin sabitleri: **286** (`All` kümesi 286; **17 Critical**; 271 yalnız şirket geneli) |
| `src/backend/GoymenERP.Api/Seed/identity-seed.json` | Mevcut 5 sistem rolü (§6) |
| `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` | Kararlı persona eşlemeleri (SAT-D141…D205, SAT-D187…D197, ALS-D53…D73, K4) |
| `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md` | Satış önerisi (SAT-D230 ile onaylı satırlar) |
| `docs/decisions/SINAN_KARARLARI_2026-11-22_ROL_YETKI.md` | ROL-D1–ROL-D20 (bu sürümün dayanağı) |
| `docs/security/GOREV_AYRILIGI_ONAY.md`, Settings F-024 (KIM-D39) | Görev ayrılığı ilkeleri |
| `docs/delivery/arastirma/work_ai/2026-11-22_ic_envanter_work_ai.md` §4 | 64 onay/karar akışı ve izinleri |
| Karar dosyaları | P6, SAT-D216/D222, KLN-D17, YOL-D6, YOL-D13, YOL-D17, YOL-D18, AI-D1, AI-D6 |

**Okuma anahtarı:** `+` = önerilir · `K` = koşullu (kayıt düzeyinde hazırlayan/talep eden kendi kaydını onaylayamaz ya
da gerekçedeki ek koşul) · `IK` = Critical; taslak vermez, bu rol yalnız insan kapısında aday · `·` = önerilmez.

**Durum:** **KARARLI** = persona kümesi bir Sinan kararında (SAT/ALS/ROL-D…) veya kartta açıkça yazılı. **KISMEN** =
kararlı çekirdek + öneri uzantısı. **ÖNERİ** = kararlarda persona kümesi yok. **IK** = Critical, insan kapısı. **YOK** =
hiçbir role önerilmez.

**Özet sayılar:** 286 izinden **268**'i en az bir role önerildi; **18**'i hiçbir role önerilmedi (17 Critical +
`identity.company-setup.first-admin.assign`). Durum dağılımı: KARARLI 88 · KISMEN 12 · ÖNERİ 168 · IK 16 · YOK 2.
Sınıflandırılamayan izin yoktur.

## 1. Standart roller

Rol bir **yetki paketidir, kişi değildir**: bir kişiye birden çok rol verilebilir (SAT-D187); kayıt düzeyindeki
"hazırlayan kendi kaydını onaylayamaz" kuralı gevşemez ve küçük kadro istisnası yoktur (ROL-D3). Bütün roller **şirket
bazında** atanır (§4).

| Kod | Rol | Kararlardaki karşılığı | İş tanımı | Durum |
|---|---|---|---|---|
| GM | Genel Müdür | SAT-D187/D188/D190, ROL-D3/D4 | Bütün modüllerde müdür onaylarının **yedek ve yükseltme** onaycısıdır (ROL-D3); kendi alanı olmayan organizasyon ve sistem dışı politika onaylarını verir. Finansta yalnız onaylar (SAT-D197). Kullanıcı/rol/sistem ayarı yönetmez (SAT-D190). Vekili ilgili izne sahip modül müdürüdür (ROL-D4). | KARARLI |
| SAT | Satış Temsilcisi | Satış Uzmanı (SAT-D141) + Satış Destek (ROL-D2) | Teklif, talep ve sipariş taslağı hazırlar, fiyat ve başlık yazar, kaynak planı girer, alış talebi/siparişi açabilir. Sipariş onaylamaz; kendi talebi dışındaki risk istisnasını ve kendi sevki dışındaki fazla teslimi onaylayabilir (SAT-D153/D216). Fatura kesmez (SAT-D202). | KARARLI |
| SATM | Satış Müdürü | SAT-D187/D188, ROL-D7/D14/D18/D21 | Satış siparişini, fiyat/iskonto istisnalarını, iptal ve zorla kapatmayı onaylar; fiyat listesini hazırlar ve yayımlar (ROL-D18); irsaliyeyi onaylayabilir (ROL-D14); kredi limiti ve cari ekstreyi okur (ROL-D7); satış marjını görür (ROL-D21); teklif politikası ve teslim toleransını yönetir. Onay eşiklerini belirlemez (ROL-D19). | KARARLI |
| ALM | Satınalma | Satın Alma Uzmanı (ALS-D53) | Alış talebi ve siparişini hazırlar, revize eder, kendi talebini geri çeker, tedarikçi iadesini hazırlar. Onaylamaz. | KARARLI |
| ALMM | Satınalma Müdürü | Satın Alma Müdürü (ALS-D53), ROL-D10 | Alış talebi ve siparişini onaylar, kalanı iptal eder, mal kabul farkı ve fazla teslimi onaylar. Gelen fatura farkını onaylamaz (ROL-D10). | KARARLI |
| OPM | **Operasyon Müdürü** *(yeni)* | ROL-D5, ROL-D9, ROL-D14 | Sevk, depo ve üretimin tek müdürüdür: sayım, hurda, transfer, irsaliye, plan üstü sevk, tartım farkı, üretim planı ve tolerans dışı kapanış onayları; üretim emri kapatma (ROL-D9); açılış stoğu, üretim fiilîsi, yükleme talimatı ve yüklemenin müdür hücreleri. | KARARLI |
| DEPO | Depo / Sevk | Sevkiyatçı (kart §4), ROL-D14 | Yükleme talimatı, yükleme, sevk, irsaliye taslağı, mal kabul, sayım, transfer ve hurda kaydı; sevk sürecinde tartımı girer (ROL-D14). Fiyat ve finans bakiyesi görmez. Kendi kaydını onaylamaz. | KISMEN |
| KNT | Kantar *(isteğe bağlı)* | ROL-D14 | Ayrı kantar personeli varsa tartım ve tartım farkını kaydeder; yoksa bu işi sevk/üretimi hazırlayan yapar. Düzeltme ve onay yapmaz. | ÖNERİ |
| UPL | Üretim Planlama | URT-D2 planlayıcı | Üretim planı ve emrini hazırlar, takvim/kapasite ve üretim profillerini hazırlar. Planı onaylamaz, emri kapatmaz (ROL-D9). | ÖNERİ |
| UOP | Üretim Operatörü | ROL-D14 | Üretim fiilîsini, etiket dönüşümünü, hurdayı ve üretim tartımını sahada kaydeder; 1./2. kalite ayrımını etiketler (URT-D14). | ÖNERİ |
| KAL | Kalite *(pasif)* | ROL-D20 | Kalite modülü (Dalga 3) gelene kadar pasif; o zamana kadar atanmaz. Hücreleri yalnız hazırlık içindir. | ÖNERİ |
| FIN | Finans | Finans Uzmanı (SAT-D196), ROL-D9/D11 | Tahsilat (ROL-D9), ödeme, masraf, virman, çek/senet, teminat ve kredi işlemlerini oluşturur; tek adımlı kayıtlar onaysızdır (ROL-D11). Onaylamaz. | KARARLI |
| FINM | Finans Müdürü | SAT-D196 | Finans işlemlerini onaylar; banka CSV eşlemesi, teminat türleri, finans açılış dönemi, finans politika taslağı. Oluşturmaz. | KARARLI |
| MUH | Muhasebe | SAT-D142, ROL-D8 | Satışta her şeyi görür; faturayı hazırlar, keser, eşleştirir, kesinleştirir; vergi ayarı önerir; cari e-belge; fiş taslağı ve ters kayıt önerir; dönem ve yevmiyeyi salt okur (ROL-D8). | KARARLI |
| MUHM | Muhasebe Müdürü | SAT-D187/D188/D192, ROL-D1/D10 | Dönem, fiş, hesap eşleme/katalog etkinleştirme; gelen fatura farkı onayı (ROL-D10); vergi ana verisi, kalem tipi, kur düzeltmesi; cari kart yönetimi (SAT-D192). | KARARLI |
| SYS | Sistem Yöneticisi | ROL-D6, ROL-D17, ROL-D22, ROL-D23 | Yalnız sistem yönetimi ve acil durum: kullanıcı yönetimi, kurulum ana verisi (organizasyon), sistem ayarları (giriş sınırı, görev ayrılığı, saklama, birim/para profili, numara serileri, referans listeleri), satış eşik/limit ayarı (ROL-D19), kritik politika yayımı (ROL-D22). Acil durum yetkisi kalıcı izin değil, süreli/gerekçeli/başka yetkilinin onayladığı ve süresi bitince kalkan atamadır (ROL-D23). Şimdilik rol kurgulama + erişim onayı adayıdır (ROL-D17, Critical, insan kapısı). İş onayı/kaydı yok. | KARARLI |

**Kaldırılan:** Salt Okunur Denetçi (v1'de DEN) — ROL-D13 gereği sisteme erişim verilmez; denetçi paketi dışa aktarılır.

## 2. Rol × izin matrisi

### 2.1 Rol başına özet

| Rol | Önerilen izin | Bunların koşullu (`K`) olanı |
|---|---|---|
| GM | 185 | 45 |
| SAT | 51 | 3 |
| SATM | 71 | 10 |
| ALM | 24 | 0 |
| ALMM | 33 | 4 |
| OPM | 65 | 16 |
| DEPO | 49 | 0 |
| KNT | 20 | 0 |
| UPL | 39 | 0 |
| UOP | 24 | 0 |
| KAL | 14 | 0 |
| FIN | 52 | 0 |
| FINM | 39 | 7 |
| MUH | 58 | 1 |
| MUHM | 84 | 9 |
| SYS | 28 (+5 `IK` adayı) | 0 |

GM'nin sayısı yüksek kalır, çünkü ROL-D3 gereği her müdür onayının yedek onaycısıdır ve müdürlerin okumalarını taşır.

### 2.2 Hiçbir role önerilmeyen izinler (18)

| İzin | Neden / insan kapısı adayı |
|---|---|
| `organization.companies.open` | Critical; şirket açma grup düzeyinde insan kararıdır (KIM-D21/KIM-D56). Aday yok. |
| `identity.company-setup.first-admin.assign` | KIM-D56: ilk Admin seçimi açık ve insan sahipli; rol paketine konmaz. |
| `identity.access.approve`, `identity.roles.manage` | Critical. Aday: Sistem Yöneticisi (ROL-D17 geçici istisna: aynı kişi ikisini de yapabilir; kabul edildi). |
| `identity.audit.read`, `identity.assignments.read`, `platform.security-events.read` | Critical. Aday: Sistem Yöneticisi. Denetçiye erişim yok (ROL-D13). |
| `finance.bank-account.propose` / `.approve` | Critical. Aday: Finans önerir, Finans Müdürü onaylar; öneren ödeme onaylamamalı (SoD-08). |
| `finance.cash-account.*` (6 ad: öner/onayla, pasifleştir, yeniden etkinleştir) | Critical. Aday: Finans önerir, Finans Müdürü onaylar. |
| `finance.credit-limits.approve`, `finance.credits.approve` | Critical. Aday: Finans Müdürü veya GM (SAT-D197). |
| `counterparty.parties.tax-identity.approve` | Critical. Aday: Muhasebe Müdürü veya GM; hazırlayandan farklı kişi. |

Kayıtta Critical olmayıp yalnız Sistem Yöneticisi'ne önerilen 5 ad: `settings.policies.publish-critical` (ROL-D22),
`settings.login-rate-limit.manage`, `settings.segregation-of-duties.manage`, `settings.notification-retention.manage`,
`identity.users.manage`.

### 2.3 Matrisin tamamı (modül sırasıyla)

Sütunlar: GM Genel Müdür · SAT Satış Temsilcisi · SATM Satış Müdürü · ALM Satınalma · ALMM Satınalma Müdürü · OPM
Operasyon Müdürü · DEPO Depo/Sevk · KNT Kantar (isteğe bağlı) · UPL Üretim Planlama · UOP Üretim Operatörü · KAL Kalite
(pasif) · FIN Finans · FINM Finans Müdürü · MUH Muhasebe · MUHM Muhasebe Müdürü · SYS Sistem Yöneticisi.

### Organizasyon (13 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `organization.companies.open` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | YOK | Critical; şirket açma grup düzeyinde insan kararıdır (KIM-D21/KIM-D56). Hiçbir role önerilmez. |
| `identity.company-setup.first-admin.assign` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | YOK | KIM-D56: ilk Admin seçimi açık ve insan sahipli; yalnız kayıtlı, rol paketi olamaz. |
| `organization.context.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin (bütün personalar). |
| `organization.sites.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin. |
| `organization.locations.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin. |
| `organization.work-centers.read` | + | · | · | · | · | + | + | · | + | + | + | · | · | · | · | · | KISMEN | Kart: Sevkiyatçı ve GM +; Operasyon Müdürü (ROL-D5), üretim ve kalite ÖNERİ. |
| `organization.locations.status.update` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Organizasyonun müdürü yok (SAT-D191): GM; Sistem Yöneticisi kurulum ana verisi olarak (ROL-D6 yorumu). |
| `organization.sites.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Organizasyon ana verisi; müdürü olmayan modül kuralı (SAT-D191) yorumu. |
| `organization.locations.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Organizasyon ana verisi; SAT-D191 yorumu. |
| `organization.work-centers.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Organizasyon ana verisi; SAT-D191 yorumu. |
| `organization.work-centers.calendar.write` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | ÖNERİ | URT-D11; Operasyon Müdürü ve planlama. |
| `organization.company.tax-id.request` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Şirket VKN değişikliği talebi muhasebe tarafından hazırlanır. |
| `organization.company.tax-id.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Hazırlayan ≠ onaylayan; ROL-D6 sonrası Sistem Yöneticisi iş onayı almaz. |

### Cari (15 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `counterparty.parties.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin. |
| `counterparty.parties.create` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D192: cari yönetimi Muhasebe Müdürü (+ GM). Admin hücresi ROL-D6 ile kalktı. |
| `counterparty.parties.update.basic` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D192; ROL-D6. |
| `counterparty.accounts.create` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D192; ROL-D6. |
| `counterparty.accounts.status.update` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D192; ROL-D6. |
| `counterparty.parties.tax-identity.update` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Vergi kimliği değişikliğini muhasebe hazırlar; onayı Critical ayrı izindir. |
| `counterparty.parties.tax-identity.approve` **(Critical)** | IK | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Muhasebe Müdürü veya GM; hazırlayandan farklı kişi. |
| `counterparty.customer-groups.manage` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Müşteri grubu satış politikasıdır. |
| `counterparty.accounts.customer-group.assign` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Müşteri grubu ataması satış politikasıdır. |
| `counterparty.accounts.e-document.update` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | · | KARARLI | SAT-D155: Muhasebe; GM ve müdürler almaz. |
| `counterparties.legal_name.propose` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Unvan değişikliğini muhasebe önerir. |
| `counterparties.legal_name.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan; öneren kendi önerisini onaylayamaz. |
| `counterparty.parties.merge.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Cari birleştirme geri dönüşü zor; müdür seviyesi. |
| `counterparty.parties.succession.record` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Halef/selef kaydı cari yönetimidir (SAT-D192 yorumu). |
| `counterparty.parties.group-deactivate` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Grup genelinde etkili; müdür seviyesi. |

### Ürün (11 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `product.operational-selection.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin. |
| `product.price-lists.read` | + | + | + | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Fiyat listesi satış ve muhasebe okumasıdır; Depo/Sevk fiyat görmez (kart §4). |
| `product.price-lists.write` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Fiyat listesi hazırlığı satış müdürlüğündedir. |
| `product.price-lists.publish` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D18: Satış Müdürü hem hazırlar hem yayımlar (kabul edilen risk, SoD-07). |
| `product.item-type.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D145/D149, SAT-D188. |
| `product.dimension.manage` | + | · | · | · | + | · | · | · | + | · | · | · | · | · | · | · | ÖNERİ | URN-D10 ortak ebat; üretim ve satınalma ana verisi. |
| `product.profile-weights.import` | + | · | · | · | · | · | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Profil ağırlıkları üretim ana verisidir. |
| `product.package-standards.manage` | · | · | · | · | · | · | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Paket standardını üretim planlama hazırlar. |
| `product.paint-memberships.manage` | + | · | · | · | · | · | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Boya üyeliği üretim ana verisidir. |
| `product.cogs-classifications.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | SMM sınıflaması muhasebe politikasıdır. |
| `product.package-standards.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | KISMEN | ROL-D5: üretim müdürlüğü Operasyon Müdürü'nde; GM yedek (ROL-D3). Hazırlayan onaylayamaz. |

### Stok (19 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `inventory.labels.read` | + | + | + | + | + | + | + | + | + | + | + | · | · | + | + | · | KISMEN | Kart: Sevkiyatçı + GM +; diğerleri ÖNERİ. |
| `inventory.balances.read` | + | + | + | + | + | + | + | + | + | + | + | · | · | + | + | · | KISMEN | Kart: Sevkiyatçı + GM +; diğerleri ÖNERİ. |
| `inventory.opening-stock.record` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D191 ara dönem hücresi, modül müdürü tanımlandığı için Operasyon Müdürü'ne geçer (ROL-D5). |
| `inventory.physical-allocation.write` | · | + | + | · | · | · | + | · | + | · | · | · | · | · | · | · | ÖNERİ | K4.4: satış tarafına tahsis/rezervasyon; depo ve planlama fiziksel ayırır. |
| `inventory.future-allocation.read` | + | + | + | + | + | + | + | · | + | · | · | · | · | · | · | · | ÖNERİ | F017 görünümü. |
| `inventory.future-allocation.write` | · | + | + | + | + | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | F017: satış ve satınalma ayırma yapar. |
| `inventory.label-transformation.record` | · | · | · | · | · | · | + | · | · | + | · | · | · | · | · | · | ÖNERİ | Etiket dönüşümü depo/üretim sahasında kaydedilir. |
| `inventory.label-classification.write` | · | · | · | · | · | · | + | · | · | + | + | · | · | · | · | · | ÖNERİ | URT-D14: yalnız 1./2. kalite ayrımı; Kalite rolü ileride. |
| `inventory.stock-count.record` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Sayımı depo girer. |
| `inventory.stock-count.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D5 sayım onayı Operasyon Müdürü; GM yedek (ROL-D3). Sayımı giren onaylayamaz. |
| `inventory.stock-scrap.record` | · | · | · | · | · | · | + | · | · | + | · | · | · | · | · | · | ÖNERİ | Hurdayı saha kaydeder. |
| `inventory.stock-scrap.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D5 hurda onayı; Kalite pasif (ROL-D20). |
| `inventory.stock-transfer.write` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Transferi depo yazar. |
| `inventory.stock-transfer.cancel` | + | · | · | · | · | + | + | · | · | · | · | · | · | · | · | · | ÖNERİ | İptal depo veya Operasyon Müdürü. |
| `inventory.stock-transfer.overage-approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D5 transfer onayları. |
| `inventory.stock-transfer.loss-record` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Kaybı depo kaydeder. |
| `inventory.stock-transfer.loss-approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D5 transfer onayları. |
| `inventory.opening-cost.submit` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | · | ÖNERİ | Açılış maliyeti muhasebe verisidir. |
| `inventory.opening-cost.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Gönderen ≠ onaylayan. |

### Finans (56 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `finance.bank-account.read` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | SAT-D196 (Finans Uzmanı); GM almaz (SAT-D197). |
| `finance.bank-account.propose` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | · | IK | Critical. Aday: Finans. Öneren, ödeme onaylayan kişi olmamalı (SoD-08). |
| `finance.bank-account.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | IK | Critical. Aday: Finans Müdürü; öneren ≠ onaylayan. |
| `finance.cash-account.read` | · | · | · | · | · | · | · | · | · | · | · | + | + | · | · | · | ÖNERİ | Kasa hesabı oluşturma ve onayın gerektirdiği okuma. |
| `finance.cash-account.propose` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | · | IK | Critical. Aday: Finans. |
| `finance.cash-account.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | IK | Critical. Aday: Finans Müdürü (öneren ≠ onaylayan). |
| `finance.cash-account.passivation.propose` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | · | IK | Critical. Aday: Finans. |
| `finance.cash-account.passivation.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | IK | Critical. Aday: Finans Müdürü (öneren ≠ onaylayan). |
| `finance.cash-account.reactivation.propose` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | · | IK | Critical. Aday: Finans. |
| `finance.cash-account.reactivation.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | IK | Critical. Aday: Finans Müdürü (öneren ≠ onaylayan). |
| `finance.credit-limits.read` | + | · | + | · | · | · | · | · | · | · | · | + | + | · | · | · | KARARLI | ROL-D7: Satış Müdürü kredi limitini okur; finans çekirdeği SAT-D196 deseni. |
| `finance.credit-limits.propose` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | SAT-D196: Finans oluşturur. |
| `finance.credit-limits.approve` **(Critical)** | IK | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | IK | Critical. Aday: Finans Müdürü veya GM (SAT-D197). |
| `finance.collaterals.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | · | · | · | ÖNERİ | Teminat finans okumasıdır. |
| `finance.collaterals.record` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | ROL-D11: tek adımda kalır. |
| `finance.opening-balances.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | · | + | · | ÖNERİ | Açılış bakiyesi finans + muhasebe müdürü okuması. |
| `finance.opening-balances.propose` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | SAT-D196. |
| `finance.opening-balances.approve` | K | · | · | · | · | · | · | · | · | · | · | · | K | · | · | · | ÖNERİ | SAT-D196/D197: Finans Müdürü ve GM onaylar; öneren onaylayamaz. |
| `finance.credits.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | · | · | · | ÖNERİ | Kredi hesabı finans okumasıdır. |
| `finance.credits.propose` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | SAT-D196. |
| `finance.credits.approve` **(Critical)** | IK | · | · | · | · | · | · | · | · | · | · | · | IK | · | · | · | IK | Critical. Aday: Finans Müdürü veya GM. |
| `finance.official-invoice-import.upload` | · | · | · | · | · | · | · | · | · | · | · | + | · | + | · | · | ÖNERİ | Resmî fatura listesi finans veya muhasebe yükler (soru). |
| `finance.official-invoice-import.approve` | K | · | · | · | · | · | · | · | · | · | · | · | K | · | · | · | ÖNERİ | Yükleyen onaylayamaz. |
| `finance.official-sales-invoices.record` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | Resmî satış faturası kaydı finans tarafıdır. |
| `finance.official-sales-invoices.approve` | K | · | · | · | · | · | · | · | · | · | · | · | K | · | · | · | ÖNERİ | SAT-D196/D197. |
| `finance.official-sales-invoices.withdraw` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | Hazırlayanın geri çekmesi. |
| `finance.manual-receipt.draft.save` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | SAT-D196 (kart Finans personaları tablosu). |
| `finance.manual-receipt.draft.read` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | SAT-D196. |
| `finance.manual-receipt.amount.read` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | SAT-D196. |
| `finance.manual-receipt.record` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | ROL-D9: tahsilat kaydı Finans rolünde. Onay adımı yok (ROL-D11 kabul). |
| `finance.manual-receipt.reverse.propose` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | SAT-D196. |
| `finance.manual-receipt.reverse.approve` | K | · | · | · | · | · | · | · | · | · | · | · | K | · | · | · | ÖNERİ | SAT-D196/D197; öneren onaylayamaz. |
| `finance.manual-bank-payment.draft.save` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | SAT-D196 (oluşturma). |
| `finance.manual-bank-payment.draft.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | · | · | · | ÖNERİ | Oluşturma + onayın gerektirdiği okuma (SAT-D197). |
| `finance.manual-bank-payment.approve` | K | · | · | · | · | · | · | · | · | · | · | · | K | · | · | · | ÖNERİ | SAT-D196/D197; hazırlayan onaylayamaz. Banka hesabı öneren kişi bu onayı almamalı. |
| `finance.manual-bank-payment.record` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | Onaylı ödemenin kaydı; 'record' adları NEEDS-SINAN (kart). |
| `finance.manual-bank-payment.reverse.propose` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | SAT-D196. |
| `finance.manual-bank-payment.reverse.approve` | K | · | · | · | · | · | · | · | · | · | · | · | K | · | · | · | ÖNERİ | SAT-D196/D197. |
| `finance.expense-payment.draft.save` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | SAT-D196. |
| `finance.expense-payment.draft.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | · | · | · | ÖNERİ | Oluşturma + onay okuması. |
| `finance.expense-payment.approve` | K | · | · | · | · | · | · | · | · | · | · | · | K | · | · | · | ÖNERİ | SAT-D196/D197. |
| `finance.expense-payment.record` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | Onaylı masrafın kaydı; NEEDS-SINAN (kart 'record' kuralı). |
| `finance.cash-transfers.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | · | · | · | ÖNERİ | Virman okuması. |
| `finance.cash-transfers.commit` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | ROL-D11: tek adımda kalır (kabul edilen risk, SoD-12). |
| `finance.customer-instrument.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | · | · | · | ÖNERİ | Çek/senet okuması. |
| `finance.customer-instrument.amount.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | · | · | · | ÖNERİ | Çek/senet tutarı. |
| `finance.customer-instrument.receive` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | ROL-D11: çek/senet kayıtları tek adımda kalır. |
| `finance.customer-instrument.status.change` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | ROL-D11: çek/senet kayıtları tek adımda kalır. |
| `finance.customer-instrument.collect` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | ROL-D11: çek/senet kayıtları tek adımda kalır. |
| `finance.party-ledger.read` | + | · | + | · | · | · | · | · | · | · | · | + | + | + | + | · | KISMEN | ROL-D7: Satış Müdürü cari ekstreyi okur; diğerleri ÖNERİ. |
| `finance.party-ledger.amount.read` | + | · | + | · | · | · | · | · | · | · | · | + | + | + | + | · | KISMEN | ROL-D7: tutarlar dahil. |
| `finance.party-balance-transfers.record` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | KARARLI | ROL-D11: tek adımda kalır. |
| `finance.open-items.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | + | + | · | ÖNERİ | Açık kalem okuması. |
| `finance.open-items.amount.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | + | + | · | ÖNERİ | Açık kalem tutarı. |
| `finance.allocation.write` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | · | ÖNERİ | Kapama/eşleştirme finans işlemidir. |
| `finance.allocation.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | + | + | · | ÖNERİ | Kapama okuması. |

### Üretim (17 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `manufacturing.plans.write` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | KISMEN | Planlama hazırlar; Operasyon Müdürü (ROL-D5). |
| `manufacturing.plans.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | KARARLI | URT-D2 + ROL-D5: Operasyon Müdürü; GM yedek (ROL-D3). Planı yazan onaylayamaz. |
| `manufacturing.orders.create` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | KISMEN | Planlama + Operasyon Müdürü. |
| `manufacturing.facts.record` | · | · | · | · | · | + | · | · | + | + | · | · | · | · | · | · | KISMEN | SAT-D191 ara dönem hücresi Operasyon Müdürü'ne geçer; Operatör/Planlama ÖNERİ. |
| `manufacturing.orders.read` | + | · | + | · | · | + | + | · | + | + | + | · | · | · | + | · | KISMEN | Kart: Sevkiyatçı + GM +; diğerleri ÖNERİ. |
| `manufacturing.facts.correct` | · | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Fiilî düzeltme; kaydı giren kendi kaydını düzeltmemeli (SoD-14). |
| `manufacturing.orders.close` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D9: üretim emri kapatma Operasyon Müdürü'nde. |
| `manufacturing.orders.close-exception.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Fiilî miktarı giren onaylayamaz; ROL-D5 + ROL-D3. |
| `manufacturing.orders.write` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Emir düzeltme. |
| `manufacturing.orders.cancel` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Emir iptali. |
| `manufacturing.orders.reopen` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Kapanmış emri yeniden açma müdür seviyesi. |
| `manufacturing.plans.cancel` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Plan iptali. |
| `manufacturing.plans.reorder` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Plan sıralaması. |
| `manufacturing.scrap-prices.read` | + | · | · | · | · | + | · | · | + | · | · | · | · | + | + | · | ÖNERİ | Hurda fiyatı maliyet okuması. |
| `manufacturing.scrap-prices.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Hurda fiyatı maliyet politikasıdır. |
| `manufacturing.reports.read` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | + | · | ÖNERİ | Üretim raporu. |
| `manufacturing.plans.read` | + | + | + | · | · | + | + | · | + | + | · | · | · | · | · | · | ÖNERİ | Plan görünümü. |

### Satış (40 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `sales.drafts.create` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D141, SAT-D194. |
| `sales.drafts.read` | + | + | + | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | Kart. Depo/Sevk engelli (kart). |
| `sales.orders.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D6, SAT-D188; P6 hazırlayan ≠ onaylayan. |
| `sales.orders.price.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D151, SAT-D188. |
| `sales.orders.price.read` | + | + | + | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D150; Depo/Sevk görmez. |
| `sales.orders.margin.read` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D21: GM + Satış Müdürü (SAT-D250/SAT-D276 (2) yerine); Sistem Yöneticisi görmez. |
| `sales.orders.price-floor-exception.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D1, SAT-D188. |
| `sales.orders.discount-exception.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D37/D67: Satış Müdürü'nün kendi limitini aşanı başkası onaylar. |
| `sales.orders.delivery-schedule.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Teslim planı sipariş düzenlemesidir. |
| `sales.orders.cancel` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D4; Satış Temsilcisi hücresi ÖNERİ: verme. |
| `sales.orders.force-close` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | SATIS_ROL_YETKI_ONERISI_v1 öneri 5. |
| `sales.orders.close` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D168, SAT-D230. |
| `sales.orders.over-delivery.approve` | K | K | K | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D168; SAT-D216 kendi sevki/talebi için onay yok; SAT-D222. |
| `sales.orders.risk-exception.request` | + | + | + | · | · | · | + | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D159 (Sevkiyatçı dahil, kart). |
| `sales.orders.risk-exception.approve` | K | K | K | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D153: kendi talebini onaylayamaz. |
| `sales.orders.lines.close` | + | K | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D168; SAT-D216. |
| `sales.orders.source-plan.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D179. |
| `sales.requests.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Satış talebi satış personelindedir. |
| `sales.requests.read` | + | + | + | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Satış okuması (SAT-D142 'her şeyi görür'). |
| `sales.requests.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Talep onayı müdür seviyesi; açan onaylayamaz. |
| `sales.quotes.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Teklif hazırlama. |
| `sales.quotes.read` | + | + | + | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Satış okuması. |
| `sales.quotes.expired-conversion.approve` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D24/D44 ('yönetici'); SAT-D44 istisnası: hazırlayan da onaylayabilir. |
| `sales.invoices.read` | + | + | + | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D110. |
| `sales.invoices.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D202: faturayı Muhasebe keser; Satış görür ama kesemez. |
| `sales.invoices.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | K | K | · | KARARLI | SAT-D88, SAT-D204: hazırlayan onaylayamaz. |
| `sales.invoices.match` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | SAT-D142 yorumu (SATIS_ROL öneri 7). |
| `sales.invoices.finalize` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D230; SAT-D235 (2) kesinleştirmede SoD yok; KLN-D17. |
| `sales.invoices.export` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D276 (1). |
| `sales.reports.read` | + | + | + | · | · | · | + | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D143 (kart Sevkiyatçı +). |
| `sales.returns.read` | + | + | + | · | · | · | + | · | · | · | · | · | · | + | + | · | ÖNERİ | İade okuması. |
| `sales.returns.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | İadeyi satış hazırlar. |
| `sales.returns.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Hazırlayan ≠ onaylayan. |
| `sales.returns.finalize` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Kesinleştirme belge/muhasebe tarafı (fatura emsali SAT-D202). |
| `sales.contracts.read` | + | + | + | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Sözleşme okuması. |
| `sales.contracts.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Sözleşmeyi satış hazırlar. |
| `sales.contracts.discount.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Hazırlayan ≠ onaylayan. |
| `sales.orders.quantity.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Miktar revizyonu (SAT-D3 yeniden onay ister). |
| `sales.orders.header.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D144, SAT-D185. |
| `sales.orders.customer-acceptance.record` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Müşteri kabulünü satış kaydeder. |

### Lojistik / Sevk / Kantar (22 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `logistics.loading-instructions.create` | · | · | · | · | · | + | + | · | · | · | · | · | · | · | · | · | KARARLI | Kart: Sevkiyatçı +; ara dönem hücresi Operasyon Müdürü'ne geçer (ROL-D5). |
| `logistics.loading-instructions.read` | + | + | + | · | · | + | + | + | · | · | · | · | · | · | · | · | KISMEN | Kart: Sevkiyatçı + GM +; diğerleri ÖNERİ. |
| `logistics.loads.record` | · | · | · | · | · | + | + | · | · | · | · | · | · | · | · | · | KARARLI | Kart: Sevkiyatçı +; ara dönem hücresi Operasyon Müdürü'ne geçer (ROL-D5). |
| `logistics.shipments.record` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Sevk kaydı depo/sevk işidir. |
| `logistics.shipments.plan-excess.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D5 plan üstü sevk; YOL-D18 4 saat. Sevki yapan onaylayamaz. |
| `logistics.deliveries.record` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Teslim kaydı. |
| `logistics.weighings.record` | · | · | · | · | · | · | + | + | · | + | · | · | · | · | · | · | KARARLI | ROL-D14: tartımı sevk/üretim sürecini hazırlayan girer; ayrı kantar rolü zorunlu değil. |
| `logistics.weighings.correct` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Tartımı giren kendi tartımını düzeltmemeli; ROL-D14 onay Operasyon Müdürü. |
| `logistics.dispatch-notes.create` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | İrsaliye taslağını sevk hazırlar. |
| `logistics.dispatch-notes.approve` | K | · | K | · | · | K | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D14: irsaliye onayı Operasyon Müdürü'ne ve Satış Müdürü'ne düşebilir (biri onaylar); YOL-D18 4 saat; hazırlayan onaylayamaz. |
| `logistics.dispatch-notes.cancel` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Onaylı irsaliye iptali müdür seviyesi. |
| `logistics.transfer-outgoing-documents.read` | + | · | · | · | · | + | + | + | · | · | · | · | · | · | · | · | ÖNERİ | Transfer çıkış belgeleri. |
| `logistics.transfer-departures.record` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Transfer çıkışı. |
| `logistics.loading-instructions.cancel` | + | · | · | · | · | + | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Talimatı açan veya müdür iptal eder. |
| `logistics.customer-returns.read` | + | + | + | · | · | + | + | · | · | · | · | · | · | + | · | · | ÖNERİ | Müşteri iadesi okuması. |
| `logistics.customer-returns.request` | · | + | + | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | İade kabul talebi. |
| `logistics.customer-returns.decision` | K | · | K | · | · | K | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Talep açan karar veremez. |
| `logistics.weighing-differences.record` | · | · | · | · | · | · | + | + | · | + | · | · | · | · | · | · | KARARLI | ROL-D14: hazırlayan girer. |
| `logistics.weighing-differences.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | KARARLI | ROL-D14: tartım farkı onayı Operasyon Müdürü; YOL-D18 4 saat; kaydeden onaylayamaz. |
| `logistics.weighing-differences.read` | + | · | + | · | + | + | + | + | · | · | · | · | · | · | · | · | ÖNERİ | Tartım farkı okuması. |
| `logistics.operations-work.sales.read` | + | + | + | · | · | + | + | + | · | · | · | · | · | · | · | · | ÖNERİ | Operasyon iş listesi (satış). |
| `logistics.operations-work.purchase.read` | + | · | · | + | + | + | + | + | · | · | · | · | · | · | · | · | ÖNERİ | Operasyon iş listesi (alış). |

### Satınalma (19 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `purchasing.orders.create` | + | + | · | + | + | · | · | · | · | · | · | · | · | · | · | · | KARARLI | ALS-D53, kart §4 (Satış Destek/Uzmanı alış siparişi girer), SAT-D188. |
| `purchasing.orders.read` | + | + | · | + | + | + | + | · | · | · | · | · | · | + | + | · | KARARLI | Kart + ALS-D73; Operasyon Müdürü ÖNERİ (mal kabul). Depo fiyat görmez (PRS-02). |
| `purchasing.orders.approve` | K | · | · | · | K | · | · | · | · | · | · | · | · | · | · | · | KARARLI | ALS-D3, ALS-D53; hazırlayan onaylayamaz. |
| `purchasing.goods-receipts.record` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Kart: deny-first, sorulmadı. Öneri: mal kabulü depo yapar. |
| `purchasing.requests.approve` | K | · | · | · | K | · | · | · | · | · | · | · | · | · | · | · | KARARLI | ALS-D2, ALS-D53. |
| `purchasing.requests.withdraw` | · | + | · | + | + | · | · | · | · | · | · | · | · | · | · | · | KARARLI | ALS-D54, ALS-D47 (talebi açan geri çeker). |
| `purchasing.orders.cancel` | + | · | · | · | + | · | · | · | · | · | · | · | · | · | · | · | KARARLI | ALS-D66. |
| `purchasing.orders.price.read` | + | + | · | + | + | · | · | · | · | · | · | · | · | + | + | · | KARARLI | ALS-D70; Depo/Sevk görmez; Satış Müdürü verilmez (EK11 yorumu). |
| `purchasing.orders.revise` | · | · | · | + | + | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Revizyonu hazırlayan; yeniden onay ister. |
| `purchasing.orders.close` | + | · | · | · | + | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Kapatma onaycılarda (ALS-D66 benzetmesi). |
| `purchasing.goods-receipts.approve-variance` | K | · | · | · | K | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Mal kabul farkı; kabulü kaydeden onaylayamaz. |
| `purchasing.goods-receipts.approve-overdelivery` | K | · | · | · | K | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Fazla teslim; kabulü kaydeden onaylayamaz. |
| `purchasing.future-allocation.release` | + | · | · | · | + | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | F017 ayırma bırakma. |
| `purchasing.supplier-returns.record` | · | · | · | + | + | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Tedarikçi iadesini satınalma hazırlar. |
| `purchasing.invoices.read` | + | · | · | + | + | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Alış faturası okuması. |
| `purchasing.invoices.import` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | · | ÖNERİ | Gelen faturayı muhasebe alır. |
| `purchasing.invoices.match` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Eşleştirme muhasebe işidir (satış emsali). |
| `purchasing.invoices.approve-variance` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | KARARLI | ROL-D10: Muhasebe Müdürü onaylar; siparişi onaylayan Satınalma Müdürü onaylamaz. |
| `purchasing.invoices.finalize` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Kesinleştirme (SAT-D230 emsali). |

### Muhasebe (16 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `accounting.periods.read` | + | · | · | · | · | · | + | · | · | · | · | · | · | + | + | · | KARARLI | K4.5 (Sevkiyatçı +), SAT-D191, ROL-D8 (Muhasebe salt okur). |
| `accounting.periods.propose` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D191; P6. |
| `accounting.periods.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | KARARLI | SAT-D191; P6. Yeniden açma ROL-D12 ile ayrı izin olacak (ad henüz kayıtlı değil), yalnız GM. |
| `accounting.periods.close` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D191. Kapatan ile kilidi kaldıran ayrı yetki (GOREV_AYRILIGI_ONAY). |
| `accounting.journals.read` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D191, ROL-D8 (Muhasebe salt okur). |
| `accounting.posting-queues.read` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D191; K4.5. |
| `accounting.normal-journal.draft.create` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Fiş taslağı; Muhasebe accounting yazması kartta '?' (soru). |
| `accounting.normal-journal.draft.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan; KLN-D17. |
| `accounting.journal.reverse.propose` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Ters kayıt önerisi. |
| `accounting.journal.reverse.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Öneren onaylayamaz. |
| `accounting.approved-source-catalog.prepare` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Kaynak kataloğu hazırlığı. |
| `accounting.approved-source-catalog.activate` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ etkinleştiren. |
| `accounting.posting-mappings.read` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Hesap eşleme okuması. |
| `accounting.posting-mappings.prepare` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Eşleme hazırlığı. |
| `accounting.posting-mappings.activate` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ etkinleştiren. |
| `accounting.account-plan.read` | + | · | · | · | · | · | · | · | · | · | · | + | + | + | + | · | ÖNERİ | Hesap planı okuması. |

### Ayarlar (39 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `settings.sales-discount-limits.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | KARARLI | ROL-D19 sonucu (Y-02): GM + Sistem Yöneticisi; Satış Müdürü almaz (SAT-D65). |
| `settings.tax-master.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D145, SAT-D188. |
| `settings.sales-delivery-tolerances.manage` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D189 (teslim toleransı). |
| `settings.sales-approval-thresholds.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | KARARLI | ROL-D19: GM + Sistem Yöneticisi. |
| `settings.sales-quote-policies.manage` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | KARARLI | SAT-D189 (teklif geçerlilik, kayıp nedenleri). |
| `settings.purchase-invoice-variance-policies.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Alış fatura farkı politikası. |
| `settings.warehouse-transfer-scale.manage` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Depo transfer kantar ayarı Operasyon Müdürü. |
| `settings.einvoice-scenarios.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | e-Fatura senaryoları muhasebe ayarıdır. |
| `settings.unit-conversion-policies.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Birim çevrim politikası (formül etkisi; dikkat). |
| `settings.unit-profiles.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Birim profilleri. |
| `settings.money-profiles.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | ÖNERİ | Para profili muhasebe ayarıdır. |
| `settings.login-rate-limit.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Sistem yönetimi (SAT-D190: GM almaz). |
| `settings.work-center-capacities.manage` | + | · | · | · | · | + | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Kapasite planlama ayarı. |
| `settings.bank-csv-mappings.manage` | · | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | ÖNERİ | Banka ekstresi eşlemesi finans ayarıdır. |
| `settings.segregation-of-duties.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Sistem yönetimi; KIM-D39 gevşetme yalnız tek kullanıcıda. |
| `settings.sales-contract-discount-limit.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | KARARLI | ROL-D19: GM + Sistem Yöneticisi. |
| `settings.collateral-types.manage` | · | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | ÖNERİ | Teminat türleri finans ayarıdır. |
| `settings.finance-opening-period.manage` | · | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | ÖNERİ | Finans açılış dönemi. |
| `settings.manufacturing-scrap-cost.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Hurda maliyeti muhasebe politikasıdır. |
| `settings.notification-retention.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Saklama ayarı sistem yönetimidir. |
| `settings.document-number-series.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | ÖNERİ | Belge numara serileri. |
| `settings.reference-lists.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Genel referans listeleri. |
| `settings.production-reference-lists.manage` | · | · | · | · | · | · | · | · | + | · | · | · | · | · | · | + | ÖNERİ | Üretim referans listeleri. |
| `settings.exchange-rates.read` | + | + | + | + | + | · | · | · | · | · | · | + | + | + | + | · | KISMEN | SAT-D199 sekiz persona; Satınalma ve |
| `settings.exchange-rates.correct` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D169, SAT-D188. |
| `settings.exchange-rates.import` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D205. |
| `settings.exchange-rates.half-day.mark` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D198. |
| `settings.manufacturing-parameter-profiles.read` | + | · | · | · | · | + | · | · | + | + | · | · | · | · | · | · | ÖNERİ | Üretim profili okuması. |
| `settings.policies.prepare` | · | · | · | · | · | · | · | · | · | · | · | · | + | · | · | · | ÖNERİ | Finans onay matrisleri vb. politika taslağı. |
| `settings.policies.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Hazırlayan ≠ onaylayan. |
| `settings.policies.publish` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Onaylı politikayı yayımlama. |
| `settings.policies.publish-critical` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | KARARLI | ROL-D22: Sistem Yöneticisi. |
| `settings.manufacturing-parameter-profiles.prepare` | · | · | · | · | · | · | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Profili planlama hazırlar. |
| `settings.manufacturing-parameter-profiles.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Hazırlayan ≠ onaylayan; ROL-D5. |
| `settings.manufacturing-parameter-profiles.publish` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Onaylı profili yayımlama. |
| `settings.manufacturing-tolerance-profiles.read` | + | · | · | · | · | + | · | · | + | + | · | · | · | · | · | · | ÖNERİ | Üretim profili okuması. |
| `settings.manufacturing-tolerance-profiles.prepare` | · | · | · | · | · | · | · | · | + | · | · | · | · | · | · | · | ÖNERİ | Profili planlama hazırlar. |
| `settings.manufacturing-tolerance-profiles.approve` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Hazırlayan ≠ onaylayan; ROL-D5. |
| `settings.manufacturing-tolerance-profiles.publish` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Onaylı profili yayımlama. |

### Belgeler (10 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `documents.files.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | ÖNERİ | Kayda bağlı dosya okuması; kayıt yetkisiyle birlikte geçerli. |
| `documents.files.upload` | · | + | + | + | + | + | + | + | + | + | · | + | + | + | + | · | ÖNERİ | İş kaydına belge ekleme. |
| `documents.files.link` | · | + | + | + | + | + | + | + | + | + | · | + | + | + | + | · | ÖNERİ | Belgeyi kayda bağlama. |
| `documents.company-branding.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Şirket logo/antet (OPEN-019). |
| `documents.templates.sales.prepare` | · | · | + | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | Satış şablonu hazırlığı. |
| `documents.templates.sales.publish` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | ÖNERİ | OPEN-019 onay rolü açık; hazırlayan ≠ yayımlayan. |
| `documents.templates.inventory.prepare` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Stok şablonu. |
| `documents.templates.inventory.publish` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | ÖNERİ | OPEN-019; hazırlayan ≠ yayımlayan. |
| `documents.templates.logistics.prepare` | · | · | · | · | · | · | + | · | · | · | · | · | · | · | · | · | ÖNERİ | Lojistik şablonu. |
| `documents.templates.logistics.publish` | K | · | · | · | · | K | · | · | · | · | · | · | · | · | · | · | ÖNERİ | OPEN-019; hazırlayan ≠ yayımlayan. |

### Kimlik (7 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `identity.profile.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel. |
| `identity.access.request` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel (kendi erişim talebi). |
| `identity.access.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | IK | Critical. ROL-D17: şimdilik Sistem Yöneticisi hem kurgular hem onaylar (geçici istisna, kabul edildi); grant insan kapısı. |
| `identity.roles.manage` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | IK | Critical. ROL-D17 geçici istisna (kabul edildi). |
| `identity.audit.read` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | IK | Critical. Aday: Sistem Yöneticisi. Denetçiye erişim yok (ROL-D13). |
| `identity.assignments.read` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | IK | Critical. Aday: Sistem Yöneticisi. |
| `identity.users.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | ÖNERİ | Kullanıcı yönetimi sistem yönetimidir (SAT-D190: GM almaz). Kayıtta Critical değil. |

### Platform (1 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `platform.security-events.read` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | IK | Critical. Aday: Sistem Yöneticisi. |

### AI (1 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | OPM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `ai.panel.use` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | ROL-D16: başlangıçta bütün kullanıcı rollerine önerilir (AI-D8'in yerine); AI-D1/AI-D6 sürer, grant insan kapısı. |

## 3. Onay izinleri

### 3.1 64 onay akışı — önerilen onaylayan

Akış numaraları ve izinler iç envanter §4'tendir. "Doğrulanmadı" = uç bloğunda izin sabiti görülmedi. **Sistem
Yöneticisi hiçbir iş onayı vermez (ROL-D6).** Önerilen onaycı yoksa ya da hazırlayanla aynı kişiyse onay ilgili modül
müdürüne ve GM'ye düşer; ikisi de yoksa sahipsiz onay olarak yükselir (ROL-D3, YOL-D17). Süre sınıfı YOL-D18: 4 saat =
irsaliye, plan üstü sevk, tartım farkı, üretim planı; diğerleri 2 iş günü. Vekil aynı izne sahip modül müdürüdür,
zincirleme devredilmez, Sistem Yöneticisi vekil olamaz (YOL-D17, ROL-D4).

🔴 = açık görev ayrılığı riski veya doğrulanmamış hazırlayan ≠ onaylayan kuralı; 🟡 = **kabul edildi (ROL-Dx)** (§3.2).

| # | Akış | Onay izni | Hazırlayan | Önerilen onaylayan | Süre | Not |
|---|---|---|---|---|---|---|
| 1 | Sipariş taslağı iç onayı | `sales.orders.approve` | SAT, SATM, GM | SATM, GM | 2 iş günü | KARARLI (SAT-D6). P6 kayıtta. |
| 2 | Taban fiyat altı istisnası | `sales.orders.price-floor-exception.approve` | SAT | SATM, GM | 2 iş günü | KARARLI (SAT-D1). 🔴 SATM'nin kendi açtığı taslakta kayıt kuralı doğrulanmadı. |
| 3 | İskonto istisnası | `sales.orders.discount-exception.approve` | SAT | SATM; kendi limitini aşanı GM | 2 iş günü | KARARLI (SAT-D37/D67). |
| 4 | Fazla teslim | `sales.orders.over-delivery.approve` | DEPO (sevk) | SAT (K), SATM, GM | 2 iş günü | KARARLI (SAT-D168). SAT-D216 kendi sevki/talebi yasak, SAT-D222. |
| 5 | Risk istisnası | `sales.orders.risk-exception.approve` | SAT, SATM, DEPO | SAT (K), SATM, GM | 2 iş günü | KARARLI (SAT-D153). 🟡 kabul edildi (SAT-D153): aynı rol talep eder ve onaylar, ayrım kayıt kuralıyla. |
| 6 | Kalan miktar değişikliği | doğrulanmadı | SAT | ÖNERİ: SATM, GM | 2 iş günü | İzin adı teknik doğrulama ister. |
| 7 | Satış talebi | `sales.requests.approve` | SAT | SATM, GM | 2 iş günü | ÖNERİ. |
| 8 | Süresi dolmuş tekliften sipariş | `sales.quotes.expired-conversion.approve` | SAT | SATM, GM | 2 iş günü | KARARLI (SAT-D24/D44). 🔴 SAT-D44 bilinçli istisna: hazırlayan da onaylayabilir. |
| 9 | Satış faturası | `sales.invoices.approve` | MUH | MUH (başka kişi), MUHM, GM | 2 iş günü | KARARLI (SAT-D88/D204; `CK_Invoices_MakerChecker`). |
| 10 | Müşteri iadesi (satış) | `sales.returns.approve` | SAT | SATM, GM | 2 iş günü | ÖNERİ. |
| 11 | Sözleşme iskontosu | `sales.contracts.discount.approve` | SAT, SATM | SATM, GM | 2 iş günü | ÖNERİ; hazırlayan onaylayamaz (doğrulanmadı 🔴). |
| 12 | Fason hizmet faturası | doğrulanmadı (servis `sales.invoices.write/read` kullanıyor) | MUH | ÖNERİ: MUHM, GM | 2 iş günü | 🔴 ayrı onay izni görülmedi. |
| 13 | Alış talebi | `purchasing.requests.approve` | ALM, SAT | ALMM, GM | 2 iş günü | KARARLI (ALS-D2). |
| 14 | Alış siparişi | `purchasing.orders.approve` | ALM, SAT | ALMM, GM | 2 iş günü | KARARLI (ALS-D3). |
| 15 | Mal kabul partisi | `purchasing.goods-receipts.approve-variance` / `-overdelivery` (eşleme doğrulanmadı) | DEPO | ALMM, GM | 2 iş günü | ÖNERİ. Kabulü kaydeden onaylayamaz. |
| 16 | Gelen fatura farkı | `purchasing.invoices.approve-variance` | MUH | MUHM, GM | 2 iş günü | KARARLI (ROL-D10); Satınalma Müdürü onaylamaz. |
| 17 | Tedarikçi iadesi maliyet istisnası | doğrulanmadı | ALM | ÖNERİ: MUHM, GM | 2 iş günü | SAT-D454 "farklı kişi onayı". |
| 18 | Hizmet siparişi | doğrulanmadı (servis `purchasing.orders.approve` kullanıyor) | ALM | ALMM, GM | 2 iş günü | ÖNERİ. |
| 19 | Sayım farkı | `inventory.stock-count.approve` | DEPO | OPM, GM | 2 iş günü | KARARLI (ROL-D5). Sayımı giren onaylayamaz. |
| 20 | Hurda | `inventory.stock-scrap.approve` | DEPO, UOP | OPM, GM | 2 iş günü | KARARLI (ROL-D5); Kalite pasif (ROL-D20). |
| 21 | Transfer fazlası | `inventory.stock-transfer.overage-approve` | DEPO | OPM, GM | 2 iş günü | KARARLI (ROL-D5). |
| 22 | Transfer kaybı | `inventory.stock-transfer.loss-approve` | DEPO | OPM, GM | 2 iş günü | KARARLI (ROL-D5). |
| 23 | Açılış maliyeti | `inventory.opening-cost.approve` | MUH | MUHM, GM | 2 iş günü | ÖNERİ. |
| 24 | İrsaliye onayı | `logistics.dispatch-notes.approve` | DEPO | OPM veya SATM; GM yedek | **4 saat** | KARARLI (ROL-D14). 🔴 Hazırlayan ≠ onaylayan kodda doğrulanmadı; Satış Müdürü kendi siparişinin irsaliyesini onaylayabilir (SoD-18). |
| 25 | Plan üstü sevk | `logistics.shipments.plan-excess.approve` | DEPO | OPM, GM | **4 saat** | KARARLI (ROL-D5). |
| 26 | Tartım farkı | `logistics.weighing-differences.approve` | DEPO, UOP, KNT (hazırlayan) | OPM, GM | **4 saat** | KARARLI (ROL-D14). 🟡 kabul edildi (ROL-D14): tartımı hazırlayan girer; onay OPM. |
| 27 | Müşteri iadesi kabul kararı | `logistics.customer-returns.decision` | SAT, DEPO | SATM, OPM, GM | 2 iş günü | ÖNERİ. |
| 28 | Üretim planı | `manufacturing.plans.approve` | UPL | OPM, GM | **4 saat** | KARARLI (URT-D2, ROL-D5). |
| 29 | Resmî fatura içe aktarma partisi | `finance.official-invoice-import.approve` | FIN, MUH | FINM, GM | 2 iş günü | ÖNERİ. |
| 30 | Resmî satış faturası | `finance.official-sales-invoices.approve` | FIN | FINM, GM | 2 iş günü | ÖNERİ (SAT-D196/D197 deseni). |
| 31 | Elle banka ödemesi | `finance.manual-bank-payment.approve` | FIN | FINM, GM | 2 iş günü | ÖNERİ. 🔴 Banka hesabı/IBAN öneren kişi ödeme onaylamamalı (SoD-08). |
| 32 | Banka ödemesi ters kaydı | `finance.manual-bank-payment.reverse.approve` | FIN | FINM, GM | 2 iş günü | ÖNERİ. |
| 33 | Tahsilat ters kaydı | `finance.manual-receipt.reverse.approve` | FIN | FINM, GM | 2 iş günü | ÖNERİ. |
| 34 | Masraf ödemesi | `finance.expense-payment.approve` | FIN | FINM, GM | 2 iş günü | ÖNERİ. |
| 35 | Banka hesabı | `finance.bank-account.approve` **(Critical)** | FIN (Critical aday) | FINM (Critical aday) | 2 iş günü | IK. |
| 36 | IBAN değişikliği | doğrulanmadı (servis `finance.bank-account.propose/approve` kullanıyor; Critical) | FIN | FINM (IK) | 2 iş günü | 🔴 SoD-08. |
| 37 | Kasa hesabı | `finance.cash-account.approve` **(Critical)** | FIN | FINM (IK) | 2 iş günü | IK. |
| 38 | Kasa pasifleştirme | `finance.cash-account.passivation.approve` **(Critical)** | FIN | FINM (IK) | 2 iş günü | IK. |
| 39 | Kasa yeniden etkinleştirme | `finance.cash-account.reactivation.approve` **(Critical)** | FIN | FINM (IK) | 2 iş günü | IK. |
| 40 | Cari kredi limiti | `finance.credit-limits.approve` **(Critical)** | FIN | FINM veya GM (IK) | 2 iş günü | IK. |
| 41 | Kredi hesabı | `finance.credits.approve` **(Critical)** | FIN | FINM veya GM (IK) | 2 iş günü | IK. |
| 42 | Banka kredi limiti | doğrulanmadı | FIN | ÖNERİ: FINM, GM | 2 iş günü | İzin adı teknik doğrulama ister. |
| 43 | Kredi hareketi | doğrulanmadı (servis `finance.credits.propose/approve` kullanıyor; Critical) | FIN | FINM veya GM (IK) | 2 iş günü | — |
| 44 | Açılış bakiyesi | `finance.opening-balances.approve` | FIN | FINM, GM | 2 iş günü | ÖNERİ. |
| 45 | Açılış bakiyesi ters kaydı | doğrulanmadı (servisteki dizge kayıtlı izin listesinde yok) | FIN | ÖNERİ: FINM, GM | 2 iş günü | 🔴 teknik doğrulama. |
| 46 | Döviz bozdurma defter değeri | doğrulanmadı (servis `accounting.normal-journal.draft.approve` kullanıyor) | FIN | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 47 | Muhasebe dönem onayı | `accounting.periods.approve` | MUHM | MUHM (başka kişi), GM | 2 iş günü | KARARLI (SAT-D191; P6). Tek Muhasebe Müdürü varsa onaycı GM (ROL-D3). |
| 48 | Dönem yeniden açma | ayrı izin (ROL-D12; ad henüz kayıtlı değil — bugün servis `accounting.periods.approve` kullanıyor) | MUHM | yalnız GM | 2 iş günü | KARARLI (ROL-D12). 🔴 izin adı kayıt edilene kadar teknik açık (SoD-09). |
| 49 | Normal fiş taslağı | `accounting.normal-journal.draft.approve` | MUH | MUHM, GM | 2 iş günü | ÖNERİ; KLN-D17. |
| 50 | Fiş ters kaydı | `accounting.journal.reverse.approve` | MUH | MUHM, GM | 2 iş günü | ÖNERİ. |
| 51 | Onaylı kaynak kataloğu | `accounting.approved-source-catalog.activate` | MUH | MUHM, GM | 2 iş günü | ÖNERİ. |
| 52 | Hesap eşleme etkinleştirme | `accounting.posting-mappings.activate` | MUH | MUHM, GM | 2 iş günü | ÖNERİ. |
| 53 | Üretim maliyet farkı | doğrulanmadı | sistem | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 54 | Fatura satırı SMM düzeltmesi | doğrulanmadı | MUH | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 55 | Stok maliyet teklifleri | doğrulanmadı | sistem, MUH | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 56 | Maliyet bileşeni iptali | doğrulanmadı | MUH | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 57 | Kaynak fiş | doğrulanmadı | sistem, MUH | ÖNERİ: MUHM, GM | 2 iş günü | KLN-D17: kaynağı kesinleştiren kendi fişini onaylayamaz. |
| 58 | Kritik rol erişim talebi | `identity.access.approve` **(Critical)** | herkes (`identity.access.request`); kurgulayan Sistem Yöneticisi | Sistem Yöneticisi (IK) | 2 iş günü | 🟡 kabul edildi (ROL-D17): şimdilik aynı kişi kurgular ve onaylar; geçici istisna. |
| 59 | Şirket VKN değişikliği | `organization.company.tax-id.approve` | MUHM | GM | 2 iş günü | ÖNERİ. |
| 60 | Cari vergi kimliği değişikliği | `counterparty.parties.tax-identity.approve` **(Critical)** | MUH, MUHM | MUHM veya GM (IK) | 2 iş günü | IK. |
| 61 | Cari unvan değişikliği | `counterparties.legal_name.approve` | MUH, MUHM | MUHM, GM | 2 iş günü | ÖNERİ. |
| 62 | Cari birleştirme | `counterparty.parties.merge.approve` | ayrı hazırlama izni yok | MUHM, GM | 2 iş günü | 🔴 Hazırlayan izni olmadığı için tek kişilik işlem olabilir (SoD-16). |
| 63 | Paket standardı sürümü | `product.package-standards.approve` | UPL | OPM, GM | 2 iş günü | KISMEN (ROL-D5). |
| 64 | Fiyat listesi yayımı | `product.price-lists.publish` | SATM (`write`) | SATM (aynı kişi olabilir), GM | 2 iş günü | KARARLI (ROL-D18). 🟡 kabul edildi (ROL-D18). |

Ayarlar (10 politika ailesi) ve Belgeler (3 şablon) yayım adımları: hazırlayan ilgili rol (`*.prepare`), onaylayan /
yayımlayan ilgili müdür (üretim profilleri ve stok/lojistik şablonları OPM) ve GM; hazırlayan ≠ onaylayan (matriste `K`).
Sistem Yöneticisi bu iş politikalarını hazırlamaz ve onaylamaz (ROL-D6).

### 3.2 Görev ayrılığı (SoD) çakışmaları

Kayıt düzeyindeki P6 kuralı aynı **kaydı** korur; aşağıdakiler rol birleşimiyle ya da kayıt kuralının eksik /
doğrulanmamış olduğu yerde doğan risklerdir. Durum: 🔴 **açık** · 🟡 **kabul edildi (ROL-Dx)** — Sinan'ın bilinçli kabul
ettiği risk; denetim kaydı ve raporla izlenir · ✅ **giderildi**.

| Kimlik | Çakışma | Durum | İzleme / öneri |
|---|---|---|---|
| SoD-01 | Sistem Yöneticisi rol kurgular + erişim onaylar + denetim kaydını okur (seed `SYSTEM_ADMINISTRATOR`) | 🟡 kabul edildi (ROL-D17) — geçici istisna | Kişi sayısı artınca Yetki Yöneticisi / Yetki Onaylayıcısı ayrılır; her grant denetim kaydında |
| SoD-02 | Admin müdürlerin yaptığı her şeyi yapıyordu (SAT-D187) | ✅ giderildi (ROL-D6) | Sistem Yöneticisi iş onayı/kaydı taşımaz |
| SoD-03 | GM'nin ara dönem kayıt + onay hücreleri (üretim fiilîsi + tolerans dışı kapanış, açılış stoğu) | ✅ giderildi (ROL-D5) | Kayıt hücreleri OPM'ye geçti; GM yalnız yedek onaycı |
| SoD-04 | Satış Temsilcisi risk istisnasını talep eder ve onaylar; fazla teslimi onaylar | 🟡 kabul edildi (SAT-D153/D168) | Kendi talebi/sevki komutta reddedilir (SAT-D216); karşılıklı onay raporla izlenir |
| SoD-05 | Onay eşiğini/limitini belirleyen = eşiğe tabi onaycı | ✅ giderildi (ROL-D19) | Eşik/limit GM + SYS; teslim toleransı SAT-D189 ile Satış Müdürü'nde (kararlı) |
| SoD-06 | Muhasebe faturayı hazırlar, başka kişi olarak onaylar, eşleştirir, kesinleştirir | 🟡 kabul edildi (SAT-D235 (2)) | KLN-D17: kesinleştiren kendi 120 fişini onaylatamaz |
| SoD-07 | Satış Müdürü fiyat listesini hazırlar ve yayımlar | 🟡 kabul edildi (ROL-D18) | Yayım denetim kaydı ve fiyat değişiklik raporu |
| SoD-08 | Banka hesabı/IBAN öneren (Finans) ↔ ödeme onaylayan (Finans Müdürü) aynı kişide | 🔴 açık | FIN ve FINM aynı kişiye atanmasın; mecbursa ödeme onayı GM (ROL-D3) |
| SoD-09 | Dönem yeniden açma, dönem onayıyla aynı izinde | 🔴 açık (teknik) — karar ROL-D12 | Ayrı izin adı Accounting sahibince kaydedilmeli; yalnız GM |
| SoD-10 | Tartımı sevk/üretimi hazırlayan girer; ayrı kantarcı zorunlu değil | 🟡 kabul edildi (ROL-D14) | Tartım farkı onayı OPM; hazırlayan onaylayamaz |
| SoD-11 | Depo sayım/hurda/transfer kaydı ↔ OPM onayı | 🔴 açık (atama) | Depo/Sevk ile Operasyon Müdürü aynı kişiye atanmasın; mecbursa onay GM |
| SoD-12 | Tek adımlı finans kayıtları (tahsilat kaydı, virman, cari bakiye virmanı, teminat, çek/senet) | 🟡 kabul edildi (ROL-D11) | Günlük finans hareket raporu ile izlenir |
| SoD-13 | Siparişi onaylayan Satınalma Müdürü gelen fatura farkını da onaylıyordu | ✅ giderildi (ROL-D10) | Fatura farkı onayı Muhasebe Müdürü |
| SoD-14 | Üretim fiilîsini giren düzeltmeyi de yapabilir (`manufacturing.facts.record` + `.correct` UPL/OPM'de) | 🔴 açık | Düzeltme, kaydı girenden farklı kişi; kayıt kuralı teknik doğrulama |
| SoD-15 | Küçük kadro birleşimleri: Satış + Muhasebe, Satınalma + Depo, Satınalma + Muhasebe, Finans + Finans Müdürü, Depo + Operasyon Müdürü | 🔴 açık (atama) — istisna yok (ROL-D3) | Atama ekranında uyarı; kaçınılmazsa onay modül müdürü + GM'ye düşer |
| SoD-16 | Cari birleştirme ve unvan değişikliğinde ayrı hazırlama izni yok / kayıt kuralı doğrulanmadı | 🔴 açık (teknik) | Teknik doğrulama; ayrı öneri izni |
| SoD-17 | Ayar politikaları ve belge şablonlarında hazırla + onayla + yayımla | 🔴 açık (teknik) — Admin kısmı giderildi (ROL-D6) | Hazırlayan ≠ onaylayan kayıt kuralı zorunlu |
| SoD-18 | İrsaliye onayı OPM **veya** Satış Müdürü: Satış Müdürü kendi siparişinin irsaliyesini onaylayabilir | 🟡 kabul edildi (ROL-D14) | Hazırlayan ≠ onaylayan sürer; kodda doğrulanmadı (🔴 teknik) |
| SoD-19 | Operasyon Müdürü tek rolde sevk + depo + üretim: açılış stoğu, üretim fiilîsi, yükleme kaydı ile bunların onayları aynı rolde | 🟡 kabul edildi (ROL-D5) | Kayıt düzeyinde kendi kaydını onaylayamaz; aynı kişide kayıt+onay raporla izlenir |

### 3.3 Critical izinler ve ikinci onay gerektirenler

- **Critical (17):** `organization.companies.open`, `identity.access.approve`, `identity.roles.manage`,
  `identity.audit.read`, `identity.assignments.read`, `platform.security-events.read`, `finance.bank-account.propose`,
  `finance.bank-account.approve`, `finance.cash-account.propose`, `finance.cash-account.approve`,
  `finance.cash-account.passivation.propose`, `finance.cash-account.passivation.approve`,
  `finance.cash-account.reactivation.propose`, `finance.cash-account.reactivation.approve`,
  `finance.credit-limits.approve`, `finance.credits.approve`, `counterparty.parties.tax-identity.approve`.
  Grant'ları kritik rol erişim talebi akışından geçer (akış #58; ROL-D17 gereği şimdilik Sistem Yöneticisi). Taslak aday
  rol yazar (`IK`), hiçbirini vermez.
- **Kayıtta Critical olmayan ama dikkat isteyenler:** `settings.policies.publish-critical` (Sistem Yöneticisi, ROL-D22),
  `settings.segregation-of-duties.manage`, `identity.users.manage`, `accounting.periods.close` ve yeniden açma (ROL-D12),
  `sales.invoices.finalize` (SAT-D249: normal yetki).
- **İki kademeli onay:** SAT-D67 — Satış Müdürü'nün kendi limitini aşan iskontosu GM'ye gider. Tek adımlı finans
  kayıtlarına ikinci onay eklenmez (ROL-D11). Finans onay matrislerinin tutar eşikleri politika kararıdır.

## 4. Şirketler arası görünüm (GDC / GSP / GIN), grup içi rapor ve AI paneli

- **Model:** atama şirket bazındadır (`Assignments[].CompanyId` + kapsam `Company`/`Site`); izinlerin 271'i yalnız şirket
  geneli verilebilir. İki şirkette çalışan kişi iki ayrı atama alır.
- **GDC ve GSP:** §1 rolleri şirket başına atanır.
- **GIN:** gerçek unvan/VKN gelene kadar placeholder ve resmî belgeye kapalıdır (DEC-ORG-005, OPEN-001-GIN); o sürede
  yalnız GM, Muhasebe Müdürü ve Sistem Yöneticisi ataması önerilir (ÖNERİ).
- **Bağımsız denetçi:** sisteme erişim yok; denetlenen şirketin rapor paketi (mizan, cari, stok, maliyet, belge izleri)
  dışa aktarılarak verilir (ROL-D13, YOL-D13).
- **Grup içi işlem dökümü ve konsolide görünüm:** GM + Sistem Yöneticisi + Muhasebe Müdürü (ROL-D15). Kayıtlı izin adı
  **yoktur**; adı rapor sahibinin teknik işidir. Şirket sınırını aşan tek okuma olduğu için ayrı insan kapısında açılır
  (YOL-D6).
- **`ai.panel.use`:** başlangıçta **bütün kullanıcı rollerine** önerilir (ROL-D16; AI-D8 başlangıcının yerine geçer). İzin,
  kullanıcının göremediği veriyi açmaz (AI-D1); AI canlıda kapalı başlar ve açılması ayrı insan kapısıdır (AI-D6); grant
  insan kapısıdır. Pasif Kalite rolü atanmadığı sürece izni fiilen kullanılmaz.
- **`ai.decisions.read` (AI-D36):** bütün kullanıcı rollerine önerilir (ROL-D16 ile tutarlı); kişi adları dizinde
  maskelenir (AI-D34); grant insan kapısıdır. İzin henüz kayıtlı değildir; kaydedildiğinde CSV'ye `ai.panel.use` ile
  aynı sütunlarla eklenir.
- **Sistem Yöneticisi acil erişimi (ROL-D23):** süreli ve gerekçeli erişim ataması; başka bir yetkili onaylar, denetim
  kaydına yazılır, süre bitince kalkar. Kalıcı iş izni olarak matriste yer almaz.

## 5. Sinan'a sorular

### 5.1 v1 soruları — cevaplandı (22 Kasım 2026)

| No | Soru | Durum |
|---|---|---|
| S-01 | Muhasebe Müdürü ayrı rol mü? | Cevaplandı — ROL-D1: evet. |
| S-02 | Satış Destek ayrı mı, birleşsin mi? | Cevaplandı — ROL-D2: birleşir. |
| S-03 | Küçük kadroda SoD istisnası? | Cevaplandı — ROL-D3: istisna yok; onay modül müdürü + GM'ye düşer. |
| S-04 | GM vekâleti? | Cevaplandı — ROL-D4: ilgili izne sahip modül müdürü; Sistem Yöneticisi değil. |
| S-05 | Depo/Sevk ve Üretim Müdürü? | Cevaplandı — ROL-D5: tek rol, Operasyon Müdürü. |
| S-06 | Admin "her şey" kalsın mı? | Cevaplandı — ROL-D6: hayır; sistem yönetimi + acil durum. |
| S-07 | Satış Müdürü ekstre/kredi limiti? | Cevaplandı — ROL-D7: ikisi de evet (öneriden farklı). |
| S-08 | Muhasebe dönem/yevmiye okuması? | Cevaplandı — ROL-D8: salt okuma. |
| S-09 | Emir kapatma ve tahsilat? | Cevaplandı — ROL-D9: emir kapatma OPM; tahsilat Finans (öneriden farklı). |
| S-10 | Gelen fatura farkı? | Cevaplandı — ROL-D10: Muhasebe Müdürü. |
| S-11 | Tek adımlı finans kayıtlarına ikinci onay? | Cevaplandı — ROL-D11: hayır (öneriden farklı; SoD-12 kabul). |
| S-12 | Dönem yeniden açma ayrı izin? | Cevaplandı — ROL-D12: evet, yalnız GM. |
| S-13 | Denetçi erişimi? | Cevaplandı — ROL-D13: erişim yok (öneriden farklı; Denetçi rolü kaldırıldı). |
| S-14 | Kantar ayrı kişi mi? | Cevaplandı — ROL-D14: hazırlayan girer; onay OPM; irsaliye OPM veya Satış Müdürü (öneriden farklı). |
| S-15 | Grup içi görünüm? | Cevaplandı — ROL-D15: GM + SYS + MUHM. |
| S-16 | `ai.panel.use` genişlemesi? | Cevaplandı — ROL-D16: baştan bütün rollere (öneriden farklı). |
| S-17 | Yetki Yöneticisi / Onaylayıcısı ayrı mı? | Cevaplandı — ROL-D17: şimdilik Sistem Yöneticisi ikisini de yapar (geçici istisna; öneriden farklı). |
| S-18 | Fiyat listesi yayımı? | Cevaplandı — ROL-D18: Satış Müdürü hazırlar ve yayımlar (öneriden farklı). |
| S-19 | Satış eşik/limit? | Cevaplandı — ROL-D19: GM + SYS; tolerans Satış Müdürü. |
| S-20 | Kalite rolü? | Cevaplandı — ROL-D20: pasif. |

### 5.2 v1.1 soruları — cevaplandı (22 Kasım 2026)

| No | Soru | Durum |
|---|---|---|
| Y-01 | Marjı kim görsün? | Cevaplandı — ROL-D21: GM + Satış Müdürü (öneriden farklı); Sistem Yöneticisi görmez. |
| Y-02 | İskonto limiti ayarına GM eklensin mi? | Cevaplandı — ROL-D19 sonucu: GM + Sistem Yöneticisi. |
| Y-03 | Kritik politika yayımı kimde? | Cevaplandı — ROL-D22: Sistem Yöneticisi (öneriden farklı). |
| Y-04 | Acil durum erişimi nasıl modellensin? | Cevaplandı — ROL-D23: süreli, gerekçeli, başka yetkili onaylı, denetim kayıtlı, süresi bitince kalkan atama. |
| Y-05 | Ayrı Kantar rolü kalsın mı? | Cevaplandı — ROL-D14 sonucu: isteğe bağlı rol olarak kalır. |

Açık soru kalmadı. Atama öncesi taslak kişi listesiyle Sinan'a yeniden sunulur.

## 6. Mevcut seed rolleriyle ilişki

`identity-seed.json` bugün 5 sistem rolü taşır: `ORGANIZATION_READER`, `ORGANIZATION_OPERATOR`, `SYSTEM_ADMINISTRATOR`,
`SECURITY_ADMINISTRATOR`, `SECURITY_APPROVER`. Bunlar geliştirme/bootstrap rolleridir; taslak onları değiştirmez.
`SYSTEM_ADMINISTRATOR`'ın rol kurgulama ile erişim onayını birlikte taşıması ROL-D17 ile şimdilik korunur.

## 7. Yöntem ve sınırlar

- İzin listesi `ErpPermissions.cs` sabitlerinden alındı; CSV satır sayısı `All` kümesiyle (286) birebir eşit; Critical
  işareti `Critical`, "yalnız şirket geneli" sütunu `CompanyWideOnly` kümesindendir. v1.1 için kaynak dal birleştirildi;
  `ErpPermissions.cs` değişmedi.
- KARARLI hücreler persona kartı, satış önerisi ve ROL-D kararlarından aktarıldı; çelişkide karar dosyası geçerlidir.
- 64 akıştan 16'sında onay izni uç bloğunda görülmedi ("doğrulanmadı"); hazırlayan ≠ onaylayan kuralının her akışta
  kodda uygulandığı tek tek doğrulanmadı. 🔴 teknik satırlar doğrulama listesidir.
- Kayıtlı olmayan izin adları (grup raporu, `quality.*`, dönem yeniden açma) icat edilmedi.
