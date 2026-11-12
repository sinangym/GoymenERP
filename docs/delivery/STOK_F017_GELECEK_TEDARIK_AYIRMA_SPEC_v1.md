# F-017 — Gelecek tedarikten ayırma: Stok tarafı spec ve dilim planı v1

Tarih: 2026-11-12 · Hazırlayan: Claude (Stok / Kalan Modüller şeridi) · Durum: **TASLAK** (plan; iş kararı değildir)
Kod atıfları main `58b96aa28`'de doğrulandı; Satış tahsis portu atfı main `cf0ae197f`'dendir. Bu belge yalnız dokümandır:
kod, migration, OpenAPI, manifest, izin veya grant değiştirmez.

Revizyon 2 (2026-11-12): §9 soruları Sinan #66 5. tur (madde 3–4) ve 11.–13. tur (yorum 6820601088, madde 1, 3, 6–9)
cevaplarıyla kapandı; karar metni KLN-D26 (6) ve Alış tarafında ALS-D83…ALS-D90 (PR #1110). §3.5, §4.3, §6, §8 ve
§10 buna göre güncellendi. S6 (izin adı) ve S9 (genel/teknik satır) teknik tercihtir, gerekçesi §9'dadır; S10
SAT-D298 ile kilitlidir.

Revizyon 3 (2026-11-12): #1116 DOCS-REVIEW A 6822074287 ve B 6822117507 bulguları ile Sinan #66 6822140358 (19. tur)
kararı işlendi. Alış karşılaştırması #1110 head `87dc49fb2` (rev6) üzerinden yapıldı. Değişenler:

- **Dilim sırası (A/B P2-1):** F017-b yalnız servis, kayıt ve bırakmadır; endpoint ve izin kaydı yoktur. Oluşturma ve
  bırakma endpoint'i, OpenAPI ve izin adı kaydı yeni **F017-b2** dilimindedir (≈ #1110 F17-2b). #1110 kapı kuralı §10'a
  alındı. F017-d artık F017-b2'ye bağlıdır.
- **Kilit (A/B P2-2):** `IPurchaseLineSerializationLockV1` §3.2'de bağlayıcı **adım 0** oldu. Bırakma, kabul çevirmesi,
  geri alma ve canlanma aynı kilit altındadır (§3.3, §3.5, §3.7, §4.3). §7'ye 409 `purchasing_line_busy`, §8'e gerçek SQL
  yarış testleri eklendi.
- **"Satır kapanırsa" (A/B P2-3):** API-11 kapanışına bağlandı. İptal KLN-D26 (6)(d) / ALS-D85 yoludur (§3.6). Kabul
  katılımcısındaki "kalan sıfır" dalı çıkarıldı. Bırakma portu çağıranlarına API-11 kapanışı eklendi (§5).
- **Kabul geri alma (A P2-4):** ALS-D91 ve 19. tur kararıyla yeniden yazıldı; ayrı bölüm §3.7 ve ayrı dilim **F017-e**
  (≈ #1110 F17-6). Yanlış "mevcut akış" ifadesi düzeltildi. API-44/60 için geçici 409 kapısı yazıldı (§10).
- **Satış tahsis portu (B P2-4):** `ISalesOrderAllocationReadPortV1` ele alındı (§5.1). Gelecek ayırma ayrı türle,
  sürümlü portta bildirilir. Satış şeridiyle cross-module K3 dilimi **F017-s** eklendi. Bırakma sahibi `NEEDS-SINAN`
  adayıydı (§9.1; rev4'te KLN-D28 ile cevaplandı).
- **P3'ler:** bayat #1108 atıfları; ayrılabilir formülü (§3.1); Alış alanı çelişkisi (§5); sıra zinciri (§3.5); neden
  kodları ve olay alanları (§3.3); termin ve uyarı sahibi (§5, §8); S11 süre notu ve DEC-STK-005 atfı (§3.3, §9); CHECK
  snapshot anlamı (§4.2); talep tarafı üst sınırı (§3.2).

Revizyon 4 (2026-11-12): Sinan #66 6822767886 (21. tur, madde 2–3) §9.1'deki iki `NEEDS-SINAN` adayını cevapladı; karar
metni `SINAN_KARARLARI_2026-11-12_STOK_F017_EK.md` (KLN-D28, KLN-D29). Madde 1 Satış/Lojistik şeridinindir; alınmadı.
Değişenler:

- **Satış satırı iptali/azaltması (KLN-D28):** §5.1 (c)'deki "iptal reddedilir" kapalı yönü kalktı. Satış iptal/azaltma
  komutu aynı host işleminde Stok bırakma portunu çağırır; iptalde bütün ayırmalar, azaltmada max(0, ayırma − (yeni
  miktar − sevk edilen)) bırakılır [**tarihçe; geçersiz formül:** yerine KLN-D28 (4) tek tanımı geçer (fazla = fiziksel +
  gelecek − açık miktar; Revizyon 6/7, #66 6823547103)]; bırakma exactly-once'dır; başarısızsa Satış komutu da yazılmaz (§3.3, §4.3, §5, §5.1, §7). Satış
  karşılığı SAT-D314 (#1119, `main` `c93433f6`). F017-s kapsamı bu çağrıyı
  içerir (§10). Testler §8'de.
- **Canlanmada bırakılmış ayırmalar (KLN-D29):** geri gelmez; canlanan miktar serbest açık miktardır, kullanıcı gerekirse
  yeniden ayırır, sistem "canlandı" bildirimi gösterir. §3.6'daki "yazarın okuması" Sinan kararına bağlandı; §3.7, §8.
- §9'a S13 ve S14 eklendi; §9.1'de adaylar 1–2 cevaplı, `NEEDS-SPEC` adayı 3 açık kaldı; azaltmada bırakılacak ayırmanın
  seçimi (aday 4) ve bildirimin alıcısı/kanalı (aday 5) yeni adaylardır.

Revizyon 5 (2026-11-12): Sinan #66 6823243819 (23. tur, madde 1–2) azaltmada bırakma sırasını cevapladı; karar metni
aynı dosyada KLN-D30, KLN-D31. Madde 3 alınmadı. Değişenler:

- **Birden çok gelecek ayırma (KLN-D30, aday 4):** en geç gelen (alış satırının beklenen teslim tarihi) önce bırakılır;
  kullanıcının komuttaki açık seçimi üstündür. Geçici "seçim yoksa azaltma 409, yazma yok" kapalı yönü kalktı (§5.1 (c),
  §7, §8). SAT-D314 "reddetmez" ile gerilim kalmadı.
- **Fiziksel tahsis + gelecek ayırma (KLN-D31, aday 3'ün bırakma sırası):** önce gelecek ayırma bırakılır; kullanıcı
  değiştirebilir; fiziksel tahsis bırakma Satış/Stok fiziksel tahsis kuralıyladır. Aday 3'ün talep üst sınırı kısmı açık.
- §8'e iki golden, §9'a S15–S16 eklendi; F017-s bağımlılığı karşılandı (§10).

Revizyon 6 (2026-11-12): #1116 r2 DOCS-REVIEW A 6823510860 ve B 6823489628 bulguları ile Sinan #66 6823547103 (25. tur
m.1 "Otomatik bırakılır (Önerilen)" ve aynı yorumdaki koordinatör formül düzeltmesi). Karar metni KLN-D32. Değişenler:
- **Tek tanım (A P2-1, B P2-1):** fazla = max(0, aktif fiziksel tahsis + aktif gelecek ayırma − (yeni satır − sevk))
  KLN-D28 (4)'te bir kez yazılır; §5.1 (c), §8 ve KLN-D30/KLN-D31/KLN-D32 ona atıf yapar. Fiziksel 0 iken eski formülle
  aynı sonucu verir; mevcut golden'lar değişmez.
- **Fiziksel fazla ve ara durum (KLN-D32, A P2-3):** gelecek yetmezse fiziksel tahsis otomatik bırakılır; fiziksel bırakma
  yolu main'e gelene kadar böyle azaltma bütünüyle reddedilir (yazma yok). #1119 SAT-D314/SAT-D315 hizası koordinatörden
  istenir.
- **Termin kaynağı (A P2-2, B P2-2):** tarih Alış `GetLineScheduleAsync`'ten (#1110 F17-1b, #1101) kilit altında okunur;
  çok dilimli satırda teslim alınmamış en geç dilim; okunamazsa fail-closed, dilimsiz satır tarihsiz. F017-s bağımlılığına
  F17-1b eklendi (§10).
- **Kısmi bırakma modeli (B P2-3):** `ReleasedQuantity`; aynı satır kimlik, oluşturma zamanı ve `ManualSequence`'i korur;
  `released.v1` bırakılan ve kalan miktarı taşır (§3.3, §3.4, §4.1, §4.2).
- **Talep tarafı çapası (B P2-4):** satış satırı kilidi; sıra satış satırı → alış satırları (artan); yarış testi
  değişmezleri (§3.2 adım 0 ve 5, §4.3, §8).

Revizyon 7 (2026-11-12): koordinatör notu (oturum mesajı, 6823760388 sonrası). #1119 (SAT-D314/SAT-D315 25. tur hizası)
`main`de `c93433f6`. Değişenler:
- **Ara red kodu ve sahibi (KLN-D32 (3)):** ret Stok bırakma portunundur; §3.2 kilitleri (satış satırı, sonra alış
  satırları) alındıktan sonra, hiçbir yazmadan önce tespit edilir. Kod 409 `inventory_future_allocation_physical_release_unavailable` (öneri,
  F017-s'de kesinleşir); Satış problemine taşınır (§5.1 (c), §7). §8'e test eklendi.
- #1119 "merge bekliyor" atıfları `main` `c93433f6`'ya çevrildi; SAT-D314/SAT-D315 25. tur ek kaynağıyla tek tanıma ve
  ara redde hizalıdır.
- **P3:** açık seçim `ExpectedAllocationVersion` taşır; fazlayı aşan seçim tek kod 422; termin değişikliği sırayı yalnız
  aynı işlemde okunan tarihle etkiler, sıra audit'e yazılır; §9 S17, §9.1, §10, §11.

Revizyon 8 (2026-11-12): r3 B 6824169369 ve A 6824158169 bulguları, koordinatör yönlendirmesiyle. Değişenler:
- **Terminal ret ve replay sahibi (B P2-1, A P3-2):** Stok portu 409'da hiçbir şey yazmaz (Stok satırı, audit, outbox,
  ayırma değişikliği yok). Terminal ret kaydı ve aynı anahtarla replay, idempotency anahtarının sahibi Satış revizyon
  komutunundur (#1119 plan); replay Stok portunu ikinci kez çağırmaz. 503/okunamaz ve satış satırı kilidi meşgul 409'u
  terminal değildir; kaydedilmez, aynı anahtar yeniden çalışır. §5.1 (c), §8.
- **Açık fiziksel seçim (B P2-2):** ara 409, bırakma fiziksele dokunduğu her durumda geçer; kullanıcının açık fiziksel
  seçimi de dahildir (#1119 SAT-D314 (2)/SAT-D315). §5.1 (c), §7, §8 golden.
- **Revizyonda satır silme (B P2-3, koordinatör kararı):** porta yeni miktar 0 olarak (azaltma) gelir, iptal olarak
  değil. §3.3, §5.1 (c), §8.
- **Yeniden açma kilidi (B P2-4):** §3.7 `Consumed` → `Active` iptal ile aynı kilit sırasını alır ve satış satırını
  kilit altında yeniden okur. §3.2 adım 0, §3.7, §8 yarış testi.
- **P3:** çok satış satırlı kilit sırası; durum kuralı tek metin (§4.1) ve §4.2 CHECK tamamlandı; karar dosyasında eski
  formül, #1119 atfı ve kaynak etiketleri düzeltildi.

Revizyon 9 (2026-11-12, #1116 merge sonrası takip): koordinatör düzeltmesi (koordinatör oturum mesajı, 2026-11-12 13:04Z; #66'da kayıtlı değil), #1116 r4 review'larının
P3'leri (B 6824462459, A 6824478357) ve #1126 r1 bulguları (A 6824763711, B 6824774406). Değişenler:
- **Revizyonda satır silme (koordinatör düzeltmesi; rev8 metninin yerine geçer):** SAT-D167 (`SINAN_KARARLARI_2026-11-02_SATIS_EK24.md`
  :52, Satış README) "sevk edilmemiş" satırı hiçbir sevk, yükleme talimatı veya stok (fiziksel) tahsisi olmayan satır
  diye okur. Satır kaldırma Stok portuna yalnız böyle bir satır için gelir; bu satırda yalnız gelecek ayırma vardır ve
  yeni miktar 0 azaltmasıyla hepsi bırakılır. Fiziksel tahsisli veya yükleme talimatlı satırın kaldırılmasını Satış
  porttan önce 422 `sales_line_revision_line_shipped` ile reddeder. Fiziksel bırakma gerektiren ara 409 yalnız miktar
  azaltmada oluşur. SAT-D167 metni değişmez. §5.1 (c), §8, golden; KLN-D28 (4).
- **P3:** yeniden açma tavanı F017-e `NEEDS-SPEC` adayı (§3.7, §9.1 aday 6); yeniden açmadaki deneme ifadesi §3.2 adım 0
  ile aynı; kilitler komutun ilk yazımından önce alınır; §4.3 listesine yeniden açma eklendi; terminal / terminal olmayan
  kod listesi tamamlandı; KLN-D32 (3) tetik ifadesi netleşti; Revizyon 4 bloğundaki eski formül tarihçe olarak işaretlendi;
  #1110 merge durumu (`4f3978d3`) ve problem kodu adları #1110 ile hizalandı;
- **#1126 r1:** karışık 409/503 sonucunda 503 kazanır ve port bütün okumaları bitirmeden 409 dönmez (§5.1 (c), §7, §8;
  koordinatör kararı (c)); 422 seçim reddi ve `inventory_allocation_state_conflict` sınıfları; silmede fiziksel tahsis
  yarışı notu ve testi (F017-s); aday 6 §10 ve §11'e eklendi;
- **#1126 r2:** "503 kazanır" kuralı bütün kaydedilen terminal retleri (409 veya 422) kapsar; "okumalar bitmeden ret
  yok" cümlesi kaydedilen 409/422'ye daraltıldı, kilit meşgul 409'u okumalardan önce gelir (A 6824996695 P2-1, B
  6825017340 P3-1); 422 + termin okunamaz testi; §3.7'deki eski #1110 cümlesi güncellendi; ALS atıfları main'e göre
  (ALS-D82…ALS-D93, 19. tur = ALS-D92, KLN-D29 eşi ALS-D93); KLN-D28 (4) yarış istisnasına atıf; Satış iptal testinde durum adı §4.1'e bağlandı; #1110 kilit sırası koordinasyon notu;
- **#1126 P3 takip (2026-11-12):** çok satırlı karışık sonuç kuralı: terminal ret ile terminal olmayan sonuç birlikteyse
  terminal olmayan kazanır, 503 varsa 503, yoksa kilit meşgul 409; kayıt yok, aynı anahtar yeniden çalışır (§5.1 (c),
  §7, §8; koordinatör okuması #66 6825436733; Satış X1 ile hizalı); §7 Stok bırakma ret kodu 409/422/503; "retde" →
  "rette".

**Kaynaklar (öncelik sırasıyla):**

- **KLN-D32** — aynı karar dosyası; Sinan 25. tur, #66 yorum 6823547103: m.1 "Otomatik bırakılır (Önerilen)" (gelecek
  ayırma yetmezse fiziksel tahsis fazlası). Formül ve golden'lar aynı yorumdaki koordinatör düzeltmesidir (Sinan metni değil).
- **KLN-D30, KLN-D31** — aynı karar dosyası; Sinan 23. tur, #66 yorum 6823243819: m.1 "En geç gelen önce (Önerilen)"
  (azaltmada birden çok gelecek ayırma), m.2 "Önce gelecek ayırma (Önerilen)" (fiziksel tahsis + gelecek ayırma).
- **KLN-D28, KLN-D29** — `docs/decisions/SINAN_KARARLARI_2026-11-12_STOK_F017_EK.md`; Sinan 21. tur, #66 yorum 6822767886:
  m.2 "Otomatik bırakılır (Önerilen)" (satış satırı iptal/azaltılınca gelecek ayırma), m.3 "Geri gelmez, yeniden ayrılır
  (Önerilen)" (canlanan alış satırında bırakılmış ayırmalar). Koordinatörün "Anlamı" satırları ve yazarın okumaları o
  belgede ayrı işaretlidir.
- **Sinan 19. tur** — #66 yorum 6822140358: iptal edilmiş sipariş veya API-11 ile kapatılmış satırda kabul geri alınırsa
  ya da "yerine yenisi gelecek" işaretli iade yapılırsa miktar açılır, sipariş canlanır. Canlanan miktar gelecek
  tedarikten yeniden ayrılabilir. Alış eşi ALS-D92: ALS-D91 bu iki durumda koşulsuz uygulanır (§3.7).
- **KLN-D26** — `docs/decisions/SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md` (main `2efb3f68d`,
  #1108 merge edildi). Sinan, #66 yorum 6819147253 madde 2: "Açık satınalma siparişi satırından satış siparişine **miktar
  bazında (kg/ton)** ayırma yapılabilir; aynı miktar iki siparişe verilemez. Mal kabulde ilgili satış talebine bağlanır,
  etiket ataması o zaman yapılır."
- **ALS-D82…ALS-D93** — Alış şeridinin eş kararları (`SINAN_KARARLARI_2026-11-12_ALIS_EK17.md`, PR #1110) ve Alış spec'i
  `docs/delivery/ALIS_F017_GELECEK_TEDARIK_AYIRMA_v1.md` (PR #1110, `main` `4f3978d3` ile merge edildi). Port
  sözleşmesi o belgenin §3'ündedir. Bu belge onu kopyalamaz, atıf yapar ve Stok tarafını hizalar (§5).
- `KARAR_SICILI`: DEC-STK-002 (gelecek arz serbest stoğa karışmaz), DEC-STK-003 (tahsis alış satırından da yapılır;
  fiziksel hareket değildir), DEC-STK-004 (atomik, idempotent, serbest miktar aşılmaz; DB kısıtı + concurrency testi
  zorunlu), DEC-STK-005 (`ReservedUntil` ve süre dolunca sessiz serbest bırakma yok), DEC-STK-006 (etki türleri ayrı;
  sahip, neden, kaynak, süre ayrı saklanır), DEC-SAL-004 (satır karma kaynakla karşılanabilir; parçaların toplamı satır
  miktarına eşittir; plandaki stok kısmı tahsisten türetilir), DEC-SAL-005 (sipariş iptalinde aktif tahsis gösterilir;
  önizleme olmadan iptal yok).
- ALS-D30 (`SINAN_KARARLARI_2026-11-02_ALIS_EK3.md`): "aynı ton ikinci kez bağlanamaz; mal gelince fiziksel tahsise döner".
- SAT-D298 (`SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md`): birim çevrim kaynağı ürün kartındaki katsayıdır;
  katsayı yoksa `NoSource`, tahmini katsayı yok; ekran yeniden hesaplamaz.
- Fable analizi F-017 satırı (`docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md:409`) ve
  `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md` B-20 (`:261`) / B-23 (`:264`).

## 1. Kapsam

**İçinde (Stok):**

1. Onaylı satış sipariş satırı için, onaylı ve açık bir satınalma sipariş satırından **miktar bazında gelecek ayırma**
   oluşturma ve bırakma (iptal).
2. "Aynı miktar iki siparişe verilemez" değişmezi: bir satınalma satırına bağlı aktif gelecek ayırmaların toplamı, Alış'ın
   o satır için verdiği ayrılabilir miktarı aşamaz. Ortak satır kilidi + DB kısıtı + concurrency testi ile.
3. Mal kabulde, aynı host işleminde, kabul edilen miktar kadar gelecek ayırmayı **fiziksel tahsise çevirme** ve etiketi
   satış sipariş satırına **o anda atama** (KLN-D26 (4)).
4. Stok'un Alış'a "bu satırda ne kadar ayrılmış" bilgisini veren okuma portu (PR #1110 §3.2) ve Alış komutlarının
   çağırdığı bırakma portu (PR #1110 §3.5).
5. Kabul geri alma ve iadede ayırmanın Stok tarafı (§3.7, F017-e).
6. Satış'ın tahsis okumasında gelecek ayırmaların ayrı türle bildirilmesi ve satış satırı iptali/azaltmasında Satış
   komutunun aynı işlemde Stok bırakma portunu çağırması (KLN-D28; §5.1, F017-s).

**Dışında:**

- Alış'ın ayrılabilir miktar portu ve Alış tarafı davranışı (Alış şeridi, PR #1110 §3.1, F17-1).
- Satış kaynak planında "alıştan" seçimi (MOD-16, Satış şeridi; PR #1110 F17-5).
- Üretim planı ve "ürün seviyesinde gelecek arz" kaynakları (DEC-STK-003'ün diğer kaynakları; ayrı iş).
- Satınalma tarafının bağ düşürme, "bağları düşür ve iptal et", API-11 kapanışı, kabul düzeltmesi (API-44), iade (API-60)
  ve canlanma komutları (Alış şeridi, PR #1110 §3.5–§3.7). Stok bunlar için bırakma ve yeniden açma yollarını sağlar.
- Uyarıların bildirim kanalı (e-posta/anlık bildirim); bu belge yalnız olay ve okuma alanını tanımlar.

## 2. Bugünkü durum (kod atıflı)

| Konu | Durum | Kanıt |
|---|---|---|
| Tahsis talep türleri | Yalnız `sales.order-line` ve `manufacturing.plan-input` | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryAllocationContractsV1.cs:10-19` |
| Tahsis domain modeli | Tek bir **fiziksel** etiket + pozisyon üzerinde; `Create` yalnız pozisyonla çalışır | `…Inventory.Domain/Allocations/StockAllocation.cs:23-66` |
| Serbest miktar | `Unreserved = serbest fiziki − aktif tahsis` | `…Inventory.Domain/Allocations/StockAllocation.cs:76-77` |
| Tahsis satırı | `LabelId`, `LocationId`, `OwnerId`, `CustodianId` zorunlu; gelecek kaynak için yer yok | `…Inventory.Infrastructure/Persistence/InventoryRows.cs:191-221` |
| DB kısıtları | `CK_Allocations_Identity` etiket/konum boş olamaz; `CK_Allocations_State` `[Block] = 1` (Free); `CK_Allocations_Demand` yalnız iki tür | `…Inventory.Infrastructure/Persistence/InventoryDbContext.cs:442-481` |
| Fiziksel tahsis oluşturma | Etiket sürümü beklenir (`:55-58`), tek Free satır aranır (`:65-72`), aktif tahsis düşülüp serbest miktar kontrol edilir (`:83-93`), etiket sürümü artırılarak aynı etikete eşzamanlı tahsis sıralanır (`:129`), komut + audit aynı save'de (`:132-136`), unique/concurrency hatasında replay veya 409 (`:142-153`) | `…Inventory.Infrastructure/Persistence/InventoryStore.Allocations.cs` |
| Eşzamanlılık çapası | Etiketin `Version` alanı concurrency token, ayrıca `RowVersion` | `…InventoryDbContext.cs:206-207` |
| Host doğrulaması | Satış satırı `ISalesOrderLineReferencePortV1` ile onaylı mı okunur; izin önce ve sonra ölçülür | `src/backend/GoymenERP.Api/Endpoints/InventoryPhysicalAllocationHostPorts.cs:47-51`, `:137-171`, `:186-223` |
| Mevcut izin | `inventory.physical-allocation.write` | `…Inventory.Application/InventoryOpeningContractsV1.cs:17` |
| HTTP | `POST /physical-allocations`, `/release`, `/change` | `src/backend/GoymenERP.Api/Endpoints/InventoryEndpoints.cs:1115`, `:1196`, `:1279`, `:1695` |
| Mal kabul | Alış kabul payı + Stok girişi **aynı host işleminde**; Stok tarafı lot, etiket, giriş hareketi, bakiye, komut, audit yazar | `src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs:32-43`; `…Inventory.Application/InventoryGoodsReceiptContractsV1.cs:14-31`, `:62-85`; `…Inventory.Infrastructure/Persistence/InventoryGoodsReceiptParticipant.cs:92`, `:144-149` |
| Kabul etiketi | Bir kabul = bir etiket; ilk blok bugün sabit `"Free"` | `…Purchasing.Application/Contracts/PurchaseGoodsReceiptContractsV1.cs:45` |
| Outbox | Stok'un kendi outbox tablosu var | `…InventoryDbContext.cs:75`, `:599` |
| Alış tek satır portu | `IPurchaseSupplyReferencePortV1.GetApprovedLineAsync` — onaylı satırın `CommittedText`, `AcceptedText`, `RemainingText`, birim kimliği, sipariş `Version`; şirket bağlamdan okunur, başka şirket `NotFound` | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Application/Contracts/IPurchaseSupplyReferencePortV1.cs:14-17`, `:39-55`; uygulama `…Purchasing.Infrastructure/Persistence/PurchaseSupplyReferencePort.cs:22-66`; kayıt `…Purchasing.Infrastructure/PurchasingModuleExtensions.cs:58` |
| Portun tüketicisi | **Yok** (Purchasing dışında referans yok) | `git grep IPurchaseSupplyReferencePortV1 -- src` |
| Açık satır listesi | `IPurchaseOpenLineReadPortV1` — şirket geneli, sayfalı, ürün süzgeci yok | `…Purchasing.Application/Contracts/IPurchaseOpenLineReadPortV1.cs:18-21` |
| Satınalma satırı miktarları | `Committed = Quantity − Technical`, `Remaining = Committed − Accepted − Cancelled`; teknik dilim genel satırdan oyulur | `…Purchasing.Domain/Orders/PurchaseOrderLine.cs:70`, `:76`, `:78-112` |
| Fazla kabul | Alış kabul payı kalanı aşamaz (`AcceptanceExceedsRemaining`) | `…Purchasing.Domain/Orders/PurchaseOrder.cs:337-341` |
| Sipariş durumları | `Draft`, `Approved`, `Cancelled` (kapalı/“Closed” durumu yok) | `…Purchasing.Domain/Orders/PurchaseOrder.cs:6-16` |
| Satış tahsis okuması | Inventory, Satış'ın `ISalesOrderAllocationReadPortV1`'ini cevaplar: satış satırı başına **yalnız fiziksel** aktif tahsis sayısı ve kg. Tüketiciler: satış iptal önizlemesi (DEC-SAL-005; okunamazsa iptal kapalı) ve kaynak planının stok kısmı (SAT-D255) | `…Inventory.Infrastructure/Persistence/InventorySalesOrderAllocationReadPort.cs:18-48`; `…Sales.Application/Contracts/SalesDraftCancellationContractsV1.cs:3-31` |
| Satır kapanışı (API-11), kabul düzeltmesi (API-44), tedarikçiye iade (API-60) | **Yok**; `RecordAcceptance` yalnız ekler | #1110 §1; `…Purchasing.Domain/Orders/PurchaseOrder.cs:330-348` |
| Modüller arası satır kilidi | **Yok** | #1110 §1, §3.4 |

Özet: gelecek tedarikten ayırma **yok**; tahsis yalnız mal geldikten sonra fiziksel etiket üzerinde yapılabiliyor (F-017).

## 3. Hedef davranış

### 3.1 Kavramlar

- **Ayrılabilir miktar (Alış'ın):** Alış portunun `AllocatableText` alanıdır (INV-006: `committed − kabulNet − cancelled −
  closed − iadeEtkisi`; API-11 ile kapalı satır, Draft ve Cancelled için 0). Bugün kodda API-11 ve iade olmadığı için
  değer `committed − accepted − cancelled` olur (PR #1110 §2). Stok bunu hesaplamaz, yalnız okur. İkinci hesaplayıcı yoktur.
- **Ayrılmış miktar (Stok'un):** bir satınalma genel satırına bağlı aktif gelecek ayırmaların kalan toplamı.
- **Aktif ayırma:** durumu `Active` olan gelecek ayırma. `Consumed` ve `Released` aktif değildir (PR #1110 §2).
- **Değişmez:** her `(CompanyId, PurchaseOrderId, PurchaseOrderLineId)` için `ayrılmış ≤ ayrılabilir`. Sahibi Stok'tur.
  İki modül arasında §3.2 adım 0'daki ortak satır kilidiyle korunur.
- Gelecek ayırma **fiziksel hareket değildir**; stok bakiyesi, serbest fiziki ve maliyet değişmez (DEC-STK-003,
  KLN-D26 (3)). Fiziksel stok listesinde ayrı kaynak türü olarak görünür, serbest stoğa karışmaz (DEC-STK-002).

### 3.2 Ayırma oluştur

Girdi (öneri): `AllocationId`, `SalesOrderId`, `SalesOrderLineId`, `PurchaseOrderId`, `PurchaseOrderLineId`, miktar
(kg veya ton olarak girilir), `Reason`, `ExpectedPurchaseOrderVersion` (bilgi amaçlı, §5 notu).

Akış (host + Stok yazıcısı; hepsi tek işlem). Adımların sırası bağlayıcıdır:

0. **Satır kilidi alınır.** Host, Alış okumasından **önce** ve aynı işlemde `IPurchaseLineSerializationLockV1` ile ortak
   satır kilidini alır (PR #1110 §3.4): `sp_getapplock`, `@LockMode = 'Exclusive'`, `@LockOwner = 'Transaction'`, kaynak
   `goymen:purchase-line:{companyId:N}:{lineId:N}`. Şirket kimliği sunucu bağlamındandır; `lineId` genel satırdır. Birden
   çok satır kilitlenecekse `lineId` artan sırayla alınır. Kilit alınamazsa yazma olmadan 409 `purchasing_line_busy`. Kilit
   dönüş kodu < 0 ise devam edilmez. Kilit işlem bitince kendiliğinden bırakılır.
   **Talep tarafı kilidi (B P2-4; yazarın okuması, geri alınabilir):** alış satırı kilidinden **önce** aynı işlemde satış
   satırı kilidi alınır (öneri kaynak adı `goymen:sales-order-line:{companyId:N}:{lineId:N}`; aynı host/BuildingBlocks
   kilit arayüzü, adı F017-b'de kesinleşir ve contract testine bağlanır). Kilit sırası her yolda aynıdır: önce satış
   satırı, sonra alış satırları artan `lineId`. Kilitler komutun ilk yazımından önce alınır; Satış komutu satış satırı
   kilidini kendi agregat yazımlarından önce alır (r4 A P3-6). Birden çok satış satırı kilitlenecekse hepsi artan satış satırı kimliğiyle
   ve bütün alış satırı kilitlerinden önce alınır; satış ve alış kilitleri iç içe geçmez (r3 A P3). Satış satırı kilidini
   oluşturma, Satış satır iptali/azaltması (F017-s) ve talebi yeniden artıran §3.7 yeniden açma alır; mal kabul çevirmesi
   ve Alış kaynaklı bırakmalar talep toplamını artırmadığı için yalnız alış satırı kilidini alır. Satıra bağlı ayırma
   kümesi ve açık miktar kilitlerden sonra okunur. Kilitlenecek satış satırları kilitten önce okunan kümeden çıkıyorsa
   (alış tarafından başlayan yollar, §3.7): küme okunur → kilitler alınır → küme kilit altında yeniden okunur; küme
   büyüdüyse kilitler bırakılıp yeniden denenir (sınırlı deneme, sonra 409; yazarın okuması, geri alınabilir).
1. İzin ölçülür (§6). Satış satırı `ISalesOrderLineReferencePortV1` ile okunur; onaylı değilse 422.
2. Alış satırı Alış portuyla, kilit altında okunur (PR #1110 `GetAllocatableLineAsync`). `NotFound` → 404,
   `NotAllocatable` (Draft/Cancelled/API-11 kapalı/`technical-line`) → 422. Ürün kimliği satış satırınınkiyle aynı
   değilse 422.
3. Miktar, merkezi birim çevrim servisiyle satınalma satırının birimine çevrilir (SAT-D298). Çevrim `NoSource` ise 422;
   ekran ve Stok kendi katsayısını üretmez.
4. Stok, aynı işlemde **satınalma satırı defter satırını** (§4.1) günceller. `ayrılmış + yeni ≤ ayrılabilir` kontrolü
   kodda, kilit altında okunan değerle yapılır; aşarsa 409 `inventory_future_allocation_insufficient_future_supply`.
   Snapshot ancak bu kontrol geçince yazılır (§4.2).
5. **Talep tarafı üst sınırı (yazarın okuması, geri alınabilir):** aynı satış satırına bağlı fiziksel ve gelecek aktif
   tahsislerin toplamı, satış satırının açık miktarını aşamaz; aşarsa 409. Dayanak DEC-SAL-004 "parçaların toplamı satır
   miktarına eşittir" cümlesidir. Bugünkü fiziksel tahsiste bu kontrol yoktur. Gelecek ayırma tarafında eşzamanlılık
   çapası adım 0'daki satış satırı kilididir: aynı satış satırına farklı alış satırlarından iki oluşturma ve oluşturma ×
   Satış azaltması sıralanır. Fiziksel tahsis oluşturma bu kilidi almadığı için fiziksel tarafın eşzamanlı artışı kapsam
   dışıdır (aday 3). Fiziksel tahsise aynı kontrolün eklenmesi bu işin dışındadır (`NEEDS-SPEC`
   adayı, §9.1).
6. Ayırma satırı, komut satırı (idempotency), audit ve outbox olayı **tek save**'de yazılır.

Çakışmada mevcut serbest miktara göre kısmi ayırma **önerilebilir** (DEC-STK-004); otomatik kısmi kayıt yapılmaz.

### 3.3 Ayırma bırak (iptal)

- `ExpectedAllocationVersion`, `reasonCode` ve `reasonText` ile. Önce §3.2 adım 0'daki satır kilidi alınır. Aktif değilse
  409. Bırakma silme değildir: kalanın tamamı bırakılırsa durum §4.1 kuralıyla belirlenir (kalan 0 olunca tüketilen > 0 ise `Consumed`, değilse `Released`), defter toplamı
  düşer, audit + outbox yazılır. Satış azaltmasında kısmi bırakma §3.4'tedir.
- **Neden kodları:** Alış kaynaklı bırakmalarda tek liste #1110 §3.5 tablosudur: `purchasing-link-dropped`,
  `source-cancelled`, `purchase-line-closed`, `acceptance-link-failed`, `acceptance-reversed-no-supply`. Satış kaynaklı
  bırakmalarda (KLN-D28) kodlar F017-s'de adlandırılır (öneri: `sales-line-cancelled`, `sales-line-reduced`; teknik,
  geri alınabilir). Stok bu kodları kendisi türetmez, çağıranın verdiğini saklar. Stok kullanıcısının kendi bırakmasının
  kodu F017-b2'de adlandırılır (öneri, teknik).
- **Olay:** `inventory.future-allocation.released.v1`; `allocationId`, satış sipariş ve satır kimliği, satınalma satırı,
  bırakılan miktar, bırakma sonrası kalan miktar, `reasonCode`, `reasonText` ve `origin` (`inventory` / `purchasing` / `sales`) alanlarını taşır. Satış ekranı uyarı
  türünü `reasonCode`'dan seçer.
- Ayırma kendiliğinden bırakılmaz; her bırakma açık bir komut ve nedenle olur. Satış satırı iptali/azaltmasındaki
  otomatik bırakma (KLN-D28) da açık Satış komutunun parçasıdır; zamanla veya arka planda bırakma yoktur. DEC-STK-005'in "süre dolunca sessiz otomatik
  serbest bırakma yok" kuralı burada da geçerlidir; bu türde süre de yoktur (§9 S11).

### 3.4 Miktar değiştirme

- **Azaltma = kısmi bırakma (B P2-3):** Satış azaltmasında (KLN-D30 (2)) ayırmanın bir kısmı bırakılabilir. Bu, aynı
  satırda `ReleasedQuantity` artışı ve `RemainingQuantity` düşüşüdür; ayırma kimliği, oluşturma zamanı ve `ManualSequence`
  korunur, `Version` artar, `released.v1` bırakılan ve kalan miktarı taşır. Bırak + yeniden oluştur kullanılmaz: oluşturma
  zamanı ve elle sıra kaybolur, §3.5 sırası değişir.
- **Artırma:** mevcut ayırmayı büyüten komut yoktur; ek miktar yeni ayırmadır (§3.2). Fiziksel tahsisteki `Resize`
  karşılığı bu ilk dilimde önerilmez (geri alınabilir teknik tercih).

### 3.5 Mal kabulde talebe bağlama ve etiket ataması

- Mal kabul zaten tek host işlemidir (`PurchaseGoodsReceiptHost.cs:32-43`). Alış kabul payı kabul edilen her genel satırın
  satır kilidini alır (#1110 §3.4 madde 3). Stok çevirmesi aynı işlemde, aynı kilit sahibiyle çalışır; yeniden kilitlemez.
- Stok katılımcısı, kabul edilen genel satıra bağlı aktif gelecek ayırmaları bulur. Kabul teknik dilime yapıldıysa genel
  satır kimliği Alış kabul sonucundan gelir (§5, S9).
- Kabul edilen miktar kadar gelecek ayırma **tüketilir** (`Consumed`) ve yeni etiket üzerinde, aynı satış sipariş satırına
  **fiziksel tahsis** (`sales.order-line`) oluşturulur. Etiket ataması budur (KLN-D26 (4), ALS-D30 "fiziksel tahsise
  döner").
- Gelecek ayırma ile oluşan fiziksel tahsis birbirine kimlikle bağlanır (izlenebilirlik).
- **Sıra (KLN-D26 (6)(b), ALS-D84):** termini en yakın satış önce alır; kullanıcı sırayı değiştirebilir veya ayırma
  seçebilir. Öncelik zinciri #1110 §3.3 ile aynıdır: kabul anında verilen `AllocationOrder` > Stok'taki kalıcı
  `ManualSequence` > satış sipariş satırı termini > ayırmanın oluşturma zamanı. Son iki eşitlik kuralı **teknik, geri
  alınabilir**. `AllocationOrder` host kabul komutundan gelir; listedeki kimlik o genel satırın aktif ayırması değilse 422 ve
  yazma yok. `ManualSequence` alan adı **yazarın önerisi, geri alınabilir**. Termin satış sipariş satırı terminidir.
- **Az gelirse (KLN-D26 (6)(c)):** gelen miktar sırayla çevrilir; çevrilemeyen kalan ayırmada `Active` kalır
  (`RemainingQuantity` düşer). Kabul katılımcısı bu noktada ayırma bırakmaz. "Satır kapanırsa" API-11 kapanışıdır ve
  bırakmayı Alış kapanış komutu yapar (§3.6). §3.2 adım 0'daki değişmez korunduğu için kabul anında "kalan sıfır ama aktif
  ayırma var" durumu oluşmaz.
- **Fazla gelirse:** ayırmaları aşan kısım etikette serbest kalır, ek tahsis yapılmaz. Alış'ın kalanı aşan kabul reddi
  (`PurchaseOrder.cs:337-341`) değişmez; bu belge onu gevşetmez.
- **Bağlama başarısızsa (KLN-D26 (6)(g)):** iş nedenli bağlama hatası (etiket bloklu, birim çevrimi `NoSource`, satış
  satırı artık onaylı değil) mal kabulü durmaz: kabul ve etiket yazılır, ilgili ayırma aynı işlemde bırakılır
  (`reasonCode = acceptance-link-failed`) ve satış uyarılır. Bloklu etiket (B-18) de bu kurala girer; blok kalkınca
  otomatik yeniden bağlama yapılmaz. Teknik hata (DB hatası, kilit zaman aşımı, eşzamanlılık çakışması) tek host işlemini
  bütünüyle geri alır; bu bir politika değil atomikliktir (**yazarın okuması, geri alınabilir**; #1110 §3.3 ile aynı).
- **Replay:** kabulün idempotency kaydı çevirmeyi de kapsar. Aynı anahtarla gelen kabul yeniden çevirme yapmaz; saklanan
  sonuç tüketilen ayırma ve oluşan fiziksel tahsis kimliklerini içerir (#1110 §3.3).
- Kabul geri alma ve iade §3.7'dedir; bu dilimin (F017-c) konusu değildir.
- Hepsi aynı host işlemindedir; tek başarı veya tek geri alma.

### 3.6 Satır kapanışı (API-11) ve sipariş iptali

- **API-11 kapanışı (KLN-D26 (6)(c) "satır kapanırsa"):** eşleme #1110 §3.6'dır. API-11 eksik teslim kapanışını Alış yapar.
  Alış kapanış komutu satır kilidini alır ve aynı host işleminde satırın aktif ayırmalarını Stok bırakma portuyla
  `reasonCode = purchase-line-closed` ile bırakır. Biri başarısız olursa kapanış da yazılmaz. Stok kapanışı kendiliğinden
  görmez ve kendiliğinden bırakmaz.
- **Sipariş kalanı iptali (KLN-D26 (6)(d), ALS-D85):** ayırması olan satırın siparişi doğrudan iptal edilmez. Önce bağlı
  satışlar gösterilir. "Bağları düşür ve iptal et" gerekçeli tek onayla, satır kilitleri altında, aynı host işleminde
  yapılır. Her ayırma Stok bırakma portuyla `reasonCode = source-cancelled` ile bırakılır. Satışa "kaynak iptal edildi"
  uyarısı gider. Görülen küme, replay ve izin kuralları #1110 §3.5'tedir.
- Bu iki yol birbirinden ayrıdır. "Satır iptal" API-11 kapanışı sayılmaz.
- **Yeniden açma ve canlanma (KLN-D29):** iptalde veya API-11 kapanışında bırakılmış ayırmalar, satır yeniden açılınca
  veya canlanınca (§3.7) geri gelmez. Canlanan miktar serbest açık miktar olur; gerekiyorsa kullanıcı §3.2 yoluyla yeniden
  ayırır; sistem "canlandı" bildirimi gösterir (Sinan 21. tur m.3, 6822767886; #1110 §3.6, §3.8). Bildirimi Alış canlanma
  olayı taşır; alıcısı ve kanalı seçilmedi (§9.1 aday 5).

### 3.7 Kabul geri alma ve tedarikçiye iade (KLN-D26 (6)(e), ALS-D86, ALS-D91, Sinan 19. tur / ALS-D92)

- **Bugün kodda yoktur.** Kabul düzeltmesi (API-44) ve tedarikçiye iade (API-60) dilimleri henüz yazılmadı;
  `RecordAcceptance` yalnız ekler (#1110 §1). Bu bölüm o dilimler geldiğinde uygulanacak Stok kurallarıdır. Uygulama
  F017-e'dir (≈ #1110 F17-6).
- **Gelecek miktar ne zaman açılır (ALS-D91):** hatalı kabul geri alınırsa miktar siparişe geri açılır. İadede "yerine
  yenisi gelecek" işaretliyse açılır, işaretli değilse sipariş kapalı kalır. Açılıp açılmadığını Alış belirler; Stok bunu
  kilit altında Alış portunun `AllocatableText`'inden okur.
- **İptal edilmiş sipariş ve API-11 kapalı satır (Sinan 19. tur, ALS-D92):** bu iki durumda da ALS-D91 koşulsuz uygulanır. Miktar
  açılır, sipariş canlanır. Canlanan satır yeniden açıktır ve gelecek tedarikten ayrılabilir. Canlanmanın teknik biçimi
  (durum dönüşü, iptal/kapanış payının düşmesi, audit, expected version, satır kilidi) Alış spec'inde K3 dilimi olarak
  yazılır. Stok buna izin verir: canlanan miktar porttan ayrılabilir okunduğu sürece §3.2 ve aşağıdaki yeniden açma
  kuralları aynen çalışır. #1110'un eski "açılmaz" okuması ve geçici 409 merge edilen metinde
  yoktur (#1110 `main` `4f3978d3`, ALS-D92; r2 A P3-3).
- **Canlanmada bırakılmış ayırmalar (KLN-D29, Alış eşi ALS-D93):** iptal ("bağları düşür ve iptal et") veya API-11 kapanışında `Released`
  olmuş ayırmalar canlanmada geri gelmez; canlanan miktar serbest açık miktardır ve yeni ayırma normal oluşturma yoludur
  (§3.2). Aşağıdaki "aynı kimlikle `Consumed` → `Active`" kuralı yalnız kabul geri almada tüketilmiş ayırmalar içindir;
  `Released` ayırma hiçbir yolda yeniden açılmaz (**yazarın okuması**, KLN-D29 (4)).
- **Stok tarafı (aynı host işlemi, satır kilidi altında; #1110 §3.7 ile aynı metin):**
  - Geri alınan etiketlere bağlı fiziksel tahsis ve etiket ataması geri alınır.
  - Gelecek miktar açıldıysa ilgili ayırma aynı kimlikle `Consumed` → `Active` döner (sürüm artar, audit). Yalnız
    tüketilmiş kısım yeniden açılır; `ReleasedQuantity` hiçbir yolda geri gelmez (KLN-D29 (4)). Kontrol:
    `ayrılmış + yeniden açılan ≤ ayrılabilir`, kilit altında okunan değerle.
  - Gelecek miktar açılmadıysa veya yetmiyorsa karşılanamayan kısım bırakılır (`reasonCode =
    acceptance-reversed-no-supply`) ve satış uyarılır. Kısmi yeniden açma Sinan metninin (var/yok) genellemesidir
    (**yazarın okuması, geri alınabilir**).
  - Kısmi geri almada önce geri alınan etiketlere bağlı tahsisler geri alınır. Etiket ayrımı yoksa ALS-D84 sırasının tersi
    uygulanır: termini en uzak satış önce geri alınır. Yeniden açma da aynı sırayla yapılır (**yazarın okuması, geri
    alınabilir**).
- **Fail-closed kurallar (yazarın okuması, geri alınabilir; #1110 §3.7):**
  - Etiket satışa sevk edildiyse veya tüketildiyse geri alma 409 `inventory_future_allocation_already_fulfilled` ile reddedilir.
  - Satış tarafı fiziksel tahsisi bıraktıysa veya satış satırı iptal edildiyse ayırma yeniden açılmaz; yalnız etiket geri
    alınır, uyarı gerekmez. "Aynı kimlikle yeniden açılır" yalnız fiziksel tahsis hâlâ aktifse geçerlidir.
- **API-44 ters + yeni:** aynı düzeltmede ters kayıt bir ayırmayı yeniden açarsa yeni kabul önce o ayırmayı tüketir
  (#1110 §3.7, yazarın okuması).
- Geri alma ve iade komutu §3.2 adım 0'daki kilidi alır (#1110 §3.4 madde 7). Yeniden açma talebi artırdığı için kilit
  sırası iptal ile aynıdır: önce ilgili satış satırları (artan kimlik), sonra alış satırları (artan `lineId`). Satış
  satırları kilitten önce okunan kümeden çıkar; adım: küme oku → kilitle → kilit altında yeniden oku, küme büyüdüyse
  yeniden dene (§3.2 adım 0). Kilit altında satış satırı yeniden okunur: iptal edilmiş veya kapanmışsa aktif gelecek
  ayırma oluşmaz, ayırma yeniden açılmaz ve durumu değişmez. Kilitten önceki okuma ile kilit altındaki yeniden okuma
  farklıysa (küme, satış satırı sürümü veya durumu değiştiyse) kilitler bırakılır, sınırlı deneme yapılır, sonra 409
  (§3.2 adım 0 ile aynı; yeniden açma satış satırına yazmaz; B P2-4, r4 B P3-2 / A P3-4; yazarın okuması,
  geri alınabilir). **Kilitlenmezlik koşulu (r4 B P3-3):** API-44/API-60 geri alma/iade komutu satış satırı kilitlerini
  kendi alış satırı kilidinden **önce** almalıdır; #1110 (`main` `4f3978d3`) bu sıraya hizalıdır (#1110 §3.4 "Sıra", §3.7
  adım 1; r1 A/B P3-1). **Yeniden açma tavanı (r4 B P3-1; F017-e `NEEDS-SPEC` adayı, §9.1 aday 6):** yukarıdaki tek kontrol alış
  tarafıdır (`ayrılmış + yeniden açılan ≤ ayrılabilir`). Fiziksel tahsis çevirmeden sonra küçülmüşse yeniden açılan
  gelecek miktar satışın açık talebini aşabilir. Önerilen sınır (yazarın önerisi, tahminle etkinleştirilmez): yeniden
  açılan ≤ o çevirmenin geri alınan ve hâlâ aktif fiziksel miktarı; satış kilidi altında talep tavanı yeniden kontrol
  edilir; aşan kısım için davranış F017-e'de sorulur. Replay kaydı akışın kendi host komutundadır;
  çevirme geri alması sonuç özetine girer.
- **Geçici kapı:** API-44 veya API-60 F017-e'den önce merge edilirse, aktif veya tüketilmiş gelecek ayırması olan satırda
  kabul geri alma/iade fail-closed 409 `purchasing_line_has_allocations` ile reddedilir. F017-e bu reddi kaldırır (#1110 §4).

## 4. Veri modeli (öneri, F017-b)

### 4.1 Tablolar

Mevcut `inventory.Allocations` tablosu fiziksel etikete bağlıdır (`CK_Allocations_Identity`, `[Block] = 1`). Onu
gevşetmek fiziksel tahsis kısıtlarını zayıflatır. Öneri: **ayrı tablolar** (geri alınabilir teknik tercih):

- `inventory.FutureSupplyAllocations` — bir satır = bir gelecek ayırma.
  `Id`, `CompanyId`, `SourceKind` (`purchasing.order-line`), `PurchaseOrderId`, `PurchaseOrderLineId`, `PurchaseUnitId`,
  `ProductId`, `ProductVersion`, `DemandKind` (`sales.order-line`), `DemandId`, `DemandLineId`, `AllocatedQuantity`,
  `RemainingQuantity`, `ReleasedQuantity` (satınalma satırı biriminde, tam ondalık; tüketilen = `AllocatedQuantity` −
  `RemainingQuantity` − `ReleasedQuantity`), `ManualSequence` (boş olabilir, §3.5), `Reason`,
  `Status` (Active/Consumed/Released; kalan > 0 iken Active; kalan 0 olunca tüketilen > 0 ise Consumed, değilse Released — bu belgedeki tek durum kuralı;
  kabul geri almada Consumed → Active, §3.7), `ReleaseReasonCode`, `ReleaseOrigin`,
  `Version`, `RowVersion`, `ObservedPurchaseOrderVersion`, oluşturma/güncelleme/bırakma zamanları ve nedeni.
- `inventory.FutureSupplyLines` — satınalma satırı başına **tek** defter satırı; Stok içi eşzamanlılık çapası.
  `CompanyId`, `PurchaseOrderId`, `PurchaseOrderLineId`, `PurchaseUnitId`, `AllocatableSnapshot`, `ActiveAllocated`,
  `Version` (concurrency token), `RowVersion`.
- `inventory.FutureSupplyConversions` (veya fiziksel tahsis satırında nullable kaynak kolonu) — mal kabulde tüketilen
  gelecek ayırma ↔ oluşan fiziksel tahsis bağı. Seçim F017-c PR'ınındır.

### 4.2 DB kısıtları

- `UNIQUE (CompanyId, PurchaseOrderId, PurchaseOrderLineId)` — defter satırı tektir.
- `CHECK (ActiveAllocated >= 0 AND ActiveAllocated <= AllocatableSnapshot)` — **önbellek tutarlılık kısıtı**. Otoriter
  değer Alış'ındır; değişmezin asıl koruması kilit altındaki kod kontrolüdür (§3.2 adım 0 ve 4). Snapshot yalnız
  `ActiveAllocated`'ı artıran komutlarda (oluşturma, §3.7 yeniden açma) ve kod kontrolü geçtikten sonra yazılır. Azaltan
  komutlar (bırakma, kabul çevirmesi) snapshot'a dokunmaz. Böylece normal akışta kısıt ihlali oluşmaz; kilit altında
  okunan değer `ActiveAllocated`'dan küçükse komut DB istisnası değil yazılı 409 döner. Kısıt ihlali bir hatadır ve testle
  yakalanır.
- "Karşılıksız" bilgisi saklanmaz; okuma anında `ActiveAllocated` ile Alış'ın canlı değeri karşılaştırılarak hesaplanır.
- Ayırma satırında: `AllocatedQuantity > 0`, `RemainingQuantity >= 0`, `ReleasedQuantity >= 0`,
  `RemainingQuantity + ReleasedQuantity <= AllocatedQuantity`, durum ile kalan tutarlılığı (`Active` ⇔ kalan > 0; kalan 0
  ve `Released` ⇒ tüketilen 0; kalan 0 ve `Consumed` ⇒ tüketilen > 0; `StockAllocation.cs:28-31` kuralının uzantısı), boş GUID yok, `Reason` boş değil, `SourceKind` ve
  `DemandKind` sabit listede.
- Şirket bileşik anahtarı (`CompanyId, Id`) ve tenant filtresi.
- Migration geri alınabilir: `Down`, veri varken `THROW` ile reddeder (forward-fix deseni).

### 4.3 Eşzamanlılık

- **Modüller arası:** her oluştur, bırak, kabul çevirmesi, geri alma ve canlanma §3.2 adım 0'daki satır kilidi altında
  çalışır. Kilidi alan yollar #1110 §3.4 listesidir: Stok oluşturma ve bırakma; Alış kabul payı (Stok çevirmesi aynı
  sahipte); sipariş kalanı iptali ve "bağları düşür ve iptal et"; satır azaltma; API-11 kapanışı ve yeniden açma; kabul
  düzeltmesi, iade ve canlanma; Satış satır iptali ve azaltması (KLN-D28, F017-s). Oluşturma, Satış iptal/azaltma ve §3.7 yeniden
  açma ayrıca önce satış satırı kilidini alır (§3.2 adım 0; sıra satış satırı → alış satırları artan). Kilit arayüzü host/BuildingBlocks'ta tek yerdedir; modüller birbirine referans vermez.
  Kilit kaynak adı contract testine bağlanır. Arayüzü ve host uygulamasını F017-a / #1110 F17-1 getirir.
- **Stok içi:** her oluştur/bırak/çevir, defter satırının `Version` alanını artırır. İki eşzamanlı ayırmadan biri
  `DbUpdateConcurrencyException` alır → replay değilse 409 `inventory_concurrent_change` (fiziksel tahsisteki
  `InventoryStore.Allocations.cs:142-153` deseni). Defter satırı ilk ayırmada oluşturulur; iki eşzamanlı ilk ayırma unique
  ihlaliyle (2601/2627) sıralanır.
- İş davranışı cevaplıdır: satınalma tarafı bağı açıkça düşürür, satınalma miktarı bağ yüzünden azalmaz (ALS-D83);
  ayırması olan sipariş doğrudan iptal edilmez (§3.6). Buna rağmen okumada `ayrılmış > ayrılabilir` görülürse Stok yeni
  ayırmayı reddeder ve satırı "karşılıksız" diye raporlar; kendiliğinden bırakmaz (§3.3).

## 5. Modül sınırı ve port sözleşmesi

- **Alış → Stok:** ayrılabilir açık miktar Alış'ın sürümlü portundan gelir. Sözleşme **PR #1110 §3.1
  `IPurchaseFutureSupplyReadPortV1`**'dir (Alış sahibi; Alış ve Stok ortak yazar). Bu belge alanları kopyalamaz.
  Stok tarafının ihtiyaç listesi:
  - tek satır okuma: şirket, sipariş, satır, ürün + sürüm, birim kimliği, satır türü, `AllocatableText`, sonuç
    (`Allocatable` / `NotAllocatable` / `NotFound`), sipariş sürümü (yalnız gözlem);
  - ürün bazlı sayfalı liste (ekran seçenekleri);
  - fiyat/döviz alanı yok (T28); başka şirket `NotFound`, 403 değil; port yetki taşımaz, tüketici kendi iznini ölçer.
- **Stok → Alış:** PR #1110 §3.2 `IInventoryFutureAllocationReadPortV1` (Stok sahibi). Bir satınalma satırının aktif
  ayrılmış toplamını satınalma satırı biriminde döner: `Present(text)` / `None` / `Unreadable`; `Unreadable` asla sıfır
  sayılmaz. Stok bu portu F017-b'de yazar.
- **Yazma yok:** Stok Alış tablosuna yazmaz, Alış Stok tablosuna yazmaz. Modüller birbirine referans vermez; host
  adaptörü çevirir (mevcut `InventoryPhysicalAllocationHostPorts.cs` deseni).
- **Stok bırakma portu (Stok sahibi):** `IInventoryFutureAllocationReleasePortV1` (PR #1110 §3.5 önerisi). Bırakma §3.3
  akışının aynısıdır (kilit, sürüm, audit, outbox). Stok bu portu F017-b'de yazar. Çağıranlar, hepsi aynı host işleminde
  ve satır kilidi altında:
  - Alış bağ düşürme — `purchasing-link-dropped` (#1110 §3.5, F17-4);
  - Alış "bağları düşür ve iptal et" — `source-cancelled` (#1110 §3.5, F17-4);
  - Alış API-11 kapanışı — `purchase-line-closed` (#1110 §3.6, API-11 veya F17-4);
  - kabul geri alma/iade — `acceptance-reversed-no-supply` (§3.7, F017-e);
  - Satış satır iptali ve azaltması — Satış kaynaklı kodlar (§3.3; KLN-D28, §5.1 (c), F017-s).
  Kabul katılımcısındaki `acceptance-link-failed` bırakması Stok'un kendi içindedir; porttan geçmez.
- **Termin:** satınalma satırı termini bu portta yoktur. Ayrı okuma `GetLineScheduleAsync` ile gelir (#1110 §3.1, F17-1b,
  #1101'e bağlı; dilim kimliği, teslim tarihi, miktar, kalan). KLN-D30 sırası bu okumayı kilit altında, aynı işlemde
  kullanır; satırın tarihi teslim alınmamış kalanı olan dilimlerin en geç `DeliveryDate`'idir, dilimi olmayan satır
  tarihsizdir, `Unreadable`/hata fail-closed 503'tür (KLN-D30 (2), (4)). SAT-D315 (`main` `c93433f6`) sırayı Stok portuna bırakır. §3.5 sırası ise **satış** sipariş satırı terminini kullanır; onu satış satırı portundan okur. Stok
  terminleri saklamaz.
- **Mal kabulde Alış alanları:** genel satıra yapılan kabulde yeni alan gerekmez; `InventoryGoodsReceiptRequestV1`
  satınalma sipariş ve satır kimliğini taşır (`InventoryGoodsReceiptContractsV1.cs:62-66`). Teknik dilime yapılan kabul
  için `PurchaseAcceptanceStageResultV1`'e genel satır kimliği alanı, kullanıcı sırası için host kabul komutuna
  `AllocationOrder` gerekir (#1110 §3.3, S9). İkisi F17-1 / F17-3'te gelir.
- Sürüm: v1 portlar değişmez; yeni port ayrı sürümlü arayüzdür. Contract testleri iki tarafta da (§8).

### 5.1 Satış tahsis okuması (`ISalesOrderAllocationReadPortV1`)

Bugün Inventory bu portu yalnız fiziksel tahsisten, kg olarak cevaplar (§2). Gelecek ayırma bu yoldan görünmezse aktif
gelecek ayırması olan satış siparişi "tahsis yok" sayılarak iptal edilebilir (DEC-SAL-005). Portu toptan genişletmek de
gelecek arzı stok kısmına karıştırır (DEC-STK-002, SAT-D255). Kurallar:

- **(a) Ayrı tür, sürümlü port:** gelecek ayırmalar ayrı bir kaynak türüyle bildirilir. Bu yeni bir sürümlü port veya v2
  olur; v1'in anlamı değişmez. Miktar satınalma satırı birimiyle ve birim kimliğiyle döner; kg'a çevrilmez, fiziksel kg
  toplamına eklenmez. Gelecek ayırma kaynak planının **stok kısmına girmez** (DEC-STK-002).
- **(b) İptal önizlemesi görür, fail-closed:** satış iptal önizlemesi gelecek ayırmaları gösterir. Okunamazsa önizleme
  kapalı başarısız olur ve iptal reddedilir (DEC-SAL-005; mevcut önizleme kuralı). Bugünkü önizleme kayıtlı bir etki varken
  iptali reddeder ve başka modülün kaydını bırakmaz; bu davranış **fiziksel tahsis için** korunur. Gelecek ayırma için
  onaylanan iptal ayırmayı (c)'ye göre bırakır (KLN-D28 (9)).
- **(c) Satış satırı iptal edilir veya azaltılırsa (KLN-D28):** gelecek ayırma **otomatik bırakılır** (Sinan 21. tur
  m.2). Satış iptal/azaltma komutu aynı host işleminde Stok bırakma portunu (§5) çağırır; artan ayırma kalmaz; bırakma
  audit'lidir (koordinatör anlamı). Satış tarafı karşılığı: SAT-D314 (#1119, `main` `c93433f6`). Ayrıntılar (**yazarın
  okuması, geri alınabilir**; KLN-D28 (3)–(8), SAT-D314 yazar okuması):
  - Satış Stok tablosuna yazmaz; bırakmayı Stok kendi portuyla, §3.2 adım 0'daki satır kilidi altında, sürüm, audit ve
    `released.v1` olayıyla yapar. Modüller birbirine referans vermez; host participant/port deseniyle bağlanır.
  - İptalde satıra bağlı bütün aktif gelecek ayırmalar bırakılır. Azaltmada yalnız satırın yeni **açık miktarını**
    (§3.2 adım 5: yeni satır miktarı − sevk edilen) aşan kısım bırakılır. **Tek tanım (KLN-D28 (4)):** fazla = max(0,
    satıra bağlı aktif fiziksel tahsis + aktif gelecek ayırma − (yeni satır miktarı − sevk edilen)); fazla önce gelecek
    ayırmalardan, yetmezse fiziksel tahsisten bırakılır (KLN-D31, KLN-D32). Fiziksel tahsis yokken bu max(0, aktif
    gelecek ayırma − açık miktar) ile aynıdır; aşağıdaki örnekler o durumdur. Karşılaştırma merkezi birim çevrimiyle tek birimde yapılır
    (SAT-D298). min(kalan ayırma, azaltılan miktar) kullanılmaz; kısmi ayrılmış satırda fazla bırakır. Yeni satır
    miktarıyla (sevki düşmeden) karşılaştırmak da kullanılmaz; kısmi sevkli satırda ayırmayı açık miktarın üstünde
    bırakır ve "artan ayırma kalmaz"ı bozar. Örnekler: satır 100, sevk 0, ayırma 60, 30 azaltma → açık 70 ≥ 60 →
    bırakılan 0 (min formülü yanlışlıkla 30 bırakırdı); satır 100, sevk 40, ayırma 60, satır 80'e iner → açık 40 →
    bırakılan 20. Açık miktar içinde kalan ayırma aktif kalır. Golden satırlar §8'dedir. Fiziksel tahsis de varsa
    sıra ve ara durum aşağıda (KLN-D31, KLN-D32). SAT-D314/SAT-D315 `main`de (`c93433f6`) 25. tur ek kaynağıyla bu
    tanıma ve KLN-D32 ara reddine hizalıdır.
  - **Birden çok gelecek ayırma (KLN-D30, Sinan 23. tur m.1):** bırakılacak miktar birden çok ayırmaya (alış satırına)
    yayılıyorsa **en geç gelen önce** bırakılır: ayırmanın bağlı olduğu alış satırının beklenen teslim tarihine göre
    azalan sırada (tarih kaynağı §5 Termin; yazarın okuması); son ayırmadan gerektiği kadar kısmi bırakılır (§3.4).
    Kullanıcı Satış azaltma komutunda bırakılacak ayırmaları açıkça seçebilir; **açık seçim varsayılandan üstündür**.
    Seçim her ayırma için `ExpectedAllocationVersion` taşır (bayatsa 409 `inventory_allocation_version_conflict`). Seçim
    fazlayı karşılamıyorsa kalan kısım varsayılan sırayla bırakılır; seçim toplamı fazlayı aşarsa komut yazma olmadan 422
    ile reddedilir (yazarın okuması, geri alınabilir). Termin okunamazsa komut yazılmaz (503); dilimi olmayan satır
    tarihsizdir. Eşitlikte (aynı tarih veya iki tarihsiz satır): ayırması en son oluşturulan, sonra ayırma kimliği büyük
    olan önce (deterministik; yazarın okuması, geri alınabilir). Sıra Stok bırakma portunda, satış satırı ve alış satırı
    kilitleri altında, aynı işlemde okunan tarihle hesaplanır; kullanılan tarihler ve sıra audit'e ve komut sonucuna
    yazılır, replay saklanan sonucu döner. Satış yalnız açık seçimi iletir. Rev4'teki geçici "seçim yoksa 409" kapalı yönü kalktı;
    azaltma seçim olmadığı için reddedilmez (SAT-D314 ile uyumlu).
  - **Fiziksel tahsis + gelecek ayırma (KLN-D31, Sinan 23. tur m.2):** satırda depodaki etiketli mala fiziksel tahsis de
    varsa **önce gelecek ayırma** bırakılır; kullanıcı komutta değiştirebilir. Bırakılacak miktar yukarıdaki tek
    tanımdır (fazla); önce gelecek ayırmalardan (aralarında KLN-D30 sırası) bırakılır.
  - **Gelecek yetmezse fiziksel fazla (KLN-D32, Sinan 25. tur m.1):** kalan kısım depodaki fiziksel tahsisten otomatik
    bırakılır; kullanıcı komutta seçerse onun seçimi geçer. **Ara durum:** fiziksel bırakma yolu main'e gelene kadar,
    fiziksel tahsise dokunan her bırakma reddedilir: otomatik sırada gelecek yetmediği için fiziksele inen azaltma da,
    kullanıcının komutta açıkça fiziksel tahsis seçtiği azaltma da (#1119 SAT-D314 (2)/SAT-D315; B P2-2). Ret
    bütündür: gelecek ayırma da bırakılmaz, Satış komutu yazılmaz ((b)'deki fiziksel iptal reddiyle aynı yön). **Reddin
    tespiti Stok bırakma portundadır:** §3.2 adım 0 kilitleri (önce satış satırı, sonra alış satırları artan `lineId`)
    alındıktan ve satıra bağlı ayırma kümesi okunduktan sonra, hiçbir yazmadan önce fazla ve bırakma planı hesaplanır;
    plan fiziksel tahsise dokunuyorsa (gelecekten bırakılabilecek < fazla veya açık seçimde fiziksel tahsis var) port 409
    `inventory_future_allocation_physical_release_unavailable` döner (öneri, F017-s'de kesinleşir). Port bu redde hiçbir
    şey yazmaz: Stok satırı, audit, outbox ve ayırma değişmez. Satış komutu geri alınır ve kod Satış problemine taşınır.
    **Terminal ret kaydı ve replay Satış'ındır (B P2-1):** idempotency anahtarının sahibi Satış revizyon komutudur; tek
    terminal ret kaydını Satış yazar ve aynı anahtarla tekrar saklanan reddi Stok portunu ikinci kez çağırmadan döner
    (#1119 plan, `k3-command-skeleton` terminal ret deseni). Port, §3.2 kilitleri altında gereken bütün okumaları (satıra
    bağlı aktif gelecek ayırma kümesi, fiziksel tahsis, satış satırı, alış termini) tamamlamadan kaydedilen terminal ret
    (409 veya 422) dönmez; satış/alış satırı kilidi meşgul 409'u okumalardan önce gelir ve kaydedilmez. Herhangi bir okuma
    okunamaz durumdaysa veya port hatası / zaman aşımı varsa, terminal ret koşulu (409 veya 422) ayrıca tespit edilmiş olsa
    bile sonuç 503'tür ve hiçbir şey kaydedilmez (karışık sonuçta 503 kazanır; koordinatör kararı (c), #1123 ile aynı; r1
    A P2-1, r2 A P2-1 / B P3-1). Çok satırlı revizyonda Satış bütün satırları değerlendirir, ilk terminal rette durmaz; bir satır terminal ret (409 veya 422), başka bir satır terminal olmayan sonuç (kilit meşgul 409, 503, port hatası, zaman aşımı) verirse terminal olmayan kazanır: hiçbir şey kaydedilmez ve aynı anahtar yeniden çalışır; dönen durum 503 varsa 503, yoksa kilit meşgul 409'dur (koordinatör okuması #66 6825436733; Satış X1 ile hizalı). 503/okunamaz, port hatası, zaman aşımı, satış satırı kilidi meşgul (409), alış satırı
    kilidi meşgul (409 `purchasing_line_busy`) ve `concurrent_change` terminal değildir: kaydedilmez, aynı anahtar yeniden
    çalışır. Bayat açık seçimde dönen 409 `inventory_allocation_version_conflict` kaydedilen terminal rettir; 409
    `inventory_allocation_state_conflict` ve fazlayı aşan seçimde 422
    `inventory_future_allocation_release_selection_invalid` da terminaldir ve kaydedilir (koordinatör sınıflaması, r1;
    A P3-2, r4 A P3-3; iskeletin uygulaması, F017-s'de testle kesinleşir; yazarın okuması, geri alınabilir). Fiziksel bırakma Stok'un fiziksel
    tahsis yazıcısıyla, aynı host işleminde ve satış satırı kilidi altında yapılır (yazarın okuması). Etiket bazlı
    tahsisin kısmi bırakılma yolu F017-s'de `NEEDS-SPEC`'tir; tahminle seçilmez, cevap gelene kadar ara red sürer.
  - **Revizyonda satır silme (B P2-3; koordinatör kararı, rev9 düzeltmesi):** Satış revizyonunda satırın kaldırılması
    porta iptal olarak değil, yeni miktar 0 olan azaltma olarak gelir. Porta yalnız SAT-D167 anlamında sevk edilmemiş
    satır gelir: sevk, yükleme talimatı ve fiziksel (stok) tahsisi yoktur (`SINAN_KARARLARI_2026-11-02_SATIS_EK24.md`
    :52, Satış README). Bu satırda yalnız gelecek ayırma vardır ve hepsi bırakılır. Sevk edilmiş, kısmen sevk edilmiş,
    yükleme talimatlı veya fiziksel tahsisli satırın kaldırılmasını Satış porttan önce 422
    `sales_line_revision_line_shipped` ile reddeder; Stok bu durumu görmez. Fiziksel bırakma gerektiren ara 409
    (KLN-D32 (3)) yalnız miktar azaltmada oluşur. **Bağımlılık (F017-s, r4 B P3-4 / A P3-5):** SAT-D167 bağlantı
    okuması (Satış `ReadAllocationsAsync` "bağlı" kontrolü) yalnız fiziksel tahsisi sayar, gelecek ayırmayı "stok
    tahsisi" saymaz; gelecek ayırma Satış tahsis okumasında ayrı türle döner (§8 "Satış
    portu"). Sayarsa yalnız gelecek ayırmalı satırın silinmesi de 422 alır ve SAT-D314 (1) uygulanamaz. SAT-D167 metni
    değişmez (koordinasyon notu). **Yarış (F017-s, r1 B P3-5):** fiziksel tahsis oluşturma satış satırı kilidini almaz
    (§3.2); Satış'ın SAT-D167 okuması ile port çağrısı arasında aynı satıra fiziksel tahsis oluşursa 0'a azaltma porta
    fiziksel tahsisle ulaşır ve KLN-D32 (3) ara 409'u terminal döner (fail-closed; hareket yok). "Ara 409 yalnız miktar
    azaltmada" ve "Stok bu durumu görmez" yarışsız yol için geçerlidir.
  - Bırakma portu okunamaz, hata verir, kilit alınamaz veya bırakma başarısız olursa Satış iptali/azaltması da yazılmaz
    (aynı işlem, fail-closed). Bırakma exactly-once'dır: başarılı bırakmanın sonucunu Satış komutu kendi idempotency
    kaydında saklar; aynı Satış komut anahtarıyla gelen tekrar Stok portunu yeniden çağırmaz, ikinci bırakma üretmez,
    saklanan sonucu döner (idempotent).
  - Bırakma silme değildir: kısmi bırakmada ayırma aynı satırda azalır, tamamı bırakılırsa durum §4.1 kuralıyla
    belirlenir (kalan 0 olunca tüketilen > 0 ise `Consumed`, değilse `Released`); audit ve
    `released.v1` outbox olayı aynı işlemde yazılır (§3.3, §3.4).
  - Bırakma fiziksel hareket değildir; stok bakiyesi, serbest fiziki ve maliyet değişmez (DEC-STK-003).
  - Seçilmeyen: "Satış reddeder, önce elle bırak".
- Satış sözleşmesine dokunduğu için bu iş Satış şeridiyle cross-module K3 dilimidir (F017-s, §10).

## 6. CompanyId, izin, audit, idempotency, outbox

- **CompanyId:** her tabloda zorunlu; şirket sunucu bağlamından gelir, istemciden alınmaz. Tenant filtresi + bileşik
  anahtar. Başka şirketin satış/satınalma satırı 404 (varlık sızdırılmaz). Kilit kaynağı da sunucu bağlamındaki şirketi
  kullanır.
- **İzin adı (yalnız ad önerisi):** `inventory.future-allocation.write` (oluştur/bırak) ve okuma için
  `inventory.future-allocation.read`. Ad kaydı F017-b2'de yapılır; **grant/rol/Critical insan kapısıdır**, bu iş açmaz.
  Ayırmayı izinli tek kişi yapar, ikinci onay yoktur (KLN-D26 (6)(f)); satış, satınalma veya depo kullanıcısına izin
  verilmesi grant kararıdır. Ayrı ad gerekçesi §9 S6.
- **Expected version:** bırakmada `ExpectedAllocationVersion`; defter satırında concurrency token. Alış sipariş sürümü
  yalnız gözlem olarak saklanır; Stok onu kilit saymaz. Modüller arası koruma satır kilididir.
- **Idempotency:** `Idempotency-Key` zorunlu; anahtar hash + payload hash (fiziksel tahsisteki `AllocationKeyHash` /
  `AllocationPayloadHash` deseni). Aynı anahtar + aynı yük → replay (`IsReplay`, `OriginalCorrelationId`); farklı yük →
  409 `idempotency_conflict`.
- **Audit:** her oluştur/bırak/çevir/yeniden aç için önce/sonra görüntüsü, neden kodu, gerekçe ve köken;
  correlation/causation.
- **Outbox:** `inventory.future-allocation.created.v1`, `.released.v1` (§3.3 alanları), `.converted.v1`, `.reopened.v1`
  (öneri). Satış ve Alış ekranları bunları inbox/dedup ile tüketebilir. Olay şeması sürümlü contract testine bağlanır.
- **Log/metric/trace:** ret kodları sayılır; concurrency çakışması ve kilit zaman aşımı ayrı metrik.

## 7. Hata, boş ve çakışma durumları

Kod adları öneridir; Alış kaynaklı kodlar #1110 adlarıyla hizalıdır (`purchasing_line_busy`,
`purchasing_line_has_allocations`, `inventory_future_allocation_already_fulfilled`); kesin adlar F017-b / #1110 F17-2'de
birlikte sabitlenir (r1 A/B P3-2).

| Durum | HTTP / kod (öneri) |
|---|---|
| Doğrulama (miktar ≤ 0, eksik alan, neden boş) | 400 `validation_failed` |
| Satış veya satınalma satırı yok / başka şirket | 404 `inventory_future_allocation_invalid_reference` |
| Satış satırı onaylı değil; satınalma Draft/Cancelled/API-11 kapalı; teknik dilim; ürün uyuşmuyor | 422 `inventory_future_allocation_invalid_reference` |
| Birim çevrimi yok (`NoSource`) | 422 `inventory_future_allocation_unit_conversion_unavailable` |
| Ayrılabilir miktar yetersiz | 409 `inventory_future_allocation_insufficient_future_supply` (+ kısmi öneri) |
| Satış satırı açık miktarı aşılıyor (§3.2 adım 5) | 409 (kod F017-b'de adlandırılır) |
| Satır kilidi alınamadı (zaman aşımı) | 409 `purchasing_line_busy` (#1110 §3.4) |
| Eşzamanlı değişiklik | 409 `inventory_concurrent_change` |
| Ayırma sürüm çakışması / aktif değil | 409 `inventory_allocation_version_conflict` / `inventory_allocation_state_conflict` |
| Geri almada etiket sevk edilmiş/tüketilmiş (§3.7) | 409 `inventory_future_allocation_already_fulfilled` |
| F017-e öncesi ayırmalı satırda kabul geri alma/iade (§3.7) | 409 `purchasing_line_has_allocations` (Alış kodu) |
| Idempotency çakışması | 409 `idempotency_conflict` |
| Alış portu okunamıyor | 503 (kapalı başarısızlık; asla "sıfır ayrılmış" sayılmaz) |
| Satış azaltmasında açık bırakma seçimi bırakılacak miktarı (fazla) aşıyor (§5.1 (c), KLN-D30) | 422 (öneri `inventory_future_allocation_release_selection_invalid`; yazma yok; Satış'ta terminal, kaydedilir) |
| Açık seçimdeki ayırma sürümü bayat (KLN-D30 (3)) | 409 `inventory_allocation_version_conflict` |
| Azaltma fiziksel tahsisten bırakma gerektiriyor veya açık seçim fiziksel tahsis içeriyor, fiziksel bırakma yolu yok (KLN-D32 (3)) | 409 `inventory_future_allocation_physical_release_unavailable` (öneri, F017-s'de kesinleşir; Stok bırakma portu §3.2 kilitleri altında tespit eder; yazma yok, gelecek ayırma da bırakılmaz; Satış problemine taşınır) |
| Alış termin okuması `Unreadable`/hata (KLN-D30 (4)) | 503 (Satış komutu yazılmaz; "tarihsiz" sayılmaz) |
| Aynı yürütmede terminal ret koşulu (409 veya 422) ve okunamaz/port hatası/zaman aşımı birlikte (§5.1 (c)) | 503; hiçbir şey kaydedilmez; port bütün okumaları bitirmeden kaydedilen 409/422 dönmez, kilit meşgul 409'u okumalardan önce gelir (koordinatör kararı (c)) |
| Çok satırlı revizyonda bir satır terminal ret (409 veya 422), başka bir satır terminal olmayan sonuç (kilit meşgul 409, 503, port hatası, zaman aşımı) (§5.1 (c)) | Terminal olmayan kazanır: 503 varsa 503, yoksa kilit meşgul 409; hiçbir şey kaydedilmez, aynı anahtar yeniden çalışır (koordinatör okuması #66 6825436733; Satış X1 ile hizalı) |
| Satış satırı kilidi alınamadı (§3.2 adım 0) | 409 (kod F017-b'de adlandırılır) |
| Satış iptali/azaltmasında Stok bırakması başarısız (§5.1 (c)) | Satış komutu yazılmaz; Stok'un ret kodu (409/422/503) Satış problemine taşınır |
| Yetki yok | 401 / 403 |

Ekran (F017-d): yükleniyor, veri yok ("Bu ürün için açık satınalma satırı yok"), süzgeçle boş, yetkisiz, bulunamadı,
çakışma (güncel ayrılabilir miktarla yenile), meşgul (kilit; yeniden dene), çevrimdışı, servis yok, bayat veri durumları.
Ekran miktar ve birim çevirisini kendisi hesaplamaz; backend'in döndüğünü gösterir.

## 8. Testler

- **Unit (Domain):** gelecek ayırma yaşam döngüsü (oluştur/bırak/tüket/yeniden aç), tutarsız kayıt reddi, kalan ≤ ayrılan,
  sürüm artışı.
- **Unit (Application/Infrastructure):** idempotent replay, farklı yükle aynı anahtar, ret kodları, `NoSource`,
  `Unreadable` portun sıfır sayılmaması, snapshot'ın yalnız artıran komutta yazılması.
- **Gerçek SQL (SQL Server relational shard):** check kısıtı DB'de reddedilir; kilit altında küçük okunan değer DB
  istisnası değil 409 verir; unique defter satırı; migration `Down` veri varken reddeder; tenant filtresi başka şirketi
  göstermez.
- **Concurrency, Stok içi (F-017 kabul testi):** aynı satınalma satırına, toplamı ayrılabiliri aşan iki eşzamanlı
  ayırmadan biri reddedilir; ilk ayırmada eşzamanlı defter satırı oluşturma; bırak ile oluştur yarışı.
- **Concurrency, modüller arası (gerçek SQL, iki eşzamanlı işlem, aynı satır):** her durumda biri bekler veya 409 alır ve
  sonuç `ayrılmış ≤ ayrılabilir` olur (#1110 §6 ikinci ölçüt):
  - oluşturma × mal kabul;
  - oluşturma × "bağları düşür ve iptal et";
  - oluşturma × API-11 kapanışı;
  - bırakma × kabul çevirmesi;
  - oluşturma × kabul geri alma / canlanma;
  - Satış satır iptali/azaltması × mal kabul çevirmesi ve × oluşturma (F017-s);
  - **talep tarafı (F017-s, B P2-4):** Satış azaltması/iptali × aynı satış satırına **başka alış satırından** oluşturma,
    ve aynı satış satırına iki farklı alış satırından eşzamanlı oluşturma. Değişmezler: iptal edilmiş satırda aktif
    gelecek ayırma kalmaz; azaltmadan sonra aktif gelecek ayırma ≤ yeni açık miktar (fiziksel taraf aday 3 dışındadır);
    kilit sırası satış satırı → alış satırları olduğu için kilitlenme (deadlock) oluşmaz.
  - **Satış iptali/azaltması × kabul geri alma (yeniden açma) (F017-e/F017-s, B P2-4):** iki eşzamanlı işlem aynı satış
    satırında; değişmez aynıdır (iptal satırda aktif gelecek ayırma kalmaz; aktif gelecek ayırma ≤ yeni açık miktar,
    KLN-D28 (7)); yeniden açma iptal edilmiş satırda ayırmayı yeniden açmaz.
  Her test, ilgili Alış yolunun merge edildiği dilimde eklenir (§10).
- **Contract:** kilit kaynak adı `goymen:purchase-line:{companyId:N}:{lineId:N}` contract testine bağlanır. Alış portu ve
  Stok portları için sürümlü contract testleri (alan adları, fiyat alanı yokluğu, `NotFound`); `released.v1` olayının
  `reasonCode` ve `origin` alanları; outbox olay şeması; OpenAPI drift (F017-b2).
- **Mal kabul (F017-c):** kabul aynı işlemde gelecek ayırmayı tüketir, fiziksel tahsis oluşturur, etiket satış satırına
  bağlanır; replay aynı sonucu döner. Ayrıca: `AllocationOrder` > `ManualSequence` > termin > oluşturma zamanı zinciri;
  geçersiz `AllocationOrder` 422; az gelmede kalan açık ve katılımcı bırakma yapmaz; fazla kısım serbest; bloklu etiket ve
  `NoSource`'ta kabul yazılır, ayırma `acceptance-link-failed` ile bırakılır, olay yazılır; teknik hatada kabul, etiket ve
  tahsis birlikte geri alınır.
- **Bırakma portu (F017-b + Alış dilimleri):** `purchasing-link-dropped`, `source-cancelled` ve `purchase-line-closed`
  çağrıları aynı işlemde, kilit altında bırakır; biri başarısızsa Alış komutu da yazılmaz.
- **Kabul geri alma (F017-e):** hatalı kabul geri alınınca ayırma aynı kimlikle yeniden açılır; işaretli iadede açılır,
  işaretsizde `acceptance-reversed-no-supply` ile bırakılır; ≤ kontrolü; kısmi geri almada ters sıra; sevk edilmiş etiket
  409 `inventory_future_allocation_already_fulfilled`; fiziksel tahsis bırakılmışsa yeniden açma yok; Cancelled ve API-11 kapalı satırda
  canlanma sonrası yeniden açma ve yeni ayırma çalışır. **Canlanma (KLN-D29):** iptal veya API-11 kapanışında bırakılmış
  (`Released`) ayırmalar canlanmada geri gelmez; canlanan miktar porttan serbest ayrılabilir okunur ve yeni ayırma §3.2
  yoluyla yapılır; "canlandı" olayı/bildirimi yazılır (olayın sahibi Alış canlanma dilimi).
- **Satış portu (F017-s):** gelecek ayırma ayrı türle döner ve fiziksel kg toplamına girmez; iptal önizlemesi gelecek
  ayırmayı gösterir; okunamazsa iptal reddedilir.
- **Satış iptali/azaltması (F017-s, KLN-D28):** satış satırı iptali aynı işlemde satıra bağlı bütün aktif gelecek
  ayırmaları bırakır (§4.1 durum kuralı: tüketim yoksa `Released`, varsa `Consumed`; silme yok; `released.v1`, `origin = sales`, audit); azaltma max(0, ayırma − (yeni
  miktar − sevk edilen)) kadar bırakır (fiziksel 0; genel tanım KLN-D28 (4)), kalan ayırma aktif kalır; kısmi bırakma
  aynı ayırma satırında kimlik, oluşturma zamanı ve `ManualSequence`'i korur; aşan kısım birden çok ayırmaya yayılıyorsa en geç gelen (beklenen teslim tarihi) önce
  bırakılır, açık seçim varsayılanı geçer, bayat seçim 409, fazlayı aşan seçim 422, eşitlik kuralı deterministiktir,
  termin okunamazsa komut yazılmaz, çok dilimli satırda teslim alınmamış en geç dilim tarihi kullanılır (KLN-D30); fiziksel
  tahsis de varsa önce gelecek ayırma bırakılır (KLN-D31); fiziksel bırakma gerekirse ara durumda komut bütünüyle
  reddedilir ve hiçbir ayırma değişmez (KLN-D32; test: fiziksel 10 + gelecek 10, satır 20 → 5 → Stok portu 409
  `inventory_future_allocation_physical_release_unavailable`; gelecek ayırmanın `Version`/`ReleasedQuantity`'si, fiziksel
  tahsis, Stok audit/outbox ve ayırma satırları değişmez; Satış satırı değişmez ve Satış'ta tek terminal ret kaydı vardır;
  aynı anahtarla replay aynı reddi döner ve Stok portunu ikinci kez çağırmaz; 503 ve kilit meşgul kaydedilmez, aynı anahtar
  yeniden çalışır; termin okunamaz + "aktif gelecek < fazla" → 503, kayıt yok, aynı anahtar yeniden yürür; çok satırlı
  revizyonda bir satır 409, diğeri 503 → 503, kayıt yok; çok satırlı revizyonda bir satır terminal ret (422 veya kaydedilen 409), diğeri kilit meşgul 409 → kilit meşgul 409, kayıt yok, aynı anahtar yeniden yürür; bir satır terminal ret, biri kilit meşgul 409, biri 503 → 503, kayıt yok; fazlayı aşan seçim (422
  `inventory_future_allocation_release_selection_invalid`) + termin okunamaz → 503, kayıt yok, aynı anahtar yeniden yürür; 20 → 15 aynı kurulumda kabul edilir; 20 → 15 açık fiziksel seçimle 409, hiçbir şey değişmez);
  revizyonda satır silme: sevksiz, yükleme talimatsız, yalnız gelecek ayırmalı satır → bütün gelecek bırakılır;
  sevkli, yükleme talimatlı veya fiziksel tahsisli satır → Satış 422 `sales_line_revision_line_shipped`, Stok portu
  çağrılmaz, gelecek ayırma değişmez; SAT-D167 okumasından sonra eşzamanlı fiziksel tahsis oluşursa 0'a azaltma 409
  terminal döner, hiçbir ayırma değişmez; 422 seçim reddi ve durum 409'u kaydedilir; 503, port hatası, kilit meşgul ve `concurrent_change` kaydedilmez, bayat seçim 409 kaydedilir; bırakma başarısızsa,
  port okunamazsa veya kilit alınamazsa Satış komutu da yazılmaz (satır iptal edilmiş, ayırma aktif durumu oluşmaz);
  aynı Satış komut anahtarıyla replay ikinci bırakma üretmez; bırakma stok bakiyesini değiştirmez. Gerçek SQL: Satış iptali × mal
  kabul çevirmesi ve Satış iptali × oluşturma yarışında sonuç tutarlıdır.
  Golden satırlar (satır miktarı / sevk edilen / aktif gelecek ayırma / değişiklik → bırakılan; tek birimde;
  açık = yeni satır − sevk edilen):

  | Satır | Sevk | Ayırma | Değişiklik | Yeni satır | Açık | Bırakılan | Kalan ayırma |
  |---|---|---|---|---|---|---|---|
  | 100 | 0 | 60 | −30 | 70 | 70 | 0 | 60 |
  | 100 | 0 | 100 | −30 | 70 | 70 | 30 | 70 |
  | 100 | 0 | 60 | −50 | 50 | 50 | 10 | 50 |
  | 100 | 0 | 60 | tam iptal | 0 | 0 | 60 | 0 |
  | 100 | 40 | 60 | −20 | 80 | 40 | 20 | 40 |

  Bırakma sırası golden'ları (KLN-D30/KLN-D31/KLN-D32; formül ve 20 → 15 / 20 → 5 satırları koordinatör düzeltmesi #66
  6823547103; Aralık/Ocak satırları yazarın örneğidir):

  | Satır | Sevk | Ayırmalar | Değişiklik | Açık | Bırakılan | Kalan |
  |---|---|---|---|---|---|---|
  | 30 t | 0 | gelecek 10 t (15 Aralık) + gelecek 20 t (5 Ocak) | 30 → 20 | 20 | Ocak'tan 10 t | Aralık 10 t + Ocak 10 t |
  | 30 t | 0 | aynı | 30 → 20, kullanıcı Aralık'ı seçer | 20 | Aralık'tan 10 t | Ocak 20 t |
  | 20 | 0 | fiziksel 10 + gelecek 10 | 20 → 15 | 15 | gelecekten 5 | fiziksel 10 + gelecek 5 |
  | 20 | 0 | fiziksel 10 + gelecek 10 | 20 → 5 | 5 | fazla 15: gelecekten 10 + fiziksel 5 (ara durumda komut reddedilir, değişiklik yok) | fiziksel 5 (ara durumda: fiziksel 10 + gelecek 10) |
  | 20 | 0 | fiziksel 10 + gelecek 10 | 20 → 15, kullanıcı fiziksel 5'i seçer | 15 | ara durumda 409, değişiklik yok (yol gelince fiziksel 5) | ara durumda: fiziksel 10 + gelecek 10 |
  | 20 | 0 | gelecek 20 | revizyonda satır silme (→ 0) | 0 | gelecekten 20 | 0 |
  | 20 | 0 | fiziksel 10 + gelecek 10 | revizyonda satır silme | — | Satış 422 `sales_line_revision_line_shipped`, Stok portu çağrılmaz | fiziksel 10 + gelecek 10 |
  | 15 | 0 | fiziksel 10 + gelecek 5 | 15 → 5 | 5 | fazla 10: gelecekten 5 + fiziksel 5 (ara durumda reddedilir) | fiziksel 5 (ara durumda: değişmez) |
  | 100 | 40 | gelecek 60 | 100 → 80 | 40 | gelecekten 20 | gelecek 40 |

- **Termin uyarısı:** Stok bir işaret saklamaz ve hesaplamaz. Uyarı #1110 §3.5'teki okuma anı hesabıdır (Alış ekranı F17-4,
  Satış F17-5). Stok testi yalnız terminin geçmesinin ayırmayı bırakmadığını doğrular.
- **Yetki:** izin yokken 403; şirket değişince 404; izin önce/sonra ölçümü.
- **Frontend (F017-d):** decoder kesin anahtar, durum blokları, a11y.

## 9. Sorular ve cevaplar

Politika soruları Sinan tarafından cevaplandı; karar metni KLN-D26 (6), Alış eşleri ALS-D83…ALS-D93 (PR #1110), 19. tur
(#66 6822140358; ALS-D92), 21. tur (KLN-D28, KLN-D29; #66 6822767886), 23. tur (KLN-D30, KLN-D31; #66 6823243819) ve 25. tur
(KLN-D32; #66 6823547103). Sinan'ın metinleri #66'dadır (5. tur 6819640816; 11.–13.
tur 6820601088; 18. tur 6821262098; 21. tur 6822767886). Uygulama yeri parantezde.

- **S1 — Az gelirse:** gelen kadar ayrılır, kalan satınalma satırının gelecek kısmında açık kalır; satır kapanırsa kalan
  bırakılır ve satış uyarılır (12. tur m.6; ALS-D87). "Satır kapanırsa" = API-11 kapanışı (#1110 §3.6; §3.5, §3.6).
- **S2 — Satınalma satırı/sipariş iptali, bağ düşürme:** satınalma tarafı bağı düşürür, miktar azalmaz (5. tur m.3; ALS-D83).
  Ayırması olan satır/sipariş iptalinde önce bağlı satışlar gösterilir; "bağları düşür ve iptal et" gerekçeli tek onayla aynı
  işlemde, satışa "kaynak iptal edildi" uyarısı (11. tur m.1; ALS-D85; §3.6, §5 bırakma portu).
- **S3 — Fazla gelirse:** fazlası serbest stoğa girer (12. tur m.6; ALS-D87; §3.5).
- **S4 — Kısmi kabulde sıra:** termini en yakın satış önce; sıra elle değiştirilebilir/seçilebilir (5. tur m.4;
  ALS-D84; §3.5).
- **S5 — Kim yapar:** izinli tek kişi (satış, satınalma veya depo), ikinci onay yok; grant insan kapısı (12. tur m.7;
  ALS-D88; §6).
- **S6 — İzin adı (teknik, yazarın tercihi, geri alınabilir):** yeni `inventory.future-allocation.write` /
  `.read`. Gerekçe: gelecek ayırma fiziksel tahsisten ayrı etki türüdür (DEC-STK-006) ve S5 cevabı satınalma kullanıcısına
  da ayırma yetkisi verilebileceğini söyler; mevcut `inventory.physical-allocation.write` yeniden kullanılırsa satınalma
  kullanıcısı fiziksel etiket tahsisi yetkisini de alır. Ayrı ad grant'ı daraltır. Alış'ın bağ düşürme izni
  `purchasing.future-allocation.release` ayrıdır (PR #1110). Ad kaydı F017-b2'de, grant insan kapısı.
- **S7 — Kabulde bağlama başarısızsa:** kabul durmaz, mal stoğa girer, ayırma bırakılır, satış uyarılır (12. tur m.8;
  ALS-D89; §3.5).
- **S8 — Bloklu etiket:** S7 ile aynı kural (12. tur m.8; ALS-D89; §3.5).
- **S9 — Genel/teknik satır (teknik, yazarın tercihi, geri alınabilir):** ayırma **genel satıra** yapılır; teknik dilime
  doğrudan ayırma yoktur (port `technical-line` → 422). Kabul teknik dilimden gelirse Alış kabul sonucu
  (`PurchaseAcceptanceStageResultV1`) bağlı genel satır kimliğini bildirir ve Stok çevirmeyi genel satırdaki ayırmalar
  üzerinden yapar (§5). Gerekçe: ayrılabilir miktar genel satır düzeyindedir (`PurchaseOrderLine.cs:70-78`); PR #1110 §2
  aynı kuralı yazar.
- **S10 — Birim:** SAT-D298 ile kilitli; katsayı yoksa `NoSource`, ayırma yapılmaz (§3.2 adım 3).
- **S11 — Süre:** ayırmanın son tarihi yoktur; satınalma termini geçerse satış ve satınalma kullanıcısı uyarılır, ayırma
  açık kalır. Süre ileride ayarlardan tanımlanabilir; bugün ayar yoktur (13. tur m.9; ALS-D90). Bu tür için KLN-D26 (6)(h),
  DEC-STK-005'teki `ReservedUntil` varsayılanının yerine geçer: gelecek ayırmada `ReservedUntil` alanı açılmaz. Uyarı okuma
  anında hesaplanır; sahibi #1110 §3.5'tir (§8).
- **S12 — Kabul geri alma/iade:** tahsis ve etiket ataması geri alınır (11. tur m.3; ALS-D86). Gelecek miktarın açılması
  nedene bağlıdır: hatalı kabul geri alınırsa açılır; iadede "yerine yenisi gelecek" işaretliyse açılır, değilse kapalı
  kalır (18. tur m.7; ALS-D91). İptal edilmiş sipariş ve API-11 kapalı satırda da açılır, sipariş canlanır (19. tur; ALS-D92). §3.7,
  F017-e.
- **S13 — Satış satırı iptali veya azaltması:** "Otomatik bırakılır (Önerilen)" (21. tur m.2, 6822767886; KLN-D28).
  Satış komutu aynı işlemde Stok bırakma portunu çağırır; artan ayırma kalmaz; audit'li. Satış tarafı karşılığı SAT-D314
  (#1119, `main` `c93433f6`). Eski §9.1 aday 1. §5.1 (c), F017-s.
- **S14 — Canlanmada bırakılmış ayırmalar:** "Geri gelmez, yeniden ayrılır (Önerilen)" (21. tur m.3, 6822767886;
  KLN-D29). Canlanan miktar serbest açık miktardır; kullanıcı gerekirse yeniden ayırır; "canlandı" bildirimi. Eski §9.1
  aday 2. §3.6, §3.7, F017-e.
- **S15 — Azaltmada birden çok gelecek ayırma:** "En geç gelen önce (Önerilen)" (23. tur m.1, 6823243819; KLN-D30).
  En geç gelecek alıştan (beklenen teslim tarihi) bırakılır; kullanıcı komutta başka ayırma seçebilir. Eski §9.1 aday 4.
  §5.1 (c), F017-s.
- **S16 — Azaltmada fiziksel tahsis + gelecek ayırma:** "Önce gelecek ayırma (Önerilen)" (23. tur m.2, 6823243819;
  KLN-D31). Depodaki hazır mal müşteride kalır; kullanıcı değiştirebilir. Eski §9.1 aday 3'ün bırakma sırası kısmı.
  §5.1 (c), F017-s.
- **S17 — Gelecek ayırma yetmezse fiziksel tahsis fazlası:** "Otomatik bırakılır (Önerilen)" (25. tur m.1, 6823547103;
  KLN-D32). Fazla önce gelecekten, yetmezse fiziksel tahsisten bırakılır; kullanıcı seçimi geçer; fiziksel bırakma yolu
  hazır olana kadar böyle azaltma reddedilir. §5.1 (c), F017-s.

### 9.1 `NEEDS-SINAN` / `NEEDS-SPEC` adayları (yazılmadı; Issue #66'ya koordinatör taşır)

- Adaylar 1 ve 2 cevaplıdır: aday 1 → S13 / KLN-D28, aday 2 → S14 / KLN-D29 (Sinan 21. tur, #66 6822767886). Aday 4
  cevaplıdır: S15 / KLN-D30 (Sinan 23. tur, #66 6823243819).
- **NEEDS-SPEC adayı 3 — Fiziksel tahsiste talep üst sınırı:** bugünkü fiziksel tahsis, aynı satış satırına satır
  miktarından fazla tahsisi kontrol etmiyor. §3.2 adım 5 gelecek ayırma için okuma olarak yazıldı; fiziksel tarafın kuralı
  ayrı spec işidir. Azaltmada fiziksel/gelecek bırakma sırası (S16 / KLN-D31) ve fiziksel fazla (S17 / KLN-D32)
  cevaplıdır; üst sınır kuralı ve etiket bazlı tahsisin kısmi bırakılma yolu açık kalır (KLN-D32 (4); cevap gelene kadar
  ara red).
- **NEEDS-SPEC adayı 6 — Yeniden açma tavanı (F017-e; r4 B P3-1):** §3.7 yeniden açması yalnız alış tarafında
  sınırlanır; fiziksel tahsis çevirmeden sonra küçülmüşse yeniden açılan gelecek miktar satışın açık talebini aşabilir.
  Yazarın önerisi (seçilmedi): yeniden açılan ≤ geri alınan ve hâlâ aktif fiziksel miktar, satış kilidi altında talep
  tavanı kontrolü. Aşan kısmın davranışı (kısmi yeniden açma veya ret) F017-e öncesi sorulur.
- **NEEDS-SPEC adayı 5 — "Canlandı" bildiriminin alıcısı ve kanalı:** KLN-D29 bildirimi verir; kime (satış/satınalma
  kullanıcısı) ve hangi kanalla gideceği seçilmedi. Alış canlanma dilimi (#1110 §3.8) ile birlikte sorulur.

## 10. Dilim planı

Sıra: koordinatörün #1101 kuyruğu bitmeden kod yok. PR #1110 §4 ile kimlik eşlemesi: F017-a ≈ F17-1 (+ ortak sözleşme ve
kilit), F017-b ≈ F17-2, F017-c ≈ F17-3, F017-b2 ≈ F17-2b, F017-e ≈ F17-6, F017-d ≈ F17-5'in Stok ekranı. F017-s'nin
#1110'da eşi yoktur.

**Kapı kuralı (#1110 §4'ten alındı; değişmez her merge'de main üzerinde korunur):**

- F017-b ayırma kaydını, değişmezi ve bırakmayı getirir. Ama **ayırma oluşturma yolu** (endpoint, OpenAPI ve izin adı
  kaydı) F017-b'de yoktur; yalnız servis ve testler vardır. Oluşturma yolu F017-c (kabulde çevirme) ve #1110 F17-4 (iptal
  koruması) merge edildikten sonra **F017-b2** ile açılır. Böylece kabul çevirmesi veya iptal koruması yokken aktif ayırma
  oluşmaz; aynı ton hem ayırmada hem serbest stokta sayılmaz ve iptal ayırmayı iptal edilmiş arzın üstünde bırakmaz.
- Ek Stok koşulu: F017-b2, F017-s merge edilmeden de açılmaz. Böylece gelecek ayırması olan satış siparişi iptal
  önizlemesinde "tahsis yok" görünmez (§5.1).
- API-44/API-60 F017-e'den önce merge edilirse, aktif veya tüketilmiş gelecek ayırması olan satırda kabul geri alma/iade
  fail-closed 409 `purchasing_line_has_allocations` ile reddedilir; F017-e bu reddi kaldırır (§3.7).
- API-11 için tek kural #1110 §3.6'dadır: API-11 F17-4'ten önce gelirse kapanışta bırakmayı F17-4 ekler; sonra gelirse
  bırakma API-11 dilimindedir. Oluşturma yolu F17-4'ten önce açılmadığı için arada bırakılacak ayırma oluşmaz.

| Dilim | Şerit | İçerik | Dosya alanı | K | Bağımlılık |
|---|---|---|---|---|---|
| F017-0 | Stok (docs) | Bu belge | `docs/delivery/STOK_F017_…` | K1 | KLN-D26 (main `2efb3f68d`; karşılandı) |
| F017-a | Alış + Stok | Port sözleşmesi: Alış `IPurchaseFutureSupplyReadPortV1` (Alış yazar) + Stok'un `IInventoryFutureAllocationReadPortV1` arayüzü; `IPurchaseLineSerializationLockV1` arayüzü ve host uygulaması (#1110 F17-1); kilit kaynak adı contract testi; ortak contract testi | `src/backend/Modules/Purchasing/**/Contracts/`, `src/backend/Modules/Inventory/**/Application/`, host/BuildingBlocks, `tests/GoymenERP.*` contract | **K3** (cross-module contract + kilit) | PR #1110 merge |
| F017-b | Stok (Codex backend) | Gelecek ayırma domain + `FutureSupplyAllocations` / `FutureSupplyLines` + migration + check/unique kısıtları; oluşturma ve bırakma servisi (§3.2 adım 0 kilidi + aynı işlemde Alış okuması); Stok okuma portu; bırakma portu; outbox; **endpoint, OpenAPI ve izin kaydı yok** | `src/backend/Modules/Inventory/**`, recovery/relational envanterleri, `tests/GoymenERP.*` | **K3** (migration + kısıt + cross-module) | F017-a |
| F017-c | Stok (Codex backend) | Mal kabulde aynı host işleminde gelecek ayırmayı tüketme + fiziksel tahsis + bağ satırı; `AllocationOrder` ve sıra zinciri; replay; `acceptance-link-failed`; bırakma × kabul çevirmesi yarış testi | `…Inventory.Infrastructure/Persistence/InventoryGoodsReceiptParticipant.cs`, `src/backend/GoymenERP.Api/PurchasingRuntime/`, migration (bağ tablosu) | **K3** (migration + cross-module işlem) | F017-b |
| F017-b2 | Stok (Codex backend) | Oluşturma ve bırakma endpoint'leri, OpenAPI, izin adı kaydı (grant yok); oluşturma × mal kabul / × "bağları düşür ve iptal et" / × API-11 kapanışı yarış testleri (ilgili Alış yolu main'deyse) | `src/backend/GoymenERP.Api/Endpoints/Inventory*`, `contracts/openapi/`, izin/manifest kaydı, `tests/GoymenERP.*` | **K3** (yeni endpoint + izin; #1110 F17-2b'yi K2 yazar, iki belge aynı seviyeye getirilir) | F017-c, #1110 F17-4, F017-s |
| F017-s | Stok + Satış (+host; Codex backend) | Satış tahsis okumasında gelecek ayırma için ayrı tür, yeni sürümlü port veya v2 (§5.1 a); iptal önizlemesinin bunu görmesi ve fail-closed (§5.1 b); stok kısmına girmediğinin testi; Satış satır iptali/azaltmasında aynı host işleminde Stok bırakma portu çağrısı, iptalde tümü / azaltmada aşan kısım, başarısızlıkta Satış komutunun geri alınması, replay (§5.1 c, KLN-D28) | `src/backend/Modules/Sales/**`, `src/backend/Modules/Inventory/**`, `src/backend/GoymenERP.Api/` (host), `tests/GoymenERP.*` contract + gerçek SQL | **K3** (cross-module contract + cross-module işlem) | F017-b; #1110 F17-1b `GetLineScheduleAsync` (#1101) main'de olmalı (termin verisi olmadan sürülmez, KLN-D30 (4)); KLN-D28, KLN-D30, KLN-D31, KLN-D32 (karşılandı); fiziksel bırakma yolu `NEEDS-SPEC` (KLN-D32 (4); o gelene kadar ara red); Satış `ReadAllocationsAsync` yalnız fiziksel tahsisi sayar (§5.1 (c)) |
| F017-d | Claude (frontend) | Satış satırı ekranından "açık satınalmadan ayır" ve ayırma listesi/bırakma; kabul ekranında ayırma bilgisi; generated client tüketimi; durum blokları | `src/frontend/goymen-erp-web/**` (Stok/Satış klasörleri) | K2 | F017-b2 OpenAPI; izin capability kaydı |
| F017-e | Stok + Alış (+host) | Kabul geri alma/iade Stok tarafı (§3.7): tahsis/etiket geri alması, yeniden açma (≤ kontrolü), `acceptance-reversed-no-supply`, ters sıra, fail-closed kurallar, canlanma sonrası yeniden açma; geçici 409'un kaldırılması; oluşturma × geri alma/canlanma yarış testi | `…Inventory.Infrastructure/Persistence/**`, `src/backend/GoymenERP.Api/PurchasingRuntime/`, `tests/GoymenERP.*` | **K3** (cross-module işlem) | F017-c, API-44, API-60; canlanma kısmı Alış canlanma K3 dilimi (19. tur); yeniden açma tavanı `NEEDS-SPEC` adayı 6 (§9.1) |

Her K3 dilimde: iki bağımsız exact-head review, gerçek SQL kanıtı, fresh CI, merge öncesi main tazelik kontrolü
(AGENTS "Merge öncesi main tazelik kontrolü"). Production aktivasyonu, grant ve `OPEN-*` kapatma insan kapısıdır.

## 11. Sınırlar

- Bu belge karar değildir; `KARAR_SICILI`, `ACIK_KARARLAR` ve karar dosyaları değişmez.
- "Öneri", "yazarın okuması" ve "yazarın önerisi, geri alınabilir" diye işaretli teknik noktalar (ayrı tablo, izin adı,
  olay adları, hata kodları, eşitlik kuralları, kısmi yeniden açma, ters sıra) review'da değişebilir.
- Para, stok veya maliyet politikası seçilmez; politika cevapları Sinan'ındır (KLN-D26 (6), ALS-D91, 19. tur / ALS-D92, KLN-D28…
  KLN-D32). Açık noktalar §9.1'de aday olarak durur (aday 3'ün üst sınır ve etiket bazlı kısmı, aday 5, aday 6); Issue #66'ya koordinatör `NEEDS-SINAN`/`NEEDS-SPEC`
  olarak yazar.
- Hiçbir `OPEN-*` kapatılmaz; grant, rol veya Critical açılmaz.
