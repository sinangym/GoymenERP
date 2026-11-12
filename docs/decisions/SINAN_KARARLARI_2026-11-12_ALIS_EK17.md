# Sinan Kararları — 2026-11-12 Alış EK17 (F-017: gelecek tedarikten satış siparişine ayırma)

Kaynak: Sinan Göymen'in koordinatör oturumunda AskUserQuestion ile verdiği kendi seçimleri; koordinatör kayıtları
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819147253 madde 2 (F-017; önerilen seçenek) ve
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819640816 madde 3–4 (F-017 S1/S2). Bu dosya o kaydı
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

## 2. İş bölümü (koordinatör kaydı, 2026-11-12)

- **Alış:** açık (kabul edilmemiş) sipariş satırı miktarını ayrılabilir olarak yayınlayan sürümlü okuma portu; mal kabulde satış
  talebi bağlantısının Alış tarafı.
- **Stok:** tahsis modeli ("aynı miktar bir kez" değişmezi, ayırma kaydı, kabulde fiziksel tahsise dönüş ve etiket ataması).
- Port sözleşmesi Alış ve Stok tarafından ortak yazılır.

## 3. Açık kalanlar

- S1 ve S2 ALS-D83/ALS-D84 ile cevaplandı. Açık kalanlar plan §5'tedir: S3 kabul geri alınırsa tahsisin durumu (#1116 S12) ve
  S4 ayırması olan satınalma satırının/siparişin iptali (ALS-D83 "ayrı bir durum").
