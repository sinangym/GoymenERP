# Sinan Kararları — 2026-11-12, 21. tur madde 2–3 23. tur madde 1–2 ve 25. tur madde 1 (Stok: gelecek tedarik ayırmasının satış iptali/azaltmasında bırakılması, bırakma sırası ve fiziksel tahsis fazlası, canlanan alış satırında bırakılmış ayırmalar)

Bu belge önceki karar dosyalarını silmez; **KLN-D28, KLN-D29, KLN-D30, KLN-D31 ve KLN-D32**'yi ekler. F-017 Stok spec'inin
(`docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md`, PR #1116) §9.1'deki adayları (21. turda 1–2, 23. turda 3'ün
bırakma sırası ve 4, 25. turda gelecek ayırma yetmediğinde fiziksel tahsis fazlası) Sinan kararına bağlar. `SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md` (KLN-D25…KLN-D27, SAT-D308; KLN-D26 F-017'nin
Stok tarafı), Alış eşleri ALS-D82…ALS-D93 (`SINAN_KARARLARI_2026-11-12_ALIS_EK17.md`, PR #1110; `main` `4f3978d3`),
DEC-STK-002/003/004/005/006, DEC-SAL-004 ve DEC-SAL-005 geçerlidir. Bu belge onların karar metnini değiştirmez; KLN-D28,
DEC-SAL-005'in "önizleme olmadan iptal yok" kuralını kaldırmaz (önizleme gelecek ayırmayı gösterir, bırakma onaylanan
iptalin parçası olur). 11-12 Stok karar dosyası (#1121) `main`dedir (`e165aaee`); bu belge o dosyayı düzenlemez.
`KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.

**Kimlikler:** KLN-D serisinde `main`deki (`c93433f6`, 2026-11-12) son kimlik KLN-D27'dir (#1121 merge edildi).
`git grep -ohE "KLN-D(2[89]|3[0-9])" origin/main` boştur. Açık PR dallarının `origin/main...head` eklemelerinde KLN-D28…KLN-D32
başka bir tanımla geçmez; #1119 (`main` `c93433f6`) KLN-D28, KLN-D30 ve KLN-D31'e yalnız atıf yapar, KLN-D32+ hiçbir açık dalda
yoktur (yazar taraması, 2026-11-12, bütün açık PR'lar). Gelecek tedarik ayırması Kalan Modüller (Stok) şeridinin işi olduğu
için KLN-D kullanıldı; yeni ön ek icat edilmedi.

**Kaynak:**

- Sinan'ın 12 Kasım 2026'da koordinatör oturumunda `AskUserQuestion` ile verdiği cevaplar; koordinatörün Issue #66'ya
  yazdığı kayıt, yorum [6822767886](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822767886)
  ("SİNAN KARARI — 21. tur (2026-11-12, AskUserQuestion; koordinatör kaydı)"). Madde 1 (fason hizmet siparişinin
  tartıdan önceki risk tutarı) Satış/Lojistik şeridinindir; bu belgeye alınmadı. Bu belgeye alınan maddeler (aynen):

  > 2. **Stok/Satış — gelecek tedarikten ayrılmış satış satırı iptal/azaltılınca** (#1116 §9.1 aday 1):
  >    Sinan'ın cevabı (birebir): "Otomatik bırakılır (Önerilen)"
  >    Anlamı: Satış iptal/azaltma komutu aynı işlemde Stok bırakma portunu çağırır; artan ayırma kalmaz; audit'li. (Seçilmeyen: Satış reddeder, önce elle bırak.)
  >
  > 3. **Alış/Stok — canlanan alış satırında önceden bırakılmış ayırmalar** (#1116 §9.1 aday 2, #1110 §3.8):
  >    Sinan'ın cevabı (birebir): "Geri gelmez, yeniden ayrılır (Önerilen)"
  >    Anlamı: canlanan miktar serbest açık miktar olur; eski ayırmalar kendiliğinden dönmez, gerekiyorsa kullanıcı yeniden ayırır; sistem "canlandı" bildirimi gösterir.

- Sinan'ın 12 Kasım 2026'da koordinatör oturumunda verdiği 23. tur cevapları; koordinatör kaydı, Issue #66 yorumu
  [6823243819](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823243819). Madde 3 (kimlik) bu belgeye
  alınmadı. Bu belgeye alınan maddeler (aynen):

  > 1. Soru: "Satış satırının miktarı azaltılınca, bırakılacak gelecek tedarik ayırması birden çok alış siparişine yayılmışsa hangisi önce bırakılsın? (Ör. 30 t'lik satır 20 t'ye iniyor; 10 t'si 15 Aralık'ta, 20 t'si 5 Ocak'ta gelecek alıştan ayrılmış.)"
  >    Sinan'ın cevabı (birebir): "En geç gelen önce (Önerilen)"
  >    Anlamı: sistem en geç gelecek alıştan bırakır, erken geleni müşteriye tutar; kullanıcı isterse komutta başka ayırma seçebilir. (Seçilmeyenler: "En son yapılan önce"; "Her seferinde kullanıcı seçer".)
  >
  > 2. Soru: "Satış satırı azaltılınca satırda hem depodaki etiketli mala yapılmış tahsis hem de gelecek alıştan ayırma varsa önce hangisi bırakılsın?"
  >    Sinan'ın cevabı (birebir): "Önce gelecek ayırma (Önerilen)"
  >    Anlamı: depodaki hazır mal müşteride kalır, sevke hazırdır; henüz gelmemiş alış ayırması önce bırakılır; kullanıcı isterse değiştirebilir. (Seçilmeyenler: "Önce depodaki tahsis"; "Her seferinde kullanıcı seçer".)

- Sinan'ın 12 Kasım 2026'da koordinatör oturumunda verdiği 25. tur cevabı; koordinatör kaydı, Issue #66 yorumu
  [6823547103](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823547103) ("Sinan kararı — tur 25 (Stok
  F-017 / Satış, #1116 r2 bulgusu) + koordinatör formül düzeltmesi"). Bu belgeye alınan madde (aynen):

  > 1. **Stok/Satış — gelecek ayırma yetmezse fiziksel ayırma fazlası** (#1116 r2 A 6823510860 P2-3 / B 6823489628 P2-1)
  >    Soru: Satış satırı azaltılınca önce gelecek tedarik ayırması bırakılıyor. Gelecek ayırma yetmezse, depodaki fiziksel ayırmanın fazlası da otomatik bırakılsın mı? (Örnek: 10 t fiziksel + 10 t gelecek ayrılmış, satır 20 t'dan 5 t'ya iniyor: 10 t gelecek bırakılır, 5 t da fiziksel.)
  >    Sinan'ın cevabı (birebir): "Otomatik bırakılır (Önerilen)"
  >    Seçenek metni: Fazla önce gelecekten, yetmezse fiziksel ayırmadan otomatik bırakılır; kullanıcı komutta hangisini bırakacağını seçerse onun seçimi geçer. Fiziksel bırakma ekranı hazır olana kadar böyle bir azaltma reddedilir.
  >    Seçilmeyenler: azaltma reddedilir (kalıcı); fazla kalır, uyarı.

  Aynı yorumdaki **koordinatör düzeltmesi** (Sinan metni değildir; yorumda "Bu bir koordinatör hesap notudur" diye
  işaretli): fazla = max(0, aktif fiziksel ayırma + aktif gelecek ayırma − (yeni satır miktarı − sevk edilen)); fazla
  önce gelecek ayırmadan (KLN-D30 sırası), gelecek yetmezse fiziksel ayırmadan bırakılır; fiziksel bırakma yolu yokken
  kalan > 0 ise azaltma bütünüyle reddedilir (yazma yok). Golden: fiziksel 10 + gelecek 10, 20 → 15 ⇒ gelecekten 5;
  20 → 5 ⇒ gelecekten 10 + fiziksel 5; satır 100, 40 sevk, 60 gelecek, 80'e iner ⇒ 20. Bu düzeltme 6823243819'daki
  yalnız-gelecek formülünün yerine geçer.
- 23. tur için koordinatörün oturum içi mesajla ilettiği "en geç gelen = beklenen teslim tarihi" ve "eşitlikte teknik
  kural yazarındır" notları #66'da kayıtlı değildir; bu belgede **yazarın okuması** diye işaretlenir (#1119 SAT-D315 ile
  aynı etiket). Formül ve golden'lar artık #66 6823547103'tedir.
- Sinan'ın cevabı yalnız tırnak içindeki seçenek adıdır. "(Önerilen)" işareti soranındır. "Anlamı: …" satırları
  koordinatörün seçeneğe yazdığı açıklamadır; Sinan seçeneği bu açıklamayla seçmiştir. Aşağıda bu satırlar
  **koordinatör anlamı** diye, onların ötesindeki ayrıntılar **yazarın okumasıdır** diye işaretlenir.
- Seçenekler: m.2 için (a) "Otomatik bırakılır" — açıklaması yukarıdaki "Anlamı" satırı (seçildi); (b) "Satış reddeder,
  önce elle bırak" — satış iptali/azaltması ayırma varken reddedilir, kullanıcı önce ayırmayı Stok'ta bırakır
  (seçilmedi). m.3 için (a) "Geri gelmez, yeniden ayrılır" — açıklaması yukarıdaki "Anlamı" satırı (seçildi);
  (b) bırakılmış ayırmaların canlanmada kendiliğinden geri gelmesi (seçilmedi; spec §9.1 aday 2'deki soru biçimi).
- Yorumun sonundaki not: bu kayıt `OPEN-*` kapatmaz; grant/rol/Critical, secret/deploy ve production aktivasyonu insan
  kapısında kalır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 21. tur m.2 | Gelecek tedarikten ayrılmış satış sipariş satırı iptal edilir veya miktarı azaltılırsa o satıra bağlı gelecek ayırmaya ne olur? (#1116 §9.1 aday 1) | "Otomatik bırakılır (Önerilen)" | Satış iptal/azaltma komutu aynı işlemde Stok bırakma portunu çağırır; artan ayırma kalmaz; audit'li. Seçilmeyen: "Satış reddeder, önce elle bırak". |
| 23. tur m.1 | Azaltmada bırakılacak gelecek ayırma birden çok alış siparişine yayılmışsa hangisi önce bırakılsın? (#1116 §9.1 aday 4) | "En geç gelen önce (Önerilen)" | Sistem en geç gelecek alıştan bırakır, erken geleni müşteriye tutar; kullanıcı isterse komutta başka ayırma seçebilir. Seçilmeyenler: "En son yapılan önce"; "Her seferinde kullanıcı seçer". |
| 23. tur m.2 | Azaltılan satırda hem depodaki etiketli mala tahsis hem gelecek ayırma varsa önce hangisi bırakılsın? (#1116 §9.1 aday 3) | "Önce gelecek ayırma (Önerilen)" | Depodaki hazır mal müşteride kalır, sevke hazırdır; henüz gelmemiş alış ayırması önce bırakılır; kullanıcı isterse değiştirebilir. Seçilmeyenler: "Önce depodaki tahsis"; "Her seferinde kullanıcı seçer". |
| 25. tur m.1 | Azaltmada gelecek ayırma yetmezse depodaki fiziksel ayırmanın fazlası da otomatik bırakılsın mı? (#1116 r2 A P2-3 / B P2-1) | "Otomatik bırakılır (Önerilen)" | Fazla önce gelecekten, yetmezse fiziksel ayırmadan otomatik bırakılır; kullanıcı komutta seçerse onun seçimi geçer. Fiziksel bırakma ekranı hazır olana kadar böyle bir azaltma reddedilir. Seçilmeyenler: azaltma reddedilir (kalıcı); fazla kalır, uyarı. |
| 21. tur m.3 | İptal veya API-11 kapanışında bırakılmış ayırmalar, alış satırı canlanınca kendiliğinden geri gelsin mi? (#1116 §9.1 aday 2, #1110 §3.8) | "Geri gelmez, yeniden ayrılır (Önerilen)" | Canlanan miktar serbest açık miktar olur; eski ayırmalar kendiliğinden dönmez, gerekiyorsa kullanıcı yeniden ayırır; sistem "canlandı" bildirimi gösterir. Seçilmeyen: bırakılmış ayırmaların kendiliğinden geri gelmesi. |

## Kararlar

| Kimlik | Konu | Karar | Etkilenen yüzey |
|---|---|---|---|
| KLN-D28 | Satış sipariş satırı iptal edilir veya azaltılırsa gelecek tedarik ayırması (21. tur m.2) | (1) Gelecek tedarikten ayrılmış satış sipariş satırı **iptal edilir veya miktarı azaltılırsa** o satıra bağlı gelecek ayırma **otomatik bırakılır** (Sinan). (2) Satış iptal/azaltma komutu **aynı işlemde Stok bırakma portunu** (`IInventoryFutureAllocationReleasePortV1`, spec §5) çağırır; **artan ayırma kalmaz**; bırakma **audit'lidir** (koordinatör anlamı). (3) "Aynı işlem" = Satış komutunun tek host işlemi; Satış Stok tablosuna yazmaz, Stok bırakmayı kendi portuyla ve kendi kurallarıyla (spec §3.2 adım 0'daki satır kilidi, sürüm, audit, outbox `inventory.future-allocation.released.v1`) yapar; modüller birbirine referans vermez, host participant/port deseniyle bağlanır (AGENTS "Bir modül başka modül tablosuna doğrudan yazmaz"; **yazarın okumasıdır, geri alınabilir**). (4) **İptalde** satıra bağlı bütün aktif gelecek ayırmalar bırakılır (fiziksel tahsis için §5.1 (b) önizleme reddi sürer). **Azaltmada** yalnız satış satırının yeni **açık miktarını** (yeni satır miktarı − sevk edilen; spec §3.2 adım 5) aşan kısım bırakılır. Satış revizyonunda satırın kaldırılması porta iptal değil yeni miktar 0 olan azaltma olarak gelir. Porta yalnız SAT-D167 anlamında sevk edilmemiş satır gelir (sevk, yükleme talimatı ve fiziksel tahsis yok; `SINAN_KARARLARI_2026-11-02_SATIS_EK24.md` :52); bu satırda yalnız gelecek ayırma vardır ve hepsi bırakılır. Sevkli, kısmen sevkli, yükleme talimatlı veya fiziksel tahsisli satırın kaldırılmasını Satış porttan önce 422 `sales_line_revision_line_shipped` ile reddeder; fiziksel bırakma gerektiren KLN-D32 (3) ara reddi yalnız miktar azaltmada oluşur; tek istisna, SAT-D167 okumasından sonra aynı satıra eşzamanlı fiziksel tahsis oluşmasıdır: 0'a azaltma porta fiziksel tahsisle ulaşır ve ara 409 terminal döner (fail-closed; spec §5.1 (c) "Yarış", F017-s). SAT-D167 bağlantı okuması (Satış `ReadAllocationsAsync` "bağlı" kontrolü) yalnız fiziksel tahsisi sayar, gelecek ayırmayı stok tahsisi saymaz; sayarsa SAT-D314 (1) uygulanamaz (F017-s bağımlılığı); SAT-D167 metni değişmez (koordinatör kararı, r3 B P2-3; koordinatör düzeltmesi, oturum mesajı 2026-11-12 13:04Z; #66'da kayıtlı değil). **Tek tanım (her yerde bu kullanılır; koordinatör düzeltmesi #66 6823547103):** **fazla = max(0, satıra bağlı aktif fiziksel tahsis + aktif gelecek ayırma toplamı − (yeni satır miktarı − sevk edilen))**. Fazla önce gelecek ayırmalardan (aralarında KLN-D30 sırası), gelecek ayırmalar yetmezse fiziksel tahsisten bırakılır (KLN-D31, KLN-D32). Fiziksel tahsis yokken bu, max(0, aktif gelecek ayırma − açık miktar) ile aynıdır. Açık miktar içinde kalan ayırma aktif kalır. Sevkte tüketilmiş fiziksel tahsis "aktif" sayılmaz (**yazarın okuması**). min(kalan ayırma, azaltılan miktar) kullanılmaz; kısmi ayrılmış satırda gereğinden fazla bırakır (örnek: 10 t satırın 4 t'si ayrılmış, sevk yok, satır 7 t'ye inerse bırakılan 0'dır, 3 değil). Sevki düşmeden yeni satır miktarıyla karşılaştırmak da kullanılmaz (örnek: satır 100, 40 sevk, 60 ayırma, satır 80'e iner → açık 40, bırakılan 20; düzeltme koordinatör, #1119 B review 6823066704 P2-1; #1119 SAT-D314/SAT-D315 bu tanıma hizalanır, koordinasyon notu). Birden çok gelecek ayırma arasında sıra KLN-D30'da, fiziksel/gelecek sırası KLN-D31'de, fiziksel fazla ve ara durum KLN-D32'dedir. Karşılaştırma tek birimde yapılır; çevrim SAT-D298'deki merkezi servisledir. "Artan ayırma kalmaz" bu iki duruma birlikte karşılık gelir (**yazarın okumasıdır, geri alınabilir**; SAT-D314 yazar okumasıyla aynı). (5) Bırakma **exactly-once ve idempotenttir**: aynı Satış komut anahtarıyla gelen tekrar (replay) ikinci bırakma üretmez, Satış'ın idempotency kaydındaki saklanan sonucu Stok portunu yeniden çağırmadan döner; aynı ayırma ikinci kez bırakılmaz (DEC-STK-004 "atomik, idempotent"; **yazarın okumasıdır**). Bırakma **silme değildir**: kalan 0 olunca durum spec §4.1 kuralıyla belirlenir (tüketilen > 0 ise `Consumed`, değilse `Released`), audit ve outbox (`inventory.future-allocation.released.v1`) aynı işlemde yazılır. (6) Bırakma **fiziksel hareket değildir**; stok bakiyesi, serbest fiziki ve maliyet değişmez (DEC-STK-003, KLN-D26 (3)). (7) **Talep tarafı çapası:** Satış iptal/azaltma komutu ve gelecek ayırma oluşturma aynı **satış satırı kilidini** alır; kilit sırası önce satış satırı (birden çoksa artan kimlikle, hepsi alış kilitlerinden önce), sonra alış satırları (artan `lineId`); talebi yeniden artıran kabul geri alma/yeniden açma (spec §3.7) da aynı sırayı alır ve satış satırını kilit altında yeniden okur; satıra bağlı ayırma kümesi bu kilitlerden sonra okunur (spec §3.2 adım 0, §4.3; kilit biçimi F017-b'de, **yazarın okuması, geri alınabilir**). Böylece aynı satış satırına farklı alış satırından eşzamanlı oluşturma, azaltılmış veya iptal edilmiş satırın eski miktarını göremez. Bırakma portu okunamaz, hata verir, kilit alınamaz veya bırakma başarısız olursa **Satış iptali/azaltması da yazılmaz** (aynı işlem, fail-closed; tümüyle geri alınır); satış satırı iptal edilmiş, ayırma aktif kalmış durum oluşmaz (kapalı yön; **yazarın okumasıdır, geri alınabilir**). (8) Bırakma nedeni Satış kaynaklıdır; neden kodu ve `origin` değeri (örneğin `sales-line-cancelled` / `sales-line-reduced`, `origin = sales`) F017-s'de adlandırılır (**yazarın önerisi, geri alınabilir**). (9) DEC-SAL-005 geçerlidir: iptal önizlemesi gelecek ayırmaları gösterir; önizleme okunamazsa iptal reddedilir (spec §5.1 (b)). Bu karar önizlemeyi kaldırmaz, onaylanan iptalin ayırmayı kendisinin bırakmasını sağlar. (10) **Satış tarafı karşılığı: SAT-D314** (`SINAN_KARARLARI_2026-11-12_SATIS_EK60.md`, #1119, `main` `c93433f6`); aynı Sinan cevabından (6822767886 m.2) Satış komutunun tarafını yazar. | Satış satır iptali ve miktar azaltma komutları, Stok bırakma portu, Satış tahsis okuması (F017-s; cross-module K3) |
| KLN-D29 | Canlanan alış satırında önceden bırakılmış ayırmalar (21. tur m.3) | (1) İptal ("bağları düşür ve iptal et", KLN-D26 (6)(d) / ALS-D85) veya API-11 kapanışında (`purchase-line-closed`) bırakılmış ayırmalar, alış satırı **canlanınca** (ALS-D92, Sinan 19. tur 6822140358; #1110 §3.8) **geri gelmez** (Sinan; Alış eşi ALS-D93). (2) Canlanan miktar **serbest açık miktar** olur; eski ayırmalar kendiliğinden dönmez; **gerekiyorsa kullanıcı yeniden ayırır**; sistem **"canlandı" bildirimi** gösterir (koordinatör anlamı). (3) Yeniden ayırma normal oluşturma yoludur (spec §3.2): satır kilidi, Alış portundan kilit altında okunan `AllocatableText`, değişmez kontrolü, izin ve idempotency aynen uygulanır; canlanma için ayrı veya öncelikli bir ayırma yolu açılmaz (**yazarın okumasıdır, geri alınabilir**). (4) Bu karar, kabul geri almada **tüketilmiş** (`Consumed`) ayırmanın aynı kimlikle yeniden açılmasını (KLN-D26 (6)(e), ALS-D86/ALS-D91, spec §3.7) değiştirmez; KLN-D29 yalnız daha önce `Released` olmuş ayırmalara uygulanır (**yazarın okumasıdır**). (5) Bildirim bir olay ve okuma alanıdır; canlanmanın sahibi Alış'tır ve olayı Alış canlanma komutu yazar; bildirimin kime (bırakılmış ayırmanın satış kullanıcısı, satınalma kullanıcısı) ve hangi kanalla (ekran uyarısı, e-posta, anlık bildirim) gideceği bu kararla seçilmemiştir (Seçilmeyenler). | Alış canlanma komutu (#1110 §3.8, Alış K3), Stok gelecek ayırma okuması, Satış/Alış ekran uyarıları |
| KLN-D30 | Azaltmada birden çok gelecek ayırma arasında bırakma sırası (23. tur m.1) | (1) Satış satırı azaltılınca KLN-D28 (4)'e göre bırakılacak miktar birden çok gelecek ayırmaya (birden çok alış satırına) yayılıyorsa **en geç gelen önce** bırakılır; erken gelen müşteriye tutulur (Sinan; koordinatör anlamı). (2) "En geç gelen" = ayırmanın bağlı olduğu **alış satırının beklenen teslim tarihi**dir (**yazarın okuması, geri alınabilir**; #1119 SAT-D315 ile aynı etiket). Tarih Stok'ta saklanmaz; Alış'ın termin dilimi okumasından (`GetLineScheduleAsync`, #1110 §3.1 F17-1b, #1101) kilit altında, aynı işlemde okunur. Bir alış satırının birden çok dilimi olabildiği için satırın tarihi = **teslim alınmamış kalanı olan dilimlerin en geç teslim tarihi**dir (etiketli teknik okuma, geri alınabilir). Bırakma bu tarihe göre azalan sırada ilerler; son ayırmadan gerektiği kadar **kısmi** bırakılır (spec §3.4; ayırma aynı kimlik, oluşturma zamanı ve `ManualSequence` ile kalır). Bırakılan gelecek toplamı KLN-D28 (4) fazlasını ve aktif gelecek ayırmayı aşmaz. (3) **Kullanıcı Satış azaltma komutunda bırakılacak ayırmaları açıkça seçebilir; açık seçim varsayılandan üstündür** (Sinan; koordinatör anlamı). Seçim her ayırma için `ExpectedAllocationVersion` taşır; bayat seçim 409 `inventory_allocation_version_conflict` ile reddedilir. Seçilenler KLN-D28 (4) fazlasını karşılamıyorsa kalan kısım varsayılan sırayla (önce gelecek, KLN-D31) bırakılır; seçim toplamı fazlayı aşarsa komut yazma olmadan **422** ile reddedilir (kod F017-s'de adlandırılır; **yazarın okumasıdır, geri alınabilir**). (4) **Tarih yoksa ve okunamazsa:** termin okuması `Unreadable`/hata dönerse sıralı bırakma yazılmaz ve Satış komutu da yazılmaz (fail-closed, 503; "tarihsiz" sayılmaz). Termini hiç girilmemiş (dilimi olmayan) satır **tarihsiz** sayılır ve en geç kabul edilir. Eşitlikte (aynı tarih veya iki tarihsiz satır): ayırması en son oluşturulan önce, sonra ayırma kimliği büyük olan önce (deterministik; **yazarın okumasıdır, geri alınabilir**). F017-s termin okuması main'e gelmeden sürülmez; böylece "tarihsiz" bütün satırlara yayılıp sıra Sinan'ın seçmediği "en son yapılan önce"ye düşmez. (5) Sıra Stok bırakma portu içinde, satış satırı ve alış satırı kilitleri altında hesaplanır; Satış sırayı hesaplamaz, yalnız açık seçimi iletir. Termin değişikliği alış satırı kilidini almıyorsa bile sıra aynı işlemde okunan tarihle hesaplanır; kullanılan tarihler ve sıra audit'e ve komut sonucuna yazılır, replay saklanan sonucu döner (**yazarın okuması**). (6) Spec'teki geçici "seçim yoksa azaltma 409, yazma yok" kapalı yönü kalkar; SAT-D314'ün "Satış komutu bu yüzden reddetmez" cümlesiyle gerilim kalmaz. | Stok bırakma portu, Satış azaltma komutu (F017-s; cross-module K3) |
| KLN-D31 | Azaltmada fiziksel tahsis ile gelecek ayırma arasında bırakma sırası (23. tur m.2) | (1) Azaltılan satış satırında hem depodaki etiketli mala yapılmış **fiziksel tahsis** hem **gelecek ayırma** varsa **önce gelecek ayırma** bırakılır; depodaki hazır mal müşteride kalır (Sinan; koordinatör anlamı). (2) Kullanıcı Satış azaltma komutunda bu sırayı değiştirebilir (Sinan; koordinatör anlamı); açık seçim varsayılandan üstündür (KLN-D30 (3) ile aynı kural). (3) Bırakılacak miktar KLN-D28 (4)'teki tek tanımdır (fazla); önce gelecek ayırmalardan (aralarında KLN-D30 sırasıyla), yetmezse fiziksel tahsisten bırakılır (KLN-D32). (4) Fiziksel tahsisin bırakılma yolu ve ara durum KLN-D32'dedir; bu karar fiziksel tahsiste talep üst sınırı kuralını (spec §9.1 aday 3, `NEEDS-SPEC`) seçmez. (5) Golden: fiziksel 10 + gelecek 10, satır 20 → 15 (sevk 0) → fazla 5 → gelecekten 5 bırakılır, fiziksel 10 kalır. | Stok bırakma portu, Satış azaltma komutu, Satış/Stok fiziksel tahsis (F017-s; cross-module K3) |
| KLN-D32 | Azaltmada gelecek ayırma yetmezse fiziksel tahsis fazlası (25. tur m.1) | (1) Gelecek ayırmalar KLN-D28 (4) fazlasını karşılamazsa kalan kısım **depodaki fiziksel tahsisten otomatik bırakılır** (Sinan). (2) Kullanıcı komutta hangisini bırakacağını seçerse onun seçimi geçer (Sinan seçenek metni; KLN-D30 (3) ile aynı kural: `ExpectedAllocationVersion`, fazlayı aşan seçim reddi). (3) **Ara durum:** fiziksel bırakma yolu (Sinan metninde "fiziksel bırakma ekranı") main'e gelene kadar, fiziksel tahsisten bırakma gerektiren azaltma reddedilir: varsayılan sırada satıra bağlı aktif gelecek ayırma toplamı fazlayı karşılamıyorsa (aktif gelecek < fazla) veya kullanıcının açık seçimi fiziksel tahsis içeriyorsa (koordinatör düzeltmesi, oturum mesajı 2026-11-12, #1116 r4 sonrası; #66'da kayıtlı değil) (Sinan seçenek metni: "böyle bir azaltma reddedilir"). Ret **bütündür; gelecek ayırma da bırakılmaz, Satış komutu yazılmaz** (koordinatör düzeltmesi #66 6823547103; spec §5.1 (b) fiziksel iptal reddiyle aynı). Kullanıcının komutta açıkça fiziksel tahsis seçtiği azaltma da aynı ara redle reddedilir (#1119 SAT-D314 (2)/SAT-D315; koordinatör notu, r3 B P2-2). (4) Fiziksel tahsisin bırakılması Stok'un fiziksel tahsis yazıcısıyla, aynı host işleminde ve aynı satış satırı kilidi altında yapılır; Satış Stok tablosuna yazmaz (**yazarın okuması**). Etiket bazlı fiziksel tahsisin kısmi bırakılması (bugün etiket bazlı hep-ya-da-hiç; `StockAllocation`) ve bu yolun dilimi F017-s'de `NEEDS-SPEC`'tir; o cevaplanana kadar (3)'teki ara red sürer, tahminle seçilmez. (5) Golden: fiziksel 10 + gelecek 10, satır 20 → 5 (sevk 0) → fazla 15 → gelecekten 10 + fiziksel 5; ara durumda komut reddedilir, hiçbir ayırma değişmez. Fiziksel 0 iken (satır 100, sevk 40, gelecek 60, 80'e iner) fazla 20, gelecekten 20 (tek tanım eski formülle aynı sonucu verir). (6) Seçilmeyenler: kalıcı red; fazlanın kalması ve uyarı. | Stok bırakma portu ve fiziksel tahsis yazıcısı, Satış azaltma komutu (F017-s; cross-module K3) |

## Uygulama notları

- **KLN-D28 — F017-s.** F017-s'nin kapsamına Satış satır iptali ve azaltmasında Stok bırakma portu çağrısı eklenir
  (cross-module K3; Satış + Stok + host). Satış komutu kendi expected version, idempotency ve audit kaydını tutar; Stok
  bırakması aynı host işleminde, satır kilidi altında çalışır. Testler: iptal bütün ayırmaları bırakır; azaltma yalnız
  KLN-D28 (4) fazlası kadar bırakır (kısmi ayrılmış ve kısmi sevkli satır golden'ı); bırakma başarısızsa Satış komutu da yazılmaz; replay ikinci bırakma üretmez; gerçek SQL'de
  Satış iptali × mal kabul çevirmesi yarışı.
- **KLN-D30/KLN-D31 — F017-s.** Bırakma sırası Stok bırakma portunun içindedir; Satış yalnız açık seçimi iletir.
  Testler: 30 t → 20 t, 10 t Aralık + 20 t Ocak → Ocak'tan 10 t; açık seçim varsayılanı geçer, bayat seçim 409;
  eşitlik kuralı deterministik; termin okunamazsa komut yazılmaz; tarihsiz satır en geç sayılır; çok dilimli satırda
  teslim alınmamış en geç dilim tarihi; fiziksel 10 + gelecek 10, satır 20 → 15 → gelecekten 5. F017-s, #1110 F17-1b
  (`GetLineScheduleAsync`, #1101) main'e gelmeden sürülmez.
- **KLN-D32 — F017-s.** Fiziksel fazla, fiziksel bırakma yolu gelene kadar ara redle korunur: fiziksel 10 + gelecek 10,
  20 → 5 komutu yazılmaz ve hiçbir ayırma değişmez. Reddin sahibi Stok bırakma portudur: spec §3.2 kilitleri altında,
  yazmadan önce tespit eder ve 409 `inventory_future_allocation_physical_release_unavailable` döner (öneri, F017-s'de kesinleşir; spec §7,
  §8). Port bu redde hiçbir şey yazmaz (Stok satırı, audit, outbox, ayırma değişmez). Satış bu kodu kendi problemine
  taşır; tek terminal ret kaydı ve aynı anahtarla replay, idempotency anahtarının sahibi Satış revizyon komutunundur ve
  replay Stok portunu ikinci kez çağırmaz. Port bütün okumaları (aktif gelecek ayırma, fiziksel tahsis, satış satırı, termin) bitirmeden kaydedilen terminal ret (409 veya 422) dönmez; kilit meşgul 409'u okumalardan önce gelir ve kaydedilmez; okunamaz, port hatası veya zaman aşımı varsa terminal ret koşulu (409 veya 422) tespit edilmiş olsa bile sonuç 503'tür ve hiçbir şey kaydedilmez; çok satırlı revizyonda Satış ilk terminal rette durmaz (koordinatör kararı (c), #1123 ile aynı). Çok satırlı değerlendirme modeli (koordinatör okuması #66 6825926365; 6825436733 ve 6825828888'in yerine geçer; teknik idempotency sınıflaması, geri alınabilir, para/stok politikası seçmez): önce bütün kilitler (satış satırı, sonra alış satırları artan `lineId`); biri meşgulse o kilidin 409 koduyla döner, okuma ve kayıt yok; sonra satırlar sabit sırayla değerlendirilir, terminal ret (409 veya 422) toplanır; ilk terminal olmayan sonuç (503/okunamaz, port hatası, zaman aşımı, 409 `inventory_concurrent_change`) değerlendirmeyi bitirir ve kendi durum ve koduyla döner, kayıt yok, aynı anahtar yeniden yürür; bütün satırlar terminal ve kodlar farklıysa sabit sıradaki ilk terminal ret kaydedilir (spec §5.1 (c), §7, §8 golden (i)–(v); Satış X1 bu okumayla hizalanacak). 503/okunamaz, port hatası, zaman aşımı, satış veya alış satırı kilidi meşgul ve 409 `inventory_concurrent_change` terminal değildir, kaydedilmez; bayat açık seçimde 409 `inventory_allocation_version_conflict`, 409 `inventory_allocation_state_conflict` ve 422 `inventory_future_allocation_release_selection_invalid` kaydedilen terminal retlerdir (koordinatör notu, r3 B P2-1; r4 A P3-3; #1126 r1, r2). Yol `NEEDS-SPEC` cevabıyla geldiğinde aynı golden gelecekten 10 +
  fiziksel 5 bırakır. Talep tarafı yarış testi: satış satırı azaltması × aynı satıra başka alış satırından oluşturma;
  sonuçta iptal satırda aktif gelecek ayırma yoktur ve aktif gelecek ayırma ≤ yeni açık miktar. Aynı değişmez Satış
  iptali/azaltması × kabul geri alma (yeniden açma) yarışında da test edilir (spec §8).
- **Satış şeridi karşılığı.** Satış tarafının karar satırı (Satış iptal/azaltma komutunun Stok portunu çağırması) Satış
  şeridinindir; bu belge SAT-D kimliği açmaz. Satış tarafı karşılığı: **SAT-D314**
  (`SINAN_KARARLARI_2026-11-12_SATIS_EK60.md`, #1119, `main` `c93433f6`). SAT-D314/SAT-D315'in "Ek kaynak (25. tur)"
  paragrafı KLN-D28 (4) tek tanımını ve KLN-D32 ara reddini aynı #66 6823547103 yorumundan yazar; hiza tamamdır.
- **F-017 dışı port notu (Sinan 26. tur m.1, #66 6823809221, AT-044; KLN kararı değildir).** "Kısmi dilinir, kalan
  aynı etikette" (Sinan). Kalan aynı etiket numarasıyla düşmüş bakiyeyle kalır; dilme planında planlanan kg etiketin
  güncel bakiyesini aşamaz (**koordinatör notu**). Stok tarafında **yeni port veya alan gerekmez** (**yazarın okuması**): Üretim'de zaten enjekte olan
  `IInventoryLabelDetailsV1.GetAsync(labelId)` aynı version-fenced okumada `LabelVersion`, `Balances.PhysicalKg/FreeKg/BlockedKg`
  ve pozisyonları (`OwnerKind`, `OwnerId`) döner. Okunamazsa (503/409/404/401/403; liste **yazarın okuması**) plan reddedilir, "bakiye yok/sıfır"
  sayılmaz. Hangi bakiyenin (fiziksel / serbest) üst sınır olduğu, planın kendi etiket ayırmasının (etiket bazlı,
  `DemandLineId = Label.Id`) kısmi dilmede kalan kg için nasıl davrandığı ve tüketim katılımcısının kısmi tüketimi
  Üretim K3 diliminde yazılır ve test edilir; Stok tahminle seçmez, Üretim dilimi de tahminle seçmez; gerekirse
  `NEEDS-SPEC`. Uygulama o dilimle birlikte yapılır.
- **KLN-D29 — Alış canlanma dilimi.** Alış karşılığı #1110 §3.8'dir (canlanma K3 dilimi). Stok tarafında yeni kod
  gerekmez: canlanan miktar Alış portundan ayrılabilir okunur ve normal oluşturma yolu çalışır (spec §3.6, §3.7, F017-e).
  "Canlandı" bildirimi Alış canlanma olayına bağlanır; Stok bir işaret saklamaz.
- Gelecek ayırma ve bırakma fiziksel hareket değildir; para, maliyet veya stok bakiyesi politikası bu kararlarla
  değişmez.

| Karar | İş | Şerit / Kapı |
|---|---|---|
| KLN-D28 | Satış satır iptali/azaltmasında aynı işlemde Stok bırakma portu çağrısı | Stok + Satış (+host); F017-s; cross-module K3 |
| KLN-D29 | Canlanmada bırakılmış ayırmalar geri gelmez; yeniden ayırma normal yol; "canlandı" bildirimi | Alış (#1110 §3.8) + Stok (okuma); Alış canlanma K3 dilimi |
| KLN-D30 | Azaltmada en geç gelen (beklenen teslim tarihi) ayırma önce bırakılır; açık seçim üstündür | Stok + Satış (+host); F017-s; cross-module K3 |
| KLN-D31 | Azaltmada önce gelecek ayırma, sonra fiziksel tahsis bırakılır; açık seçim üstündür | Stok + Satış (+host); F017-s; cross-module K3 |
| KLN-D32 | Gelecek yetmezse fiziksel fazla otomatik bırakılır; yol gelene kadar böyle azaltma reddedilir | Stok + Satış (+host); F017-s (+ fiziksel bırakma `NEEDS-SPEC`); cross-module K3 |

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz.
- **Kapatma yoktur.** `KARAR_SICILI`/`ACIK_KARARLAR` değişmez; `OPEN-*` kapatma insan kapısıdır.
- **Seçilmeyenler:** (1) Fiziksel tahsiste talep üst sınırı kuralı (spec §9.1 `NEEDS-SPEC` adayı 3; bırakma sırası
  KLN-D31 ile, fiziksel fazla KLN-D32 ile cevaplandı, üst sınır kuralı açık). (2) Etiket bazlı fiziksel tahsisin kısmi
  bırakılma yolu (KLN-D32 (4); o gelene kadar ara red). (3) KLN-D29 "canlandı" bildiriminin alıcısı ve kanalı. Bunlar ilgili dilimde
  `NEEDS-SINAN`/`NEEDS-SPEC` olarak sorulur; bu belge tahminle seçmez. Birden çok gelecek ayırma arasında bırakma sırası
  ve fiziksel/gelecek sırası artık KLN-D30/KLN-D31'de, fiziksel fazla KLN-D32'dedir.
- "Yazarın okumasıdır (geri alınabilir)" ve "koordinatör anlamı" diye işaretli noktalar Sinan'ın sözü değildir; Sinan
  yalnız seçenek adını seçmiştir. Sinan farklı isterse ayrı karar gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
