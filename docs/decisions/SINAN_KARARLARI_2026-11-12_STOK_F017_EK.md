# Sinan Kararları — 2026-11-12, 21. tur madde 2–3 (Stok: gelecek tedarik ayırmasının satış iptali/azaltmasında bırakılması, canlanan alış satırında bırakılmış ayırmalar)

Bu belge önceki karar dosyalarını silmez; **KLN-D28 ve KLN-D29**'u ekler. F-017 Stok spec'inin
(`docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md`, PR #1116) §9.1'deki iki `NEEDS-SINAN` adayını Sinan kararına
bağlar. `SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md` (KLN-D25…KLN-D27, SAT-D308; KLN-D26 F-017'nin
Stok tarafı), Alış eşleri ALS-D82…ALS-D91 (`SINAN_KARARLARI_2026-11-12_ALIS_EK17.md`, PR #1110; merge edilince `main`de),
DEC-STK-002/003/004/005/006, DEC-SAL-004 ve DEC-SAL-005 geçerlidir. Bu belge onların karar metnini değiştirmez; KLN-D28,
DEC-SAL-005'in "önizleme olmadan iptal yok" kuralını kaldırmaz (önizleme gelecek ayırmayı gösterir, bırakma onaylanan
iptalin parçası olur). 11-12 Stok karar dosyası ayrı PR'da (#1121) review'dadır; bu belge o dosyayı düzenlemez.
`KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.

**Kimlikler:** KLN-D serisinde `main`deki (`cf0ae197f`) son kimlik KLN-D26'dır; KLN-D27 açık PR'dadır. `main`de
ve açık PR dallarının eklemelerinde KLN-D28 ve KLN-D29 geçmiyordu (koordinatör doğrulaması; `git grep -ohE "KLN-D2[89]"
origin/main -- docs` boş). Gelecek tedarik ayırması Kalan Modüller (Stok) şeridinin işi olduğu için KLN-D kullanıldı;
yeni ön ek icat edilmedi.

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
| 21. tur m.3 | İptal veya API-11 kapanışında bırakılmış ayırmalar, alış satırı canlanınca kendiliğinden geri gelsin mi? (#1116 §9.1 aday 2, #1110 §3.8) | "Geri gelmez, yeniden ayrılır (Önerilen)" | Canlanan miktar serbest açık miktar olur; eski ayırmalar kendiliğinden dönmez, gerekiyorsa kullanıcı yeniden ayırır; sistem "canlandı" bildirimi gösterir. Seçilmeyen: bırakılmış ayırmaların kendiliğinden geri gelmesi. |

## Kararlar

| Kimlik | Konu | Karar | Etkilenen yüzey |
|---|---|---|---|
| KLN-D28 | Satış sipariş satırı iptal edilir veya azaltılırsa gelecek tedarik ayırması (21. tur m.2) | (1) Gelecek tedarikten ayrılmış satış sipariş satırı **iptal edilir veya miktarı azaltılırsa** o satıra bağlı gelecek ayırma **otomatik bırakılır** (Sinan). (2) Satış iptal/azaltma komutu **aynı işlemde Stok bırakma portunu** (`IInventoryFutureAllocationReleasePortV1`, spec §5) çağırır; **artan ayırma kalmaz**; bırakma **audit'lidir** (koordinatör anlamı). (3) "Aynı işlem" = Satış komutunun tek host işlemi; Satış Stok tablosuna yazmaz, Stok bırakmayı kendi portuyla ve kendi kurallarıyla (spec §3.2 adım 0'daki satır kilidi, sürüm, audit, outbox `inventory.future-allocation.released.v1`) yapar; modüller birbirine referans vermez, host participant/port deseniyle bağlanır (AGENTS "Bir modül başka modül tablosuna doğrudan yazmaz"; **yazarın okumasıdır, geri alınabilir**). (4) **İptalde** satıra bağlı bütün aktif gelecek ayırmalar bırakılır. **Azaltmada** yalnız satış satırının yeni miktarını aşan ayırma kısmı bırakılır: bırakılan miktar = **max(0, satıra bağlı aktif gelecek ayırma toplamı − yeni satır miktarı)**; yeni miktar içinde kalan ayırma aktif kalır. min(kalan ayırma, azaltılan miktar) kullanılmaz; kısmi ayrılmış satırda gereğinden fazla bırakır (örnek: 10 t satırın 4 t'si ayrılmış, satır 7 t'ye inerse bırakılan 0'dır, 3 değil). Karşılaştırma tek birimde yapılır; çevrim SAT-D298'deki merkezi servisledir. "Artan ayırma kalmaz" bu iki duruma birlikte karşılık gelir (**yazarın okumasıdır, geri alınabilir**; SAT-D314 yazar okumasıyla aynı). (5) Bırakma **exactly-once ve idempotenttir**: aynı Satış komut anahtarıyla gelen tekrar (replay) ikinci bırakma üretmez, saklanan sonucu döner; aynı ayırma ikinci kez bırakılmaz (DEC-STK-004 "atomik, idempotent"; **yazarın okumasıdır**). Bırakma **silme değildir**: ayırma `Released` olur, audit ve outbox (`inventory.future-allocation.released.v1`) aynı işlemde yazılır. (6) Bırakma **fiziksel hareket değildir**; stok bakiyesi, serbest fiziki ve maliyet değişmez (DEC-STK-003, KLN-D26 (3)). (7) Bırakma portu okunamaz, hata verir, kilit alınamaz veya bırakma başarısız olursa **Satış iptali/azaltması da yazılmaz** (aynı işlem, fail-closed; tümüyle geri alınır); satış satırı iptal edilmiş, ayırma aktif kalmış durum oluşmaz (kapalı yön; **yazarın okumasıdır, geri alınabilir**). (8) Bırakma nedeni Satış kaynaklıdır; neden kodu ve `origin` değeri (örneğin `sales-line-cancelled` / `sales-line-reduced`, `origin = sales`) F017-s'de adlandırılır (**yazarın önerisi, geri alınabilir**). (9) DEC-SAL-005 geçerlidir: iptal önizlemesi gelecek ayırmaları gösterir; önizleme okunamazsa iptal reddedilir (spec §5.1 (b)). Bu karar önizlemeyi kaldırmaz, onaylanan iptalin ayırmayı kendisinin bırakmasını sağlar. (10) **Satış tarafı karşılığı: SAT-D314** (`SINAN_KARARLARI_2026-11-12_SATIS_EK60.md`, #1119 — merge bekliyor); aynı Sinan cevabından (6822767886 m.2) Satış komutunun tarafını yazar. | Satış satır iptali ve miktar azaltma komutları, Stok bırakma portu, Satış tahsis okuması (F017-s; cross-module K3) |
| KLN-D29 | Canlanan alış satırında önceden bırakılmış ayırmalar (21. tur m.3) | (1) İptal ("bağları düşür ve iptal et", KLN-D26 (6)(d) / ALS-D85) veya API-11 kapanışında (`purchase-line-closed`) bırakılmış ayırmalar, alış satırı **canlanınca** (ALS-D91, Sinan 19. tur 6822140358; #1110 §3.8) **geri gelmez** (Sinan). (2) Canlanan miktar **serbest açık miktar** olur; eski ayırmalar kendiliğinden dönmez; **gerekiyorsa kullanıcı yeniden ayırır**; sistem **"canlandı" bildirimi** gösterir (koordinatör anlamı). (3) Yeniden ayırma normal oluşturma yoludur (spec §3.2): satır kilidi, Alış portundan kilit altında okunan `AllocatableText`, değişmez kontrolü, izin ve idempotency aynen uygulanır; canlanma için ayrı veya öncelikli bir ayırma yolu açılmaz (**yazarın okumasıdır, geri alınabilir**). (4) Bu karar, kabul geri almada **tüketilmiş** (`Consumed`) ayırmanın aynı kimlikle yeniden açılmasını (KLN-D26 (6)(e), ALS-D86/ALS-D91, spec §3.7) değiştirmez; KLN-D29 yalnız daha önce `Released` olmuş ayırmalara uygulanır (**yazarın okumasıdır**). (5) Bildirim bir olay ve okuma alanıdır; canlanmanın sahibi Alış'tır ve olayı Alış canlanma komutu yazar; bildirimin kime (bırakılmış ayırmanın satış kullanıcısı, satınalma kullanıcısı) ve hangi kanalla (ekran uyarısı, e-posta, anlık bildirim) gideceği bu kararla seçilmemiştir (Seçilmeyenler). | Alış canlanma komutu (#1110 §3.8, Alış K3), Stok gelecek ayırma okuması, Satış/Alış ekran uyarıları |

## Uygulama notları

- **KLN-D28 — F017-s.** F017-s'nin kapsamına Satış satır iptali ve azaltmasında Stok bırakma portu çağrısı eklenir
  (cross-module K3; Satış + Stok + host). Satış komutu kendi expected version, idempotency ve audit kaydını tutar; Stok
  bırakması aynı host işleminde, satır kilidi altında çalışır. Testler: iptal bütün ayırmaları bırakır; azaltma yalnız
  max(0, ayırma − yeni miktar) kadar bırakır (kısmi ayrılmış satırda fazla bırakmaz); bırakma başarısızsa Satış komutu da yazılmaz; replay ikinci bırakma üretmez; gerçek SQL'de
  Satış iptali × mal kabul çevirmesi yarışı.
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

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz.
- **Kapatma yoktur.** `KARAR_SICILI`/`ACIK_KARARLAR` değişmez; `OPEN-*` kapatma insan kapısıdır.
- **Seçilmeyenler:** (1) Satış satırı azaltılırken aşan kısım birden çok gelecek ayırmaya yayılıyorsa **hangi ayırmanın
  önce bırakılacağı** bu kararla seçilmemiştir. Yazar bir öncelik politikası icat etmez; spec karar gelene kadar kapalı
  yönü yazar: aşan kısım tek bir ayırmadan karşılanamıyor ve kullanıcı bırakılacak ayırmaları komutta seçmemişse azaltma
  komutu yazma olmadan reddedilir (**yazarın okumasıdır, kapalı yön, geri alınabilir**). Kural F017-s'de `NEEDS-SPEC`
  olarak sorulur. (2) Azaltmada fiziksel ve gelecek tahsis birlikte aşıyorsa hangisinin önce bırakılacağı (fiziksel
  tahsis Satış/Stok fiziksel tahsis kuralıdır; spec §9.1 `NEEDS-SPEC` adayı 3 ile bağlı). (3) KLN-D29 "canlandı"
  bildiriminin alıcısı ve kanalı. Bunlar ilgili dilimde `NEEDS-SINAN`/`NEEDS-SPEC` olarak sorulur; bu belge tahminle seçmez.
- "Yazarın okumasıdır (geri alınabilir)" ve "koordinatör anlamı" diye işaretli noktalar Sinan'ın sözü değildir; Sinan
  yalnız seçenek adını seçmiştir. Sinan farklı isterse ayrı karar gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
