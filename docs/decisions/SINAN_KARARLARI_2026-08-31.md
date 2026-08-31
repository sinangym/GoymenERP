# Sinan Kararları — 2026-08-31

Kullanıcı (Sinan Göymen) `GOYMEN_ERP_HIZLI_TESLIM_v3.md` §9 sorularına verdiği cevaplar.
Bu belge `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` için **öneri** kaydıdır; merge sonrası
`KARAR_SICILI.md` `DEC-*` ve `ACIK_KARARLAR.md` `OPEN-*` satırları buna göre güncellenir.

Kaynak önceliği: bu belge = "son kullanıcı kararı" seviyesindedir (`AGENTS.md` kaynak sırası 0).

---

## 1. Şirketler — `OPEN-001` → KAPANDI (kısmi)

Üç hukuki şirket:

| CompanyCode | Tam unvan | VKN | Durum |
|---|---|---|---|
| `GDC` | Göymen Demir Çelik İnş. San. ve Tic. Ltd. Şti. | `4100046586` | GERÇEK |
| `GSP` | Göymen Sac Profil San. ve Tic. Ltd. Şti. | `4101507252` | GERÇEK |
| `GIN` | Göymen İnşaat | `1111111111` | **PLACEHOLDER** — tam unvan + gerçek VKN eksik |

- `GDC` ve `GSP` gerçek; seed'e girilebilir.
- `GIN` tam unvan ve gerçek VKN bekliyor → `OPEN-001-GIN` olarak açık kalır; `GIN` şirketi
  fatura/resmî belge üretimine kapalı feature-flag ile korunur.
- CompanyCode değerleri bu kararla üretildi (grup ön eki `G` + şirket kısaltması).
- **Şirket ekleme/düzenleme Ayarlar sayfasından yapılabilir olmalıdır** (bkz. §5). Seed
  yalnız başlangıç verisidir; runtime CRUD Ayarlar/Organizasyon masterı üzerindendir.

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

Karar (`DEC-SET-001` önerisi):

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

## 7. Hâlâ açık kalan `OPEN-*`

Bloke etmeyen, dalga geldiğinde sahibiyle kesinleşecek:

- `OPEN-001-GIN` — Göymen İnşaat tam unvan + gerçek VKN
- `OPEN-007` değerleri — formül registry yapısı kararlı; kesin sayılar validasyon bekliyor
- `OPEN-008`, `OPEN-011` — ertelendi (maliyet, nakit planı)
- `OPEN-014/015/016/017/018/019/020/021` — geçici değerler kabul (v3 §9.3), production kapılarında
