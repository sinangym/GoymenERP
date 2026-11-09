# Sinan Kararları — 2026-11-08 (Satış: VUK 232 tevkifat eşiği 12.000 TL)

Bu belge önceki karar dosyalarını silmez; SAT-D231'i ekler. VUK 232 tevkifat tutar eşiğinin 12.000 TL
olduğunu mali müşavir teyidiyle kaydeder ve eşiğin "mali müşavir teyidi şart" notunu kapatır. SAT-D93 (eşik
Ayarlar'da tarihli değer olarak tutulur, siparişin bütünü üzerinden değerlendirilir), EK19 soru 3 ve SAT-D230
geçerlidir; bu belge onları değiştirmez.

**Kaynak:** Sinan'ın 8 Kasım 2026 22:53Z'de koordinatör oturumunda (`session_015Usc2qbq4qsgayZcDY3UTb`)
AskUserQuestion ile verdiği doğrudan cevap; Satış şeridi cevabı o oturumun olay kaydından ayrıca okudu (soru,
seçenekler, açıklamalar ve seçim aşağıda aynen). Bu cevap Issue #66'daki `NEEDS-SINAN`
(<https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6728263791>) sorusunu yanıtlar. "(önerilen)"
işareti soranındır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | "Düzeltme: 12.000 TL aslında müşavir ücreti DEĞİL; VUK 232 tevkifat eşiği (kayıtta 'mali müşavir teyidi şart'). Önceki sorumu yanlış yazmışım. 12.000 TL eşiğini onaylıyor musunuz?" | **Mali müşavirim 12.000 TL'yi teyit etti** | "Eşik 12.000 TL olarak ayarlanabilir değer girilsin ve açık karar kapatılsın" (seçilmeyenler: "Henüz teyit yok" — "Ayarlanabilir kalsın; canlıya çıkmadan önce mali müşavire sorulacak (önerilen)"; "Tutar farklı" — "Doğru tutarı 'Other' kısmına yazacağım") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D231 | VUK 232 tevkifat tutar eşiği | Eşik **12.000 TL (TRY)** olarak mali müşavir teyitlidir. Değer kodda sabit tutulmaz ve seed edilmez; SAT-D93'ün mevcut tarihli Ayarlar kaydıyla (vergi ana verisi, `RecordSettingsWithholdingThresholdV1` — tutar, para birimi, gerekçe, yürürlük tarihi) yetkili kullanıcı tarafından çalışma zamanı verisi olarak girilir. Yürürlük başlangıç tarihi girişte Ayarlar'da belirtilir; sonraki yılların değeri aynı kayıtla yeni tarihli değer olarak girilir. | Ayarlar vergi ana verisi (tevkifat eşiği kaydı); EK13 "Açık kalanlar"; #66 6726883926 madde 3 |

Kapatılan notlar:

- EK13 (`SINAN_KARARLARI_2026-10-28_SATIS_EK13.md`) "Açık kalanlar" içindeki **12.000 TL** kısmı. Aynı satırdaki
  5/10 oranları ve kodların mali müşavir/YMM teyidi bu belgeyle kapanmaz; açık kalır.
- Sinan'ın 8 Kasım kaydı (<https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6726883926>) madde 3'teki
  "mali müşavir teyidi go-live öncesi şart (açık)" notu.
- Eşiğin bir `OPEN-NNN` kimliği yoktur; bu belge hiçbir `OPEN-*` kaydını kapatmaz.

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, migration, seed veya Ayarlar verisi değiştirmez. Mevcut kayıt "boş
  başlar" (EK19 soru 3); değerin gerçek ortama girilmesi yetkili kullanıcının işidir.
- Yürürlük tarihi için bu belge bir tarih seçmez; araştırmadaki "2026 için" ifadesi değerin 2026 değeri olduğunu
  söyler, tarih girişte Ayarlar'da kaydedilir.
- EK44 (SAT-D230 belgesi) soru 2'nin "müşavir ücreti" etiketi yanlıştı; o belge eşiği kapatmamıştı. Kapatma bu belgededir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
