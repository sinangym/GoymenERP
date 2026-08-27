# Göymen ERP ekran referans kataloğu

Bu katalog, repo Master bölüm 27’deki **159 kanonik ekran** ile mevcut görsel/prototip kaynaklarını birbirinden ayırır. Bu sürümde 72 kaynak ekran/bileşen kaydı vardır; 79 kanonik ekran en az bir doğrudan referansla ilişkilidir. Bunların 68’i kabul edilmiş UI uzlaştırmasına, 11’i aday veya çözümlenmemiş referansa sahiptir; 80 kanonik ekranın doğrudan görsel/prototip referansı henüz yoktur.

## Kapsam özeti

| Kaynak ailesi | Kayıt | Kanıt | Statü |
|---|---:|---|---|
| Dashboard / global kabuk | 1 | Kullanıcı tarafından verilen 1536×1024 PNG | DESIGNED_NOT_LOCKED; D-01 adayı |
| Master DOCX Satış | 12 | 12 özgün PNG, sayfa 18–29 | S-01…S-12 LOCKED_VISUAL |
| Master DOCX Satın Alma | 12 | 12 özgün PNG, sayfa 30–41 | P-01…P-03 LOCKED; P-04…P-12 FINAL-QA |
| Ürün/Hizmet v1.0 | 15 | 15 özgün 1600×960 raster + 43 sayfa PDF + HTML | LOCKED_VISUAL; U-06/A-01 HTML’de yok |
| Cari v3 HTML | 10 route/workspace kaydı | Tek benzersiz interaktif HTML | PROTOTYPE_REFERENCE; kimlikler öneridir |
| Finans v3+v4 | 13 | İki interaktif HTML + 26 v3 ve 8 v4 PNG + kaynak seti | ACTIVE_BASE_PLUS_DELTA |
| İrsaliye/Fatura v1.0 | 9 merkez/workspace/bileşen | HTML + PNG + Markdown şartname | DESIGNED_NOT_LOCKED |

## Okuma sırası

1. [UI tasarım sistemi](UI_DESIGN_SYSTEM.md)
2. [Kaynak uzlaştırması](SOURCE_RECONCILIATION.md)
3. [Makine-okunur kaynak ekran kataloğu](SCREEN_CATALOG.csv)
4. [159 kanonik ekranın kapsama durumu](CANONICAL_SCREEN_STATUS.csv)
5. İlgili ekran kartı, varlık ve kaynak manifesti

## Kritik sonuçlar

- Master DOCX ekran kimlikleri repo kanonik kimlikleriyle aynı anlama gelmez; kaynak kimliği olarak korunur. UI-MAP-001 yalnız referans topolojisini kesinleştirir, repo kanonik kimliklerini değiştirmez.
- Ürün/Hizmet paketindeki F-01…F-04 fiyat ekranları Finans değildir; record kimlikleri PRICE-01…PRICE-04 olarak ayrılmıştır.
- Cari HTML route kimlikleri yeni ekran ID değildir. UI-MAP-003 ile C-01…C-11 referansları kesinleştirilmiştir; C-12’nin görsel/workspace referansı eksiktir.
- İrsaliye/Fatura şartnamesi güçlü ve geliştirmeye yakındır, fakat kendi metnine göre kullanıcı kabulü bekler; LOCKED değildir.
- Finans v3 13 ekran/73 workspace ve 26 PNG tabanıdır; v4 bu tabana kullanıcı kolaylığı, navigasyon, görünür AI ve 8 PNG delta ekler.
- Master DOCX’in yalnız sayfa 18–41 / image2–25 bölümü güvenilir ekran kanıtıdır; sonraki tarihsel ek figürleri kırık/yanlış ilişkilidir.

## Dashboard ve global kabuk referansı

| Record | Kaynak ID | Başlık | Kaynak statüsü | Görsel | Eşleme | Kanonik / ilişkili hedef |
|---|---|---|---|---|---|---|
| [DASHBOARD-REFERENCE-D-01](screens/dashboard/DASHBOARD-REFERENCE-D-01.md) | dashboard-main | Ana Sayfa ve Global Kabuk Referansı | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | CANDIDATE_EXACT | D-01; ilişkili: GLOBAL-SHELL |

## Satış kaynak ekranları

| Record | Kaynak ID | Başlık | Kaynak statüsü | Görsel | Eşleme | Kanonik / ilişkili hedef |
|---|---|---|---|---|---|---|
| [MASTER3-S-01](screens/sales/MASTER3-S-01.md) | S-01 | Satış Talep / Siparişler Ana Liste | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | S-01, S-02 |
| [MASTER3-S-02](screens/sales/MASTER3-S-02.md) | S-02 | Satış Sipariş Detayı / Sipariş Satırları | LOCKED_VISUAL | LOCKED_VISUAL | MERGE | S-04 |
| [MASTER3-S-03](screens/sales/MASTER3-S-03.md) | S-03 | Satış Sipariş Satırı Ekle / Düzenle | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | S-03, S-04 |
| [MASTER3-S-04](screens/sales/MASTER3-S-04.md) | S-04 | Toplu Satır Ekle / Düzenle | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | S-03, S-04 |
| [MASTER3-S-05](screens/sales/MASTER3-S-05.md) | S-05 | AI ile Satır Oluştur | LOCKED_VISUAL | LOCKED_VISUAL | MERGE | S-03 |
| [MASTER3-S-06](screens/sales/MASTER3-S-06.md) | S-06 | Tahsis / Kaynak Yönetimi | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | ST-01, AL-01, AL-02, AL-03, AL-04, AL-05, AL-06 |
| [MASTER3-S-07](screens/sales/MASTER3-S-07.md) | S-07 | Gerçekleşme / İrsaliye Ana Sekmesi | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | S-04, S-07, L-05, L-07, L-08 |
| [MASTER3-S-08](screens/sales/MASTER3-S-08.md) | S-08 | Yeni Sevkiyat / Gerçekleşme Workspace | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | L-03, L-05, L-07, L-08 |
| [MASTER3-S-09](screens/sales/MASTER3-S-09.md) | S-09 | Sipariş Faturalar Sekmesi | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | S-04, S-08, S-09 |
| [MASTER3-S-10](screens/sales/MASTER3-S-10.md) | S-10 | Fatura Taslağı / Eşleştirme Workspace | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | S-08, S-09 |
| [MASTER3-S-11](screens/sales/MASTER3-S-11.md) | S-11 | Satış Sözleşme & Belgeler | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | S-11, B-06, B-07, B-09 |
| [MASTER3-S-12](screens/sales/MASTER3-S-12.md) | S-12 | Satış Revizyon & Geçmiş | LOCKED_VISUAL | LOCKED_VISUAL | EMBEDDED_COMPONENT | S-04; koşullu: S-10 |

## Satın alma kaynak ekranları

| Record | Kaynak ID | Başlık | Kaynak statüsü | Görsel | Eşleme | Kanonik / ilişkili hedef |
|---|---|---|---|---|---|---|
| [MASTER3-P-01](screens/purchasing/MASTER3-P-01.md) | P-01 | Alış Siparişleri Ana Liste | LOCKED_VISUAL | LOCKED_VISUAL | EXACT | P-03 |
| [MASTER3-P-02](screens/purchasing/MASTER3-P-02.md) | P-02 | Alış Sipariş Detayı / Sipariş Satırları | LOCKED_VISUAL | LOCKED_VISUAL | MERGE | P-05 |
| [MASTER3-P-03](screens/purchasing/MASTER3-P-03.md) | P-03 | Tedarik & Termin | LOCKED_VISUAL | LOCKED_VISUAL | SPLIT | P-05, P-06 |
| [MASTER3-P-04](screens/purchasing/MASTER3-P-04.md) | P-04 | Mal Kabul / İrsaliye Sekmesi | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | SPLIT | P-07, L-09, L-10 |
| [MASTER3-P-05](screens/purchasing/MASTER3-P-05.md) | P-05 | Alış Faturaları Sekmesi | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | SPLIT | P-08, P-09 |
| [MASTER3-P-06](screens/purchasing/MASTER3-P-06.md) | P-06 | Alış Sözleşme & Belgeler | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | SPLIT | P-05, B-06, B-07, B-09 |
| [MASTER3-P-07](screens/purchasing/MASTER3-P-07.md) | P-07 | Alış Revizyon & Geçmiş | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | SPLIT | P-05, P-10 |
| [MASTER3-P-08](screens/purchasing/MASTER3-P-08.md) | P-08 | Alış Sipariş Satırı Ekle | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | EXACT | P-04 |
| [MASTER3-P-09](screens/purchasing/MASTER3-P-09.md) | P-09 | Termin Bildirimi Ekle | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | SPLIT | P-05, P-06 |
| [MASTER3-P-10](screens/purchasing/MASTER3-P-10.md) | P-10 | Yeni Mal Kabul / Alış İrsaliyesi | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | SPLIT | P-07, L-10 |
| [MASTER3-P-11](screens/purchasing/MASTER3-P-11.md) | P-11 | Gelen Fatura Girişi / Eşleştirme | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | SPLIT | P-08, P-09 |
| [MASTER3-P-12](screens/purchasing/MASTER3-P-12.md) | P-12 | Ürün Kırılımı Oluştur | DESIGNED_FINAL_QA | DESIGNED_FINAL_QA | EMBEDDED_COMPONENT | P-04; koşullu: U-02 |

## Ürün, hizmet, fiyat ve ayarlar

| Record | Kaynak ID | Başlık | Kaynak statüsü | Görsel | Eşleme | Kanonik / ilişkili hedef |
|---|---|---|---|---|---|---|
| [PRODUCT-A-01](screens/catalog/PRODUCT-A-01.md) | A-01 | Ayarlar > Ürün & Hizmet | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_SPLIT | U-08, U-09, AV-02 |
| [PRODUCT-H-01](screens/catalog/PRODUCT-H-01.md) | H-01 | Hizmetler Liste / Detay | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_EXACT | U-03 |
| [PRODUCT-H-02](screens/catalog/PRODUCT-H-02.md) | H-02 | AI Destekli Yeni Hizmet | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_EXACT | U-04 |
| [PRODUCT-H-03](screens/catalog/PRODUCT-H-03.md) | H-03 | Hizmet Düzenle | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_MERGE | U-04 |
| [PRODUCT-PRICE-01](screens/catalog/PRODUCT-PRICE-01.md) | F-01 | Fiyatlar Çalışma Alanı | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_EXACT | U-05 |
| [PRODUCT-PRICE-02](screens/catalog/PRODUCT-PRICE-02.md) | F-02 | Toplu Fiyat Güncelleme | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_EXACT | U-06 |
| [PRODUCT-PRICE-03](screens/catalog/PRODUCT-PRICE-03.md) | F-03 | Excel Fiyat Eşleştirme ve İçe Aktarma | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_EXACT | U-10 |
| [PRODUCT-PRICE-04](screens/catalog/PRODUCT-PRICE-04.md) | F-04 | Fiyat Geçmişi ve Revizyon Karşılaştırma | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_MERGE | U-05 |
| [PRODUCT-U-01](screens/catalog/PRODUCT-U-01.md) | U-01 | Ürünler Liste / Detay | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_EXACT | U-01 |
| [PRODUCT-U-01A](screens/catalog/PRODUCT-U-01A.md) | U-01A | Ürünler Sağ AI Paneli | LOCKED_VISUAL | LOCKED_VISUAL | EMBEDDED_COMPONENT | U-01 |
| [PRODUCT-U-02](screens/catalog/PRODUCT-U-02.md) | U-02 | AI Destekli Yeni Ürün | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_EXACT | U-02 |
| [PRODUCT-U-03](screens/catalog/PRODUCT-U-03.md) | U-03 | Ürün Düzenle | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_MERGE | U-02 |
| [PRODUCT-U-04](screens/catalog/PRODUCT-U-04.md) | U-04 | Benzer / Mükerrer Ürün Karşılaştırma | LOCKED_VISUAL | LOCKED_VISUAL | UNRESOLVED | U-02, R-04 |
| [PRODUCT-U-05](screens/catalog/PRODUCT-U-05.md) | U-05 | Paket Standardı ve Sürüm Yönetimi | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_EXACT | U-07 |
| [PRODUCT-U-06](screens/catalog/PRODUCT-U-06.md) | U-06 | Sipariş İçinde Ürün / Hizmet Seçici | LOCKED_VISUAL | LOCKED_VISUAL | CANDIDATE_SPLIT | S-03, P-04 |

## Cari HTML route kayıtları

| Record | Kaynak ID | Başlık | Kaynak statüsü | Görsel | Eşleme | Kanonik / ilişkili hedef |
|---|---|---|---|---|---|---|
| [CARI-ROUTE-01](screens/cari/CARI-ROUTE-01.md) | cari-list | Cari Kartlar | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | EXACT | C-01 |
| [CARI-ROUTE-02](screens/cari/CARI-ROUTE-02.md) | tracking/* | Cari Hesap Takibi | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | C-04, C-05, C-08, C-09 |
| [CARI-ROUTE-03](screens/cari/CARI-ROUTE-03.md) | cari/:id/summary | Cari 360 Özet | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | EXACT | C-03 |
| [CARI-ROUTE-04](screens/cari/CARI-ROUTE-04.md) | cari/:id/info/* | Cari Bilgileri | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | C-02, C-06, C-07 |
| [CARI-ROUTE-05](screens/cari/CARI-ROUTE-05.md) | cari/:id/orders/* | Cari Siparişleri | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | EMBEDDED_COMPONENT | C-03; ilişkili: S-02, P-03 |
| [CARI-ROUTE-06](screens/cari/CARI-ROUTE-06.md) | cari/:id/account/* | Cari Hesap | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | C-04, C-05, C-08, C-09 |
| [CARI-ROUTE-07](screens/cari/CARI-ROUTE-07.md) | cari/:id/stock/* | Cari Stok ve Tahsis | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | EMBEDDED_COMPONENT | C-03; ilişkili: ST-01, ST-08, AL-03 |
| [CARI-ROUTE-08](screens/cari/CARI-ROUTE-08.md) | cari/:id/docs/* | Cari Belgeleri ve Sicil | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | C-07, C-10; ilişkili: B-06, B-07, R-04 |
| [CARI-ROUTE-09](screens/cari/CARI-ROUTE-09.md) | workspace:new-edit-cari | Yeni / AI ile Yeni / Düzenle Cari | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | C-02, C-11 |
| [CARI-ROUTE-10](screens/cari/CARI-ROUTE-10.md) | modal:critical-change | Kritik Değişiklik ve Etki / Onay | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | C-07, C-08, C-11; ilişkili: SY-04 |

## Finans v3 taban + v4 delta

| Record | Kaynak ID | Başlık | Kaynak statüsü | Görsel | Eşleme | Kanonik / ilişkili hedef |
|---|---|---|---|---|---|---|
| [FINANCE-V3V4-F-01](screens/finance/FINANCE-V3V4-F-01.md) | F-01 | Finans Genel Bakış | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | EXACT | F-01 |
| [FINANCE-V3V4-F-02](screens/finance/FINANCE-V3V4-F-02.md) | F-02 | Tahsilat / Ödeme | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | EXACT | F-02 |
| [FINANCE-V3V4-F-03](screens/finance/FINANCE-V3V4-F-03.md) | F-03 | Açık Kalemler ve Finansal Dağıtım | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | EXACT | F-03 |
| [FINANCE-V3V4-F-04](screens/finance/FINANCE-V3V4-F-04.md) | F-04 | Tahsilat Takibi / Cari Risk | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | EXACT | F-13 |
| [FINANCE-V3V4-F-05](screens/finance/FINANCE-V3V4-F-05.md) | F-05 | Banka / Kasa | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | SPLIT | F-04, F-05 |
| [FINANCE-V3V4-F-06](screens/finance/FINANCE-V3V4-F-06.md) | F-06 | Ödeme Yönetimi | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | EXACT | F-15 |
| [FINANCE-V3V4-F-07](screens/finance/FINANCE-V3V4-F-07.md) | F-07 | Ödeme Araçları | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | SPLIT | F-06, F-07 |
| [FINANCE-V3V4-F-08](screens/finance/FINANCE-V3V4-F-08.md) | F-08 | Nakit Planı | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | SPLIT | F-11, F-12 |
| [FINANCE-V3V4-F-09](screens/finance/FINANCE-V3V4-F-09.md) | F-09 | Krediler ve Limitler | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | SPLIT | F-08, F-09 |
| [FINANCE-V3V4-F-10](screens/finance/FINANCE-V3V4-F-10.md) | F-10 | Teminatlar / Yükümlülükler | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | EXACT | F-10 |
| [FINANCE-V3V4-F-11](screens/finance/FINANCE-V3V4-F-11.md) | F-11 | Nakit Bütçesi | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | MERGE | F-12 |
| [FINANCE-V3V4-F-12](screens/finance/FINANCE-V3V4-F-12.md) | F-12 | Finans Kontrol Merkezi | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | SPLIT | D-05, D-06, F-16 |
| [FINANCE-V3V4-F-13](screens/finance/FINANCE-V3V4-F-13.md) | F-13 | Finans Raporları | ACTIVE_BASE_PLUS_DELTA | PROTOTYPE_REFERENCE | SPLIT | F-17, R-03, R-05 |

## İrsaliye/Fatura merkez ve bileşenleri

| Record | Kaynak ID | Başlık | Kaynak statüsü | Görsel | Eşleme | Kanonik / ilişkili hedef |
|---|---|---|---|---|---|---|
| [DISPATCH-INVOICE-DCD-01](screens/logistics/DISPATCH-INVOICE-DCD-01.md) | DCD-01 | Ortak Belge Detay Çekmecesi | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | EMBEDDED_COMPONENT | B-02, B-07 |
| [DISPATCH-INVOICE-DCE-01](screens/logistics/DISPATCH-INVOICE-DCE-01.md) | DCE-01 | Kontrol ve İstisna Bileşeni | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | EMBEDDED_COMPONENT | D-06, R-04 |
| [DISPATCH-INVOICE-DCR-01](screens/logistics/DISPATCH-INVOICE-DCR-01.md) | DCR-01 | İade / Düzeltme İlişki Görünümü | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | EMBEDDED_COMPONENT | S-10, P-10, L-12 |
| [DISPATCH-INVOICE-PDH-01](screens/logistics/DISPATCH-INVOICE-PDH-01.md) | PDH-01 | Alış İrsaliye & Fatura Merkezi | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | P-07, P-08, P-09, P-10 |
| [DISPATCH-INVOICE-PDW-01](screens/logistics/DISPATCH-INVOICE-PDW-01.md) | PDW-01 | Yeni Mal / Hizmet Kabulü | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | P-07, L-10 |
| [DISPATCH-INVOICE-PDW-02](screens/logistics/DISPATCH-INVOICE-PDW-02.md) | PDW-02 | Gelen Fatura Girişi / Eşleştirme | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | P-08, P-09 |
| [DISPATCH-INVOICE-SDH-01](screens/logistics/DISPATCH-INVOICE-SDH-01.md) | SDH-01 | Satış İrsaliye & Fatura Merkezi | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | S-07, S-08, S-09, S-10 |
| [DISPATCH-INVOICE-SDW-01](screens/logistics/DISPATCH-INVOICE-SDW-01.md) | SDW-01 | Yeni Sevkiyat / Gerçekleşme | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | L-03, L-05, L-07, L-08 |
| [DISPATCH-INVOICE-SDW-02](screens/logistics/DISPATCH-INVOICE-SDW-02.md) | SDW-02 | Satış Fatura Taslağı / Eşleştirme | DESIGNED_NOT_LOCKED | PROTOTYPE_REFERENCE | SPLIT | S-08, S-09 |

## Görseli henüz kataloglanmamış alanlar

Kanonik ekranların tamamı [CANONICAL_SCREEN_STATUS.csv](CANONICAL_SCREEN_STATUS.csv) içinde görünür. Güçlü mevcut kaynak bulunan Dashboard, Satış, Satın Alma, Ürün/Hizmet, Cari, Finans ve İrsaliye/Fatura ele alınmıştır. Karar bulunması, özgün görselin bulunduğu veya ekranın kilitlendiği anlamına gelmez. C-12 için yeni görsel/workspace hazırlanması ayrıca kayıtlı bir eksiktir.

Yeni görsel üretme sırası UI-MAP-006 ile şöyledir:

1. Sistem Yönetimi.
2. Belgeler.
3. Stok & Tahsis.
4. Üretim.
5. Finans eksikleri.
6. Raporlar.

Her dalgada önce mevcut arşivler yeniden taranır; yalnız bulunmayan referanslar mevcut kararlarla tasarlanır ve kullanıcı kabulünden sonra kilitlenir.

## Sonraki kabul kapısı

`EXACT`, `SPLIT`, `MERGE` veya `EMBEDDED_COMPONENT` statüsü yalnız UI referans topolojisinin kabul edildiğini gösterir; kodlama öncesinde permission/scope, iş kuralı, acceptance, responsive, accessibility, performans ve audit kaynakları yine zorunludur. `CANDIDATE_*` ve `UNRESOLVED` kayıtlar ayrıca kesinleştirilmeden uygulama kaynağı olamaz. Pixel benzerliği tek başına Definition of Done değildir.
