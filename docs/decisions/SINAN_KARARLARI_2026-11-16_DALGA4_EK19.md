# Sinan Kararları — 2026-11-16 (Dalga 4 ek 19: SC32 toplu cevap çelişkileri, termin izni, güvenilir proxy listesi, fason nokta vade)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: KLN-D109, SAT-D398, KIM-D50 ve SAT-D399.
Aşağıda adı geçen önceki kararlar, bu belgede açıkça güncellenen noktalar dışında aynen geçerlidir:
- KLN-D73 ve KLN-D74 (EK8);
- SAT-D358 (EK8) ve SAT-D346 (`SINAN_KARARLARI_2026-11-14_IZLENEBILIRLIK.md`);
- FNS-D68 (EK5);
- ALS-D99 (EK2);
- SAT-D129 (`SINAN_KARARLARI_2026-10-31_SATIS_EK18.md`);
- DEC-SAL-003 (`KARAR_SICILI.md`).

Bu belge henüz merge edilmemiş şu karar PR'larındaki metne atıf yapar:
- EK11, SC32 (#1175);
- EK12, SC33 (#1176);
- EK13, SC34 (#1177);
- EK15, SC36 (#1179).

Çelişkide daha yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 41; Issue #66
yorumu 6911095960). Sorular, karar PR'larının bağımsız incelemelerinde çıkan çelişkilerden ve açık
`NEEDS-SINAN` maddelerinden geldi. 41.2'nin cevabı serbest metindir ve aşağıda aynen alıntılanmıştır. "(Önerilen)"
işaretleri soranındır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 41.1 | SC32'deki "Hepsi önerildiği gibi" toplu cevabı, daha önce tek tek verilmiş 5 açık seçimle çelişiyor: KLN-D73 şablon yayınlayan, KLN-D74 ileri tarihli ayrım personaları, SAT-D358/SAT-D346 limit boşken sözleşme indirimi, FNS-D68 teminat kaydı personaları, ALS-D99 açık belgeli pasifleştirme. Hangisi geçerli olsun? | **Önceki açık seçimler (Önerilen)** | "Tek tek bilerek seçtiklerin geçerli kalır. SC32'deki çelişen 5 madde düşürülür, kalan SC32 kararları geçerli." Seçilmeyen: "SC32 toplu cevabı" — Daha yeni toplu cevap geçerli olur; 5 önceki karar değişir. "Tek tek sor" — Beş çelişkiyi ayrı ayrı sorarım; PR o zamana kadar bekler. |
| 41.2 | SAT-D375 (36.6) ayrı termin değiştirme iznini kaldırdı; 39.1 ise mevcut termin izninin kalmasını söyledi. Kaldırma geri alınsın mı? | Serbest metin: "Bu sipariş düzenleme izni olan değiştirebilir, hatta termin değiştirme onay düşürmez ile aynı olabilir" | Sunulan seçenekler: "Evet, izin kalsın (Önerilen)" — Termin mevcut `sales.orders.delivery-schedule.write` izniyle değiştirilir; SAT-D375 bu noktada geri alınır. "Hayır, SAT-D375 geçerli" — Ayrı termin izni kalkar; termin düzenleme izniyle değiştirilir; EK12 düzeltilir. Sinan ikisini de seçmedi, serbest metinle cevapladı. |
| 41.3 | Güvenilir proxy listesini (KIM-D42) kim değiştirebilir? | **Yalnız Admin (Önerilen)** | "Sistem Yöneticisi Ayarlar'dan değiştirir, denetim kaydı tutulur. kg/m sınırıyla aynı model." Seçilmeyen: "Yalnız kurulum ayarı" — Ekrandan değişmez; sunucu yapılandırmasında durur, değişikliği deploy ile yapılır (insan kapısı). |
| 41.4 | SAT-D379'daki "nokta vade" ne demek? | **Sipariş tarihi + N gün (Önerilen)** | "SAT-D129'daki gibi: vade, sipariş tarihinden itibaren sayılan gün sayısıyla belirlenir." Seçilmeyen: "Sabit takvim tarihi" — Vade belirli bir gün olarak girilir (ör. 15 Aralık), gün sayısı değil. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D109 | SC32 toplu cevabının önceki açık seçimlerle çelişmesi (41.1) | SC32'deki toplu cevap, daha önce tek tek verilmiş açık seçimlerle çeliştiği yerde **geçersizdir**; önceki açık seçimler geçerli kalır: KLN-D73, KLN-D74, SAT-D358/SAT-D346, FNS-D68 ve ALS-D99(1). EK11'deki (#1175) şu çelişen maddeler **düşürülür**: KLN-D80 (KLN-D73'e karşı), KLN-D82 (KLN-D74'e karşı), SAT-D364 (SAT-D358/SAT-D346'ya karşı), FNS-D72'nin `finance.collaterals.record` persona dağılımı (FNS-D68'e karşı) ve ALS-D103 (ALS-D99(1)'e karşı). EK11'in geri kalan kararları aynen geçerlidir. | EK11 (#1175) metni; Kalıp şablon yayını, ileri tarihli ayrım, sözleşme indirimi limiti, teminat kaydı personaları, alıcı pasifleştirme |
| SAT-D398 | Termin değiştirme izni (41.2; SAT-D375 ve SAT-D370'i günceller) | Serbest metin cevap: "Bu sipariş düzenleme izni olan değiştirebilir, hatta termin değiştirme onay düşürmez ile aynı olabilir". Sipariş termini (başlık ve satır), **sipariş düzenleme izni olan** kullanıcı tarafından değiştirilebilir. Termin değişikliği **sipariş onayını düşürmez**; bu, DEC-SAL-003'teki mevcut kuralla aynıdır ("yeniden onay gerektirmez, ancak auditlidir"). Değişiklik denetime yazılır. Bu karar EK15'teki SAT-D375'i (#1179) ve EK12'deki SAT-D370 / teyit 39.1'i (#1176) bu noktada günceller: termin için sipariş düzenleme izni yeterlidir. **Yorum (geri alınabilir):** "hatta … ile aynı olabilir" ifadesi, termin değişikliğinin onayı düşürmemesinin mevcut kuralla aynı olduğu ve termin izninin sipariş düzenleme izniyle aynı olabileceği şeklinde okunmuştur. Ayrı `sales.orders.delivery-schedule.write` izninin kaldırılması, takma ad olarak tutulması veya yanında kalması bu belgede seçilmez (Sınırlar). | Satış termin komutu ve endpoint metadata, sipariş satırı düzenleme, onay durumu |
| KIM-D50 | Güvenilir proxy listesini değiştiren (41.3; KIM-D42'yi tamamlar) | KIM-D42'deki güvenilir proxy listesini **yalnız Admin (Sistem Yöneticisi)** Ayarlar'dan değiştirir. Her değişiklik denetime yazılır. Model, kg/m içe aktarım sınırıyla (URN-D21) aynıdır. Yeni izin adı açılmaz; grant ataması insanda kalır. | Ayarlar (güvenilir proxy kaydı), giriş deneme sınırı |
| SAT-D399 | Fason "nokta vade"nin anlamı (41.4; SAT-D379'u tamamlar) | SAT-D379'daki "nokta vade", SAT-D129'daki nokta vadeyle aynıdır: **vade = sipariş tarihi + N gün**. Sabit takvim tarihi girilmez. SAT-D379'un geri kalanı aynen geçerlidir. | Fason sipariş başlığı, fason fatura vade planı |

## Sınırlar

- SAT-D398: Ayrı `sales.orders.delivery-schedule.write` izninin (DEC-SAL-003) akıbeti `NEEDS-SINAN` olarak kalır.
  Üç seçenek var: emekliye ayrılması, sipariş düzenleme izninin takma adı olarak tutulması veya yanında ayrıca
  kalması. Bu belge hiçbir izin kodu kaldırmaz veya eklemez.
- EK11 (#1175), EK12 (#1176), EK13 (#1177) ve EK15 (#1179) metinleri bu belgeyle hizalanmalıdır. Bu belge o PR'ların
  dosyalarını değiştirmez.
- Bu belge yeni izin adı açmaz ve rol/grant ataması yapmaz. Grant ataması insanda kalır.
- Hiçbir `OPEN-*` kapatılmaz.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
