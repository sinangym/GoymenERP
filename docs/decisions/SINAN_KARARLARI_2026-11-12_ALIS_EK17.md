# Sinan Kararları — 2026-11-12 Alış EK17 (F-017: gelecek tedarikten satış siparişine ayırma)

Kaynak: Sinan Göymen'in koordinatör oturumunda AskUserQuestion ile verdiği kendi seçimi; koordinatör kaydı
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819147253 madde 2 (F-017; önerilen seçenek). Bu dosya o kaydı
Alış şeridi için numaralandırır; yeni karar üretmez. Uygulama planı: `docs/delivery/ALIS_F017_GELECEK_TEDARIK_AYIRMA_v1.md`.

## 0. Yetki ve sınır

- EK13 §0 geçerlidir. **Değişmeyen insan kapıları:** production aktivasyonu, secret/deployment, grant/rol ve `OPEN-*` kapatma
  insanda kalır. Bu dosya hiçbir `OPEN-*` kapatmaz.

## 1. Karar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D82 | Açık satınalma siparişi satırından satış siparişine ayırma | "Açık satınalma siparişi satırından satış siparişine **miktar bazında (kg/ton)** ayırma yapılabilir; **aynı miktar iki siparişe verilemez.** Mal kabulde ilgili satış talebine bağlanır, **etiket ataması o zaman yapılır.**" | ALS-D30 (tedarikçide hazır mal gelecek mal olarak ayrılır, aynı ton bir kez); F-017 "gelecek tedarikten tahsis yok" | ALS-D30, ALS-D22, ALS-D25, MOD-14, MOD-16, A3-0, DEC-STK-002/003 |

## 2. İş bölümü (koordinatör kaydı, 2026-11-12)

- **Alış:** açık (kabul edilmemiş) sipariş satırı miktarını ayrılabilir olarak yayınlayan sürümlü okuma portu; mal kabulde satış
  talebi bağlantısının Alış tarafı.
- **Stok:** tahsis modeli ("aynı miktar bir kez" değişmezi, ayırma kaydı, kabulde fiziksel tahsise dönüş ve etiket ataması).
- Port sözleşmesi Alış ve Stok tarafından ortak yazılır.

## 3. Açık kalanlar

- Satır azaltma/iptal ile ayrılmış miktar çakışması ve kısmi kabulde ayırmaların sırası: plan §5'teki NEEDS-SINAN.
