# Sinan Kararları — 2026-11-08 (Satış varsayılan rol/yetki taslağı; 12.000 TL kaydı)

Bu belge önceki karar dosyalarını silmez; SAT-D230'u ekler. Satış izinleri rol öneri tablosunu
(`docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md`, #993) Satış'ın **onaylı varsayılan rol/yetki seti** yapar.
SAT-D4, SAT-D6, SAT-D37, SAT-D67, SAT-D88, SAT-D106, SAT-D141, SAT-D168, SAT-D187, SAT-D188, SAT-D190,
SAT-D202, SAT-D204, SAT-D213, SAT-D216, SAT-D222 ve KLN-D17 geçerlidir; bu belge onları değiştirmez.

**Kaynak:** Sinan'ın 8 Kasım 2026 22:46Z'de koordinatör oturumunda (`session_015Usc2qbq4qsgayZcDY3UTb`)
AskUserQuestion ile verdiği doğrudan cevaplar; koordinatörün kaydı:
<https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6728217660>. Satış şeridi cevapları o oturumun
olay kaydından ayrıca okudu (soru, seçenekler, açıklamalar ve seçim aşağıda aynen). "(önerilen)" işareti soranındır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | "Rol yetkileri: Satış, Alış, Finans, Üretim için hazırladığım ÖNERİLEN varsayılan rol/yetki taslağını onaylayıp yetkileri vermemi ister misiniz? (Yetki verme sizin kararınız; vermeden canlıya çıkılamaz.)" | **Taslağı onaylıyorum** | "Önerilen varsayılan roller ve yetkiler geçerli olsun; ben vermiş sayılırım" (seçilmeyenler: "Taslağı önce göster" — "Öneriyi özet olarak göreyim, sonra karar vereyim (önerilen)"; "Şimdi değil" — "Yetki işi beklesin; kod geliştirme devam etsin") |
| 2 | "Müşavir (danışman) ücreti 12.000 TL: bu tutarı onaylıyor musunuz?" | **Onaylıyorum, 12.000 TL** | "Tutar sistemde ayarlanabilir değer olarak 12.000 TL girilsin" (seçilmeyenler: "Tutar farklı" — "Doğru tutarı 'Other' kısmına yazacağım"; "Henüz karar vermedim" — "Ayarlanabilir kalsın; canlıya çıkmadan önce sorulsun") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D230 | Satış varsayılan rol/yetki seti | `SATIS_ROL_YETKI_ONERISI_v1.md`'deki iki tablo (istenen dört izin + ilgili diğer Satış yazma/onay izinleri) **olduğu gibi** Satış'ın onaylı varsayılan rol/yetki setidir. "ÖNERİ" satırları ve "Sinan'ın seçeceği noktalar"ın öneri taşıyan maddeleri bu cevapla onaylanmıştır: (1) Yönetici = Genel Müdür + Admin, Satış Personeli = Satış Uzmanı; Satış Destek'e SAT-D168 izinleri verilmez. (2) `sales.invoices.finalize`: Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin; Satış Müdürü ve Satış Uzmanı verilmez. (5) `sales.orders.force-close`: Satış Müdürü, Genel Müdür, Admin; Satış Uzmanı verilmez. (6) `sales.orders.cancel`: Satış Uzmanı **verilmez**. (7) `sales.invoices.match`: Muhasebe tarafı, Genel Müdür, Admin; Satış verilmez. "Koşullu" hücrelerin koşulları (SAT-D216, SAT-D222, SAT-D67) komutta zorlandığı gibi kalır. | `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` satırlarının kayıtlı adlara eşlenmesi; ileride rol seed'i / grant uygulaması |

Belgenin durumu: `SATIS_ROL_YETKI_ONERISI_v1.md` başlığında "ÖNERİ" yazar; o belge yerinde değiştirilmez, onay bu
dosyadadır (eski belgeyi sessizce değiştirmemek için).

## Sınırlar

- **Grant uygulanmadı.** Bu belge hiçbir rolü, kullanıcıyı veya grant'ı açmaz; kod, manifest, seed veya grant
  değiştirmez. Seçilen seçeneğin açıklamasındaki "ben vermiş sayılırım" ifadesi varsayılan setin onayıdır; setin
  gerçek ortamda uygulanması (rol seed'i, kullanıcı ataması, grant'ın açılması) ve production aktivasyonu koordinatörün
  kaydında yazdığı gibi **ayrı insan kapısıdır**.
- **Kararlaştırılmayanlar** (taslakta öneri yoktu; cevapla seçilmiş sayılmaz):
  - Nokta 3 — kesinleştirmede görev ayrılığı (faturayı hazırlayan/onaylayan aynı faturayı kesinleştirebilsin mi).
    Bugün yalnız KLN-D17 geçerlidir.
  - Nokta 4 — `sales.invoices.finalize` Critical olsun mu. Kod Critical işaretlemez; Critical sınıfı insan kapısıdır.
  - Nokta 8 — gerçek kullanıcı atamaları ve grant'ların ne zaman açılacağı.
- **Kapsam:** Soru dört modülü kapsar; bu dosya yalnız Satış'ın taslağını (#993) kaydeder. Alış, Finans ve Üretim
  taslakları kendi şeritlerinin karar dosyalarında aynı cevaba atıfla kaydedilir.
- **Soru 2 (12.000 TL) — kayıt, kapatma değil.** Soru "müşavir (danışman) ücreti" diye soruldu. Repoda müşavir
  ücreti diye bir kayıt yoktur. 12.000 TL'nin repodaki tek karşılığı VUK 232 tevkifat tutar eşiğidir (EK13 "Açık
  kalanlar", EK19 soru 3: tarihli Ayarlar değeri, "sen veya mali müşavir girene kadar boş kalır"). Sinan'ın 8 Kasım
  kaydı (<https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6726883926>, madde 3) bu eşik için "mali
  müşavir teyidi go-live öncesi şart (açık)" der. Sorunun etiketi ile eşik farklı olduğu için bu belge eşik notunu
  kapatmaz; eşiğin bir `OPEN-NNN` kimliği de yoktur. Eşleşme Issue #66'ya `NEEDS-SINAN` olarak soruldu. Sinan
  "evet, VUK 232 eşiği, 12.000 TL ile kapansın" derse ayrı bir EK eşiği kapatır ve değer Ayarlar'da yapılandırılabilir
  tarihli değer olarak girilir (sabit kodlanmaz).
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
