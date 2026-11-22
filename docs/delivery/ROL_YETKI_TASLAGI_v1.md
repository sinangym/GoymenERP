# Rol–Yetki Taslağı v1 (bütün modüller)

- **Durum:** TASLAK / ÖNERİ. Sinan düzeltecek ve onaylayacak. Bu belge **karar değildir**; yeni karar kimliği üretmez,
  `KARAR_SICILI.md`'yi, `docs/decisions/**`'ı, seed'i, manifesti veya kodu değiştirmez.
- **Dayanak:** YOL-D2 (`SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md`: rol–yetki taslağı, mevcut satış önerisinin
  genişletilmesi) ve YOL-D20 (`SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md`: standart roller, görev ayrılığı
  çakışmaları işaretli taslak). Dalga 1, dilim W1-D.
- **İnsan kapısı:** Hiçbir rol, izin veya grant bu belgeyle etkinleşmez. Rol tanımı, rol seed'i, grant, Critical sınıfı
  ve gerçek kişi ataması AGENTS.md "Değişmeyen insan kapıları" gereği Sinan'ın somut kararını ister. Atama kurulumda
  Ayarlar/Kimlik ekranından yapılır.
- **Makine-okunur eş:** [`ROL_YETKI_TASLAGI_v1.csv`](ROL_YETKI_TASLAGI_v1.csv) — her izin için bir satır; bu belgedeki
  §2 matrisi o dosyadan üretilmiştir. Çelişki olursa CSV'deki hücre esas alınır ve belge yeniden üretilir.

## 0. Kaynaklar ve sayılar

| Kaynak | Ne alındı |
|---|---|
| `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs` | Bütün izin sabitleri: **286** (`All` kümesi 286; **17 Critical**; 271 yalnız şirket geneli) |
| `src/backend/GoymenERP.Api/Seed/identity-seed.json` | Mevcut 5 sistem rolü (aşağıda §6) |
| `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` | Kararlı persona eşlemeleri (SAT-D141…D205, SAT-D187…D197, ALS-D53…D73, K4) |
| `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md` | Satış önerisi (SAT-D230 ile onaylı satırlar); bu taslak onu bütün modüllere genişletir |
| `docs/security/GOREV_AYRILIGI_ONAY.md`, Settings F-024 (`settings.segregation-of-duties.manage`, KIM-D39) | Görev ayrılığı ilkeleri; "hazırlayan onaylayabilir" gevşetmesi yalnız tek aktif kullanıcılı şirkette |
| `docs/delivery/arastirma/work_ai/2026-11-22_ic_envanter_work_ai.md` §4 | 64 onay/karar akışı ve izinleri |
| Karar dosyaları | P6 (hazırlayan ≠ onaylayan), SAT-D216/D222, KLN-D17, YOL-D6, YOL-D13, YOL-D17, YOL-D18, AI-D8 |

**Okuma anahtarı (matris hücreleri):**
`+` = önerilir · `K` = koşullu önerilir (kayıt düzeyinde: hazırlayan/talep eden kendi kaydını onaylayamaz ya da gerekçede
yazan ek koşul) · `IK` = Critical; taslak hiçbir role vermez, aday yalnız insan kapısında değerlendirilir · `·` = önerilmez.

**Durum sütunu:** **KARARLI** = persona kümesi bir Sinan kararında/kartta açıkça yazılı; taslak yalnız rol adına
eşler (yeni rollere — Kantar, Üretim, Kalite, Denetçi — eklenen hücreler yine ÖNERİ'dir). **KISMEN** = kararlı çekirdek
+ öneri uzantısı. **ÖNERİ** = kararlarda persona kümesi yok; tahmin değil öneri. **IK** = Critical, insan kapısı.
**YOK** = hiçbir role önerilmez.

**Özet sayılar:** 286 izinden **268**'i en az bir role önerildi (sınıflandırılan); **18**'i hiçbir role önerilmedi
(17 Critical + `identity.company-setup.first-admin.assign`). Durum dağılımı: KARARLI 66 · KISMEN 9 · ÖNERİ 193 ·
IK 16 · YOK 2. Sınıflandırılamayan (belirsiz bırakılan) izin **yoktur**; belirsizlik "ÖNERİ" ve §5 soruları ile taşınır.

## 1. Standart roller (YOL-D20)

Rol bir **yetki paketidir, kişi değildir**: küçük kadroda bir kişiye birden çok rol verilebilir (SAT-D187); bu, kayıt
düzeyindeki "hazırlayan kendi kaydını onaylayamaz" kuralını gevşetmez. Hangi rollerin aynı kişide birleşmemesi
gerektiği §3.2'dedir. Bütün roller **şirket bazında** atanır (bkz. §4).

| Kod | Rol | Kararlardaki karşılığı | İş tanımı (öneri) | Durum |
|---|---|---|---|---|
| GM | Genel Müdür | Genel Müdür (SAT-D187/D188/D190) | Bütün modüllerde müdürlerin onay ve değişikliklerini yapar; modül müdürü tanımlanmamış alanlarda (stok, lojistik, üretim, organizasyon) ara dönem müdürüdür (SAT-D191). Finansta yalnız onaylar, işlem oluşturmaz (SAT-D197). Kullanıcı/rol/sistem ayarı yönetmez (SAT-D190). | KARARLI |
| SAT | Satış Temsilcisi | Satış Uzmanı (SAT-D141); Satış Destek aynı grant'ları alır | Teklif, talep ve sipariş taslağı hazırlar, fiyat ve başlık yazar, kaynak planı girer, alış talebi/siparişi açabilir. Sipariş onaylamaz; kendi talebi dışındaki risk istisnasını ve kendi sevki dışındaki fazla teslimi onaylayabilir (SAT-D153/D216). Fatura kesmez (SAT-D202). | KARARLI |
| SATM | Satış Müdürü | Satış Müdürü (SAT-D187/D188) | Satış siparişini, fiyat/iskonto istisnalarını, iptal ve zorla kapatmayı onaylar; satış ayarlarını (teklif politikası, teslim toleransı) yönetir. Muhasebe/vergi, cari yönetimi ve diğer modül okumalarını almaz (SAT-D191/D192). | KARARLI |
| ALM | Satınalma | Satın Alma Uzmanı (ALS-D53) | Alış talebi ve siparişi hazırlar, revize eder, kendi talebini geri çeker, tedarikçi iadesini hazırlar. Onaylamaz, kalanı iptal etmez (ALS-D66). | KARARLI |
| ALMM | Satınalma Müdürü | Satın Alma Müdürü (ALS-D53) | Alış talebini ve siparişini onaylar/reddeder, kalanı iptal eder, mal kabul farkı ve fazla teslimi onaylar. Alış faturası kurunu düzeltmez (ALS-D56). | KARARLI (onay farkları ÖNERİ) |
| DEPO | Depo / Sevk | Sevkiyatçı (kart §4) + stok saha işleri | Yükleme talimatı, yükleme, sevk, irsaliye taslağı, mal kabul, sayım, transfer ve hurda kaydı yapar. Fiyat ve finans bakiyesi görmez (kart §4, PRS-02). Kendi kaydını onaylamaz; onaylar GM'de (ara dönem). | KISMEN |
| KNT | Kantar | yok (yeni) | Kantar tartımını ve tartım farkını kaydeder, sevk/alış iş listesini görür. Tartımı düzeltmez, farkı onaylamaz. Depo/Sevk ile aynı kişide birleşmemesi önerilir (SoD-10). | ÖNERİ |
| UPL | Üretim Planlama | Üretim müdürü yer tutucusu (URT-D2) değil; planlayıcı | Üretim planı ve emrini hazırlar, emir kapatır, takvim/kapasite ve üretim profillerini hazırlar. Planı ve tolerans dışı kapanışı onaylamaz (GM ara dönem). | ÖNERİ |
| UOP | Üretim Operatörü | yok (yeni) | Üretim fiilîsini, etiket dönüşümünü ve hurdayı sahada kaydeder; 1./2. kalite ayrımını etiketler (URT-D14). Düzeltme ve onay yapmaz. | ÖNERİ |
| KAL | Kalite (ileride) | Kalite sorumlusu adayı (EK-14 Q-05, I05.1) | Bugün yalnız okuma ve 2. kalite sınıflaması önerilir; üretimde karantina yok (URT-D14). `quality.*` izinleri kayıtlı değildir; gelince hurda/sapma onayı adayıdır. **Başlangıçta pasif.** | ÖNERİ |
| FIN | Finans | Finans Uzmanı (SAT-D196) | Tahsilat, ödeme, masraf, virman, çek/senet ve kredi işlemlerini oluşturur; Critical banka/kasa/kredi önerileri adaydır (insan kapısı). Onaylamaz. | KARARLI (çekirdek) |
| FINM | Finans Müdürü | Finans Müdürü (SAT-D196) | Finans işlemlerini onaylar; banka CSV eşlemesi, teminat türleri, finans açılış dönemi ve finans politika taslağı. Oluşturmaz. Critical onaylar insan kapısında adaydır. | KARARLI (çekirdek) |
| MUH | Muhasebe | Muhasebe (SAT-D142) | Satışta her şeyi görür; satış/alış faturasını hazırlar, keser, eşleştirir, kesinleştirir; vergi ayarı önerir; cari e-belge durumunu günceller (SAT-D155); fiş taslağı ve ters kayıt önerir. Sipariş yazmaz/onaylamaz. | KARARLI (fiş yazmaları ÖNERİ) |
| MUHM | Muhasebe Müdürü *(16. rol; kararlarda var, görev listesinde yok)* | Muhasebe Müdürü (SAT-D187/D188/D192) | Muhasebe dönemi, fiş onayı, hesap eşleme/katalog etkinleştirme, vergi ana verisi, kalem tipi, kur düzeltmesi; cari kart yönetimi yalnız onda (SAT-D192). Satış yetkisi almaz. | KARARLI |
| SYS | Sistem Yöneticisi | En Yetkili Admin (kart; SAT-D187/D190) | Kullanıcı, sistem ayarları, görev ayrılığı ve saklama ayarları; karar gereği müdürlerin yaptığı her şeyi de yapabilir (SAT-D187). Critical adlar (rol yönetimi, erişim onayı, denetim/güvenlik okuması) yalnız insan kapısında ve ayrı kişilere (SoD-01). | KARARLI (SoD-02 sorusu) |
| DEN | Salt Okunur Denetçi | yok (YOL-D13) | Bağımsız denetime tabi şirkette mizan, cari, stok, maliyet, fatura ve belge izlerini **salt okur**. Hiçbir yazma/onay izni yok; marj görmez (SAT-D250). Sisteme erişim verilmesi ayrı insan kararıdır (YOL-D13). | ÖNERİ |

Bugünkü kararlarda **ayrı rol olarak tanımlanmamış** ama ihtiyaç duyulan müdürlükler: Depo/Lojistik (Sevk) Müdürü ve
Üretim Müdürü. Bunlar tanımlanana kadar ilgili onaylar SAT-D191 gereği GM + Admin'dedir (soru S-05).

## 2. Rol × izin matrisi

### 2.1 Rol başına özet

| Rol | Önerilen izin | Bunların koşullu (`K`) olanı |
|---|---|---|
| GM | 190 | 39 |
| SAT | 50 | 3 |
| SATM | 66 | 10 |
| ALM | 23 | 0 |
| ALMM | 33 | 5 |
| DEPO | 48 | 0 |
| KNT | 19 | 0 |
| UPL | 39 | 0 |
| UOP | 21 | 0 |
| KAL | 13 | 0 |
| FIN | 51 | 2 |
| FINM | 38 | 7 |
| MUH | 55 | 1 |
| MUHM | 83 | 9 |
| SYS | 268 (+16 `IK` adayı) | 48 |
| DEN | 49 | 0 |

GM'nin sayısı yüksektir, çünkü SAT-D191 ara dönemi stok, lojistik, üretim ve organizasyonun müdür hücrelerini GM'de
tutar. Modül müdürleri tanımlanırsa (S-05) bu hücreler onlara geçer ve GM onay darboğazı küçülür.

### 2.2 Hiçbir role önerilmeyen izinler (18)

| İzin | Neden |
|---|---|
| `organization.companies.open` | Critical; şirket açma grup düzeyinde insan kararıdır (KIM-D21/KIM-D56). |
| `identity.company-setup.first-admin.assign` | KIM-D56: ilk Admin seçimi açık ve insan sahipli; rol paketine konmaz. |
| `identity.access.approve` | Critical; kritik rol erişim talebinin onayı. Aday: ayrı Yetki Onaylayıcısı (seed `SECURITY_APPROVER`). |
| `identity.roles.manage` | Critical; rol kurgulama. Aday: Yetki Yöneticisi (seed `SECURITY_ADMINISTRATOR`); onaylayıcıdan ayrı kişi. |
| `identity.audit.read` | Critical; denetim kaydı okuma. Aday: Yetki Onaylayıcısı; Denetçi'ye verilmesi YOL-D13 kapsamında ayrı insan kararı. |
| `identity.assignments.read` | Critical. Aday: Sistem Yöneticisi, Denetçi (insan kapısı). |
| `platform.security-events.read` | Critical. Aday: Sistem Yöneticisi. |
| `finance.bank-account.propose` / `.approve` | Critical. Aday: Finans önerir, Finans Müdürü onaylar; öneren kişi ödeme onaylamamalı (SoD-08). |
| `finance.cash-account.propose` / `.approve` / `.passivation.propose` / `.passivation.approve` / `.reactivation.propose` / `.reactivation.approve` | Critical (6 ad). Aday: Finans önerir, Finans Müdürü onaylar. |
| `finance.credit-limits.approve` | Critical. Aday: Finans Müdürü veya GM (SAT-D197). |
| `finance.credits.approve` | Critical. Aday: Finans Müdürü veya GM. |
| `counterparty.parties.tax-identity.approve` | Critical. Aday: Muhasebe Müdürü veya GM; değişikliği hazırlayandan farklı kişi. |

Critical adlar Admin dahil hiçbir role taslakta verilmez; Admin sütununda `IK` (insan kapısı adayı) görünür.
Kayıtta Critical olmayan ama **sistem yönetimi** olduğu için yalnız Sistem Yöneticisi'ne önerilen 6 ad:
`settings.sales-discount-limits.manage` (SAT-D65/D69 kararlı), `settings.login-rate-limit.manage`,
`settings.segregation-of-duties.manage`, `settings.notification-retention.manage`, `settings.policies.publish-critical`,
`identity.users.manage`.

### 2.3 Matrisin tamamı (modül sırasıyla)

Sütunlar: GM Genel Müdür · SAT Satış Temsilcisi · SATM Satış Müdürü · ALM Satınalma · ALMM Satınalma Müdürü · DEPO
Depo/Sevk · KNT Kantar · UPL Üretim Planlama · UOP Üretim Operatörü · KAL Kalite · FIN Finans · FINM Finans Müdürü ·
MUH Muhasebe · MUHM Muhasebe Müdürü · SYS Sistem Yöneticisi · DEN Salt Okunur Denetçi.

### Organizasyon (13 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `organization.companies.open` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | YOK | Critical; şirket açma grup düzeyinde insan kararıdır (KIM-D21/KIM-D56). Hiçbir role önerilmez. |
| `identity.company-setup.first-admin.assign` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | YOK | KIM-D56: ilk Admin seçimi açık ve insan sahipli; yalnız kayıtlı, rol paketi olamaz. |
| `organization.context.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin (bütün personalar); Denetçi için ÖNERİ. |
| `organization.sites.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin; Denetçi için ÖNERİ. |
| `organization.locations.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin; Denetçi için ÖNERİ. |
| `organization.work-centers.read` | + | · | · | · | · | + | · | + | + | + | · | · | · | · | + | + | KISMEN | Kart: Sevkiyatçı ve GM +. Üretim/Kalite/Denetçi ÖNERİ (iş merkezi seçimi ve izleme). |
| `organization.locations.status.update` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D191 ara dönem: müdürü olmayan modül GM + Admin. |
| `organization.sites.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Organizasyon ana verisi; müdürü olmayan modül kuralı (SAT-D191) yorumu. |
| `organization.locations.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Organizasyon ana verisi; SAT-D191 yorumu. |
| `organization.work-centers.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Organizasyon ana verisi; SAT-D191 yorumu. |
| `organization.work-centers.calendar.write` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | URT-D11 hat takvimi planlamanın işidir. |
| `organization.company.tax-id.request` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Şirket VKN değişikliği talebi muhasebe tarafından hazırlanır. |
| `organization.company.tax-id.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan; talebi açan Admin onaylayamaz (kayıt kuralı). |

### Cari (15 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `counterparty.parties.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin; Denetçi için ÖNERİ (YOL-D13 cari paketi). |
| `counterparty.parties.create` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D192: cari yönetimi yalnız Muhasebe Müdürü (+ GM, Admin). |
| `counterparty.parties.update.basic` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D192. |
| `counterparty.accounts.create` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D192. |
| `counterparty.accounts.status.update` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D192. |
| `counterparty.parties.tax-identity.update` | · | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Vergi kimliği değişikliğini muhasebe hazırlar; onayı Critical ayrı izindir. |
| `counterparty.parties.tax-identity.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical; insan kapısı. Aday: Muhasebe Müdürü veya GM (update sahibinden farklı kişi). |
| `counterparty.customer-groups.manage` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Müşteri grubu satış politikasıdır. |
| `counterparty.accounts.customer-group.assign` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Müşteri grubu ataması satış politikasıdır. |
| `counterparty.accounts.e-document.update` | · | · | · | · | · | · | · | · | · | · | · | · | + | · | + | · | KARARLI | SAT-D155: Muhasebe; GM ve müdürler almaz. |
| `counterparties.legal_name.propose` | · | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Unvan değişikliğini muhasebe önerir. |
| `counterparties.legal_name.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | K | K | · | ÖNERİ | Hazırlayan ≠ onaylayan; öneren kendi önerisini onaylayamaz. |
| `counterparty.parties.merge.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Cari birleştirme geri dönüşü zor; müdür seviyesi. |
| `counterparty.parties.succession.record` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Halef/selef kaydı cari yönetimidir (SAT-D192 yorumu). |
| `counterparty.parties.group-deactivate` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Grup genelinde etkili; müdür seviyesi. |

### Ürün (11 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `product.operational-selection.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel izin; Denetçi için ÖNERİ. |
| `product.price-lists.read` | + | + | + | · | · | · | · | · | · | · | · | · | + | + | + | + | ÖNERİ | Fiyat listesi satış ve muhasebe okumasıdır; Depo/Sevk fiyat görmez (kart §4). |
| `product.price-lists.write` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Fiyat listesi hazırlığı satış müdürlüğündedir. |
| `product.price-lists.publish` | + | · | K | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | 'write yayımı kapsamaz'. Satış Müdürü yalnız başkasının hazırladığı sürümü yayımlayabilir; hazırlayan ≠ yayımlayan doğrulanmadı (SoD-07). |
| `product.item-type.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D145/D149, SAT-D188. |
| `product.dimension.manage` | + | · | · | · | + | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | URN-D10 ortak ebat; üretim ve satınalma ana verisi. |
| `product.profile-weights.import` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Profil ağırlıkları üretim ana verisidir. |
| `product.package-standards.manage` | · | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Paket standardını üretim planlama hazırlar. |
| `product.paint-memberships.manage` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Boya üyeliği üretim ana verisidir. |
| `product.cogs-classifications.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | SMM sınıflaması muhasebe politikasıdır. |
| `product.package-standards.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan; Üretim Müdürü tanımlanana kadar GM (SAT-D191). |

### Stok (19 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `inventory.labels.read` | + | + | + | + | + | + | + | + | + | + | · | · | + | + | + | + | KISMEN | Kart: Sevkiyatçı + GM +; diğerleri ÖNERİ (operasyon ve denetim için stok görünümü). |
| `inventory.balances.read` | + | + | + | + | + | + | + | + | + | + | · | · | + | + | + | + | KISMEN | Kart: Sevkiyatçı + GM +; diğerleri ÖNERİ. |
| `inventory.opening-stock.record` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D191 ara dönem (Stok Müdürü yok). Depo Müdürü tanımlanırsa ona geçer (soru). |
| `inventory.physical-allocation.write` | · | + | + | · | · | + | · | + | · | · | · | · | · | · | + | · | ÖNERİ | K4.4: satış tarafına tahsis/rezervasyon; depo ve planlama fiziksel ayırır. |
| `inventory.future-allocation.read` | + | + | + | + | + | + | · | + | · | · | · | · | · | · | + | + | ÖNERİ | Gelecek tedarik ayırma (F017) satış ve satınalma görünümü. |
| `inventory.future-allocation.write` | · | + | + | + | + | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | F017: satış ve satınalma ayırma yapar. |
| `inventory.label-transformation.record` | · | · | · | · | · | + | · | · | + | · | · | · | · | · | + | · | ÖNERİ | Etiket dönüşümü depo/üretim sahasında kaydedilir. |
| `inventory.label-classification.write` | · | · | · | · | · | + | · | · | + | + | · | · | · | · | + | · | ÖNERİ | URT-D14: yalnız 1./2. kalite ayrımı; Kalite rolü ileride. |
| `inventory.stock-count.record` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Sayımı depo girer. |
| `inventory.stock-count.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Sayımı giren onaylayamaz (GOREV_AYRILIGI_ONAY). Depo Müdürü yoksa GM. |
| `inventory.stock-scrap.record` | · | · | · | · | · | + | · | · | + | · | · | · | · | · | + | · | ÖNERİ | Hurdayı saha kaydeder. |
| `inventory.stock-scrap.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Kayıt ≠ onay. Kalite rolü gelince aday (EK-14 Q-05). |
| `inventory.stock-transfer.write` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Transferi depo yazar. |
| `inventory.stock-transfer.cancel` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | İptal depo veya yönetici. |
| `inventory.stock-transfer.overage-approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Transfer fazlası onayı; kabul eden depo onaylayamaz. |
| `inventory.stock-transfer.loss-record` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Kaybı depo kaydeder. |
| `inventory.stock-transfer.loss-approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Kaybı kaydeden onaylayamaz. |
| `inventory.opening-cost.submit` | · | · | · | · | · | · | · | · | · | · | · | · | + | · | + | · | ÖNERİ | Açılış maliyeti muhasebe verisidir. |
| `inventory.opening-cost.approve` | + | · | · | · | · | · | · | · | · | · | · | · | · | K | K | · | ÖNERİ | Gönderen ≠ onaylayan. |

### Finans (56 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `finance.bank-account.read` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | + | KARARLI | SAT-D196 (Finans Uzmanı); GM almaz (SAT-D197). Denetçi ÖNERİ. |
| `finance.bank-account.propose` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans. Öneren, elle banka ödemesini onaylayan kişi olmamalı (GOREV_AYRILIGI_ONAY ilk madde). |
| `finance.bank-account.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans Müdürü; öneren ≠ onaylayan. |
| `finance.cash-account.read` | · | · | · | · | · | · | · | · | · | · | + | + | · | · | + | + | ÖNERİ | Kasa hesabı oluşturma ve onayın gerektirdiği okuma. |
| `finance.cash-account.propose` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans. |
| `finance.cash-account.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans Müdürü (öneren ≠ onaylayan). |
| `finance.cash-account.passivation.propose` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans. |
| `finance.cash-account.passivation.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans Müdürü (öneren ≠ onaylayan). |
| `finance.cash-account.reactivation.propose` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans. |
| `finance.cash-account.reactivation.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans Müdürü (öneren ≠ onaylayan). |
| `finance.credit-limits.read` | + | · | + | · | · | · | · | · | · | · | + | + | · | · | + | + | ÖNERİ | Kredi limiti finans + risk istisnasını değerlendiren Satış Müdürü okuması (soru). |
| `finance.credit-limits.propose` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | SAT-D196: Finans oluşturur. |
| `finance.credit-limits.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans Müdürü veya GM (SAT-D197). |
| `finance.collaterals.read` | + | · | · | · | · | · | · | · | · | · | + | + | · | · | + | + | ÖNERİ | Teminat finans okumasıdır. |
| `finance.collaterals.record` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | SAT-D196: Finans oluşturur. Onay adımı yok (SoD-12). |
| `finance.opening-balances.read` | + | · | · | · | · | · | · | · | · | · | + | + | · | + | + | + | ÖNERİ | Açılış bakiyesi finans + muhasebe müdürü okuması. |
| `finance.opening-balances.propose` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | SAT-D196. |
| `finance.opening-balances.approve` | K | · | · | · | · | · | · | · | · | · | · | K | · | · | K | · | ÖNERİ | SAT-D196/D197: Finans Müdürü ve GM onaylar; öneren onaylayamaz. |
| `finance.credits.read` | + | · | · | · | · | · | · | · | · | · | + | + | · | · | + | + | ÖNERİ | Kredi hesabı finans okumasıdır. |
| `finance.credits.propose` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | SAT-D196. |
| `finance.credits.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Finans Müdürü veya GM. |
| `finance.official-invoice-import.upload` | · | · | · | · | · | · | · | · | · | · | + | · | + | · | + | · | ÖNERİ | Resmî fatura listesi finans veya muhasebe yükler (soru). |
| `finance.official-invoice-import.approve` | K | · | · | · | · | · | · | · | · | · | · | K | · | · | K | · | ÖNERİ | Yükleyen onaylayamaz. |
| `finance.official-sales-invoices.record` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Resmî satış faturası kaydı finans tarafıdır. |
| `finance.official-sales-invoices.approve` | K | · | · | · | · | · | · | · | · | · | · | K | · | · | K | · | ÖNERİ | SAT-D196/D197. |
| `finance.official-sales-invoices.withdraw` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Hazırlayanın geri çekmesi. |
| `finance.manual-receipt.draft.save` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | KARARLI | SAT-D196 (kart Finans personaları tablosu). |
| `finance.manual-receipt.draft.read` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | KARARLI | SAT-D196. |
| `finance.manual-receipt.amount.read` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | + | KARARLI | SAT-D196; Denetçi ÖNERİ. |
| `finance.manual-receipt.record` | · | · | · | · | · | · | · | · | · | · | K | · | · | · | + | · | ÖNERİ | Kart: 'tek dar yetkili operatör' (FIN-T05) NEEDS-SINAN. Öneri: yalnız adı verilen Finans kişisi; onay adımı yok (SoD-12). |
| `finance.manual-receipt.reverse.propose` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | SAT-D196. |
| `finance.manual-receipt.reverse.approve` | K | · | · | · | · | · | · | · | · | · | · | K | · | · | K | · | ÖNERİ | SAT-D196/D197; öneren onaylayamaz. |
| `finance.manual-bank-payment.draft.save` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | SAT-D196 (oluşturma). |
| `finance.manual-bank-payment.draft.read` | + | · | · | · | · | · | · | · | · | · | + | + | · | · | + | · | ÖNERİ | Oluşturma + onayın gerektirdiği okuma (SAT-D197). |
| `finance.manual-bank-payment.approve` | K | · | · | · | · | · | · | · | · | · | · | K | · | · | K | · | ÖNERİ | SAT-D196/D197; hazırlayan onaylayamaz. Banka hesabı öneren kişi bu onayı almamalı. |
| `finance.manual-bank-payment.record` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Onaylı ödemenin kaydı; 'record' adları NEEDS-SINAN (kart). |
| `finance.manual-bank-payment.reverse.propose` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | SAT-D196. |
| `finance.manual-bank-payment.reverse.approve` | K | · | · | · | · | · | · | · | · | · | · | K | · | · | K | · | ÖNERİ | SAT-D196/D197. |
| `finance.expense-payment.draft.save` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | SAT-D196. |
| `finance.expense-payment.draft.read` | + | · | · | · | · | · | · | · | · | · | + | + | · | · | + | · | ÖNERİ | Oluşturma + onay okuması. |
| `finance.expense-payment.approve` | K | · | · | · | · | · | · | · | · | · | · | K | · | · | K | · | ÖNERİ | SAT-D196/D197. |
| `finance.expense-payment.record` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Onaylı masrafın kaydı; NEEDS-SINAN (kart 'record' kuralı). |
| `finance.cash-transfers.read` | + | · | · | · | · | · | · | · | · | · | + | + | · | · | + | + | ÖNERİ | Virman okuması. |
| `finance.cash-transfers.commit` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Kendi hesapları arası virman; onay adımı yok (SoD-12). |
| `finance.customer-instrument.read` | + | · | · | · | · | · | · | · | · | · | + | + | · | · | + | + | ÖNERİ | Çek/senet okuması. |
| `finance.customer-instrument.amount.read` | + | · | · | · | · | · | · | · | · | · | + | + | · | · | + | + | ÖNERİ | Çek/senet tutarı. |
| `finance.customer-instrument.receive` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Çek/senet alma finans işlemidir. |
| `finance.customer-instrument.status.change` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Durum değişikliği. |
| `finance.customer-instrument.collect` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Tahsil. |
| `finance.party-ledger.read` | + | · | · | · | · | · | · | · | · | · | + | + | + | + | + | + | ÖNERİ | Cari ekstre finans + muhasebe; Satış görünümü soru (S-07). |
| `finance.party-ledger.amount.read` | + | · | · | · | · | · | · | · | · | · | + | + | + | + | + | + | ÖNERİ | Cari ekstre tutarları. |
| `finance.party-balance-transfers.record` | · | · | · | · | · | · | · | · | · | · | K | · | · | · | + | · | ÖNERİ | Cari bakiye virmanı; onay adımı yok (SoD-12) — kim yapsın soru. |
| `finance.open-items.read` | + | · | · | · | · | · | · | · | · | · | + | + | + | + | + | + | ÖNERİ | Açık kalem okuması. |
| `finance.open-items.amount.read` | + | · | · | · | · | · | · | · | · | · | + | + | + | + | + | + | ÖNERİ | Açık kalem tutarı. |
| `finance.allocation.write` | · | · | · | · | · | · | · | · | · | · | + | · | · | · | + | · | ÖNERİ | Kapama/eşleştirme finans işlemidir. |
| `finance.allocation.read` | + | · | · | · | · | · | · | · | · | · | + | + | + | + | + | + | ÖNERİ | Kapama okuması. |

### Üretim (17 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `manufacturing.plans.write` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | KISMEN | Kart: GM + Admin ara dönem; Üretim Planlama ÖNERİ. |
| `manufacturing.plans.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | KARARLI | URT-D2 Üretim müdürü; tanımlanana kadar GM + Admin (SAT-D191). Planı yazan onaylayamaz. |
| `manufacturing.orders.create` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | KISMEN | Kart: GM + Admin; Üretim Planlama ÖNERİ. |
| `manufacturing.facts.record` | + | · | · | · | · | · | · | + | + | · | · | · | · | · | + | · | KISMEN | Kart: GM + Admin ara dönem; Operatör/Planlama ÖNERİ. |
| `manufacturing.orders.read` | + | · | + | · | · | + | · | + | + | + | · | · | · | + | + | + | KISMEN | Kart: Sevkiyatçı + GM +; diğerleri ÖNERİ. |
| `manufacturing.facts.correct` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Fiilî düzeltme operatör dışında; kaydı giren kendi düzeltmesini yapmamalı (soru). |
| `manufacturing.orders.close` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Kart: kimin kapatacağı NEEDS-SINAN. Öneri: planlama kapatır. |
| `manufacturing.orders.close-exception.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Tolerans dışı kapanışı fiilî miktarı giren onaylayamaz (GOREV_AYRILIGI_ONAY). |
| `manufacturing.orders.write` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Emir düzeltme planlamanındır. |
| `manufacturing.orders.cancel` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Emir iptali. |
| `manufacturing.orders.reopen` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Kapanmış emri yeniden açma müdür seviyesi. |
| `manufacturing.plans.cancel` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Plan iptali. |
| `manufacturing.plans.reorder` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Plan sıralaması. |
| `manufacturing.scrap-prices.read` | + | · | · | · | · | · | · | + | · | · | · | · | + | + | + | + | ÖNERİ | Hurda fiyatı maliyet okumasıdır. |
| `manufacturing.scrap-prices.write` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Hurda fiyatı maliyet politikasıdır. |
| `manufacturing.reports.read` | + | · | · | · | · | · | · | + | · | · | · | · | · | + | + | + | ÖNERİ | Üretim raporu. |
| `manufacturing.plans.read` | + | + | + | · | · | + | · | + | + | · | · | · | · | · | + | + | ÖNERİ | Plan görünümü (SAT-D179 kaynak planı için satış da görür). |

### Satış (40 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `sales.drafts.create` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D141, SAT-D194. |
| `sales.drafts.read` | + | + | + | · | · | · | · | · | · | · | · | · | + | + | + | + | KARARLI | Kart; Denetçi ÖNERİ. Depo/Sevk engelli (kart). |
| `sales.orders.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | K | · | KARARLI | SAT-D6, SAT-D188; P6 hazırlayan ≠ onaylayan. |
| `sales.orders.price.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D151, SAT-D188. |
| `sales.orders.price.read` | + | + | + | · | · | · | · | · | · | · | · | · | + | + | + | + | KARARLI | SAT-D150; Depo/Sevk görmez. Denetçi ÖNERİ. |
| `sales.orders.margin.read` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D250, SAT-D276 (2): yalnız GM + Admin. |
| `sales.orders.price-floor-exception.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | K | · | KARARLI | SAT-D1, SAT-D188. |
| `sales.orders.discount-exception.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | K | · | KARARLI | SAT-D37/D67: Satış Müdürü'nün kendi limitini aşanı başkası onaylar. |
| `sales.orders.delivery-schedule.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Teslim planı sipariş düzenlemesidir. |
| `sales.orders.cancel` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D4; Satış Temsilcisi hücresi ÖNERİ: verme. |
| `sales.orders.force-close` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | SATIS_ROL_YETKI_ONERISI_v1 öneri 5. |
| `sales.orders.close` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D168, SAT-D230. |
| `sales.orders.over-delivery.approve` | K | K | K | · | · | · | · | · | · | · | · | · | · | · | K | · | KARARLI | SAT-D168; SAT-D216 kendi sevki/talebi için onay yok; SAT-D222. |
| `sales.orders.risk-exception.request` | + | + | + | · | · | + | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D159 (Sevkiyatçı dahil, kart). |
| `sales.orders.risk-exception.approve` | K | K | K | · | · | · | · | · | · | · | · | · | · | · | K | · | KARARLI | SAT-D153: kendi talebini onaylayamaz. |
| `sales.orders.lines.close` | + | K | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D168; SAT-D216. |
| `sales.orders.source-plan.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D179. |
| `sales.requests.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Satış talebi satış personelindedir. |
| `sales.requests.read` | + | + | + | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Satış okuması (SAT-D142 'her şeyi görür'). |
| `sales.requests.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Talep onayı müdür seviyesi; açan onaylayamaz. |
| `sales.quotes.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Teklif hazırlama. |
| `sales.quotes.read` | + | + | + | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Satış okuması. |
| `sales.quotes.expired-conversion.approve` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D24/D44 ('yönetici'); SAT-D44 istisnası: hazırlayan da onaylayabilir. |
| `sales.invoices.read` | + | + | + | · | · | · | · | · | · | · | · | · | + | + | + | + | KARARLI | SAT-D110; Denetçi ÖNERİ. |
| `sales.invoices.write` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | KARARLI | SAT-D202: faturayı Muhasebe keser; Satış görür ama kesemez. |
| `sales.invoices.approve` | K | · | · | · | · | · | · | · | · | · | · | · | K | K | K | · | KARARLI | SAT-D88, SAT-D204: hazırlayan onaylayamaz. |
| `sales.invoices.match` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | SAT-D142 yorumu (SATIS_ROL öneri 7). |
| `sales.invoices.finalize` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | KARARLI | SAT-D230; SAT-D235 (2) kesinleştirmede SoD yok; KLN-D17. |
| `sales.invoices.export` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | KARARLI | SAT-D276 (1). |
| `sales.reports.read` | + | + | + | · | · | + | · | · | · | · | · | · | + | + | + | + | KARARLI | SAT-D143 (kart Sevkiyatçı +); Denetçi ÖNERİ. |
| `sales.returns.read` | + | + | + | · | · | + | · | · | · | · | · | · | + | + | + | + | ÖNERİ | İade okuması. |
| `sales.returns.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | İadeyi satış hazırlar. |
| `sales.returns.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan. |
| `sales.returns.finalize` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Kesinleştirme belge/muhasebe tarafı (fatura emsali SAT-D202). |
| `sales.contracts.read` | + | + | + | · | · | · | · | · | · | · | · | · | + | + | + | + | ÖNERİ | Sözleşme okuması. |
| `sales.contracts.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Sözleşmeyi satış hazırlar. |
| `sales.contracts.discount.approve` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan. |
| `sales.orders.quantity.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Miktar revizyonu (SAT-D3 yeniden onay ister). |
| `sales.orders.header.write` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D144, SAT-D185. |
| `sales.orders.customer-acceptance.record` | + | + | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Müşteri kabulünü satış kaydeder. |

### Lojistik / Sevk / Kantar (22 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `logistics.loading-instructions.create` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | KARARLI | Kart: Sevkiyatçı +; GM + Admin ara dönem. |
| `logistics.loading-instructions.read` | + | + | + | · | · | + | + | · | · | · | · | · | · | · | + | + | KISMEN | Kart: Sevkiyatçı + GM +; diğerleri ÖNERİ. |
| `logistics.loads.record` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | KARARLI | Kart: Sevkiyatçı +; GM + Admin ara dönem. |
| `logistics.shipments.record` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Sevk kaydı depo/sevk işidir. |
| `logistics.shipments.plan-excess.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Plan üstü sevk (YOL-D18: 4 saat sınıfı); Lojistik Müdürü yoksa GM. Sevki yapan onaylayamaz. |
| `logistics.deliveries.record` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Teslim kaydı. |
| `logistics.weighings.record` | · | · | · | · | · | + | + | · | · | · | · | · | · | · | + | · | ÖNERİ | Tartımı kantar girer; kantarcı yoksa depo (SoD-10). |
| `logistics.weighings.correct` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Tartımı giren kendi tartımını düzeltmemeli. |
| `logistics.dispatch-notes.create` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | İrsaliye taslağını sevk hazırlar. |
| `logistics.dispatch-notes.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | İrsaliye onayı (YOL-D18: 4 saat); hazırlayan onaylayamaz. Sevk Sorumlusu/Lojistik Müdürü tanımı soru. |
| `logistics.dispatch-notes.cancel` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Onaylı irsaliye iptali yönetici seviyesi. |
| `logistics.transfer-outgoing-documents.read` | + | · | · | · | · | + | + | · | · | · | · | · | · | · | + | + | ÖNERİ | Transfer çıkış belgeleri. |
| `logistics.transfer-departures.record` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Transfer çıkışı. |
| `logistics.loading-instructions.cancel` | + | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Talimatı açan veya yönetici iptal eder. |
| `logistics.customer-returns.read` | + | + | + | · | · | + | · | · | · | · | · | · | + | · | + | + | ÖNERİ | Müşteri iadesi okuması. |
| `logistics.customer-returns.request` | · | + | + | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | İade kabul talebi. |
| `logistics.customer-returns.decision` | K | · | K | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Talep açan karar veremez. |
| `logistics.weighing-differences.record` | · | · | · | · | · | + | + | · | · | · | · | · | · | · | + | · | ÖNERİ | Tartım farkını kantar/depo kaydeder. |
| `logistics.weighing-differences.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | YOL-D18: 4 saat. Farkı kaydeden onaylayamaz; alışta Satınalma Müdürü adaylığı soru. |
| `logistics.weighing-differences.read` | + | · | + | · | + | + | + | · | · | · | · | · | · | · | + | + | ÖNERİ | Tartım farkı okuması. |
| `logistics.operations-work.sales.read` | + | + | + | · | · | + | + | · | · | · | · | · | · | · | + | · | ÖNERİ | Operasyon iş listesi (satış). |
| `logistics.operations-work.purchase.read` | + | · | · | + | + | + | + | · | · | · | · | · | · | · | + | · | ÖNERİ | Operasyon iş listesi (alış). |

### Satınalma (19 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `purchasing.orders.create` | + | + | · | + | + | · | · | · | · | · | · | · | · | · | + | · | KARARLI | ALS-D53, kart §4 (Satış Destek/Uzmanı alış siparişi girer), SAT-D188. |
| `purchasing.orders.read` | + | + | · | + | + | + | · | · | · | · | · | · | + | + | + | + | KARARLI | Kart + ALS-D73; Denetçi ÖNERİ. Depo fiyatı görmez (PRS-02). |
| `purchasing.orders.approve` | K | · | · | · | K | · | · | · | · | · | · | · | · | · | K | · | KARARLI | ALS-D3, ALS-D53; hazırlayan onaylayamaz. |
| `purchasing.goods-receipts.record` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Kart: deny-first, sorulmadı. Öneri: mal kabulü depo yapar. |
| `purchasing.requests.approve` | K | · | · | · | K | · | · | · | · | · | · | · | · | · | K | · | KARARLI | ALS-D2, ALS-D53. |
| `purchasing.requests.withdraw` | · | + | · | + | + | · | · | · | · | · | · | · | · | · | + | · | KARARLI | ALS-D54, ALS-D47 (talebi açan geri çeker). |
| `purchasing.orders.cancel` | + | · | · | · | + | · | · | · | · | · | · | · | · | · | + | · | KARARLI | ALS-D66. |
| `purchasing.orders.price.read` | + | + | · | + | + | · | · | · | · | · | · | · | + | + | + | + | KARARLI | ALS-D70; Depo/Sevk görmez; Satış Müdürü verilmez (EK11 yorumu). Denetçi ÖNERİ. |
| `purchasing.orders.revise` | · | · | · | + | + | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Revizyonu hazırlayan; yeniden onay ister. |
| `purchasing.orders.close` | + | · | · | · | + | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Kapatma onaycılarda (ALS-D66 benzetmesi). |
| `purchasing.goods-receipts.approve-variance` | K | · | · | · | K | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Mal kabul farkı; kabulü kaydeden onaylayamaz. |
| `purchasing.goods-receipts.approve-overdelivery` | K | · | · | · | K | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Fazla teslim; kabulü kaydeden onaylayamaz. |
| `purchasing.future-allocation.release` | + | · | · | · | + | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | F017 ayırma bırakma. |
| `purchasing.supplier-returns.record` | · | · | · | + | + | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Tedarikçi iadesini satınalma hazırlar. |
| `purchasing.invoices.read` | + | · | · | + | + | · | · | · | · | · | · | · | + | + | + | + | ÖNERİ | Alış faturası okuması. |
| `purchasing.invoices.import` | · | · | · | · | · | · | · | · | · | · | · | · | + | · | + | · | ÖNERİ | Gelen faturayı muhasebe alır. |
| `purchasing.invoices.match` | · | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Eşleştirme muhasebe işidir (satış emsali). |
| `purchasing.invoices.approve-variance` | K | · | · | · | K | · | · | · | · | · | · | · | · | K | K | · | ÖNERİ | Fark onayı; eşleştiren onaylayamaz. Muhasebe mi Satınalma mı soru. |
| `purchasing.invoices.finalize` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Kesinleştirme (SAT-D230 emsali). |

### Muhasebe (16 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `accounting.periods.read` | + | · | · | · | · | + | · | · | · | · | · | · | · | + | + | + | KARARLI | K4.5 (Sevkiyatçı +), SAT-D191; Muhasebe almaz (SAT-D164, soru). Denetçi ÖNERİ. |
| `accounting.periods.propose` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D191; P6. |
| `accounting.periods.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | K | K | · | KARARLI | SAT-D191; kendi önerisini onaylayamaz (P6). |
| `accounting.periods.close` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D191. Kapatan ile kilidi kaldıran ayrı yetki (GOREV_AYRILIGI_ONAY). |
| `accounting.journals.read` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | + | KARARLI | SAT-D191; Muhasebe ve Depo almaz (SAT-D164, K4.1). Denetçi ÖNERİ (mizan paketi). |
| `accounting.posting-queues.read` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D191; K4.5. |
| `accounting.normal-journal.draft.create` | · | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Fiş taslağı; Muhasebe accounting yazması kartta '?' (soru). |
| `accounting.normal-journal.draft.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | K | K | · | ÖNERİ | Hazırlayan ≠ onaylayan; KLN-D17. |
| `accounting.journal.reverse.propose` | · | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Ters kayıt önerisi. |
| `accounting.journal.reverse.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | K | K | · | ÖNERİ | Öneren onaylayamaz. |
| `accounting.approved-source-catalog.prepare` | · | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Kaynak kataloğu hazırlığı. |
| `accounting.approved-source-catalog.activate` | K | · | · | · | · | · | · | · | · | · | · | · | · | K | K | · | ÖNERİ | Hazırlayan ≠ etkinleştiren. |
| `accounting.posting-mappings.read` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | + | ÖNERİ | Hesap eşleme okuması. |
| `accounting.posting-mappings.prepare` | · | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | ÖNERİ | Eşleme hazırlığı. |
| `accounting.posting-mappings.activate` | K | · | · | · | · | · | · | · | · | · | · | · | · | K | K | · | ÖNERİ | Hazırlayan ≠ etkinleştiren. |
| `accounting.account-plan.read` | + | · | · | · | · | · | · | · | · | · | + | + | + | + | + | + | ÖNERİ | Hesap planı okuması. |

### Ayarlar (39 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `settings.sales-discount-limits.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D65/D69: yalnız Admin; GM ve Satış Müdürü almaz. |
| `settings.tax-master.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | KARARLI | SAT-D145, SAT-D188. |
| `settings.sales-delivery-tolerances.manage` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D189 (teslim toleransı). |
| `settings.sales-approval-thresholds.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Onay eşiğini, eşiğe tabi onaycı (Satış Müdürü) belirlememeli (SoD-05). |
| `settings.sales-quote-policies.manage` | + | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | SAT-D189 (teklif geçerlilik, kayıp nedenleri). |
| `settings.purchase-invoice-variance-policies.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Alış fatura farkı politikası. |
| `settings.warehouse-transfer-scale.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Depo transfer kantar ayarı. |
| `settings.einvoice-scenarios.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | e-Fatura senaryoları muhasebe ayarıdır. |
| `settings.unit-conversion-policies.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Birim çevrim politikası (formül etkisi; dikkat). |
| `settings.unit-profiles.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Birim profilleri. |
| `settings.money-profiles.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Para profili muhasebe ayarıdır. |
| `settings.login-rate-limit.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Sistem yönetimi (SAT-D190: GM almaz). |
| `settings.work-center-capacities.manage` | + | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Kapasite planlama ayarı. |
| `settings.bank-csv-mappings.manage` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | + | · | ÖNERİ | Banka ekstresi eşlemesi finans ayarıdır. |
| `settings.segregation-of-duties.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Sistem yönetimi; KIM-D39 gevşetme yalnız tek kullanıcıda. |
| `settings.sales-contract-discount-limit.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Limiti, limite tabi onaycı belirlememeli (SoD-05; SAT-D65 emsali). |
| `settings.collateral-types.manage` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | + | · | ÖNERİ | Teminat türleri finans ayarıdır. |
| `settings.finance-opening-period.manage` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | + | · | ÖNERİ | Finans açılış dönemi. |
| `settings.manufacturing-scrap-cost.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Hurda maliyeti muhasebe politikasıdır. |
| `settings.notification-retention.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Saklama ayarı sistem yönetimidir. |
| `settings.document-number-series.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | + | + | · | ÖNERİ | Belge numara serileri. |
| `settings.reference-lists.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Genel referans listeleri. |
| `settings.production-reference-lists.manage` | · | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Üretim referans listeleri. |
| `settings.exchange-rates.read` | + | + | + | + | + | · | · | · | · | · | + | + | + | + | + | + | KISMEN | SAT-D199 sekiz persona; Satınalma ve Denetçi ÖNERİ. |
| `settings.exchange-rates.correct` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | KARARLI | SAT-D169, SAT-D188. |
| `settings.exchange-rates.import` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | KARARLI | SAT-D205. |
| `settings.exchange-rates.half-day.mark` | + | · | · | · | · | · | · | · | · | · | · | · | + | + | + | · | KARARLI | SAT-D198. |
| `settings.manufacturing-parameter-profiles.read` | + | · | · | · | · | · | · | + | + | · | · | · | · | · | + | · | ÖNERİ | Üretim profili okuması. |
| `settings.policies.prepare` | · | · | · | · | · | · | · | · | · | · | · | + | · | · | + | · | ÖNERİ | Finans onay matrisleri vb. politika taslağı. |
| `settings.policies.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan. |
| `settings.policies.publish` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Onaylı politikayı yayımlama. |
| `settings.policies.publish-critical` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Kritik politika yayımı; kayıtta Critical değil. İkinci onay gereksin mi soru. |
| `settings.manufacturing-parameter-profiles.prepare` | · | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Profili planlama hazırlar. |
| `settings.manufacturing-parameter-profiles.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan. |
| `settings.manufacturing-parameter-profiles.publish` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Onaylı profili yayımlama. |
| `settings.manufacturing-tolerance-profiles.read` | + | · | · | · | · | · | · | + | + | · | · | · | · | · | + | · | ÖNERİ | Üretim profili okuması. |
| `settings.manufacturing-tolerance-profiles.prepare` | · | · | · | · | · | · | · | + | · | · | · | · | · | · | + | · | ÖNERİ | Profili planlama hazırlar. |
| `settings.manufacturing-tolerance-profiles.approve` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | Hazırlayan ≠ onaylayan. |
| `settings.manufacturing-tolerance-profiles.publish` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Onaylı profili yayımlama. |

### Belgeler (10 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `documents.files.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | ÖNERİ | Kayda bağlı dosya okuması; kayıt yetkisiyle birlikte geçerli. |
| `documents.files.upload` | · | + | + | + | + | + | + | + | + | · | + | + | + | + | + | · | ÖNERİ | İş kaydına belge ekleme. |
| `documents.files.link` | · | + | + | + | + | + | + | + | + | · | + | + | + | + | + | · | ÖNERİ | Belgeyi kayda bağlama. |
| `documents.company-branding.manage` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Şirket logo/antet (OPEN-019). |
| `documents.templates.sales.prepare` | · | · | + | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Satış şablonu hazırlığı. |
| `documents.templates.sales.publish` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | OPEN-019 onay rolü açık; hazırlayan ≠ yayımlayan. |
| `documents.templates.inventory.prepare` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Stok şablonu. |
| `documents.templates.inventory.publish` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | OPEN-019. |
| `documents.templates.logistics.prepare` | · | · | · | · | · | + | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Lojistik şablonu. |
| `documents.templates.logistics.publish` | K | · | · | · | · | · | · | · | · | · | · | · | · | · | K | · | ÖNERİ | OPEN-019. |

### Kimlik (7 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `identity.profile.read` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | KARARLI | SAT-D191 temel; Denetçi ÖNERİ. |
| `identity.access.request` | + | + | + | + | + | + | + | + | + | + | + | + | + | + | + | · | KARARLI | SAT-D191 temel (kendi erişim talebi). |
| `identity.access.approve` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Seed: SECURITY_APPROVER. Talep eden/kurgulayan ≠ onaylayan. |
| `identity.roles.manage` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Seed: SECURITY_ADMINISTRATOR. Onaylayıcıdan ayrı kişi (SoD-01). |
| `identity.audit.read` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Yetki Onaylayıcısı, Denetçi (YOL-D13 ayrı insan kararı). |
| `identity.assignments.read` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Sistem Yöneticisi, Denetçi. |
| `identity.users.manage` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | ÖNERİ | Kullanıcı yönetimi sistem yönetimidir (SAT-D190: GM almaz). Kayıtta Critical değil. |

### Platform (1 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `platform.security-events.read` **(Critical)** | · | · | · | · | · | · | · | · | · | · | · | · | · | · | IK | · | IK | Critical. Aday: Sistem Yöneticisi. |

### AI (1 izin)

| İzin | GM | SAT | SATM | ALM | ALMM | DEPO | KNT | UPL | UOP | KAL | FIN | FINM | MUH | MUHM | SYS | DEN | Durum | Gerekçe |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `ai.panel.use` | + | · | · | · | · | · | · | · | · | · | · | · | · | · | + | · | KARARLI | AI-D8: başlangıç Admin + Genel Müdür; genişletme ayrı karar. |

## 3. Onay izinleri

### 3.1 64 onay akışı — önerilen onaylayan

Akış numaraları ve izinler iç envanter §4'tendir. "Doğrulanmadı" = uç bloğunda izin sabiti görülmedi; servis içinde
görülen dizge parantezde yazılıdır ve teknik doğrulama ister. **Sistem Yöneticisi (Admin) her satırda karar gereği
(SAT-D187) onaylayabilir**; tabloda tekrar edilmez, SoD-02'ye bakınız. Süre sınıfı YOL-D18'dir: 4 saat = irsaliye,
plan üstü sevk, tartım farkı, üretim planı; diğerleri 2 iş günü (Ayarlar parametresi). Vekil yalnız aynı onay iznine
sahip kişidir, zincirleme devredilmez, kendi hazırladığını onaylayamaz (YOL-D17).

🔴 = görev ayrılığı çakışması veya hazırlayan ≠ onaylayan kuralının doğrulanmadığı/eksik olduğu satır (ayrıntı §3.2).

| # | Akış | Onay izni | Hazırlayan (öneri) | Önerilen onaylayan | Süre | Not |
|---|---|---|---|---|---|---|
| 1 | Sipariş taslağı iç onayı | `sales.orders.approve` | SAT, SATM, GM | SATM, GM | 2 iş günü | KARARLI (SAT-D6). P6 kayıtta. |
| 2 | Taban fiyat altı istisnası | `sales.orders.price-floor-exception.approve` | SAT | SATM, GM | 2 iş günü | KARARLI (SAT-D1). 🔴 SATM'nin kendi açtığı taslakta kayıt kuralı doğrulanmadı. |
| 3 | İskonto istisnası | `sales.orders.discount-exception.approve` | SAT | SATM; kendi limitini aşanı GM | 2 iş günü | KARARLI (SAT-D37/D67). |
| 4 | Fazla teslim | `sales.orders.over-delivery.approve` | DEPO (sevk) | SAT (K), SATM, GM | 2 iş günü | KARARLI (SAT-D168). SAT-D216 kendi sevki/talebi yasak, SAT-D222 sevkiyatçı bilinmezse kimse. |
| 5 | Risk istisnası | `sales.orders.risk-exception.approve` | SAT, SATM, DEPO | SAT (K), SATM, GM | 2 iş günü | KARARLI (SAT-D153). 🔴 Aynı rol hem talep eder hem onaylar; ayrım yalnız kayıt kuralıyla (SoD-04). |
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
| 16 | Gelen fatura farkı | `purchasing.invoices.approve-variance` | MUH | ÖNERİ: MUHM (veya ALMM), GM | 2 iş günü | 🔴 Siparişi onaylayan ALMM fatura farkını da onaylarsa üç yönlü kontrol zayıflar (SoD-13; S-10). |
| 17 | Tedarikçi iadesi maliyet istisnası | doğrulanmadı | ALM | ÖNERİ: MUHM, GM | 2 iş günü | SAT-D454 "farklı kişi onayı". |
| 18 | Hizmet siparişi | doğrulanmadı (servis `purchasing.orders.approve` kullanıyor) | ALM | ALMM, GM | 2 iş günü | ÖNERİ. |
| 19 | Sayım farkı | `inventory.stock-count.approve` | DEPO | GM | 2 iş günü | ÖNERİ; Depo Müdürü tanımlanırsa o (S-05). Sayımı giren onaylayamaz. |
| 20 | Hurda | `inventory.stock-scrap.approve` | DEPO, UOP | GM | 2 iş günü | ÖNERİ; Kalite rolü ileride aday. |
| 21 | Transfer fazlası | `inventory.stock-transfer.overage-approve` | DEPO | GM | 2 iş günü | ÖNERİ. |
| 22 | Transfer kaybı | `inventory.stock-transfer.loss-approve` | DEPO | GM | 2 iş günü | ÖNERİ. |
| 23 | Açılış maliyeti | `inventory.opening-cost.approve` | MUH | MUHM, GM | 2 iş günü | ÖNERİ. |
| 24 | İrsaliye onayı | `logistics.dispatch-notes.approve` | DEPO | GM | **4 saat** | ÖNERİ. 🔴 Tek onaycı GM: 4 saatlik sınıfta darboğaz; hazırlayan ≠ onaylayan doğrulanmadı (S-05). |
| 25 | Plan üstü sevk | `logistics.shipments.plan-excess.approve` | DEPO | GM | **4 saat** | ÖNERİ; Satış Müdürü adaylığı soru (S-05). |
| 26 | Tartım farkı | `logistics.weighing-differences.approve` | KNT, DEPO | GM (alışta ALMM adayı) | **4 saat** | ÖNERİ. 🔴 Depo+Kantar aynı kişiyse (SoD-10). |
| 27 | Müşteri iadesi kabul kararı | `logistics.customer-returns.decision` | SAT, DEPO | SATM, GM | 2 iş günü | ÖNERİ. |
| 28 | Üretim planı | `manufacturing.plans.approve` | UPL | GM | **4 saat** | KARARLI (URT-D2 Üretim müdürü; ara dönem GM + Admin). |
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
| 47 | Muhasebe dönem onayı | `accounting.periods.approve` | MUHM | MUHM (başka kişi), GM | 2 iş günü | KARARLI (SAT-D191; P6). 🔴 tek Muhasebe Müdürü varsa onaycı fiilen GM. |
| 48 | Dönem yeniden açma | `accounting.periods.approve` (servis) | MUHM | ÖNERİ: yalnız GM | 2 iş günü | 🔴 "Dönem kapatan ile kilidi kaldıran ayrı yetki" ilkesi aynı izinle ihlal edilebilir (SoD-09, S-12). |
| 49 | Normal fiş taslağı | `accounting.normal-journal.draft.approve` | MUH | MUHM, GM | 2 iş günü | ÖNERİ; KLN-D17. |
| 50 | Fiş ters kaydı | `accounting.journal.reverse.approve` | MUH | MUHM, GM | 2 iş günü | ÖNERİ. |
| 51 | Onaylı kaynak kataloğu | `accounting.approved-source-catalog.activate` | MUH | MUHM, GM | 2 iş günü | ÖNERİ. |
| 52 | Hesap eşleme etkinleştirme | `accounting.posting-mappings.activate` | MUH | MUHM, GM | 2 iş günü | ÖNERİ. |
| 53 | Üretim maliyet farkı | doğrulanmadı | sistem | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 54 | Fatura satırı SMM düzeltmesi | doğrulanmadı | MUH | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 55 | Stok maliyet teklifleri | doğrulanmadı | sistem, MUH | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 56 | Maliyet bileşeni iptali | doğrulanmadı | MUH | ÖNERİ: MUHM, GM | 2 iş günü | — |
| 57 | Kaynak fiş | doğrulanmadı | sistem, MUH | ÖNERİ: MUHM, GM | 2 iş günü | KLN-D17: kaynağı kesinleştiren kendi fişini onaylayamaz. |
| 58 | Kritik rol erişim talebi | `identity.access.approve` **(Critical)** | herkes (`identity.access.request`); kurgulayan Yetki Yöneticisi | Yetki Onaylayıcısı (IK) | 2 iş günü | 🔴 Seed `SYSTEM_ADMINISTRATOR` hem `identity.roles.manage` hem `identity.access.approve` taşır (SoD-01). |
| 59 | Şirket VKN değişikliği | `organization.company.tax-id.approve` | MUHM | GM | 2 iş günü | ÖNERİ. |
| 60 | Cari vergi kimliği değişikliği | `counterparty.parties.tax-identity.approve` **(Critical)** | MUH, MUHM | MUHM veya GM (IK) | 2 iş günü | IK. |
| 61 | Cari unvan değişikliği | `counterparties.legal_name.approve` | MUH, MUHM | MUHM, GM | 2 iş günü | ÖNERİ. |
| 62 | Cari birleştirme | `counterparty.parties.merge.approve` | ayrı hazırlama izni yok | MUHM, GM | 2 iş günü | 🔴 Hazırlayan izni olmadığı için tek kişilik işlem olabilir (SoD-16). |
| 63 | Paket standardı sürümü | `product.package-standards.approve` | UPL | GM | 2 iş günü | ÖNERİ. |
| 64 | Fiyat listesi yayımı | `product.price-lists.publish` | SATM (`write`) | GM; SATM yalnız başkasının sürümünü | 2 iş günü | 🔴 Hazırlayan ≠ yayımlayan kuralı doğrulanmadı (SoD-07). |

Ayarlar (10 politika ailesi) ve Belgeler (3 şablon) yayım adımları için öneri: hazırlayan ilgili müdür/rol
(`*.prepare`), onaylayan/yayımlayan GM + Admin; hazırlayan ≠ onaylayan (matriste `K`).

### 3.2 Görev ayrılığı (SoD) çakışmaları 🔴

Her madde "aynı kişi hem hazırlayıp hem onaylayabilir mi" sorusuna bakar. Kayıt düzeyindeki P6 kuralı aynı **kaydı**
korur; aşağıdakiler rol birleşimiyle ya da kayıt kuralının eksik/doğrulanmamış olduğu yerde doğan risklerdir.

| Kimlik | Çakışma | Etki | Öneri |
|---|---|---|---|
| 🔴 SoD-01 | Seed `SYSTEM_ADMINISTRATOR` rolü `identity.roles.manage` + `identity.access.approve` + `identity.audit.read` birlikte taşır | Aynı kişi kritik yetkiyi kurgular, onaylar ve kaydı okur ("talep eden/kurgulayan ≠ onaylayan" ilkesine aykırı) | Gerçek kurulumda Yetki Yöneticisi ve Yetki Onaylayıcısı ayrı kişiler; seed değişikliği insan kapısı (S-17) |
| 🔴 SoD-02 | Admin müdürlerin yaptığı her şeyi yapar (SAT-D187) + bütün hazırlama izinleri | Tek kişi sipariş → sevk → fatura → tahsilat zincirini farklı kayıtlarda kendisi yürütebilir | Admin'i günlük iş onaylarından çıkarmak veya Admin hesabını kişiye değil acil durum hesabına bağlamak (S-06) |
| 🔴 SoD-03 | GM taslak açar (SAT-D194) ve onaylar; ara dönemde `manufacturing.facts.record` + `manufacturing.orders.close-exception.approve`, `inventory.opening-stock.record` gibi kayıt + onay hücreleri | Tolerans dışı kapanışta "fiilî giren onaylayamaz" ilkesi; kayıt kuralının varlığı doğrulanmadı | Modül müdürleri (S-05); GM'nin operasyonel kayıt hücreleri kaldırılsın |
| 🔴 SoD-04 | Satış Temsilcisi risk istisnasını talep eder ve onaylar (SAT-D153/D159); fazla teslimi onaylar (SAT-D168) | Karar gereği; kendi talebi/sevki komutta reddedilir (SAT-D216) | Karar korunur; iki temsilcinin karşılıklı onayı izlenir (rapor) |
| 🔴 SoD-05 | Onay eşiğini/limitini belirleyen = eşiğe tabi onaycı: Satış Müdürü teslim toleransını yönetir (SAT-D189, kararlı); taslak `sales-approval-thresholds` ve `sales-contract-discount-limit`'i Satış Müdürü'ne **vermez** | Kendi onay bandını genişletebilme | Eşik/limit ayarı GM + Admin (SAT-D65 emsali); teslim toleransı kararı için S-19 |
| 🔴 SoD-06 | Muhasebe faturayı hazırlar, (başka kişi olarak) onaylar, eşleştirir ve kesinleştirir; kesinleştirmede SoD yok (SAT-D235 (2)) | Kararlı; KLN-D17 120 fişini onaylatmaz | Karar korunur; iki Muhasebe kişisi yoksa onay MUHM/GM'ye düşer |
| 🔴 SoD-07 | Satış Müdürü `product.price-lists.write` + `product.price-lists.publish` | Kendi hazırladığı fiyat listesini yayımlayabilir (kayıt kuralı doğrulanmadı) | Yayımı GM; ya da kayıt kuralı teknik olarak eklenmeli |
| 🔴 SoD-08 | Banka hesabı/IBAN öneren (Finans) ↔ ödeme onaylayan (Finans Müdürü) aynı kişide birleşirse | Sahte IBAN + ödeme onayı (GOREV_AYRILIGI_ONAY ilk madde) | FIN ve FINM aynı kişiye verilmez; küçük kadroda ödeme onayı GM |
| 🔴 SoD-09 | Muhasebe Müdürü dönemi önerir, onaylar, kapatır; yeniden açma da `accounting.periods.approve` ile | "Kapatan ile kilidi kaldıran ayrı yetki" | Yeniden açmaya ayrı izin adı ve yalnız GM (S-12) |
| 🔴 SoD-10 | Depo/Sevk ve Kantar aynı kişi | Kendi yükünü tartar, tartım farkını kaydeder | Kantar ayrı kişi; değilse tartım farkı onayı GM (S-14) |
| 🔴 SoD-11 | Depo sayım/hurda/transfer kaydı ↔ onay | Onaylar GM'de; Depo Müdürü tanımlanırsa sahayla aynı kişi olmamalı | S-05 |
| 🔴 SoD-12 | Tek adımlı finans kayıtları: `finance.manual-receipt.record`, `finance.cash-transfers.commit`, `finance.party-balance-transfers.record`, `finance.collaterals.record`, `finance.customer-instrument.*` | Onay adımı yok; tek kişi para hareketi | Tutar eşiği üstü ikinci onay (politika, S-11) |
| 🔴 SoD-13 | Satınalma Müdürü siparişi onaylar ve (önerilirse) gelen fatura farkını onaylar | Sipariş–kabul–fatura üç yönlü kontrolü tek kişide | Fatura farkı onayı Muhasebe Müdürü (S-10) |
| 🔴 SoD-14 | Üretim Planlama fiilî kaydeder ve düzeltir (`manufacturing.facts.record` + `.correct`) | Kendi kaydını düzeltme | Düzeltme yalnız planlama, kayıt yalnız operatör; ya da düzeltme GM onayı |
| 🔴 SoD-15 | Küçük kadro birleşimleri: Satış + Muhasebe (kendi siparişinin faturası), Satınalma + Depo (sipariş + mal kabul), Satınalma + Muhasebe (sipariş + fatura eşleştirme), Finans + Muhasebe (ödeme + fiş onayı), Finans + Finans Müdürü | Kayıt kuralı yalnız aynı kaydı korur | Bu birleşimler atama ekranında uyarı; kaçınılmazsa onay GM'ye (S-03) |
| 🔴 SoD-16 | Cari birleştirme ve unvan değişikliğinde hazırlama izni ayrı değil ya da kayıt kuralı doğrulanmadı | Tek kişilik geri dönüşü zor işlem | Teknik doğrulama; ayrı öneri izni |
| 🔴 SoD-17 | Ayar politikaları ve belge şablonlarında Admin `prepare` + `approve` + `publish` üçünü de taşır | Kendi politikasını yayımlama | Kayıt kuralı (hazırlayan ≠ onaylayan) zorunlu; yayımı ayrı kişi |

### 3.3 Critical izinler ve ikinci onay gerektirenler

- **Critical (17):** `organization.companies.open`, `identity.access.approve`, `identity.roles.manage`,
  `identity.audit.read`, `identity.assignments.read`, `platform.security-events.read`, `finance.bank-account.propose`,
  `finance.bank-account.approve`, `finance.cash-account.propose`, `finance.cash-account.approve`,
  `finance.cash-account.passivation.propose`, `finance.cash-account.passivation.approve`,
  `finance.cash-account.reactivation.propose`, `finance.cash-account.reactivation.approve`,
  `finance.credit-limits.approve`, `finance.credits.approve`, `counterparty.parties.tax-identity.approve`.
  Bunların grant'ı kritik rol erişim talebi akışından geçer (talep → farklı kişinin onayı; akış #58). Taslak aday rol
  yazar, hiçbirini vermez.
- **Kayıtta Critical olmayan ama ikinci onay/ayrı kişi önerilenler (ÖNERİ):** `settings.policies.publish-critical`,
  `settings.segregation-of-duties.manage`, `identity.users.manage`, `accounting.periods.close` + yeniden açma,
  `sales.invoices.finalize` (SAT-D249 gereği Critical değil; kararlı), `product.price-lists.publish`.
- **İki kademeli onay (kararlı):** SAT-D67 — Satış Müdürü'nün kendi limitini aşan iskontosu başka onaycıya (GM) gider.
  Finans onay matrisleri (Ayarlar politika ailesi) tutar eşiğine göre ikinci onaycı tanımlayabilir; eşik değerleri
  politika kararıdır, taslak seçmez (S-11).

## 4. Şirketler arası görünüm (GDC / GSP / GIN) ve grup içi rapor

- **Bugünkü model:** atama şirket bazındadır (`Assignments[].CompanyId` + kapsam `Company`/`Site`); izinlerin 271'i
  yalnız şirket geneli verilebilir. Bir kişi iki şirkette çalışıyorsa iki ayrı atama alır. Bu taslak bu modeli korur.
- **Öneri (ÖNERİ):**
  - GDC ve GSP: §1 rolleri şirket başına ayrı atanır; kişi hangi şirkette hangi rolde ise o kadar görür.
  - GIN: gerçek unvan/VKN gelene kadar placeholder ve resmî belgeye kapalıdır (DEC-ORG-005, OPEN-001-GIN). O sürede
    yalnız GM, Sistem Yöneticisi ve Muhasebe Müdürü ataması önerilir.
  - Denetçi (DEN): yalnız bağımsız denetime tabi **tek şirkette**, süreli atama (YOL-D13; hangi şirket ve süre S-13).
  - **Grup içi işlem dökümü / emsal fiyat raporu** (YOL-D6) ve konsolide görünüm için kayıtlı bir izin adı **yoktur**.
    Adı rapor sahibinin teknik işidir (taslak ad icat etmez). Önerilen tutucular: GM + Sistem Yöneticisi (+ Muhasebe
    Müdürü, S-15). Bu izin şirket sınırını aşan tek okuma olacağı için ayrı insan kapısında açılmalıdır (YOL-D6).
- **`ai.panel.use` (AI-D8, kararlı):** şirket çapında izin; başlangıç **Sistem Yöneticisi (Admin) + Genel Müdür**.
  İzin, kullanıcının zaten göremediği veriyi açmaz (AI-D1); AI canlıda kapalı başlar (`Ai:Enabled=false`, AI-D6).
  Diğer rollere genişletme ayrı Sinan kararıdır (S-16).

## 5. Sinan'a sorular (önerili)

| No | Soru | Öneri |
|---|---|---|
| S-01 | Muhasebe Müdürü 16. standart rol olarak listeye girsin mi? (Kararlarda var, görev listesinde yok.) | Evet. |
| S-02 | Satış Destek ayrı rol mü kalsın, Satış Temsilcisi ile birleşsin mi? (SAT-D141 zaten aynı grant.) | Birleşsin. |
| S-03 | Küçük kadroda görev ayrılığı istisnası olsun mu? KIM-D39 gevşetmeyi yalnız tek aktif kullanıcılı şirkette açar. | İstisna yok; onay GM'ye düşer, uygun onaycı yoksa "sahipsiz onay" (YOL-D17). |
| S-04 | Vekâlet: GM yokken GM onaylarını kim vekâleten alır? (YOL-D17: vekil aynı izne zaten sahip olmalı.) | Admin değil; ilgili izne sahip modül müdürü. Süreli, zincirsiz. |
| S-05 | Depo/Sevk (Lojistik) Müdürü ve Üretim Müdürü rolleri açılsın mı? Bugün irsaliye, plan üstü sevk, tartım farkı, sayım, hurda ve üretim planı onayı GM'de; dördü 4 saatlik sınıfta. | Açılsın; bu onaylar onlara geçsin, GM yedek onaycı kalsın. |
| S-06 | Admin "müdürlerin yaptığı her şey" (SAT-D187) kalsın mı? | Admin sistem yönetimi + acil durum; günlük iş onayları müdürlerde. |
| S-07 | Satış Müdürü cari ekstreyi ve kredi limitini görsün mü? | Kredi limiti okuması evet (risk istisnası için); cari ekstre tutarı hayır. |
| S-08 | Muhasebe (personel) muhasebe dönemi ve yevmiye okumasını alsın mı? (SAT-D164'te seçilmedi; fiş taslağı hazırlayacaksa gerekir.) | Evet, salt okuma. |
| S-09 | `manufacturing.orders.close` ve `finance.manual-receipt.record` kimde olsun? (Kartta açık.) | Emir kapatma Üretim Planlama; tahsilat kaydı adı verilen tek Finans kişisi. |
| S-10 | Gelen fatura farkını kim onaylasın: Muhasebe Müdürü mü, Satınalma Müdürü mü? | Muhasebe Müdürü (siparişi onaylayan farkı onaylamasın). |
| S-11 | Tek adımlı finans kayıtlarına (virman, cari bakiye virmanı, teminat, çek/senet) tutar eşiğiyle ikinci onay gelsin mi? | Evet; eşik Ayarlar'da parametre (politika kararı). |
| S-12 | Muhasebe dönemini yeniden açma ayrı izin olsun mu? | Evet; yalnız GM. |
| S-13 | Denetçiye sistem erişimi verilecek mi; hangi şirket, ne süre, fiyat/tutar dahil mi, dışa aktarma? | Yalnız denetlenen şirket, süreli; tutarlar dahil salt okuma; dışa aktarma ayrı izin. |
| S-14 | Kantar ayrı kişi mi? Kantarcı yoksa tartımı Depo/Sevk mi yapsın? | Ayrı kişi; değilse tartım farkı onayı GM. |
| S-15 | Grup içi rapor/konsolide görünüm izni kimde olsun? | GM + Admin (+ Muhasebe Müdürü). |
| S-16 | `ai.panel.use` ne zaman ve kime genişlesin? | İlk değerlendirme setinden sonra modül müdürlerine. |
| S-17 | Seed'deki `SYSTEM_ADMINISTRATOR` rolünden `identity.access.approve` ayrılsın mı? (Seed değişikliği insan kapısı.) | Evet; Yetki Yöneticisi ve Yetki Onaylayıcısı ayrı kişiler. |
| S-18 | Fiyat listesini kim yayımlasın? | Hazırlayan Satış Müdürü ise GM yayımlar. |
| S-19 | Satış onay eşiklerini ve sözleşme iskonto limitini kim belirlesin? Teslim toleransı Satış Müdürü'nde kalsın mı (SAT-D189)? | Eşik/limit GM + Admin; tolerans kararı korunur. |
| S-20 | Kalite rolü şimdi açılsın mı? | Hayır; pasif. 2. kalite sınıflaması Depo/Operatörde. |

## 6. Mevcut seed rolleriyle ilişki

`identity-seed.json` bugün 5 sistem rolü taşır: `ORGANIZATION_READER`, `ORGANIZATION_OPERATOR`, `SYSTEM_ADMINISTRATOR`,
`SECURITY_ADMINISTRATOR` (rol kurgulama), `SECURITY_APPROVER` (erişim onayı + denetim okuma). Bunlar geliştirme/bootstrap
rolleridir; taslak onları değiştirmez ve iş rolleriyle birleştirmez. `SECURITY_ADMINISTRATOR`/`SECURITY_APPROVER`
ayrımı SoD-01 önerisiyle uyumludur; `SYSTEM_ADMINISTRATOR`'ın ikisini birlikte taşıması S-17'dir.

## 7. Yöntem ve sınırlar

- İzin listesi `ErpPermissions.cs`'teki sabitlerden alındı; CSV'deki satır sayısı `All` kümesiyle (286) birebir eşit,
  Critical işareti `Critical` kümesinden, "yalnız şirket geneli" sütunu `CompanyWideOnly` kümesindendir.
- KARARLI hücreler persona kartı ve satış önerisindeki kararlardan aktarıldı; kart ile bu taslak çelişirse kart ve
  karar dosyası geçerlidir.
- 64 akıştan 16'sında onay izni uç bloğunda görülmedi ("doğrulanmadı"); servis içi kontrol okunmadı.
- Hazırlayan ≠ onaylayan kuralının her akışta kodda uygulandığı tek tek doğrulanmadı; 🔴 işaretli satırlar teknik
  doğrulama listesidir.
- Kayıtlı olmayan izin adı (grup raporu, `quality.*`, dönem yeniden açma) icat edilmedi; ad sahibinin işidir.
