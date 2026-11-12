# Sinan Kararları — 2026-11-12, 21. tur madde 2–3 ve 23. tur madde 1–2 (Stok: gelecek tedarik ayırmasının satış iptali/azaltmasında bırakılması ve bırakma sırası, canlanan alış satırında bırakılmış ayırmalar)

Bu belge önceki karar dosyalarını silmez; **KLN-D28, KLN-D29, KLN-D30 ve KLN-D31**'i ekler. F-017 Stok spec'inin
(`docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md`, PR #1116) §9.1'deki adayları (21. turda 1–2, 23. turda 3'ün
bırakma sırası ve 4) Sinan kararına bağlar. `SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md` (KLN-D25…KLN-D27, SAT-D308; KLN-D26 F-017'nin
Stok tarafı), Alış eşleri ALS-D82…ALS-D91 (`SINAN_KARARLARI_2026-11-12_ALIS_EK17.md`, PR #1110; merge edilince `main`de),
DEC-STK-002/003/004/005/006, DEC-SAL-004 ve DEC-SAL-005 geçerlidir. Bu belge onların karar metnini değiştirmez; KLN-D28,
DEC-SAL-005'in "önizleme olmadan iptal yok" kuralını kaldırmaz (önizleme gelecek ayırmayı gösterir, bırakma onaylanan
iptalin parçası olur). 11-12 Stok karar dosyası ayrı PR'da (#1121) review'dadır; bu belge o dosyayı düzenlemez.
`KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.

**Kimlikler:** KLN-D serisinde `main`deki (`cf0ae197f`) son kimlik KLN-D26'dır; KLN-D27 açık PR'dadır. `main`de
ve açık PR dallarının eklemelerinde KLN-D28 ve KLN-D29 geçmiyordu (koordinatör doğrulaması; `git grep -ohE "KLN-D2[89]"
origin/main -- docs` boş). KLN-D30 ve KLN-D31 de `main`de ve açık PR dallarında (#1114, #1119) geçmiyordu
(yazar doğrulaması, 2026-11-12; en yüksek KLN-D28). Gelecek tedarik ayırması Kalan Modüller (Stok) şeridinin işi olduğu
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

- Koordinatör ayrıca şunu iletti: "En geç gelen" = alış satırının beklenen teslim tarihidir; eşitlikte teknik kural
  yazarındır (geri alınabilir); fiziksel tahsis bırakma Satış/Stok fiziksel tahsis kuralıyla yapılır; spec'teki geçici
  "409, yazma yok" kapalı yönü kalkar. Golden: 30 t → 20 t, 10 t Aralık + 20 t Ocak → Ocak'tan 10 t bırakılır; fiziksel
  10 + gelecek 10, satır 20 → 15 → gelecekten 5.
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
| 21. tur m.3 | İptal veya API-11 kapanışında bırakılmış ayırmalar, alış satırı canlanınca kendiliğinden geri gelsin mi? (#1116 §9.1 aday 2, #1110 §3.8) | "Geri gelmez, yeniden ayrılır (Önerilen)" | Canlanan miktar serbest açık miktar olur; eski ayırmalar kendiliğinden dönmez, gerekiyorsa kullanıcı yeniden ayırır; sistem "canlandı" bildirimi gösterir. Seçilmeyen: bırakılmış ayırmaların kendiliğinden geri gelmesi. |

## Kararlar

| Kimlik | Konu | Karar | Etkilenen yüzey |
|---|---|---|---|
| KLN-D28 | Satış sipariş satırı iptal edilir veya azaltılırsa gelecek tedarik ayırması (21. tur m.2) | (1) Gelecek tedarikten ayrılmış satış sipariş satırı **iptal edilir veya miktarı azaltılırsa** o satıra bağlı gelecek ayırma **otomatik bırakılır** (Sinan). (2) Satış iptal/azaltma komutu **aynı işlemde Stok bırakma portunu** (`IInventoryFutureAllocationReleasePortV1`, spec §5) çağırır; **artan ayırma kalmaz**; bırakma **audit'lidir** (koordinatör anlamı). (3) "Aynı işlem" = Satış komutunun tek host işlemi; Satış Stok tablosuna yazmaz, Stok bırakmayı kendi portuyla ve kendi kurallarıyla (spec §3.2 adım 0'daki satır kilidi, sürüm, audit, outbox `inventory.future-allocation.released.v1`) yapar; modüller birbirine referans vermez, host participant/port deseniyle bağlanır (AGENTS "Bir modül başka modül tablosuna doğrudan yazmaz"; **yazarın okumasıdır, geri alınabilir**). (4) **İptalde** satıra bağlı bütün aktif gelecek ayırmalar bırakılır. **Azaltmada** yalnız satış satırının yeni **açık miktarını** (yeni satır miktarı − sevk edilen; spec §3.2 adım 5) aşan ayırma kısmı bırakılır: bırakılan miktar = **max(0, satıra bağlı aktif gelecek ayırma toplamı − (yeni satır miktarı − sevk edilen))**; açık miktar içinde kalan ayırma aktif kalır. min(kalan ayırma, azaltılan miktar) kullanılmaz; kısmi ayrılmış satırda gereğinden fazla bırakır (örnek: 10 t satırın 4 t'si ayrılmış, sevk yok, satır 7 t'ye inerse bırakılan 0'dır, 3 değil). Sevki düşmeden yeni satır miktarıyla karşılaştırmak da kullanılmaz (örnek: satır 100, 40 sevk, 60 ayırma, satır 80'e iner → açık 40, bırakılan 20; düzeltme koordinatör, #1119 B review 6823066704 P2-1; SAT-D314 aynı formülle yazılır). Fiziksel tahsis de varsa önce gelecek ayırma bırakılır (KLN-D31); birden çok gelecek ayırma arasında sıra KLN-D30'dadır. Karşılaştırma tek birimde yapılır; çevrim SAT-D298'deki merkezi servisledir. "Artan ayırma kalmaz" bu iki duruma birlikte karşılık gelir (**yazarın okumasıdır, geri alınabilir**; SAT-D314 yazar okumasıyla aynı). (5) Bırakma **exactly-once ve idempotenttir**: aynı Satış komut anahtarıyla gelen tekrar (replay) ikinci bırakma üretmez, saklanan sonucu döner; aynı ayırma ikinci kez bırakılmaz (DEC-STK-004 "atomik, idempotent"; **yazarın okumasıdır**). Bırakma **silme değildir**: ayırma `Released` olur, audit ve outbox (`inventory.future-allocation.released.v1`) aynı işlemde yazılır. (6) Bırakma **fiziksel hareket değildir**; stok bakiyesi, serbest fiziki ve maliyet değişmez (DEC-STK-003, KLN-D26 (3)). (7) Bırakma portu okunamaz, hata verir, kilit alınamaz veya bırakma başarısız olursa **Satış iptali/azaltması da yazılmaz** (aynı işlem, fail-closed; tümüyle geri alınır); satış satırı iptal edilmiş, ayırma aktif kalmış durum oluşmaz (kapalı yön; **yazarın okumasıdır, geri alınabilir**). (8) Bırakma nedeni Satış kaynaklıdır; neden kodu ve `origin` değeri (örneğin `sales-line-cancelled` / `sales-line-reduced`, `origin = sales`) F017-s'de adlandırılır (**yazarın önerisi, geri alınabilir**). (9) DEC-SAL-005 geçerlidir: iptal önizlemesi gelecek ayırmaları gösterir; önizleme okunamazsa iptal reddedilir (spec §5.1 (b)). Bu karar önizlemeyi kaldırmaz, onaylanan iptalin ayırmayı kendisinin bırakmasını sağlar. (10) **Satış tarafı karşılığı: SAT-D314** (`SINAN_KARARLARI_2026-11-12_SATIS_EK60.md`, #1119 — merge bekliyor); aynı Sinan cevabından (6822767886 m.2) Satış komutunun tarafını yazar. | Satış satır iptali ve miktar azaltma komutları, Stok bırakma portu, Satış tahsis okuması (F017-s; cross-module K3) |
| KLN-D29 | Canlanan alış satırında önceden bırakılmış ayırmalar (21. tur m.3) | (1) İptal ("bağları düşür ve iptal et", KLN-D26 (6)(d) / ALS-D85) veya API-11 kapanışında (`purchase-line-closed`) bırakılmış ayırmalar, alış satırı **canlanınca** (ALS-D91, Sinan 19. tur 6822140358; #1110 §3.8) **geri gelmez** (Sinan). (2) Canlanan miktar **serbest açık miktar** olur; eski ayırmalar kendiliğinden dönmez; **gerekiyorsa kullanıcı yeniden ayırır**; sistem **"canlandı" bildirimi** gösterir (koordinatör anlamı). (3) Yeniden ayırma normal oluşturma yoludur (spec §3.2): satır kilidi, Alış portundan kilit altında okunan `AllocatableText`, değişmez kontrolü, izin ve idempotency aynen uygulanır; canlanma için ayrı veya öncelikli bir ayırma yolu açılmaz (**yazarın okumasıdır, geri alınabilir**). (4) Bu karar, kabul geri almada **tüketilmiş** (`Consumed`) ayırmanın aynı kimlikle yeniden açılmasını (KLN-D26 (6)(e), ALS-D86/ALS-D91, spec §3.7) değiştirmez; KLN-D29 yalnız daha önce `Released` olmuş ayırmalara uygulanır (**yazarın okumasıdır**). (5) Bildirim bir olay ve okuma alanıdır; canlanmanın sahibi Alış'tır ve olayı Alış canlanma komutu yazar; bildirimin kime (bırakılmış ayırmanın satış kullanıcısı, satınalma kullanıcısı) ve hangi kanalla (ekran uyarısı, e-posta, anlık bildirim) gideceği bu kararla seçilmemiştir (Seçilmeyenler). | Alış canlanma komutu (#1110 §3.8, Alış K3), Stok gelecek ayırma okuması, Satış/Alış ekran uyarıları |
| KLN-D30 | Azaltmada birden çok gelecek ayırma arasında bırakma sırası (23. tur m.1) | (1) Satış satırı azaltılınca KLN-D28 (4)'e göre bırakılacak miktar birden çok gelecek ayırmaya (birden çok alış satırına) yayılıyorsa **en geç gelen önce** bırakılır; erken gelen müşteriye tutulur (Sinan; koordinatör anlamı). (2) "En geç gelen" = ayırmanın bağlı olduğu **alış satırının beklenen teslim tarihi**dir (koordinatör, 23. tur iletimi). Bırakma bu tarihe göre azalan sırada ilerler; son ayırmadan gerektiği kadar kısmi bırakılır (KLN-D28 (4) miktarı aşılmaz). (3) **Kullanıcı Satış azaltma komutunda bırakılacak ayırmaları açıkça seçebilir; açık seçim varsayılandan üstündür** (Sinan; koordinatör anlamı). Seçilen ayırmalar KLN-D28 (4) miktarını karşılamıyorsa kalan kısım (2)'deki sırayla bırakılır; seçim bu miktarı aşamaz, aşarsa komut yazma olmadan reddedilir (**yazarın okumasıdır, geri alınabilir**). (4) Eşitlikte (aynı beklenen teslim tarihi veya tarihi olmayan satırlar) teknik kural: tarihi olmayan satır en geç sayılır; sonra ayırması en son yapılan önce, sonra ayırma kimliği büyük olan önce (deterministik; **yazarın okumasıdır, geri alınabilir**). (5) Sıra Stok bırakma portu içinde, §3.2 adım 0'daki satır kilitleri altında hesaplanır; Satış sırayı hesaplamaz, yalnız açık seçimi iletir. (6) Spec'teki geçici "seçim yoksa azaltma 409, yazma yok" kapalı yönü kalkar; SAT-D314'ün "Satış komutu bu yüzden reddetmez" cümlesiyle gerilim kalmaz. | Stok bırakma portu, Satış azaltma komutu (F017-s; cross-module K3) |
| KLN-D31 | Azaltmada fiziksel tahsis ile gelecek ayırma arasında bırakma sırası (23. tur m.2) | (1) Azaltılan satış satırında hem depodaki etiketli mala yapılmış **fiziksel tahsis** hem **gelecek ayırma** varsa **önce gelecek ayırma** bırakılır; depodaki hazır mal müşteride kalır (Sinan; koordinatör anlamı). (2) Kullanıcı Satış azaltma komutunda bu sırayı değiştirebilir (Sinan; koordinatör anlamı); açık seçim varsayılandan üstündür (KLN-D30 (3) ile aynı kural). (3) Bırakılacak toplam: satıra bağlı fiziksel tahsis + aktif gelecek ayırma toplamının yeni açık miktarı (yeni satır miktarı − sevk edilen) aşan kısmıdır; bu kısım önce gelecek ayırmalardan (aralarında KLN-D30 sırasıyla), gelecek ayırmalar yetmezse fiziksel tahsisten bırakılır (**yazarın okumasıdır, geri alınabilir**; KLN-D28 (4)'ün fiziksel tahsisli satıra uzantısı). (4) Fiziksel tahsisin bırakılması Satış/Stok **fiziksel tahsis kuralıyla** yapılır (koordinatör iletimi); bu karar fiziksel tahsisin etiket bazlı kuralını veya fiziksel tahsiste talep üst sınırı kuralını (spec §9.1 aday 3, `NEEDS-SPEC`) seçmez. Etiket bazlı tahsis kısmi bırakılamıyorsa bu durum F017-s'de `NEEDS-SPEC` olarak sorulur; tahminle seçilmez. (5) Golden: fiziksel 10 + gelecek 10, satır 20 → 15 (sevk 0) → gelecekten 5 bırakılır, fiziksel 10 kalır. | Stok bırakma portu, Satış azaltma komutu, Satış/Stok fiziksel tahsis (F017-s; cross-module K3) |

## Uygulama notları

- **KLN-D28 — F017-s.** F017-s'nin kapsamına Satış satır iptali ve azaltmasında Stok bırakma portu çağrısı eklenir
  (cross-module K3; Satış + Stok + host). Satış komutu kendi expected version, idempotency ve audit kaydını tutar; Stok
  bırakması aynı host işleminde, satır kilidi altında çalışır. Testler: iptal bütün ayırmaları bırakır; azaltma yalnız
  max(0, ayırma − (yeni miktar − sevk edilen)) kadar bırakır (kısmi ayrılmış ve kısmi sevkli satır golden'ı); bırakma başarısızsa Satış komutu da yazılmaz; replay ikinci bırakma üretmez; gerçek SQL'de
  Satış iptali × mal kabul çevirmesi yarışı.
- **KLN-D30/KLN-D31 — F017-s.** Bırakma sırası Stok bırakma portunun içindedir; Satış yalnız açık seçimi iletir.
  Testler: 30 t → 20 t, 10 t Aralık + 20 t Ocak → Ocak'tan 10 t; açık seçim varsayılanı geçer; eşitlik kuralı
  deterministik; fiziksel 10 + gelecek 10, satır 20 → 15 → gelecekten 5. F017-s kodu artık bu sıra kararı için beklemez.
- **Satış şeridi karşılığı.** Satış tarafının karar satırı (Satış iptal/azaltma komutunun Stok portunu çağırması) Satış
  şeridinindir; bu belge SAT-D kimliği açmaz. Satış tarafı karşılığı: **SAT-D314**
  (`SINAN_KARARLARI_2026-11-12_SATIS_EK60.md`, #1119 — merge bekliyor). İki metin aynı bırakma kuralını yazar.
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

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz.
- **Kapatma yoktur.** `KARAR_SICILI`/`ACIK_KARARLAR` değişmez; `OPEN-*` kapatma insan kapısıdır.
- **Seçilmeyenler:** (1) Fiziksel tahsiste talep üst sınırı kuralı (spec §9.1 `NEEDS-SPEC` adayı 3; bırakma sırası
  KLN-D31 ile cevaplandı, üst sınır kuralı açık). (2) Etiket bazlı fiziksel tahsisin kısmi bırakılıp bırakılamayacağı
  (KLN-D31 (4)). (3) KLN-D29 "canlandı" bildiriminin alıcısı ve kanalı. Bunlar ilgili dilimde
  `NEEDS-SINAN`/`NEEDS-SPEC` olarak sorulur; bu belge tahminle seçmez. Birden çok gelecek ayırma arasında bırakma sırası
  ve fiziksel/gelecek sırası artık KLN-D30/KLN-D31'dedir.
- "Yazarın okumasıdır (geri alınabilir)" ve "koordinatör anlamı" diye işaretli noktalar Sinan'ın sözü değildir; Sinan
  yalnız seçenek adını seçmiştir. Sinan farklı isterse ayrı karar gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
