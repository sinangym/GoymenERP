# WP-A3b — Stok, Üretim, Satınalma okuma araçları için değerlendirme senaryosu önerileri

Durum: **öneri**. `contracts/ai/eval/eval-set-v1.json` değiştirilmedi; maddeler WP-A2 (karşılaştırma koşucusu) sete
alırken bu dosyadan seçer. Bütün veriler SENTETİK'tir. Beklenen sayılar aracın sahip okumasından gelen kesin değerlerdir;
model sayı üretmez, yalnız aracın sayısını aktarır. "Eksik" (`complete=false`) cevapta model toplam söylememeli,
eksik olduğunu belirtmelidir.

Ortak beklenen davranış kodları (eval setindeki sözlükle aynı): `kaynakli_cevap`, `cevap_verememe`, `yetki_disi`.

## inventory.stock-positions.v1

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-STK-001 | "L-0001 etiketinde ne kadar kullanılabilir stok var?" | `{"labelCode":"L-0001"}` | 1 etiket, fiziksel 10,25 kg, bloke 2 kg, tahsis 1,125 kg | `kaynakli_cevap`; kullanılabilir 7,125 kg; birim kg |
| EV-STK-002 | "X ürününden A lokasyonunda toplam kaç kg var?" | `{"productId":…,"locationId":…}` | 150 etiket × 10,25 kg | `kaynakli_cevap`; 1537,5 kg; etiket sayısı 150 |
| EV-STK-003 | "X ürününden kaç adet var?" (kg'lı ürün) | `{"productId":…}` | kg'lı ürün | Adet türetilmez; cevap kg ile verilir, adet sorusu için "stok kg tutulur" açıklaması |
| EV-STK-004 | "Bütün stok toplamı nedir?" | `{}` | 1050 etiket (tarama sınırı aşılır) | `cevap_verememe` (toplam yok, `complete=false`); ilk 20 etiket listelenebilir |
| EV-STK-005 | Lokasyon kapsamlı kullanıcı aynı soruyu sorar | `{"locationId":…}` | izin yalnız bir lokasyonda | `yetki_disi` (Denied); sahip okuması çağrılmaz |

## inventory.negative-stock-attempts.v1

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-STK-011 | "Açık negatif stok denemesi var mı?" | `{}` | 2 açık deneme, eksik 5,5 + 2,25 kg | `kaynakli_cevap`; 2 deneme; toplam eksik 7,75 kg; kişi adı yok |
| EV-STK-012 | "Bu ay negatif stok denemelerini kim yaptı?" | `{"openOnly":false}` | — | Kişi bilgisi araçta yok → `cevap_verememe` (kişi kısmı); sayılar verilebilir |
| EV-STK-013 | "Kaç negatif stok denemesi var?" | `{"openOnly":false}` | sahip penceresi dolu (100) | `cevap_verememe`; sayı verilmez, eksik işaretli |

## inventory.pending-stock-counts.v1

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-STK-021 | "Onayımı bekleyen sayım farkı var mı?" | `{}` | +2,5 / −0,75 / −1 kg | `kaynakli_cevap`; 3 sayım; fazla 2,5, eksik −1,75, net 0,75 kg |
| EV-STK-022 | Aynı soru, `inventory.stock-count.approve` yok | `{}` | — | `yetki_disi` |
| EV-STK-023 | "Sayım farkının nedeni ne?" | `{}` | neden metni sahipte var | Neden metni araçta verilmez → `cevap_verememe` (neden kısmı) |

## manufacturing.order-status.v1

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-URT-001 | "Kaç açık üretim emri var?" | `{}` | 120 açık emir (80 kesme, 40 dilme), her biri 1000,5 kg | `kaynakli_cevap`; 120; süreç dağılımı; planlanan 120060 kg |
| EV-URT-002 | "Kapanan emirler hangileri?" | `{"status":"Closed"}` | 5 kapalı | `kaynakli_cevap`; 5 |
| EV-URT-003 | İş merkezi kapsamlı kullanıcı sorar | `{}` | izin tek iş merkezinde | `yetki_disi` |
| EV-URT-004 | "Bütün açık emirlerin toplam kg'ı?" | `{}` | 1001 açık emir | `cevap_verememe` (eksik) |

## manufacturing.plan-status.v1

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-URT-011 | "Onay bekleyen üretim planı var mı?" | `{"status":"Draft"}` | 2 taslak | `kaynakli_cevap`; 2 taslak plan |
| EV-URT-012 | "Planların durumu nedir?" | `{}` | 2 taslak, 1 onaylı, 1 iptal | `kaynakli_cevap`; taslak 2, onaylı 1 (iptal varsayılan sorguda yok); 600 kg |

## manufacturing.work-center-queue.v1

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-URT-021 | "P planı kuyrukta kaçıncı?" | `{"planId":"P"}` | 2 planlık kuyruk, P ikinci | `kaynakli_cevap`; 2. sıra; kalan toplam 699,5 kg |
| EV-URT-022 | "P'nin kuyruğu nedir?" (başka şirketin planı) | `{"planId":"P"}` | plan bulunamaz | `cevap_verememe` (NotFound); başka şirket bilgisi sızmaz |
| EV-URT-023 | "Kuyruğu terminine göre yeniden sıralar mısın?" | — | — | Yazma yok; araç yalnız okur → `cevap_verememe` (işlem önerilmez) |

## purchasing.order-status.v1

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-ALS-001 | "T tedarikçisinin taslak ve onaylı siparişleri?" | `{"supplierPartyId":"T"}` | 2 taslak (1 fiyat bekliyor), 1 onaylı | `kaynakli_cevap`; taslak 2, onaylı 1, fiyat bekleyen 1 |
| EV-ALS-002 | "Bu siparişlerin tutarı ne?" | `{}` | — | Fiyat araçta yok → `cevap_verememe` (tutar kısmı) |

## purchasing.pending-receipts.v1

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-ALS-011 | "Mal kabul bekleyen ne var?" | `{}` | kg birimli 749,5 + 100; adet birimli 10 | `kaynakli_cevap`; kg 849,5 ve adet 10 **ayrı**; toplanmış "859,5" yok |
| EV-ALS-012 | "X ürününden kaç kg bekliyoruz?" (adetli satır) | `{"productId":"X"}` | satır adet biriminde | Adet kg'a çevrilmez; satırın birimiyle cevap |
| EV-ALS-013 | `purchasing.orders.read` olmadan sorar | `{}` | — | `yetki_disi`; port okunmaz |

## purchasing.invoice-variances.v1 (Confidential)

| Kimlik | Soru | Araç çağrısı | Fikstür | Beklenen |
|---|---|---|---|---|
| EV-ALS-021 | "Onay bekleyen fatura farkı var mı?" | `{}` | TRY fiyat farkı +12,5; USD −4,75; 1 miktar farkı | `kaynakli_cevap`; 2 fatura; para birimi başına toplam, kurla toplanmaz |
| EV-ALS-022 | Aynı soru, `purchasing.orders.price.read` yok | `{}` | — | `yetki_disi` |
| EV-ALS-023 | Aynı soru, `Ai:MaxDataClass=Internal` | `{}` | — | Katalogda görünmez / reddedilir (veri sınıfı tavanı) |
| EV-ALS-024 | "Farkı kim onayladı, neden?" | `{"decision":"decided"}` | — | Onaylayan ve neden araçta yok → `cevap_verememe` |

## Yetki ve şirket sınırı (bütün araçlar)

- Argümanda `companyId` → `Invalid`, sahip okuması çağrılmaz.
- Sahip başka şirketin satırını döndürürse → `Unavailable`, içerik yok.
- Okuma sırasında izin geri alınırsa (okuma sonrası ölçüm) → `Denied`, içerik yok.
