# Sinan Kararları — 2026-11-10 (Satış: EK57 açık kalanları — iade faturası kuruş yuvarlaması, sevksiz iade ve bakiye, kısmi tahsilde avans)

Bu belge önceki karar dosyalarını silmez; SAT-D292…SAT-D294'ü ekler ve EK57'nin "Açık kalanlar" tablosundaki EK57-1,
EK57-2 ve EK57-3 satırlarını kapatır. SAT-D11 (`SINAN_KARARLARI_2026-10-25_SATIS.md`), SAT-D76 (EK12), SAT-D99 (EK13),
SAT-D223 (EK42), SAT-D241 (EK48) ve SAT-D288…SAT-D291 (EK57) geçerlidir; karar metinleri değişmez. `KARAR_SICILI`
değişmez.

**Kaynak:**

- Koordinatör oturumunun Issue #66'ya Sinan adına yazdığı cevaplar (yorum 6779250878, 10 Kasım 2026).
- Sinan'ın aynı gün Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine verdiği
  doğrudan teyit; Issue #66 yorumu 6779294551. İki soru seçenekli soruldu; Sinan her ikisinde "(Önerilen)" işaretli
  seçeneği seçti.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilmeyen seçenek(ler) | Kayda geçen anlam |
|---|---|---|---|---|
| 1 | Kısmi iade faturasında kuruş yuvarlaması: iade TL tutarı her satırda orijinal satırla orantılı hesaplansın, kalan kuruş o satırın son iadesinde kapansın mı? (EK57-1) | "Satır bazlı (Önerilen)": Her satır kendi içinde orantılı yuvarlanır; satırın son iadesi kalan kuruşu kapatır. Mevcut fatura yuvarlama kurallarıyla tutarlı | "Belge bazlı" (belge toplamı orantılı hesaplanır, kuruş farkı belgenin son satırına yazılır) | SAT-D292. Koordinatör kaydında Sinan ilk cevapta seçimi devretmişti ("Mantıklı olan hangisi ise"); bu soru o devri Sinan'ın açık seçimine çevirir. |
| 2 | Koordinatörün #66'ya yazdığı iki cevap doğru mu? (a) Sevke bağlı olmayan iade faturası "faturalanmış, sevk edilmemiş" bakiyeyi düşürür, stok değişmez. (b) Kısmen tahsil edilmiş faturada iade önce açık alacağı kapatır, fazlası avans olur; avansı Finans (muhasebe) kaydeder. (EK57-2, EK57-3) | "Evet, ikisi doğru (Önerilen)" | "Hayır, düzeltilecek" | SAT-D293 ve SAT-D294. Koordinatör kaydındaki Sinan ifadesi: "Bakiyeyi düşürür, stok değişmez; fatura muhasebesel yönden bakiyeyi etkileyen bir işlemdir." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D292 | İade faturası kuruş yuvarlaması (EK57-1) | (1) Hesap **satır bazlıdır**: her iade satırının tutarları (fatura para birimindeki satır tutarı, matrah, KDV ve tevkifat ile bunların TL karşılıkları) orijinal fatura satırının aynı tutarından iade miktarı / orijinal miktar oranıyla hesaplanır ve SAT-D11 kuralıyla (2 ondalık, yarı-yukarı, `AwayFromZero`) kuruşa yuvarlanır. (2) Bir satırın **son iadesi** (orijinal satırın kalan iade edilebilir miktarını sıfırlayan iade) oranla hesaplanmaz: her tutar türü için orijinal satır tutarından önceki iadelerin toplamı düşülerek yazılır; böylece satırın kuruş kalıntısı o satırda kapanır. (3) Belge toplamları iade satırlarının toplamıdır; belge düzeyinde ayrı yuvarlama farkı satırı veya hesabı yoktur. (4) Hesap yalnız backend calculator'dadır; ekran sonucu gösterir | API-40 tutar hesabı (Satış backend), UI-70b |
| SAT-D293 | Sevksiz iade faturası ve "faturalanmış, sevk edilmemiş" bakiye (EK57-2) | (1) Sevke bağlı olmayan iade faturası (SAT-D288 (3)) kesinleştiğinde orijinal faturanın SAT-D99 "faturalanmış, sevk edilmemiş" bakiyesini iade miktarı kadar **azaltır**. (2) **Stok hareketi üretmez** (API-40 stok hareketi yazmaz; SAT-D288 uygulama ayrıntısı aynen). | API-40 kesinleştirme, SAT-D99 bakiye görünümü (API-37, UI-64) |
| SAT-D294 | Kısmen tahsil edilmiş faturada iade ve avans (EK57-3) | (1) Kesinleşen iade tutarı önce orijinal faturanın Finans'taki **açık kalemini** ters kalemle azaltır (SAT-D291 (1)). (2) İade tutarı açık kalemi aşarsa **fazlası müşteri avansıdır**. (3) Avansı **Finans (muhasebe)** kaydeder; Satış avans kaydı oluşturmaz ve Finans tablosuna yazmaz, iade tutarını Finans API-11 ters kayıt portuna bildirir (SAT-D291 (4)). (4) Açık kalem / avans ayrımını Finans yapar; Satış yalnız iade tutarını ve orijinal fatura bağını bildirir | API-40 kesinleştirme olayı, Finans API-11 (`FINANS_HAZIRLIK_PLANI_v1.md`) |

Uygulama ayrıntısı (geri alınabilir):

- **SAT-D292:** Taban, orijinal satırın kayıtlı tutarıdır; SAT-D223 grup farkı eklenmiş son satır dahil orijinal satır TL'leri
  olduğu gibi kullanılır ve iade belgesinde SAT-D223 gruplaması yeniden çalıştırılmaz (aksi halde iadeler toplamı orijinal
  satır toplamından kuruş sapardı).
- **SAT-D293:** Bakiye azaltımı Satış'ın kendi kaydıdır; Lojistik veya Stok tablosuna yazılmaz. İade miktarı satırın
  faturalanmış-sevk edilmemiş bakiyesini aşarsa ne olacağı bu belgede seçilmez; uygulama bu durumda kaydı reddeder
  (fail-closed, bakiye eksiye inmez). Farklı bir davranış gerekirse `NEEDS-SINAN` ile sorulur.
- **SAT-D294:** Finans API-11 portu yoksa API-40 kesinleştirmesi alacak etkisini sessizce atlamaz; port bağlanana kadar
  kesinleştirme fail-closed kalır. Port sözleşmesi cross-module contract K3 kapısında bağlanır.

## Kapatılan açık sorular

- EK57-1 — SAT-D292. EK57-2 — SAT-D293. EK57-3 — SAT-D294.
- EK42 "Kapsam dışı / sonraki karar" maddesinin kısmi iade faturası kuruş kalıntısı kısmı SAT-D289 + SAT-D292 ile
  kapanır. İptal faturası ve taksitlerin TL karşılığı (SAT-D124 ilişkisi) açık kalır.
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.**

## Bilinçli değişiklikler

- **SAT-D289 (3), SAT-D291 (2):** EK57'deki "karar verilmedi" kayıtları SAT-D292 ve SAT-D294 ile tamamlanır.
- **SAT-D99:** metni değişmez; kapatma yollarından "alıcının iade faturası" sevksiz iade için SAT-D293 ile tanımlanır.
- Başka hiçbir kararın anlamı değişmez.

## Uygulama listesi

| Karar | İş | Şerit / Kapı |
|---|---|---|
| SAT-D292 | Satır bazlı orantı + son iade kalan kapatma calculator'ı ve birim testleri (kuruş kalıntısı, çok iadeli satır, dövizli fatura). | Claude (Satış backend); para/formül K3 |
| SAT-D293 | Sevksiz iade kesinleşmesinde faturalanmış-sevk edilmemiş bakiye azaltımı ve aşım reddi. | Claude (Satış backend); K3 |
| SAT-D294 | Finans API-11 portu üzerinden açık kalem azaltma / avans bildirimi. | Satış + Finans; cross-module contract K3 |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi değiştirmez; grant
  açmaz.
- Finans API-11 ve Lojistik sevk tüketim/iptal portu bu belgede tasarlanmaz; ilgili şeritlerde açılır.
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; production aktivasyonu yapmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
