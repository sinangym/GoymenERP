# Sinan Kararları — 2026-11-10 (Satış: API-40 / UI-70b müşteri iade faturası — kapsam, tutarlar, kayıt ve onay, alacak etkisi)

Bu belge önceki karar dosyalarını silmez; SAT-D288…SAT-D291'i ekler. Delegasyon No 21 ve No 38
(`SINAN_KARARLARI_2026-10-21.md`), DEC-FIN-003, DEC-SAL-007, AT-028 (Master rapor), SAT-D8
(`SINAN_KARARLARI_2026-10-25_SATIS.md`), SAT-D33 (EK3), SAT-D88, SAT-D89, SAT-D96, SAT-D99 (EK13), SAT-D124 (EK18),
SAT-D172 (EK24), SAT-D202 (EK32), SAT-D204 (EK33), SAT-D208 (EK35), SAT-D229 (EK43),
SAT-D230 (EK44), SAT-D232 (EK46) ve SAT-D241 (EK48) geçerlidir. Delegasyon No 21'in "sevk bağı" ifadesi SAT-D288 ile
daraltılır (bkz. "Bilinçli değişiklikler"); diğerlerinin karar metni değişmez. `KARAR_SICILI` DEC-FIN-003 satırının yalnız
`KabulKaniti` sütununa EK57 referansı eklenir.

**Kaynak:**

- Sinan'ın 10 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine,
  API-40 (müşteri iade kaydı) ve UI-70b (iade kaydı ekranı) başlamadan önce sorulan dört soruya verdiği cevaplar.
- Soru 1'in cevabı serbest metindir ve aynen alınmıştır: "İade faturasında sevkte olabilir, olmayadabilir ama bunu karşı
  firma kesecek zaten, bizim keseceğimiz iadeler alış modülünden olabilir".
- Soru 2–4 seçenekli soruldu; Sinan her birinde "(Önerilen)" işaretli seçeneği seçti. Seçilen etiket tırnak içinde,
  seçeneğin oturumdaki açıklaması ardından yazılmıştır; seçilmeyen seçenekler tabloda listelenir.
- Cevaplar ve EK57-1…3 `NEEDS-SINAN` satırları Issue #66'ya ayrıca yazılır; merge makbuzu merge eden tarafından yazılır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilmeyen seçenek(ler) | Kayda geçen anlam |
|---|---|---|---|---|
| 1 | Müşteri iade faturası kaydında (API-40) sevk / fiziksel iade bağı zorunlu mu; ERP müşteriye iade faturası keser mi? | "İade faturasında sevkte olabilir, olmayadabilir ama bunu karşı firma kesecek zaten, bizim keseceğimiz iadeler alış modülünden olabilir" | "Fiziksel + sevksiz (Önerilen)" (onaylı fiziksel iade + faturalanmış/sevk edilmemiş bakiye; fiyat farkı ayrı belge); "Yalnız fiziksel iade"; "Hepsi + fiyat farkı" — Sinan seçenek yerine serbest metinle cevapladı | API-40, **müşterinin kestiği** iade faturasını kaydeder; sevk / fiziksel iade bağı **isteğe bağlıdır**, orijinal satış faturası bağı zorunlu kalır. ERP müşteri iade faturası **kesmez**. Bizim (tedarikçiye) keseceğimiz iade faturaları **Alış** modülünün konusudur, Satış'ın değil. |
| 2 | İade faturasının tutarları nasıl hesaplanır; kısmi iadede kuruş kalıntısı ne olur? | "Orijinalden hesapla (Önerilen)": Orijinal fatura satırından hesaplanır; birim fiyat, iskonto, KDV/tevkifat oranı ve kur kopyalanır; TL tutar orantılıdır; son iade kalanı kapatır; müşterinin resmî iade faturası SAT-D229/SAT-D241 toleransıyla eşleştirilir, fark onaya gider | "Müşteri tutarı girilir" (müşteri faturasındaki tutarlar girilir, ERP yalnız orijinali aşmamayı denetler); "İade günü kuru" (fiyat/vergi orijinalden, kur iade gününden, fark kur farkı) | SAT-D289 ilkesi kayda geçer; kuruş yuvarlama yöntemi ve SAT-D11/SAT-D223/SAT-D241 etkileşimi açık kalır (EK57-1). |
| 3 | İade faturasını kim kaydeder; akış nedir? | "Muhasebe, normal akış (Önerilen)": Muhasebe kaydeder; satış faturasıyla aynı akış: onaya gönder → onay (hazırlayan onaylayamaz) → kesinleştir; rol seti SAT-D202/SAT-D230 ile aynı | "Satış kaydeder" (Satış kaydeder, Muhasebe onaylar); "Onaysız kesinleşir" (Muhasebe kaydeder, ayrı onay olmadan kesinleşir) | Kayıt ve onay satış faturası akışını izler (SAT-D290). Rol ataması Ayarlar'da insan kapısıdır (`OPEN-013`); kodda grant yoktur. |
| 4 | İade faturası alacağı nasıl etkiler; fatura tahsil edilmişse ne olur? | "Ters kalemle düş (Önerilen)": Orijinal faturanın açık kalemi ayrı bir ters kalemle azaltılır (orijinal kalem değişmez); tahsil edilmişse fark müşteri alacağı (avans) olarak kalır | "Ayrı açık kalem" (müşteri bazında bağımsız alacak kalemi, sonra elle eşleştirme); "Otomatik geri ödeme" (tahsil edilmiş faturada otomatik geri ödeme talebi) | SAT-D96/SAT-D208 ve Finans API-11 ile uyumlu ters kayıt (SAT-D291). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D288 | API-40 kapsamı ve belge sahibi | (1) API-40, **müşterinin kestiği** iade faturasını ayrı belge olarak kaydeder (DEC-FIN-003; delegasyon No 21). ERP müşteri iade faturası **kesmez**. (2) **Orijinal satış faturası bağı zorunludur** (AT-028). (3) **Sevk / fiziksel iade bağı isteğe bağlıdır:** iade faturası bir sevke veya fiziksel iadeye bağlı olabilir ya da olmayabilir (soru 1). Sevke bağlı iadede sevk payı defterinde "iade edilen" düşümü delegasyon No 21 ve No 38'e göre aynen uygulanır; sevke bağlı olmayan iade sevk payını değiştirmez. (4) Bizim tedarikçiye keseceğimiz iade faturaları **Alış** modülünün konusudur; Satış'ta bu belge açılmaz. (5) Fiyat farkı faturası (SAT-D96, S6'da ayrı belge) bu kararla değişmez ve API-40'a taşınmaz. (6) Ayrı menü yoktur (SAT-D8, DEC-SAL-007 aynen); iade sipariş ve fatura üzerinde görünür | API-40 `POST customer-returns` · `GET customer-returns/{id}`, UI-70b; `SATIS_HAZIRLIK_PLANI_v1.md` API-40 satırı |
| SAT-D289 | İade faturası tutarları | (1) İade satırı **orijinal fatura satırından** hesaplanır: birim fiyat, iskonto, KDV oranı, tevkifat oranı ve kur (SAT-D172 kilitli fatura kuru) orijinal satırdan **kopyalanır**. (2) İlke (soru 2'de seçilen seçeneğin metni): iade satırının TL tutarı orijinal satırın TL tutarıyla iade miktarına **orantılıdır** ve bir satırın **son iadesi kalanı kapatır**. (3) **Karar verilmedi (`NEEDS-SINAN` EK57-1):** orantılı TL tutarın kuruş yuvarlama yöntemi, son iadede kapatılan kalıntının satır mı belge mi düzeyinde hesaplanacağı ve bunun SAT-D11, SAT-D223 ve SAT-D241 yuvarlama/eşleştirme kurallarıyla etkileşimi. Bu noktalar cevaplanana kadar calculator yuvarlama varsayımı etkinleştirmez; para/formül K3 uygulaması bu cevabı bekler. (4) Müşterinin resmî iade faturası ERP kaydıyla SAT-D229 toleransıyla (dövizli faturada SAT-D232: TL karşılığı üzerinden), tutar, KDV ve tevkifat ayrı ayrı (SAT-D241) eşleştirilir; tolerans aşılırsa fark **onaya gider**, resmî değer esastır (SAT-D89 aynen). (5) Bu karar yeni eşik veya tolerans getirmez; mevcut eşik ve hesap kuralları backend calculator'da uygulanır, ekranda yeniden yazılmaz | API-40 tutar hesabı (Satış backend), UI-70b; EK42 "Kapsam dışı" maddesi |
| SAT-D290 | Kaydı yapan ve onay akışı | (1) İade faturasını **Muhasebe** kaydeder. (2) Akış satış faturasıyla aynıdır: **onaya gönder → onay → kesinleştir**. (3) **Hazırlayan onaylayamaz** (SAT-D88, SAT-D204 kişi düzeyinde aynen). (4) Kaydetme, onaylama ve kesinleştirme rol seti satış faturasının rol setiyle aynıdır (SAT-D202, SAT-D230). (5) Kodda grant, rol tanımı veya seed **yoktur**; izinler kurulumda Ayarlar rol ekranından atanır (EK55 SAT-D282 (4), EK56 SAT-D287 yolu). Ayarlar rol ekranı gelene kadar E4 / `OPEN-013` gereği bekler. Bu belge grant açmaz | API-40 komutları, UI-70b; `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` |
| SAT-D291 | Alacak etkisi | (1) Kesinleşen iade faturası, orijinal faturanın Finans'taki açık kalemini **ayrı bir ters kalemle** azaltır; **orijinal kalem değiştirilmez** (SAT-D96, SAT-D208; silme yok). (2) İlke (soru 4'te seçilen seçeneğin metni): orijinal fatura **tahsil edilmişse**, fark müşterinin **alacağı (avans)** olarak kalır. **Karar verilmedi (`NEEDS-SINAN` EK57-3):** kısmen tahsil edilmiş faturada iade tutarının açık kalem azaltması ile avans arasında nasıl bölüneceği ve avansın Finans'ta hangi kayıtla, kim tarafından oluşturulacağı; Finans API-11 sözleşmesi bu cevabı bekler. (3) Cari kartına ayrı hareket yazılmaz (SAT-D208 aynen). (4) Satış, Finans tablosuna yazmaz; etki Finans'ın iptal/iade ters kayıt portu (Finans API-11) üzerinden olur | API-40 kesinleştirme olayı; Finans API-11 (`FINANS_HAZIRLIK_PLANI_v1.md`); Muhasebe ters fiş tüketicisi |

Uygulama ayrıntısı (geri alınabilir):

- **SAT-D288:** Sevk bağı istek gövdesinde isteğe bağlı bir alan olarak taşınır; verilirse sevkin orijinal faturaya bağlı
  olduğu doğrulanır. Fiziksel stok hareketi API-40'ta yazılmaz: fiziksel iade Lojistik onayıyla zaten stok hareketi üretir
  ve aynı fiziksel olay ikinci kez kaydedilmez (AGENTS.md "Mimari değişmezler").
- **SAT-D289:** "Son iade" orijinal satırın kalan iade edilebilir miktarını sıfırlayan iadedir.
- **SAT-D290:** Yeni izin adları (ör. bugünkü plan satırındaki `sales.returns.write` ve onay/kesinleştirme izinleri) uygulama
  PR'ında manifest'e eklenir, hiçbir role atanmaz (permission-capability kaydı).

## Kapatılan açık sorular

- EK42 "Kapsam dışı / sonraki karar" — kısmi iade faturasında orijinal satır TL'lerinin kopyalanması ve kuruş kalıntısı —
  SAT-D289 yalnız **ilke düzeyinde** (oran ve kur kopyalanır, TL orantılı, son iade kalanı kapatır). Kuruş yuvarlama
  yöntemi ve SAT-D11/SAT-D223/SAT-D241 etkileşimi **açık kalır** (`NEEDS-SINAN` EK57-1). İptal faturası ve taksitlerin TL karşılığı
  (SAT-D124 ilişkisi) bu belgede kararlaştırılmadı; EK42'deki ilgili kısım açık kalır.
- API-40'ta sevk bağının zorunluluğu — SAT-D288 (isteğe bağlı; orijinal fatura bağı zorunlu).
- İade faturasının kaydı ve onayı — SAT-D290. Alacak etkisi — SAT-D291.
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur; rol atama ekranının
  yokluğu (`OPEN-013`, kurulum rehberi E4) açık kalır.

## Bilinçli değişiklikler

- **Delegasyon No 21:** "orijinal satış faturası + sevk bağıyla" ifadesindeki sevk bağı SAT-D288 (3) ile **isteğe bağlı**
  olur; orijinal fatura bağı, ayrı belge olma ve "ters kayıt + yeni fatura" ile düzeltilmeme kuralı aynen kalır.
  `SATIS_HAZIRLIK_PLANI_v1.md` API-40 satırındaki "sevk bağı zorunlu" ifadesi ayrı doküman adımında buna çekilir.
- **Delegasyon No 38, SAT-D33:** metni değişmez; yalnız sevke bağlı iadeye uygulanır.
- **SAT-D96:** metni değişmez; fiyat farkı faturası S6'da ayrı belge olarak kalır.
- **SAT-D99:** metni değişmez; "faturalanmış, sevk edilmemiş" bakiyenin kapatma yolu mali müşavirle belirlenir. Sevke
  bağlı olmayan bir iade faturasının (SAT-D288 (3)) bu bakiyeyi azaltıp azaltmadığı bu belgede **karar verilmedi**
  (`NEEDS-SINAN` EK57-2).
- **SAT-D202, SAT-D230, SAT-D88, SAT-D204:** metni değişmez; SAT-D290 aynı rol setini ve hazırlayan-onaylayamaz kuralını
  iade faturasına uygular.
- Başka hiçbir kararın anlamı değişmez.

## Açık kalanlar (`NEEDS-SINAN`)

Aşağıdaki üç nokta kaynaklı kural değildir; Issue #66'ya `NEEDS-SINAN` satırı olarak yazılır ve cevaplanana kadar
uygulamada varsayımla etkinleştirilmez:

| Kimlik | Soru | Bekleyen iş |
|---|---|---|
| EK57-1 | Orantılı TL tutarın kuruş yuvarlama yöntemi, son iade kalıntısının satır/belge düzeyi ve SAT-D11/SAT-D223/SAT-D241 ile etkileşimi | SAT-D289 calculator (para/formül K3) |
| EK57-2 | Sevke bağlı olmayan iade faturası SAT-D99 "faturalanmış, sevk edilmemiş" bakiyeyi azaltır mı; azaltırsa hangi kayıtla kapanır | SAT-D288 (3) sevk payı / bakiye hesabı |
| EK57-3 | Kısmen tahsil edilmiş faturada iade tutarının açık kalem ile avans arasında bölünmesi; avansın Finans'ta oluşum yolu ve sahibi | SAT-D291 (2), Finans API-11 sözleşmesi |

## Uygulama listesi

| Karar | İş | Şerit / Kapı |
|---|---|---|
| SAT-D288 | API-40 komut ve okuma uçları (sevk bağı isteğe bağlı), `SATIS_HAZIRLIK_PLANI_v1.md` API-40 satırının güncellenmesi; UI-70b sevk alanının isteğe bağlı gösterilmesi. | Codex (backend), Claude (UI-70b), doküman; K3 |
| SAT-D289 | Orijinal satırdan kopyalama, orantılı TL ve son iade kalan kapatma hesabı (backend calculator; yuvarlama EK57-1 cevabını bekler), resmî iade faturası eşleştirmesi ve testler; ön yüz yalnız backend sonucunu gösterir. | Codex; para/formül K3 |
| SAT-D290 | Onaya gönder / onay / kesinleştir komutları (expected version, idempotency, audit), izin adları manifest'te (atanmaz). | Codex + Claude (UI-70b); permission K3 |
| SAT-D291 | Kesinleştirme olayı ve Finans API-11 ters kalem portu üzerinden alacak azaltma; Muhasebe ters fiş tüketicisi. | Codex (Satış + Finans), Muhasebe; cross-module contract K3 |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; grant açmaz, rol atamaz.
- **Bağımlılıklar (bu belgede karar verilmedi):** Lojistik okuma portunun iade kimliği/durumu ile genişletilmesi; Finans
  API-11 iptal/iade ters kayıt portu; Muhasebe B-04 ters fiş tüketicisi. Sözleşmeleri ilgili şeritlerde ve K3 kapısında
  belirlenir; Satış bu modüllerin tablolarına yazmaz.
- **Açık kalan (karar verilmedi):** İptal faturası ve taksitlerin TL karşılığı (EK42; SAT-D124 ilişkisi). Fiziksel iade
  olmadan fiyat farkı / kalite iskontosu iadesinin API-40 ile mi SAT-D96'daki fiyat farkı belgesiyle mi kaydedileceği
  bu belgede seçilmez; SAT-D288 yalnız sevk bağının isteğe bağlı olduğunu söyler (gerekirse `NEEDS-SINAN`).
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; production aktivasyonu yapmaz; yeni izin adı açmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
