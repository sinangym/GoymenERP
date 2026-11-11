# Sinan Kararları — 2026-11-11 Alış EK14 (D4: alış tevkifatı)

Kaynak: Sinan Göymen'in koordinatör oturumuna verdiği cevap; koordinatör kaydı
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6794934951 (NEEDS-SINAN
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6794888600 madde 1-2'nin cevabı). **Doğrudan kullanıcı cevabı
koordinatör aktarımıdır; soru ve seçenek metni aynen elimizde değildir** (yalnız seçilen: "Evet, Satış gibi hemen açılsın").
Bu dosya o kaydı numaralandırır; yeni karar üretmez.

## 0. Yetki ve sınır

- EK13 §0 geçerlidir. **Değişmeyen insan kapıları:** mali müşavir teyidi, production aktivasyonu, secret/deployment,
  Ayarlar'da gerçek vergi/eşik kaydı aktivasyonu, grant/rol ve `OPEN-*` kapatma insanda kalır. Bu dosya hiçbir `OPEN-*`
  kapatmaz.

## 1. Karar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D78 | Alışta tevkifat | "Evet, Satış gibi hemen açılsın"; Alış'a özgü kapalı ayar yok. Kodda Alış, Satış S5 uygulamasıyla birebir aynı kuralı kullanır: pay ürün türünün Settings tevkifat varsayılanı, yuvarlanmış satır KDV'sinden (SAT-D75); VUK 232 eşiği **kapı değildir** ve Alış'ta okunmaz | EK13 ALS-D74: tevkifat N-20'ye kadar "hesaplanamıyor" | N-20 (tevkifat kısmı kapandı; tedarikçi muafiyeti açık), ALS-D74, SAT-D75 |

## 2. Açık kalanlar

- **Eşik tabanı** (sipariş brütü mü fatura/belge mi, "aşan" yönü) cevaplanmadı. Alış'ta eşik kapısı yoktur; fatura dilimi
  (A5) eşiği Satış'la aynı kuralla kullanacaksa ayrıca NEEDS-SINAN yazılır.
- Mali müşavir teyidi go-live öncesi insan kapısıdır.
