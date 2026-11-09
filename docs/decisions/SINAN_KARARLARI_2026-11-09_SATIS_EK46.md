# Sinan Kararları — 2026-11-09 (Satış: dövizli faturada eşleştirme toleransı, eksi son taksit, teklif kayıp nedenleri ve geçerlilik, `sales.invoices.finalize` Kritik)

Bu belge önceki karar dosyalarını silmez; SAT-D232, SAT-D233, SAT-D234 ve SAT-D235'i ekler. Fatura ve teklif
dalgasında açık kalan dört soruyu cevaplar. SAT-D11, SAT-D25, SAT-D39, SAT-D89, SAT-D124, SAT-D172, SAT-D202,
SAT-D208, SAT-D209, EK42'deki SAT-D223…SAT-D226, EK43'teki SAT-D227…SAT-D229, EK44'teki SAT-D230 ve KLN-D17
geçerlidir; bu belge onların metnini değiştirmez (EK43'ün geri alınabilir bir uygulama ayrıntısının yerine geçen
seçim için "Bilinçli değişiklikler"e bakın).

**Kaynak:** Sinan'ın 9 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`)
Satış (Claude) şeridine sohbette verdiği cevaplar: dört soru. Cevaplar aşağıda **aynen** alıntılanır. "(Önerilen)"
işareti soranındır. Bu belgeyi yazan Satış ajanına yalnız Sinan'ın cevap metinleri ve kararın koordinatör özeti
iletildi; seçilmeyen seçeneklerin adları ve açıklamaları iletilmediği için tabloda yazılmadı (uydurulmadı).

## Soru ve cevaplar

| # | Soru | Cevap (aynen) | Kayda geçen anlam |
|---|---|---|---|
| 1 | Dövizli satış faturasında resmî belge eşleştirmesindeki 0,05 yuvarlama toleransı TL karşılığı üzerinden mi, fatura para birimi üzerinden mi ölçülsün? (EK43 "Sınırlar" `NEEDS-SINAN`) | "Mantıklı olan diğer erp lerde dahil hangisi ise tl karşılığı mantıklı gibi" | Tolerans **TL karşılığı** üzerinden ölçülür: 0,05 TL; ERP tarafının TL karşılığı faturanın kilitli kuruyla bulunur. |
| 2 | Taksitli dövizli faturada son taksidin TL tutarı, TL yuvarlaması yüzünden eksiye düşerse ne olsun? (README "SAT-D227 deviation" `NEEDS-SINAN`) | "Reddedilsin (Önerilen)" | Bugünkü fail-closed davranış onaylanır: kesinleştirme 422 ile reddedilir, hiçbir şey yazılmaz. |
| 3 | Teklif kayıp nedenleri listesi ve varsayılan teklif geçerlilik süresi ne olsun? (`SATIS_MODUL_BAGLANTILARI_v1.md` 4g, Sales README `NEEDS-SINAN`) | "Standart liste+15 gün seçeneği ve ayrıca ayarlardan ekleme yapılabilsin" | Standart kayıp nedeni listesi ve 15 gün varsayılan geçerlilik; yetkili kullanıcı Ayarlar'dan neden ekleyebilir. |
| 4 | `sales.invoices.finalize` Critical olsun mu; kesinleştirmede görev ayrılığı olsun mu? (`SATIS_ROL_YETKI_ONERISI_v1.md` "Sinan'ın seçeceği noktalar" 3 ve 4; EK44 "Kararlaştırılmayanlar") | "Kritik, ayrılık yok" | İzin **Critical** sınıfındadır; faturayı hazırlayan/onaylayan ile kesinleştiren arasında ek görev ayrılığı yoktur. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D232 | Dövizli faturada resmî belge eşleştirme toleransı | SAT-D229'un **0,05 TL** varsayılan toleransı dövizli faturaya da uygulanır ve **TL karşılığı** üzerinden ölçülür: ERP faturasının TL ödenecek tutarı (SAT-D225; faturanın kilitli kuru, SAT-D172/SAT-D226) ile resmî belgenin TL karşılığı arasındaki mutlak fark **0,05 TL'ye kadar (dahil)** kabul edilir; fazlası SAT-D89'a göre onaya gider. Fatura para birimi üzerinden ayrı bir tolerans yoktur. EK43 "Sınırlar"daki "dövizli faturada 0,05 TL uygulanmaz, eski varsayılan 0 kalır" geçici durumu bu kararla sona erer. Toleransın değeri SAT-D229 gibi Satış'ın kendi yapılandırma varsayılanıdır; Ayarlar'da düzenleme SAT-D209 ile ertelidir | Fatura eşleştirme (API-35), Kontrol Merkezi; EK43 "Sınırlar" `NEEDS-SINAN` |
| SAT-D233 | Taksitli dövizli faturada eksi son taksit | SAT-D227'ye göre son taksite eklenen fark son taksidin TL tutarını **eksiye** düşürürse faturanın kesinleştirilmesi **reddedilir**: 422 `sales_invoice_instalment_functional_invalid`, hiçbir şey yazılmaz, Finans'a devir olmaz (bugünkü uygulama). Fark eksi taksit olarak Finans'a taşınmaz; Kontrol Merkezi işaretiyle kesinleştirme yapılmaz. SAT-D227'nin TL hesaplama kuralı değişmez | Fatura kesinleştirme (API-36), `SalesInvoiceInstalmentCalculator`; DEC-SAL-008 |
| SAT-D234 | Teklif kayıp nedenleri ve varsayılan geçerlilik | (1) SAT-D25'in Ayarlar listesi standart **beş nedenle** başlar: **Fiyat yüksek**, **Termin uygun değil**, **Rakip seçildi**, **Müşteri vazgeçti**, **Diğer** (SAT-D25'e göre "Diğer"de açıklama zorunludur). (2) SAT-D39'un varsayılan teklif geçerlilik süresi **15 gün**dür; teklif oluşturulurken önerilir, teklifte değiştirilebilir, tarih zorunlu olmaz (SAT-D39 aynen). (3) Yetkili kullanıcı Ayarlar'dan listeye **neden ekleyebilir**. Bu üçüncü madde Ayarlar'da kalıcı kayıt ve bakım ekranı/API'si ister; SAT-D209 (Ayarlar'da yeni tablo/ekran açılmaz) buna şimdilik engeldir ve **aşılmaz**: ekleme yüzeyi "ertelendi: Ayarlar başlayınca" takip işidir. O zamana kadar liste ve süre mevcut gözden geçirilmiş kaynak kataloğunda (`ApprovedSalesQuotePolicyCatalog`) tutulur | Teklif kaybı (API-24), geçerlilik önerisi (API-22, UI-20), `ApprovedSalesQuotePolicyCatalog`; 4g |
| SAT-D235 | `sales.invoices.finalize` sınıfı ve görev ayrılığı | (1) `sales.invoices.finalize` **Critical** sınıfında bir izindir (grant'ı Critical sınıfın insan kapısı ve iki kişili verme kuralına tabidir). (2) Kesinleştirmede **görev ayrılığı yoktur**: faturayı hazırlayan, onaylayan veya eşleştiren kişi, izni varsa aynı faturayı kesinleştirebilir. KLN-D17 (kesinleştiren kişi kendi 120 fişini onaylayamaz) aynen geçerlidir. İzni alacak roller SAT-D230'daki gibidir (Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin). Gerçek grant/rol ataması ve Critical grant'ın açılması **insan kapısıdır**; bu belge grant açmaz | `SalesPermissionManifestV1` (izin sınıfı), yetki kataloğu; `SATIS_ROL_YETKI_ONERISI_v1.md` nokta 3 ve 4; EK44 "Kararlaştırılmayanlar" |

Uygulama ayrıntısı (geri alınabilir):

- SAT-D234'teki beş nedenin teknik kodları (ör. `price_too_high`, `delivery_not_suitable`, `competitor_chosen`,
  `customer_withdrew`, `other`), sırası ve UUIDv5 kimlikleri kataloğun mevcut kurallarıyla atanır; görünen Türkçe
  adlar yukarıdakilerdir.
- Liste ve süre, kataloğun mevcut şeklinde, Satış politika kaynağı olan şirketler (SAT-D14) için tek sürüm olarak
  girilir; yürürlük başlangıcı katalog girişinde belirtilir.

## Kapatılan açık sorular

- EK43 (`SINAN_KARARLARI_2026-11-07_SATIS_EK43.md`) "Sınırlar" — "Dövizli faturada tolerans para birimi —
  `NEEDS-SINAN`" (SAT-D232).
- `src/backend/Modules/Sales/README.md` "SAT-D227 deviation" satırındaki `NEEDS-SINAN: confirm the fail-closed
  reading or specify the flag` (SAT-D233; fail-closed okuma onaylandı).
- `docs/handoffs/SATIS_MODUL_BAGLANTILARI_v1.md` 4g satırları ve Sales README'deki kayıp nedenleri / varsayılan
  geçerlilik verisi `NEEDS-SINAN`'ı (SAT-D234 (1)–(2)); 4g'nin "(b) kalıcı kayıt + bakım ekranı — ertelendi: Ayarlar
  başlayınca" kısmı açık kalır (SAT-D234 (3)).
- `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md` "Sinan'ın seçeceği noktalar" nokta 3 ve nokta 4 ile EK44 "Sınırlar →
  Kararlaştırılmayanlar"daki aynı iki madde (SAT-D235). Nokta 8 (gerçek kullanıcı atamaları, grant zamanı) açık kalır.
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; yukarıdaki soruların `OPEN-NNN` kimliği yoktur.

## Bilinçli değişiklikler

- **EK43 "Uygulama ayrıntısı (geri alınabilir)" birinci madde:** "son taksit TL tutarının işaretini çevirecek veya
  tutarını aşacaksa fatura kesinleştirme reddedilmez, … Kontrol Merkezi'nde işaretlenir" ayrıntısı, **eksi son taksit
  için** SAT-D233 ile yerini reddetmeye bırakır. EK43'ün metni yerinde değiştirilmez; SAT-D227'nin kendisi değişmez.
- **EK43 "Sınırlar"daki dövizli fatura toleransı:** dövizli faturada eski varsayılan 0'ın sürmesi geçiciydi;
  SAT-D232 ile 0,05 TL TL karşılığında uygulanır. SAT-D89 ve SAT-D229'un geri kalanı aynen geçerlidir.
- **Sales manifest notu:** kodda `sales.invoices.finalize` bugün "not Critical" kayıtlıdır; SAT-D235 bu sınıfı
  Critical yapar (uygulama takip işi, aşağıda).
- Başka hiçbir kararın anlamı değişmez.

## Uygulama takip listesi

| Karar | Açtığı kod/doküman işi | Şerit |
|---|---|---|
| SAT-D232 | Eşleştirmenin dövizli faturada TL karşılığı üzerinden 0,05 TL toleransla çalıştığını doğrulamak (`SalesInvoice.RecordMatch` farkı zaten TL karşılıkları üzerinden hesaplar — `PayableFunctionalAmount`); dövizli fatura için birim + gerçek-SQL kanıtı (sınır 0,05 dahil, 0,06 onaya gider); Sales README "Open points" satırından "which invoice amount is compared" ve dövizli tolerans notunun düşürülmesi. | Satış (backend) |
| SAT-D233 | Kod değişmez (`SalesInvoiceInstalmentCalculator` eksi son taksitte 422). README "SAT-D227 deviation" satırını "SAT-D233 ile onaylı" olarak güncellemek; ekranda (`SalesInvoiceFinalization.tsx`) 422'nin Türkçe açıklamasının kalması. | Satış (backend README, frontend kontrol) |
| SAT-D234 | `ApprovedSalesQuotePolicyCatalog`'a (Ayarlar uygulama katmanı, mevcut statik katalog — yeni tablo/ekran değil) beş nedenli liste ve 15 günlük geçerlilik sürümünü girmek; katalog testleri, teklif kaybı (API-24) ve `validity-suggestion` (API-22) kabul testleri; 4g satırını 🟢 veri / 🟡 bakım yüzeyi olarak güncellemek. Ayarlar'dan neden ekleme yüzeyi (kalıcı kayıt + API + ekran): **ertelendi: Ayarlar başlayınca** (SAT-D209). | Satış/Claude (katalog verisi); Ayarlar (bakım yüzeyi, ertelendi) |
| SAT-D235 | `SalesPermissionManifestV1`'de `sales.invoices.finalize` sınıfının Critical işaretlenmesi (permission-capability-registration akışı; manifest/shell sürümü, yetki kataloğu testleri). Görev ayrılığı kontrolü **eklenmez** (kod bugün eklemiyor). Grant/rol ataması yapılmaz (insan kapısı). K3 (permission/rol). | Satış (backend) |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration, seed veya Ayarlar verisi değiştirmez;
  yukarıdaki takip işleri ayrı PR'lardır.
- SAT-D233 yalnız **eksi** son taksidi kapsar. Son taksidin TL tutarının **tam sıfır** çıkması (hesaplayıcı bugün
  reddetmez; Finans açık kalem kabulü yalnız pozitif tutar alır) Sinan'ın cevabında yoktur ve burada seçilmez
  (`NEEDS-SINAN`).
- SAT-D234 (3) Ayarlar'da yeni kayıt/ekran açmaz; SAT-D209 aynen geçerlidir.
- SAT-D235 Critical sınıfı ve görev ayrılığı hakkındadır; kimin hangi role bağlanacağı ve grant'ların ne zaman açılacağı
  (SAT-D230 nokta 8) açık kalır.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
