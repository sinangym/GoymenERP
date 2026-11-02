# Sinan Kararları — 2026-11-02 Alış EK4 (uygulama şeridi ve modül sınırı)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), Alış planının
uygulamasına başlanırken verdiği talimat ve iki seçenekli soruya verdiği **doğrudan cevaplar**. Serbest metinler
aynen aktarılmıştır. Claude yalnız soruları ve seçenekleri hazırlamıştır.

## 0. Yetki ve sınır

`SINAN_KARARLARI_2026-11-02_ALIS.md` §0 aynen geçerlidir. Review ve merge kapıları değişmez; K3 işlerde iki bağımsız
exact-head review gerekir; insan kapıları (grant/rol, production aktivasyonu, deployment, secret, `OPEN-*` kapatma)
değişmez.

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D44 | Planın uygulanması ve ortak altyapının yazarı | Talimat aynen: **"Codexte değil claude ile yukardaki planın tamamına sırasıyla başlayalım ve bana soracağın soru varsa bunlarla ilgili sorabilirsib"**. Ortak altyapı sorusuna cevap aynen: **"Alış burda yazılsın, satış satış konuşmasında yazılsın, finans tarafını ise daha konuşma açmadım açınca finans konuşmasında yazsın"** | Plan §13 B-2/B-3: outbox (ERP-000F, şerit codex) ve fatura çekirdeği sahibi açıktı | ALS-D1, plan A0…A6, MOD-12, MOD-15, B-2, B-3 |
| ALS-D45 | Diğer modüllerdeki küçük eklemeler | Seçenek: **Evet, Satış/Üretim hariç.** Seçenek açıklaması: Ayarlar, Finans, Cari, Stok'ta Alış'ın ihtiyacı olan eklemeleri bu konuşma yapar (iki bağımsız inceleme ile); Satış ve Üretim dosyalarına dokunmaz, görev notu bırakır; Stok'ta Üretim konuşması da çalıştığı için merge sırası notla ayarlanır | URT-D19/D20 yalnız Satış/Üretim/Alış sınırını tanımlıyordu | MOD-05, MOD-06, MOD-08, MOD-14 |

## 2. Notlar (geri alınabilir)

- **ALS-D44 + ALS-D45 birlikte okunur:** Finans'taki borç/alacak ayrımı (plan MOD-15, B-1) ALS-D44'ün özel cevabı
  gereği **Finans konuşmasının** işidir; ALS-D45'teki "Finans'ta küçük ekleme" yetkisi bunu kapsamaz. Alış, Finans
  konuşması açılana kadar MOD-15 için görev notu (`ALIS_FINANS_DEVIR_v1.md`) bırakır; A5'in gelen fatura kesinleştirme
  adımı MOD-15 gelene kadar fail-closed kalır.
- **Olay iletme (outbox):** Alış, ihtiyaç duyduğu olay kaydını Work ve Stok modüllerindeki mevcut desenle **kendi
  modülünde** yazar (modül outbox tablosu + aynı transaction). Platform düzeyindeki dağıtıcı (ERP-000F) ortak iştir;
  Alış onu ancak Satış konuşmasıyla çakışmayacak biçimde ve görev notuyla haber vererek yazar. Satış'ın kendi olayları
  Satış konuşmasındadır.
- **Fatura çekirdeği:** Alış gelen fatura (Mikro'dan içe aktarma + eşleştirme) çekirdeğini kendi ihtiyacı için yazar;
  Satış fatura çekirdeğini Satış konuşması yazar. Ortaklaştırma fırsatı doğarsa görev notuyla önerilir; sessiz kopya
  ya da diğer şeridin dosyasına yazma yapılmaz.
- **Codex şeridi:** Alış planındaki CX/Codex atamaları Claude'a geçer (ALS-D1 ile zaten öyleydi); Codex şeridine Alış
  için iş bırakılmaz.
