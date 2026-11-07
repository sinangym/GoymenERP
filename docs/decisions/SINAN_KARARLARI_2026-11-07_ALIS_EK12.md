# Sinan Kararları — 2026-11-07 Alış EK12 (Muhasebe alış siparişini okur; EK11 fiyat okumasının tamamlanması)

Kaynak: Sinan Göymen'in 7 Kasım 2026 tarihli Claude cloud oturumunda (Alış şeridi), PR #959 (EK11 düzeltmeleri)
review'unda açılan Muhasebe `purchasing.orders.read` / `purchasing.orders.price.read` NEEDS-SINAN çifti için verdiği
**doğrudan cevap** (makbuz: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6707388863). Soru **İngilizce** soruldu; soru, bütün seçenekler ve seçenek açıklamaları sorulduğu gibi aynen
aktarılmıştır, altında Türkçe karşılığı **çeviri** olarak verilmiştir (çeviri karar metni değildir; ayrılık olursa
İngilizce metin geçerlidir). "(Önerilen)" işareti Claude'un soruyu sorarken koyduğu işarettir. Claude yalnız soruyu ve
seçenekleri hazırlamıştır; seçimi Sinan yapmıştır.

Numaralama: ALS-D69…D72 EK11'dedir. Bu dosya ALS-D73'ü içerir. EK11'in **karar** içeriği (§1) değişmez; bu PR'da yalnız etiket/atıf düzeltmesi yapıldı.

## 0. Yetki ve sınır

- `SINAN_KARARLARI_2026-11-02_ALIS.md` §0 (ALS-D45 ile daraltılmış hâliyle), EK8 §0, EK9 §0 (ALS-D57), EK10 §0 ve
  EK11 §0 geçerlidir. ALS-D73'te seçilen seçenek doğrudan kullanıcı cevabıdır.
- **Değişmeyen insan kapıları:** production aktivasyonu, secret/deployment, grant/rol/Critical ve `OPEN-*` kapatma
  insanda kalır (AGENTS.md). ALS-D73 bir izin adının **kime önerildiğini** seçer; gerçek kullanıcıya veya production
  rolüne grant değildir. İzin yalnız SENTETIK (test/browser-suite) personalarına verilebilir; gerçek grant insan
  kapısındadır.
- Bu dosya hiçbir `OPEN-*` kimliğini kapatmaz, grant/rol vermez. Persona kartındaki değişiklik (§2) bir **öneridir**.

## 1. Kararlar

| No | Konu | Soru (aynen, İngilizce) | Seçenekler (aynen) | Seçilen (aynen) | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|---|---|
| ALS-D73 | Muhasebe alış siparişini okur mu | **"Should Muhasebe (accounting) be able to open purchase orders? You already decided Muhasebe can see purchase prices (EK11), but whether they can open orders at all was never decided. Without opening orders, the price permission does nothing."** | **"Evet, okuyabilsin (Önerilen)"** — "Muhasebe and Muhasebe Müdürü can open purchase orders and see prices, read-only. They cannot create, edit or approve."; **"Hayır, görmesin"** — "Muhasebe does not see purchase orders. Their price-view right is removed too; they see amounts in Finance." | **"Evet, okuyabilsin (Önerilen)"** | Kart: Muhasebe `purchasing.orders.read` `?`, Muhasebe Müdürü `—` (SAT-D191); ALS-D70 ikisine de `price.read` `+` önerdi; PR #959'da çift NEEDS-SINAN | ALS-D70, PRS-01, PRS-02, SAT-D142, SAT-D191, kart "Satın alma personaları" |

**Çeviri (Türkçe; karar metni değildir):**

- Soru: "Muhasebe alış siparişlerini açabilsin mi? Muhasebe'nin alış fiyatlarını görebileceğine zaten karar verdiniz
  (EK11), ama siparişleri açıp açamayacağı hiç kararlaştırılmadı. Siparişi açamazsa fiyat yetkisi hiçbir işe yaramaz."
- "Evet, okuyabilsin (Önerilen)": "Muhasebe ve Muhasebe Müdürü alış siparişlerini açar ve fiyatları görür, yalnız
  okuma. Oluşturamaz, düzenleyemez, onaylayamaz."
- "Hayır, görmesin": "Muhasebe alış siparişlerini görmez. Fiyat görme yetkisi de kaldırılır; tutarları Finans'ta görür."

## 2. Etkiler ve yorumlar

Bu bölümdeki **yorum** etiketli maddeler geri alınabilir; karar değildir.

- **ALS-D73 (okuma):** `purchasing.orders.read` kart için **Muhasebe** ve **Muhasebe Müdürü**'ne **önerilir**; ikisi
  ALS-D70'teki `purchasing.orders.price.read` ile birlikte alış siparişini açar ve fiyatları görür. **Yalnız
  okumadır:** create/approve/cancel Muhasebe ve Muhasebe Müdürü için `—` (ALS-D73 seçenek açıklaması: "They cannot
  create, edit or approve"); talep açma/geri çekme/onay ve fiyat yazma hücreleri değişmez (`—`).
- **SAT-D191 istisnası:** SAT-D191 Muhasebe Müdürü'ne öteki modüllerin okumalarını vermez (ana tabloda `—`). ALS-D73
  bu hücreyi **yalnız kartın "Satın alma personaları" ek tablosunda** `+` yapar; SAT-D191'in öteki modüller (stok,
  üretim, lojistik …) için `—` kuralı aynen geçerlidir.
- **Persona kartı:** "Satın alma personaları" tablosunda `purchasing.orders.read` satırı Muhasebe `+` ve Muhasebe
  Müdürü `+` olur (ALS-D73); PR #959'da açılan NEEDS-SINAN notu ALS-D73 atfıyla değişir. Öneridir; gerçek grant insan
  kapısındadır, yalnız SENTETIK personalar test için alabilir.
  - *Yorum:* Muhasebe Müdürü, Muhasebe personasının yetkilerini kendiliğinden almaz (kart, SAT-D187; Muhasebe Müdürü satırı ve NS-13 gerekçesi); burada seçenek
    açıklaması ikisini ayrı ayrı adlandırdığı için ikisi de `+` alır. Geri alınabilir.
- **Kod:** değişmez. `purchasing.orders.read` ve `.price.read` manifestte kayıtlıdır; grant yoktur.

## 3. Değişmeyenler

- ALS-D53, ALS-D54, ALS-D66, ALS-D70 (fiyatı görenler) ve EK11'in bütün kararları aynen; SAT-D142 (Muhasebe sipariş
  yazmaz, onaylamaz) alış tarafında da aynen.
- SAT-D191 ana tablodaki bütün hücrelerde aynen; istisna yalnız ek tablodaki bu tek hücredir.
- Grant/rol ataması, production aktivasyonu, secret/deployment ve `OPEN-*` kapatma insan kapısıdır.
