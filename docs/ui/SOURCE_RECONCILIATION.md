# UI kaynak uzlaştırması

Bu kayıt, aynı ekran kimliklerinin farklı belgelerde farklı anlam taşıdığı durumları açık tutar. Amaç geçmişi silmek değil; Codex’in bir kaynağı diğerinin yerine yanlışlıkla kullanmasını engellemektir. `UI-MAP-001…006` kararları 27 Ağustos 2026 kullanıcı kabulüyle UI referans topolojisi için kesinleştirilmiştir; repo Master veya domain iş kurallarını değiştirmez.

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

**UI-MAP-001:** Dosya adındaki `S-*`/`P-*` yalnız `source_id` sayılır. Repo Master kimlikleri kanonik kalır; kabul edilen `EXACT/SPLIT/MERGE/EMBEDDED_COMPONENT` ilişkileri yalnız kaynak görselin hangi kanonik ekran veya bileşene referans olduğunu gösterir.

**UI-MAP-002:** Kaynak `S-12`, kanonik `S-04 Sipariş 360` içindeki `Revizyon & Geçmiş` sekmesidir. `S-10` yalnız düzeltme/iptal eyleminde açılan `conditional_workspace_ids` kaydıdır; S-12 için eşdeğer ikinci kanonik hedef değildir. Aynı model kaynak `P-12` için uygulanır: `P-04` içindeki ürün kırılımı workspace’i, gerektiğinde koşullu `U-02` akışını açar.

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
- Finans v3, 13 ana ekran, 73 işlem workspace’i ve 26 doğrulanmış PNG’nin ayrıntılı temelidir.
- Finans v4, v3’ü silmez; navigasyon, rol/yoğunluk görünümü, “Benim İşlerim/Sade” varsayılanı ve görünür AI kontrol katmanını ekler.
- Finans v4 8 doğrulanmış PNG ile kullanıcı kolaylığı/navigasyon/AI deltasını görselleştirir.
- İki self-contained HTML ve toplam 34 PNG `PROTOTYPE_REFERENCE` olarak tutulur; pixel-kilitli statü verilmez.
- **UI-MAP-005:** Finans v3/v4 kaynak kimlikleri ile repo kanonik `F-01…F-17` anlamları ilk üç ekrandan sonra kaydığı için kaynak ID’ler korunmuş, katalogdaki kanonik referans topolojisi kesinleştirilmiştir.

## 6. Cari prototip kimlikleri

Cari HTML kendi içinde resmî ekran ID taşımıyor. `CARI-ROUTE-01…10`, hash tabanlı route ve çalışma alanlarını kataloglamak için oluşturulmuş record kimlikleridir; yeni kanonik ekran ilanı değildir.

**UI-MAP-003:** Route kayıtları C-01…C-11’e kesin referans olarak bağlanır. C-05 ve C-07 kaynakları C-03 içindeki gömülü bileşenlerdir. S/P/ST/AL/B/R/SY bağlantıları doğrudan Cari sahipliği üretmez; `related_reference_ids` alanında tutulur. `C-12 İlişki/Takip Görevleri` için görev CRUD, sorumlu, termin, sonuç, hatırlatma ve audit akışını gösteren yeni görsel/workspace hazırlanmalıdır.

Üç Cari HTML dosyası byte-for-byte aynıdır. Repo yalnız temel adlı tek kopyayı taşır; `(1)` ve `(2)` kopyaları manifestte yinelenen kaynak olarak belirtilir.

## 7. İrsaliye/Fatura kimlik ve statü çatışmaları

24 Ağustos 2026 şartnamesinin açık statüsü “Tasarım önerisi tamamlandı — kullanıcı kabulü sonrası kilitlenebilir”dir. HTML önizlemesindeki “nihai” ifadeleri bunu tek başına `LOCKED` yapmaz.

Şartname metni `SW-01/SW-02/PW-01/PW-02`, final tablosu ise `SDW-01/SDW-02/PDW-01/PDW-02` kullanır. Katalog final tablo kimliklerini record ID yapar; kısa kimlikleri alias olarak saklar.

**UI-MAP-004:** SDH/SDW/PDH/PDW kimlikleri yalnız kaynak alias’ıdır; gerçek kanonik hedefler repo S/P/L/B ekranlarıdır. Referans topolojisi kesinleştirilmiştir ancak tasarım `DESIGNED_NOT_LOCKED` kalır. Bu karar, birleşik menü veya kullanıcı kabulünü kendiliğinden kilitlemez.

## 8. Karara bağlanan altı uzlaştırma

| Karar | Kesin sonuç |
|---|---|
| `UI-MAP-001` | Repo Master kanonik ID’leri değişmez; eski DOCX S/P kodları yalnız kaynak kimliğidir |
| `UI-MAP-002` | S-12 → S-04 Revizyon & Geçmiş sekmesi; S-10 yalnız koşullu workspace |
| `UI-MAP-003` | Cari route’ları C-01…C-11’e bağlanır; yeni ID üretmez; C-12 referansı eksiktir |
| `UI-MAP-004` | İrsaliye/Fatura kaynak alias’ları S/P/L/B kanonik hedeflerine bağlanır; statü `DESIGNED_NOT_LOCKED` |
| `UI-MAP-005` | Finans v3 işlevsel taban, v4 kullanıcı kolaylığı/navigasyon/AI deltasıdır |
| `UI-MAP-006` | Görsel üretme sırası Sistem Yönetimi → Belgeler → Stok & Tahsis → Üretim → Finans eksikleri → Raporlar |

## 9. Kalan sınırlar

- Ürün/Hizmet paketindeki `CANDIDATE_*` kayıtları ve `PRODUCT-U-04 UNRESOLVED` bu altı kararın kapsamı dışındadır; ayrıca onaylanmadan kesin sayılmaz.
- Dashboard görseli D-01 ve global kabuk için güçlü adaydır fakat kilitli değildir.
- C-12 için yeni görsel/workspace hazırlanmalıdır.
- UI eşlemesinin kesinleşmesi permission, CompanyId, formül, onay, audit veya domain davranışını kesinleştirmez; ilgili repo DEC/ADR/acceptance kaydı kodlama öncesinde yine zorunludur.
