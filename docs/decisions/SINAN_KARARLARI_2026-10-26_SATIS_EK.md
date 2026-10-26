# Sinan Kararları — 2026-10-26 (Satış ekleri ve merge review teyidi)

Bu belge `SINAN_KARARLARI_2026-10-25_SATIS.md` ve `SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md`
dosyalarını silmez. Kaydettikleri:

- Issue #66'daki üç `NEEDS-SINAN` sorusunun cevabı:
  [6404080004](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6404080004) ve
  [6404405195](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6404405195);
- aynı oturumda bağımsız review'ların ortaya çıkardığı üç ek soru.

**Kaynak:** Sinan'ın 26 Ekim 2026 Claude Code cloud oturumundaki doğrudan cevapları
(oturum `session_01SPADrzecggx3apH2gfNW7b`). Soru ve cevaplar kelimesi kelimesine:

> Soru: "Bağımsız review: Codex'i beklememek için review'u ayrı bir ajan oturumuna yaptırıyorum
> (ASSUMPTION-MERGE-1). Bu şekilde devam edeyim mi?" — Cevap: "Evet"
>
> Soru: "Politikası olmayan şirket: GSP ve GIN'de iskonto reddedilsin mi, yoksa onaya mı düşsün?" —
> Cevap: "Onayla, göymen demir çelik gibi devam etsin aynı yetkiler ile"
>
> Soru: "Tolerans sınırları: dahil mi, ve fazla/eksik teslim aynı eşiklerle mi değerlendirilsin?" —
> Cevap: "Evet dahil, aynı eşiklerle değerlendirilebilir, toleransla fazla eksik sevk çıkabilelim"
>
> Seçenekli ek sorular (seçilen seçenek):
> - "Eksik teslim sapması ne zaman ölçülsün? (Kısmi sevkler satır içinde toplanır, sipariş birimiyle;
>   fazla teslim her sevkte kontrol edilir.)" → "Satır kapanırken (Önerilen)": açık kalan miktar
>   eksik sayılmaz; eksik, satır kapatılırken ya da son sevk işaretlenince ölçülür.
> - "Eksik teslim uyarı (%1–%2) veya onay (%2–%3) bandındaysa satır ne olsun?" → "Tamamlanır, kalan
>   kapanır (Önerilen)": uyarıda doğrudan, onay bandında onaydan sonra satır 'Tamamlandı' olur; kalan
>   miktar iptal edilen kalan sayılır.
> - "GSP ve GIN'de satış yapılabilmesi için kg birim profili ve TRY para profili de GDC ile aynı
>   açılsın mı?" → "Evet, GDC ile aynı (Önerilen)": kg (3 ondalık) ve TRY (tutar 2 ondalık)
>   profilleri GSP ve GIN için de açılır; GIN'in faturaya kapalı olması (DEC-ORG-005) değişmez.

## Kararlar

| No | Konu | Karar | Etkilenen |
|---|---|---|---|
| SAT-D13 | Bağımsız review (ASSUMPTION-MERGE-1) | **Teyit edildi.** Claude şeridi kendi PR'larında bağımsız review'u, yazarın bağlamını taşımayan ayrı bir ajan oturumuyla sağlar. Review sayıları (K1/K2 bir, K3 ve governance iki) ve exact-head şartı aynen kalır | `SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md` M2 |
| SAT-D14 | Satış politikası olmayan şirket | **GSP ve GIN, GDC ile aynı Satış politika değerleriyle başlar.** Değerler GDC'den canlı devralınmaz; her şirkete aynı başlangıç değerleriyle ayrı, şirket kapsamlı ve sürümlü bir politika kaydı açılır (`DEC-SET-001`). Teslim toleransı %1/%2/%3; iskonto limiti tanımsız olduğu için her iskonto onaya düşer (SAT-D2); onay tutar eşiği kapalı (SAT-D6). Onaylayan persona aynıdır (Müdür; SAT-D1/D2/D6) | Ayarlar Satış politika kaydı (MOD-03 dilim 1, PR #813) |
| SAT-D15 | Tolerans sınırları ve yönü | **Sınırlar dahildir; fazla ve eksik teslim aynı eşiklerle değerlendirilir.** Toleransa göre fazla veya eksik sevk yapılabilir | SAT-D5; gerçekleşme ekseni; Lojistik sevk kontrolü |
| SAT-D16 | Sapmanın ölçüm esası | **Sipariş satırı başına**, satırın sipariş biriminde (ör. kg) ölçülür. Teslim = o satırın bütün sevklerinin toplamı. Sapma = \|teslim − sipariş\| / sipariş × 100. **Fazla teslim her sevkte** kontrol edilir. **Eksik teslim** yalnız satır kapatılırken veya son sevk işaretlenince ölçülür; açık kalan miktar eksik sayılmaz | SAT-D15; Lojistik sevk; gerçekleşme ekseni |
| SAT-D17 | Banttaki eksik satırın kapanışı | Uyarı bandında satır doğrudan, onay bandında onaydan sonra **"Tamamlandı"** olur; kalan miktar **iptal edilen kalan** sayılır. Tolerans içi (< %1) eksik teslimde satır SAT-D5 uyarınca doğrudan "Tamamlandı" olur ve kalan yine iptal edilen kalandır. Blok bandında satır bu yolla kapanmaz; kalan sevk edilir ya da zorla kapatma (No 30, SAT-D12) uygulanır | SAT-D15/D16; zorla kapatma (No 30) ile aynı "iptal edilen kalan" kavramı |
| SAT-D18 | GSP/GIN birim ve para profili | **GSP ve GIN için kg birim profili ve TRY para profili GDC ile aynı açılır.** kg 3 ondalık, TRY tutar 2 ondalık (Ö2). Profiller her şirket için ayrı ve sürümlüdür. **TRY para profilinin işlem kapsamı GSP/GIN için yalnız `sales.order-draft.v1`'dir**; muhasebe (`accounting.journal-amount.v1`) ve finans (`finance.manual-bank-receipt.v1`) işlemleri ayrı karar ister. GIN'in faturaya kapalı olması (`DEC-ORG-005`, `IsPlaceholder`, `InvoiceCapable=false`) değişmez | Ayarlar `ApprovedUnitProfileCatalog`, `ApprovedMoneyProfileCatalog` |

### SAT-D15 bantları

Bir sınır değeri kendi başlattığı banda girer; bu, 2026-10-21 kararlarındaki K4 `AtOrAbove` kuralıyla
aynıdır.

| Sapma | Sonuç |
|---|---|
| < %1 | Uyarı yok (eksik teslimde satır kapanırken "Tamamlandı"; SAT-D17) |
| %1 ≤ sapma < %2 | Uyarı |
| %2 ≤ sapma < %3 | Onay gerekir |
| ≥ %3 | Blok |

- Eksik teslimde bu bantlar satır kapanırken uygulanır (SAT-D16); bandın satıra etkisi SAT-D17'dedir.
- Fazla teslimde delegasyon No 57 (satış sorumlusu inceleme kuyruğu, otomatik fatura yok) ayrıca
  geçerlidir.
- Yüzde değerleri kod sabiti olmaz; Ayarlar politika kaydından (`DEC-SET-001`) okunur.

## Sınırlar

- Hiçbir `OPEN-*` kimliği bütünüyle **KAPANDI** yapılmaz. `OPEN-004` Satış alt sorusu cevaplanır;
  `OPEN-009` için SAT-D6 burada yalnız uygulanır.
- "Aynı yetkiler" Satış politika değerleri ve persona içindir. GSP/GIN kullanıcılarına rol veya grant
  ataması değildir; grant her zaman insan kapısında ve `OPEN-013` kapsamındadır.
- Production aktivasyonu yapılmaz.
