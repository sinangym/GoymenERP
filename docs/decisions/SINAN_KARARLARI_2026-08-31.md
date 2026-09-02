# Sinan Kararları — 2026-08-31

Kullanıcı (Sinan Göymen) `GOYMEN_ERP_HIZLI_TESLIM_v3.md` §9 sorularına verdiği cevaplar.
Bu belge `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` için **öneri** kaydıdır; merge sonrası
`KARAR_SICILI.md` `DEC-*` ve `ACIK_KARARLAR.md` `OPEN-*` satırları buna göre güncellenir.

Kaynak önceliği: bu belgedeki doğrudan kullanıcı ifadeleri ve bağlı Issue yorumları
"son kullanıcı kararı" seviyesindedir; teknik sentez/öneri bölümleri aynı yetkiyi taşımaz.

---

## 1. Şirketler — `OPEN-001` → KISMEN

Üç hukuki şirket:

| CompanyCode | Tam unvan | VKN | Durum |
|---|---|---|---|
| `GDC` | Göymen Demir Çelik İnş. San. ve Tic. Ltd. Şti. | `4100046586` | GERÇEK |
| `GSP` | Göymen Sac Profil San. ve Tic. Ltd. Şti. | `4101507252` | GERÇEK |
| `GIN` | Göymen İnşaat | `1111111111` (**TARİHSEL TEST DEĞERİ**) | **PLACEHOLDER** — tam unvan + gerçek VKN eksik |

- `GDC` ve `GSP` gerçek; seed'e girilebilir.
- `GIN` tam unvan ve gerçek VKN bekliyor → `OPEN-001-GIN` olarak açık kalır; `GIN` şirketi
  fatura/resmî belge üretimine kapalı feature-flag ile korunur.
- CompanyCode değerleri bu kararla üretildi (grup ön eki `G` + şirket kısaltması).
- **Şirket ekleme/düzenleme Ayarlar sayfasından yapılabilir olmalıdır** (bkz. §5). Seed
  yalnız başlangıç verisidir; runtime CRUD Ayarlar/Organizasyon masterı üzerindendir.

> **SUPERSEDED / tarihsel not:** `1111111111` gerçek VKN değildir. §9 Q1 benzersizlik
> kararını, Issue #66 [Q1 sahip açıklaması](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5506986884)
> kesinleştirir. Placeholder'ın `NULL` veya geçici değer taşıması ve sonradan VKN
> değişikliği ise `OPEN-030` kapsamında açık kalır.

## 2. Kimlik — `OPEN-013` → GECICI_VARSAYIM kabul

- Dev/staging tek `admin` (yerel bootstrap) hesabıyla yazılır ve çalışır.
- Kullanıcı yetkilendirmeleri, rol atamaları, ek kullanıcılar **sistem canlıya yaklaşırken**
  eklenir; OIDC/SSO sağlayıcısı o aşamada seçilir (OIDC soyutlaması hazır).
- `OPEN-013` production kimlik kapısında açık kalır; geliştirmeyi bloke etmez.

## 3. Depolar / lokasyonlar — `OPEN-002` → KAPANDI

- **Göymen lokasyonları** (Osmaniye Hol-0…7, Gaziantep) = Göymen'in kendi deposu,
  `external: false`. Başlangıçta işleten/sahip = **`GDC` (Göymen Demir Çelik)**; şirket
  bazlı sahiplik Ayarlar'dan düzeltilebilir (bkz. §5).
- **Harici depolar** (Baştuğ, Selimoğlu, Alya, Şadiler, Tosçelik, Doğruluk) = **tedarikçi
  depoları**, `external: true`. İşleten = ilgili tedarikçi; Göymen malı bu depolarda
  emanet/dış-fason durumunda tutulur (`DEC-STK-008`).
- Mevcut `ORGANIZASYON_SEED.yaml` bu ayrımı zaten taşıyor; yalnız legal entity listesi
  §1'e göre 3 şirkete güncellenir.

## 4. Geçici değer / politika kararları (`OPEN-003…012`)

Ortak ilke: **her eşik/tolerans/oran/formül değeri Ayarlar sayfasından yönetilir**
(bölüm 5). Aşağıda "başlangıç varsayılanı" = seed/config default; "override" = yetkili
kullanıcının işlem/plan sırasında değiştirebilmesi.

| OPEN | Karar | Başlangıç varsayılanı | Override |
|---|---|---|---|
| `OPEN-003` | Fiziksel stok kabulünde miktar: **kantar ağırlığı elle (manuel) girilir**, irsaliye ile eşleştirilir. İrsaliye kg ↔ kantar kg farkı toleransa tabi. | Tolerans: `%1` veya min `±20 kg` (hangisi büyükse) | Ayarlar'dan ürün/işlem bazlı |
| `OPEN-004` | Kantar/yükleme tolerans eşikleri Ayarlar'dan ürün/işlem bazlı. | Uyarı `%1`, onay `%2`, blok `%3` (ağırlık farkı) | Ayarlar'dan |
| `OPEN-005` | Üretim kütle dengesi toleransı **hat bazlı**, Ayarlar'da değer + varsayılan. | Hat başına varsayılan `±%2` | Yetkili kullanıcı **her planlamada** değiştirebilir |
| `OPEN-006` | Kısmi hammadde asgari parti: **kısıtlama yok**; Ayarlar'dan varsayılan yönetilir. | Varsayılan: kısıt yok (0 / sınırsız) | Yetkili kullanıcı planlama sırasında |
| `OPEN-007` | Formüller merkezî registry'de; **Ayarlar'dan değiştirilebilir**. Yoğunluk/kerf/kaplama/yuvarlama sabit master; hız gibi üretime bağlı alanlar Ayarlar'da varsayılan + **plan sırasında override**. İlk değerler: varsa mevcut Göymen projelerinden, yoksa açık teknik kaynak (web) + golden test. | Formula registry + golden şablon (ChatGPT) | Hız vb. plan sırasında |
| `OPEN-008` | Stok değerleme + üretim maliyet yöntemi: **ertelendi.** Lot fiilî maliyet ham verisi toplanır. | — | — |
| `OPEN-009` | Finans: **mevcut kayıtlar elle girilir; hiçbir banka entegrasyonu yok.** Onay eşikleri Ayarlar'dan; başlangıçta hard-gate yok. | Onay politikası motoru + boş şablon | Ayarlar'dan |
| `OPEN-010` | DBS/teminat haircut: `DEC-FIN-008` doğrultusunda. Oranlar başlangıçta `0` / manuel onaylı, Ayarlar'dan tür/banka/vade bazlı. | `0` / manuel | Ayarlar'dan |
| `OPEN-011` | Minimum nakit tamponu + 13 haftalık plan kaynak önceliği: **ertelendi.** | — | — |
| `OPEN-012` | Kur: `DEC-FIN-010` (önceki iş günü TCMB satış) doğrultusunda; **Ayarlar'dan değiştirilebilir** (saat/tatil/manuel istisna). | Önceki iş günü TCMB satış | Ayarlar'dan |

**`OPEN-004`/`OPEN-005` varsayılan sayıları (`%1/%2/%3`, `±%2`) `ASSUMPTION` seviyesindedir**
— Sinan onayı veya saha golden testiyle kesinleşir; kod içine gömülmez, config'ten gelir.

## 5. Yeni mimari karar — Ayarlar (Settings) modülü + Politika Registry

Sinan'ın §1–4 cevaplarının ortak talebi: **neredeyse her master/eşik/tolerans/oran/formül
değeri runtime'da Ayarlar sayfasından değiştirilebilir olmalı; bir kısmı ayrıca işlem/plan
sırasında yetkili kullanıcı tarafından override edilebilir olmalı.**

Karar (`DEC-SET-001`):

1. **Ayarlar modülü** (`Settings` / `AV-01…03` + `SY-04` ekran aileleri) organizasyon
   masterı (şirket/tesis/lokasyon/iş merkezi CRUD), ortak master (birim/para/kur/vergi),
   ve **politika registry**'yi yönetir.
2. **Politika Registry** sürümlü, auditli, şirket-kapsamlı bir key→value politika tablosudur.
   Kapsadığı politika aileleri (başlangıç): kantar toleransı, kütle dengesi toleransı,
   asgari parti, formül varsayılanları (hız vb.), kur alma politikası, finansal onay eşikleri.
3. **Kod içinde sabit eşik/oran/rol adı yasak** (`AGENTS.md` zaten diyor). Her değer
   Politika Registry'den okunur; yoksa güvenli fail-closed varsayılan.
4. **Plan/işlem-seviyesi override**: üretim planlama ve kantar/mal-kabul akışları, yetkili
   kullanıcının o plan/işlem için registry varsayılanını **geçersiz kılmasına** izin verir;
   override değeri işlem snapshot'ına yazılır (`DEC-PRDCT-008` snapshot ilkesiyle uyumlu),
   geçmiş kayıt sonradan değişmez.
5. Politika değişikliği audit üretir; kritik politikalar (`OPEN-004` blok eşiği, onay eşiği)
   ikinci onay gerektirebilir (`SY-04` onay matrisi).

Bu karar `DELIVERY_BACKLOG` içinde bir **cross-cutting dilim** açar: Ayarlar/Politika
Registry çekirdeği, ilk tolerans/formül tüketen dilimden (Üretim / Kantar) **önce** gelir.

## 6. Süreç kararları

| Konu | Karar |
|---|---|
| `AGENTS.md` v3 referansı | Ayrı öneri PR ile eklenecek; Sinan onayı sonrası merge (bkz. o PR) |
| `main`'deki `dummy` commit (`f60540b`) | Bilinçli değil → revert öneri PR'ı açılır |
| Frontend paketler | `react-router@8.3.1` + `@tanstack/react-query@5.102.8` + CSS Modules + reducer — `ADR-013` (kabul) |
| Branch protection | Coordinator prosedürel kapı (GitHub plan yükseltmesi yok) |
| Ajan modeli | 3 ajan (Codex/Claude/ChatGPT) sürekli, aynı dilimin farklı katmanında; coordinator merge |

## 7. TARİHSEL / SUPERSEDED — o tarihte açık sayılan `OPEN-*`

Bu liste yalnız 2026-08-31 anlık görünümüdür; güncel durum değildir. §8 ve
`ACIK_KARARLAR.md` tarafından supersede edilmiştir.

- `OPEN-001-GIN` — Göymen İnşaat tam unvan + gerçek VKN
- `OPEN-007` değerleri — formül registry yapısı kararlı; kesin sayılar validasyon bekliyor
- `OPEN-008`, `OPEN-011` — ertelendi (maliyet, nakit planı)
- `OPEN-014/015/016/017/018/019/020/021` — geçici değerler kabul (v3 §9.3), production kapılarında

## 8. 2026-09-01 — Yetkili uzlaştırma düzeltmesi

Bu bölüm, §7'deki aşağıdaki toplu ifadeyi **SUPERSEDE eder**:

```text
OPEN-014/015/016/017/018/019/020/021 — geçici değerler kabul
```

Bu ifade Hızlı Teslim v3 §9.3'teki soruyu, verilmiş kullanıcı cevabı gibi yorumladığı için fazla geniştir. Doğru durum; aşağıdaki 2026-09-01 karar turu ve `docs/decisions/ACIK_KARARLAR.md` uzlaştırma tablosudur. Kapanmış alt bölüm yeniden sorulmaz; saha, runtime veya production kanıtı gerektiren alt bölüm açık kalır.

## 9. 2026-09-01 — `DECISION-ROUND-2026-09-01-v1` cevapları

### Q1 — GIN ve VKN benzersizliği: `A`

- `GIN` gerçek tam unvanı ve gerçek VKN'si henüz bilinmiyor.
- `GIN` bu değerler gelene kadar `IsPlaceholder=true`, `InvoiceCapable=false` olarak kalır.
- Eski `1111111111` değeri gerçek VKN değildir; implementation işinde kaldırılacaktır.
- Gerçek, `NULL` olmayan VKN aktif ve pasif bütün şirketlerde grup çapında benzersizdir.
- SQL Server'da bu kural düz unique index ile değil, `WHERE Vkn IS NOT NULL` filtered unique index ile uygulanır; birden fazla `Vkn=NULL` placeholder şirket mümkündür.
- Resmî fatura/e-belge, gerçek unvan ve VKN girilmeden GIN için fail-closed kalır.
- [Issue #66 Q1 sahip açıklaması](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5506986884)
  grup çapı benzersizliği açıkça onaylar ve VKN'nin ilk kayıttan sonra değiştirilebilir
  olması gereksinimini ekler.
- Placeholder'ın `NULL` mı yoksa geçici bir değer mi taşıyacağı; VKN değiştirme
  yetkisi/onayı/audit izi ve çakışmalı update davranışı bu kararla seçilmez,
  `OPEN-030` kapsamında ayrıca kilitlenir.

### Q2 — Veri hacmi/performance: kısmi gerçek veri + ölçülebilir varsayım

- Beklenen yıllık ciro yaklaşık `5.000.000.000 TL`.
- Ortalama ürün fiyatı yaklaşık `30.000 TL/ton`.
- Planlama çıpası yaklaşık `166.667 ton/yıl`dır (`5 milyar / 30 bin`).
- Faaliyet toptan/ağır malzeme ağırlıklıdır.
- Ortalama sipariş tonajı, cari/müşteri sayısı, eşzamanlı kullanıcı, yoğun saat ve belge/OCR hacmi henüz bilinmiyor.
- Gerçek veri gelene kadar yalnız performans testinde kullanılmak üzere `10–25 ton/sipariş` teknik varsayımıyla yaklaşık `6.700–16.700 sipariş/yıl` sentetik aralığı kullanılır; bu kullanıcı gerçeği veya production SLO değildir.
- Production kapasite/SLO, migrasyon provası ve gerçek yük ölçümüyle yeniden kilitlenir.

### Q3 — Muhasebe/e-belge/banka entegrasyonları: ilk sürümde yok

- İlk sürüm muhasebe/e-belge/banka entegrasyonları olmadan çalışır.
- Bu karar e-posta ve belge paylaşımını kapsamaz; `DEC-DOC-006/007` yürürlükte kalır.
- İleri öncelik: `e-fatura/e-arşiv` → `Mikro muhasebe mutabakatı/kullanıcı onaylı fiş taslağı` → `banka hareketleri`.
- Erişim seçenekleri, ilgili sağlayıcının gerçek imkânına göre adapter üzerinden dosya aktarımı, salt-okunur DB, manuel Excel/CSV veya yerel AI/OCR ile belgeyi taslağa çevirme olabilir.
- AI/OCR doğrudan kesin kayıt veya resmî fiş yazmaz; kullanıcı doğrulaması gerekir.
- Harici sisteme serbest/doğrudan DB yazımı yapılmaz.
- Sağlayıcı, credential, sandbox ve kesin teknik yöntem ayrı entegrasyon işi başlayınca seçilir.

### Q4 — RPO/RTO: `GECICI_VARSAYIM`

- Kullanıcı kesin RPO/RTO değerini bilmediğini ve hedeflerin şirket/veri yoğunluğuna
  bağlı olduğunu belirtmiştir.
- `RPO=15 dakika`, `RTO=4 saat` yalnız geçici teknik hedeftir; kabul edilmiş
  production hedefi değildir.
- Ana çalışma on-prem/hibrid; tesis dışı şifreli yedek zorunludur.
- Production kabulü gerçek restore tatbikatı ile DB/belge checksum mutabakatı ister.
- Hedefler gerçek yük/restore ölçümü ve ayrıca işletme onayıyla kilitlenir; restore
  kanıtı olmadan production açılmaz.

### Q5 — Yıllık dönem mimarisi ile retention birbirinden ayrıldı

**Kilitlenen işletme gereksinimi:**

- Operasyonel veriler yıllık dönemler hâlinde yönetilebilir.
- Sonraki dönemin aktif çalışma kümesinde yalnız açık işlem/kalemler kullanılabilir kalır.
- Yetkili kullanıcı seçtiği birden fazla yılı aynı anda rapor ve AI analizinde kullanabilir.
- Gerçek veri silmeyi yalnız en yüksek yetkili kullanıcı gerçekleştirebilmelidir.

**Teknik yöntem kilitli değildir:**

- Açık ve kapanmış kayıtların dönemler arası davranışı ile `carry-forward`, snapshot veya
  başka bir temsil yöntemi; çok-yıllı okumanın derived read model veya başka mimariyle
  kurulması bu kullanıcı cevabıyla seçilmemiştir.
- Legal-hold, ikinci onay, audit ve otomatik purge davranışı ile belge/audit/idempotency
  türü bazlı yasal saklama süreleri ayrı teknik/işletme kabulü gerektirir.
- Bu yöntem başlıkları `OPEN-018/021` kapsamında ve default-deny production kapılarında
  açık kalır.

### Q6 — PDF/antet: `A` + şirket bazlı gerçek asset

- Her şirketin ayrı logo ve anteti vardır.
- Şablonlar şirket bazlı, versiyonlu ve auditlidir.
- Açık lisanslı gömülü `Noto Sans` teknik fallback olarak kabul edildi.
- Gerçek logo/antet dosyaları kullanıcı tarafından ayrıca sağlanacaktır; verilmeden kurumsal belge UAT tamamlanmaz.
- Şablon yayımlama/değiştirme yetkili kullanıcı onayı ister; exact permission/rol matrisi ilgili Ayarlar/Belge diliminde kararlaştırılır.

### Q7 — AI: yerel-first, sürekli erişilebilir temel UX

- ERP verisi kullanıcı tarafında/on-prem kalır.
- AI ve yerel OCR, kolay kullanımın temel ve sürekli erişilebilir bir parçası olarak tasarlanır; ertelenebilir süs özelliği değildir.
- Buna rağmen AI yetki, onay, formül veya kesin iş doğrusu değildir; `DEC-AI-001` korunur ve AI kesintisi deterministik normal ERP akışlarını bloke etmez.
- Yerel model/OCR ve GPU kapasitesi gerçek sunucu envanteriyle PoC'de doğrulanır.
- Bulut AI, sağlayıcı/sözleşme/KVKK/veri sınıfı ve ayrı aktivasyon kararı olmadan default-deny kapalı kalır.

### Q8 — C-01 continuation: `A`

- İlk sayfa doğrulanmışsa ikinci sayfa `offline`, `network-error`, HTTP `500` veya `503` olduğunda doğrulanmış satırlar görünür kalır.
- Hata yalnız continuation alanında gösterilir ve açık kullanıcı `Tekrar Dene` eylemi sunulur.
- Otomatik yeniden gönderim yapılmaz.
- Retry aynı actor/session, CompanyContext/generation, query/filter/sort ve cursor snapshot'ını kullanır; context değişirse eski retry/yanıt stale-rejected olur.
- Hatalı sayfadan yeni satır eklenmez; başarılı retry aynı sayfayı yalnız bir kez append eder.
- `401`, `403`, tenant-binding ihlali veya malformed response bu korumanın arkasına saklanmaz; fail-closed davranış sürer.

## 10. Sicil ve uygulama etkisi

Bu karar turu aşağıdaki sicil kayıtlarını üretir: `DEC-ORG-005`, `DEC-CAR-004`, `DEC-SET-001`, `DEC-PERF-001`, `DEC-INT-001`, `DEC-DATA-001`, `DEC-DOC-010`, `DEC-AI-009`.

Q1 sahip açıklaması ayrıca yeni karar üretmeden `OPEN-030` aktif kalemini açar.

Karar kaydı kendi başına migration, production aktivasyonu, bulut veri aktarımı, purge veya entegrasyon credential yetkisi değildir. Bu etkiler ilgili K3 implementation ve UAT kapılarında ayrı uygulanır.
