# UI kaynak uzlaştırması

Bu kayıt, aynı ekran kimliklerinin farklı belgelerde farklı anlam taşıdığı durumları açık tutar. Amaç geçmişi silmek değil; Codex’in bir kaynağı diğerinin yerine yanlışlıkla kullanmasını engellemektir.

## 1. Yetki ve kaynak önceliği

1. System/developer talimatları, güncel kullanıcı isteği ve repo `AGENTS.md`.
2. Kabul edilmiş Karar Sicili.
3. Repo Master ve kabul edilmiş ADR/feature/acceptance/formül kaynakları.
4. Onaylı `docs/ui/screens/*` uzlaştırma kaydı.
5. Hash’i kayıtlı özgün görsel, PDF, DOCX veya HTML.
6. Envanter/aktarım planı ve prompt taslakları.

Görsel; CompanyId, yetki, onay, formül, stok veya finansal gerçeklik kaynağı değildir. Bir dosyanın daha yeni tarihli olması tek başına üst kaynağı geçersiz kılmaz; açık `supersedes` veya Karar Sicili gerekir.

## 2. Repo Master ile ekli Master DOCX kimlik çatışması

Ekli `Goymen_ERP_Master_Kararlar_ve_Kilitli_Ekran_Tasarimlari_Raporu_v3_0(2).docx` içinde `S-01…S-12` ve `P-01…P-12` ekranları vardır. Repo Master bölüm 27’de aynı kimliklerin anlamı farklıdır.

Örnekler:

- DOCX `S-06` Tahsis/Kaynak Yönetimi; repo `S-06` Satış Onaylarıdır. Tahsis repo kanonunda `AL-01…AL-06` ve `ST-01` alanına bölünmüştür.
- DOCX `S-01` birleşik Talep/Sipariş listesidir; repo `S-01` Talep/Teklif Havuzu, `S-02` Satış Siparişleridir.
- DOCX `S-12` vardır; repo satış kataloğu `S-11` ile biter.
- DOCX `P-01` alış siparişleri listesidir; repo `P-01` ihtiyaç havuzu, `P-03` satın alma siparişleridir.

Bu nedenle dosya adındaki `S-*`/`P-*` yalnız `source_id` sayılır. `canonical_ids` ekran kartında ayrı ve `CANDIDATE_*` statüsüyle tutulur. Karar Sicili onayı olmadan repo kanonik kimlikleri değiştirilmez.

## 3. Master DOCX güvenilir bölüm sınırı

- Güvenilir katalog: PDF render sayfa 18–41 ve DOCX `word/media/image2.png…image25.png`.
- `image2…image13`: DOCX `S-01…S-12`.
- `image14…image25`: DOCX `P-01…P-12`.
- `image1`: dört ekranlı kapak kolajı; tek ekran değildir ve kataloğa asset olarak alınmamıştır.
- Sayfa 52 sonrası tarihsel eklerde boş drawing ilişkileri, yeniden kullanılan yanlış görseller ve caption uyuşmazlıkları vardır. Bunlar ekran eşleme kaynağı değildir.

Kaynak DOCX Word tarafından onarım gerektirdiği için 27 MB dosyanın tamamı repoya alınmamıştır. Yalnız güvenilir ham PNG’ler ve kaynak hash/kanıt bilgisi taşınmıştır.

## 4. Ürün/Hizmet fiyat kimliği çatışması

24 Ağustos 2026 Ürün/Hizmet paketindeki `F-01…F-04`, Finans değil ürün fiyat yönetimidir. Repo kanonunda `F-01…F-17` Finans & Hazine ekranlarıdır.

Katalogda bu dört kayıt `PRICE-01…PRICE-04` record kimliğiyle tutulur; özgün `F-01…F-04` değeri `source_id` alanında korunur. Aday eşlemeler sırasıyla repo `U-05`, `U-06`, `U-10` ve `U-05` alanlarıdır.

## 5. Finans sürüm ilişkisi

- Finans v1 kimlik anlamları tarihsel kaynaktır; aktif referans değildir.
- Finans v3, 13 ana ekran ve 73 işlem workspace’inin ayrıntılı temelidir.
- Finans v4, v3’ü silmez; navigasyon, rol/yoğunluk görünümü, “Benim İşlerim/Sade” varsayılanı ve görünür AI kontrol katmanını ekler.
- Finans v3/v4’te kilitli özgün ekran PNG seti bulunmadı; buna karşılık iki self-contained HTML prototip vardır. `F-01…F-13` kayıtları `PROTOTYPE_REFERENCE` olarak tutulur, pixel-kilitli statü verilmez.
- Finans v3/v4 kaynak kimlikleri ile repo kanonik `F-01…F-17` anlamları ilk üç ekrandan sonra kayar; uzlaştırma `CANDIDATE_*` olarak tutulur.

## 6. Cari prototip kimlikleri

Cari HTML kendi içinde resmî ekran ID taşımıyor. `CARI-ROUTE-01…10`, hash tabanlı route ve çalışma alanlarını kataloglamak için oluşturulmuş record kimlikleridir; yeni kanonik ekran ilanı değildir. Repo `C-01…C-10` eşlemeleri adaydır.

Üç Cari HTML dosyası byte-for-byte aynıdır. Repo yalnız temel adlı tek kopyayı taşır; `(1)` ve `(2)` kopyaları manifestte yinelenen kaynak olarak belirtilir.

## 7. İrsaliye/Fatura kimlik ve statü çatışmaları

24 Ağustos 2026 şartnamesinin açık statüsü “Tasarım önerisi tamamlandı — kullanıcı kabulü sonrası kilitlenebilir”dir. HTML önizlemesindeki “nihai” ifadeleri bunu tek başına `LOCKED` yapmaz.

Şartname metni `SW-01/SW-02/PW-01/PW-02`, final tablosu ise `SDW-01/SDW-02/PDW-01/PDW-02` kullanır. Katalog final tablo kimliklerini record ID yapar; kısa kimlikleri alias olarak saklar. Repo kanonik S/P/L/B ekranlarına eşleme adaydır.

## 8. Açık kararlar

Kodlama öncesinde şu konular Karar Sicili/ADR ile sonuçlandırılmalıdır:

1. DOCX S/P kimliklerinin repo kanonik kimliklerine kesin split/merge eşlemesi.
2. `S-12` revizyon ekranının kanonik `S-04`, `S-10` veya yeni alt workspace olarak yeri.
3. Cari route kayıtlarının `C-01…C-12` kapsamındaki kesin sınırı.
4. İrsaliye/Fatura şartnamesinin kullanıcı kabulü ve kısa/final workspace kimliği standardı.
5. Finans `F-01…F-13` v3/v4 ekranlarının repo `F-01…F-17` ile kesin uzlaştırması.
6. Görseli eksik Finans, Stok, Üretim, Belge, Rapor ve Sistem Yönetimi ekranlarının hangi sırayla üretileceği.
