# Sinan Kararları — 2026-11-11 Alış EK15 (masraf yapısı, API-98, ek masrafın maliyete etkisi)

Kaynak: Sinan Göymen'in AskUserQuestion cevapları; koordinatör kaydı
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6809898906 ("SINAN-KARARI 2026-11-11 (2. tur)", madde 7, 8 ve 10).
Bu dosya o kaydın Alış'ı ilgilendiren maddelerini numaralandırır; yeni karar üretmez. Aynı günün 1. tur kararlarından Alış'ı
ilgilendirenler (tevkifat eşik tabanı BS-3, A3 yeniden başlatma BS-8) `SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md`
dosyasındadır; burada yalnız bağlantı verilir.

## 0. Yetki ve sınır

- EK13 §0 geçerlidir. **Değişmeyen insan kapıları:** mali müşavir teyidi, production aktivasyonu, secret/deployment,
  Ayarlar'da gerçek kayıt aktivasyonu, grant/rol ve `OPEN-*` kapatma insanda kalır. Bu dosya hiçbir `OPEN-*` kapatmaz;
  `OPEN-INV-AVGCOST-LANDED` Stok şeridinin karar dosyasında bu karara bağlanır.

## 1. Kararlar

| No | Konu | Seçilen (kayıttaki metin) | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D79 | API-98 Ayarlar listeleri (masraf tipi, hizmet tipi, işlem türü) | "Alış'taki API-98 listeleri (masraf tipi, hizmet tipi, işlem türü) ALS-D63 istisnasıyla açılmaz; Ayarlar modülünü bekler." Sinan ayrı bir **masraf yapısı** istiyor: örnek kalemler sarf, bakım, makine bakım-onarım, araç, yakıt, demirbaş, makine alımı, araç alımı; tek düzen hesap planına ve mizan yönetimine uyumlu, gerçek maliyet yönetimine ve patron ekranına uygun; **Alış ve Satış'ta ortak** kullanılır. Durum `NEEDS-SPEC` (ChatGPT şeridi) | Plan: "API-98 ALS-D63 kapsamında anılmadı → NEEDS-SINAN; o zamana kadar ertelenmiş, fail-closed" | API-98, ALS-D63 (kapsamı değişmez: yalnız API-95/96/97), ALS-D20, ALS-D23, ALS-D37, ALS-D58 |
| ALS-D80 | Nakliye/gümrük gibi ek alış masraflarının ortalama maliyete etkisi | "Nakliye/gümrük gibi ek masraflar: v1'de ortalamaya girmez; masraf yapısı kurulunca ayarla açılabilir." | ALS-D23 masraf satırı, ALS-D50 gümrük/ithalat masraf satırı; maliyete etkisi tanımsızdı | ALS-D23, ALS-D50, API-23, `OPEN-INV-AVGCOST-LANDED` (Stok) |

## 2. Alış'a etkisi

- **API-98 açılmaz.** Alış'ta masraf tipi/hizmet tipi/işlem türü için Ayarlar listesi, sağlayıcı veya migration yazılmaz.
  İlgili alanlar mevcut davranışta kalır (fail-closed). Ortak masraf yapısının tasarımı (`NEEDS-SPEC`) gelince Alış ve Satış
  ayrı dilimle bağlanır.
- **Madde 7 (bilgi, Finans):** cariye bağlı olmayan ödemeler şimdilik tek "Masraf" kalemi + zorunlu açıklama ile girilir;
  geçiş yolu açık tutulur. Alış kodu bundan etkilenmez.
- **ALS-D80:** API-23 masraf satırı (nakliye, gümrük vb.) v1'de stok ortalama maliyetine dağıtılmaz. Alış, masraf
  satırını maliyet girdisi olarak Stok'a göndermez; ileride açılacak ayar Stok/Ayarlar işidir.

## 3. Açık kalanlar

- Ortak masraf yapısı spesifikasyonu (`NEEDS-SPEC`).
- Alış A3 kapsamı (#750/#772/#773'ün Stok/Kalite parçaları ve kapı dışı Alış dilimleri): NEEDS-SINAN
  https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6811561164.
