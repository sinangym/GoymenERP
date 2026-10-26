# Sinan Kararları — 2026-10-26 (Satış ekleri ve merge review teyidi)

Bu belge `SINAN_KARARLARI_2026-10-25_SATIS.md` ve `SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md`
dosyalarını silmez. Issue #66'daki üç `NEEDS-SINAN` sorusunun cevabını kaydeder:
[6404080004](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6404080004) ve
[6404405195](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6404405195).

**Kaynak:** Sinan'ın 26 Ekim 2026 Claude Code cloud oturumundaki doğrudan cevapları
(oturum `session_01SPADrzecggx3apH2gfNW7b`), kelimesi kelimesine:

> "Bağımsız review: … Bu şekilde devam edeyim mi? Evet"
>
> "Politikası olmayan şirket: GSP ve GIN'de iskonto reddedilsin mi, yoksa onaya mı düşsün? Onayla, göymen
> demir çelik gibi devam etsin aynı yetkiler ile"
>
> "Tolerans sınırları: dahil mi, ve fazla/eksik teslim aynı eşiklerle mi değerlendirilsin? Evet dahil, aynı
> eşiklerle değerlendirilebilir, toleransla fazla eksik sevk çıkabilelim"

## Kararlar

| No | Konu | Karar | Etkilenen |
|---|---|---|---|
| SAT-D13 | Bağımsız review (ASSUMPTION-MERGE-1) | **Teyit edildi.** Claude şeridi kendi PR'larında bağımsız review'u, yazarın bağlamını taşımayan ayrı bir ajan oturumuyla sağlar. Review sayıları (K1/K2 bir, K3 ve governance iki) ve exact-head şartı aynen kalır | `SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md` M2 |
| SAT-D14 | Satış politikası olmayan şirket | **GSP ve GIN, GDC (Göymen Demir Çelik) ile aynı Satış politikalarıyla çalışır.** Teslim toleransı %1/%2/%3 olur. İskonto limiti tanımsızdır, bu yüzden her iskonto onaya düşer (SAT-D2). Onay tutar eşiği kapalıdır (SAT-D6) | `ApprovedSalesPolicyCatalog`; MOD-03 |
| SAT-D15 | Tolerans sınırları ve yönü | **Sınırlar dahildir; fazla ve eksik teslim aynı eşiklerle değerlendirilir.** Toleransa göre fazla veya eksik sevk yapılabilir. Sapma = \|teslim − sipariş\| / sipariş × 100; bantlar aşağıda | SAT-D5; gerçekleşme ekseni; Lojistik sevk kontrolü |

### SAT-D15 bantları

"Sınır dahil" okumasına göre bir sınır değeri kendi başlattığı banda girer:

| Sapma | Sonuç |
|---|---|
| < %1 | Tolerans içi; satır gerçekleşme ekseninde "Tamamlandı" sayılabilir |
| %1 ≤ sapma < %2 | Uyarı |
| %2 ≤ sapma < %3 | Onay gerekir |
| ≥ %3 | Blok |

Yüzde değerleri kod sabiti olmaz; Ayarlar politika kaydından (`DEC-SET-001`) okunur.

## Sınırlar

- Hiçbir `OPEN-*` kimliği bütünüyle **KAPANDI** yapılmaz. `OPEN-004` ve `OPEN-009`'un Satış alt soruları
  cevaplanır.
- "Aynı yetkiler" Satış politika değerleri içindir; GSP/GIN kullanıcılarına rol veya grant ataması
  değildir. Grant her zaman insan kapısındadır.
- Production aktivasyonu yapılmaz.
