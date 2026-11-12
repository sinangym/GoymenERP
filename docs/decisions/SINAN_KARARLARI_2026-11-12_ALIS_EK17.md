# Sinan Kararları — 2026-11-12 Alış EK17 (F-017: gelecek tedarikten satış siparişine ayırma)

Kaynak: Sinan Göymen'in koordinatör oturumunda AskUserQuestion ile verdiği kendi seçimleri; koordinatör kayıtları
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819147253 madde 2 (F-017; önerilen seçenek) ve
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819640816 madde 3–4 (F-017 S1/S2) ve
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088 madde 1, 3, 6, 7, 8, 9 (11.–13. tur). Bu dosya o kaydı
Alış şeridi için numaralandırır; yeni karar üretmez. Uygulama planı: `docs/delivery/ALIS_F017_GELECEK_TEDARIK_AYIRMA_v1.md`.

## 0. Yetki ve sınır

- EK13 §0 geçerlidir. **Değişmeyen insan kapıları:** production aktivasyonu, secret/deployment, grant/rol ve `OPEN-*` kapatma
  insanda kalır. Bu dosya hiçbir `OPEN-*` kapatmaz.

## 1. Karar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D82 | Açık satınalma siparişi satırından satış siparişine ayırma | "Açık satınalma siparişi satırından satış siparişine **miktar bazında (kg/ton)** ayırma yapılabilir; **aynı miktar iki siparişe verilemez.** Mal kabulde ilgili satış talebine bağlanır, **etiket ataması o zaman yapılır.**" | ALS-D30 (tedarikçide hazır mal gelecek mal olarak ayrılır, aynı ton bir kez); F-017 "gelecek tedarikten tahsis yok" | ALS-D30, ALS-D22, ALS-D25, MOD-14, MOD-16, A3-0, DEC-STK-002/003 |
| ALS-D83 | F-017 S1: ayrılmış miktarın altına inen satınalma satırı | Sinan metni (aynen): "Satınalma tarafından bu bağ düşürülmeli ama satınalma tarafındaki alınacak miktar düşmez ya başka satış siparişine ayrılabilir yada stok için alınabilir, özellikle ilgili alış sipariş satırı veya siparişin komplesi iptal edilmek istenirse o ayrı bi durum". Koordinatör okuması (geri alınabilir): satınalma tarafı satış ayırma bağını düşürebilir (ayırma bırakılır); satınalma miktarı bundan dolayı azalmaz, serbest kalan miktar başka satış siparişine ayrılabilir veya stok için alınır. Satır/sipariş **iptali** ayrı durumdur (plan §5 S4). | Plan §5 S1 (Alış işlemi reddeder / karşılıksız işaretle) | ALS-D82, KLN-D26 (6), DEC-STK-005 |
| ALS-D84 | F-017 S2: kısmi kabulde ayırma sırası | Termini en yakın satış önce alır; sıra **elle değiştirilebilir veya seçilebilir** olmalı (önerilen seçenek + elle seçim). | Plan §5 S2 | ALS-D82, KLN-D26 (4) |
| ALS-D85 | F-017 S4: ayırması olan satınalma satırının/siparişin iptali (11. tur m.1) | "İptal doğrudan yapılmaz. Ekran bağlı satış siparişlerini listeler ve 'bağları düşür ve iptal et' seçeneğini sunar. Gerekçeli tek onayla önce bağlar düşer (ALS-D83), sonra iptal aynı işlemde yapılır. Satışa 'kaynak iptal edildi' uyarısı gider." | Plan §5 S4 (cevaba kadar ret) | ALS-D83, #1116 S2 |
| ALS-D86 | F-017 S3 / #1116 S12: kabul geri alma/iade (11. tur m.3) | "Fiziksel tahsis ve etiket ataması geri alınır. Satınalma satırında hâlâ gelecek miktar varsa gelecek ayırma yeniden açılır. Gelecek miktar yoksa ayırma bırakılır ve satış uyarılır." | Plan §5 S3 (otomatik geri dönüş yok, ret) | ALS-D82, #1116 S12 |
| ALS-D87 | Kabulde farklı miktar (12. tur m.6; #1116 S1/S3) | "Az gelirse gelen miktar ayrılır, kalan ayırma satınalma satırının gelecek kısmında açık kalır. Satır kapanırsa kalan bırakılır ve satış uyarılır. Fazla gelen kısım (kg farkı) serbest stoğa girer." | #1116 §9 | ALS-D82, ALS-D84 |
| ALS-D88 | Ayırmayı kim yapar (12. tur m.7; #1116 S5) | "İzinli kişi (satış, satınalma ya da depo) tek başına ayırır, ikinci onay yoktur. İzin adı kodlanır; grant insan kapısıdır." | #1116 §9 | ALS-D82 |
| ALS-D89 | Kabulde bağlama başarısızsa (12. tur m.8; #1116 S7, S8 dahil) | "Mal kabulü durmaz, mal stoğa girer. Satış ayırması bırakılır ve satış uyarılır. Bloklu etiket (S8) de bu kurala girer." | Plan §3.3 "#1116 S7" | ALS-D82, DEC-STK-005 |
| ALS-D90 | Ayırma süresi (13. tur m.9; #1116 S11) | "Ayırmanın son tarihi yoktur. Satınalma termini geçerse satış ve satınalma kullanıcısı uyarılır, ayırma açık kalır. Süre ileride ayarlardan tanımlanabilir." | #1116 §9 | ALS-D82 |

## 2. İş bölümü (koordinatör kaydı, 2026-11-12)

- **Alış:** açık (kabul edilmemiş) sipariş satırı miktarını ayrılabilir olarak yayınlayan sürümlü okuma portu; mal kabulde satış
  talebi bağlantısının Alış tarafı.
- **Stok:** tahsis modeli ("aynı miktar bir kez" değişmezi, ayırma kaydı, kabulde fiziksel tahsise dönüş ve etiket ataması).
- Port sözleşmesi Alış ve Stok tarafından ortak yazılır.

## 3. Açık kalanlar

- S1–S4 ALS-D83..ALS-D86 ile cevaplandı; #1116 S1/S3, S5, S7/S8, S11 ALS-D87..ALS-D90 ile kayda alındı. Alış tarafında açık
  iş kararı kalmadı.
- #1116 S6 (izin adı) ve S9 (teknik/genel satır eşlemesi) koordinatör notuna göre teknik konulardır; şerit PR'da gerekçelendirir.
  S10 SAT-D298 ile kilitlidir. Hiçbir `OPEN-*` bu kayıtla kapanmaz.
