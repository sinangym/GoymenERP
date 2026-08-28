# ERP-000I — SQL Server Arama Yetenek Envanteri Kanıt Şablonu

- Belge durumu: **KANIT ŞABLONU — KARAR DEĞİLDİR**
- Envanter betiği: `tools/erp000i/sql_server_search_capability_inventory.sql`
- Statik doğrulayıcı: `tools/erp000i/validate_sql_server_search_capability_inventory.py`
- Amaç: Yetkili bir SQL Server ortamının yalnız sistem metadata çıktısını tekrar üretilebilir biçimde kaydetmek

## 1. Kesin kapsam sınırı

Bu şablon ve betik:

- ERP-000I Global Search PoC kabulü değildir;
- SQL Server veya başka bir veritabanı sağlayıcısı kabulü değildir;
- production veritabanı collation kararı veya kabulü değildir;
- full-text dil/LCID, katalog ya da stoplist seçimi değildir;
- p95, RPO/RTO veya başka bir performans/operasyon eşiği kabulü değildir;
- CompanyId, permission filtreleme, sıralama, suggestion, cursor veya keyset davranışı kanıtı değildir;
- kullanıcı tablosu, iş verisi, indeks veya sorgu planı incelemez;
- migration, DDL/DML, bağlantı yapılandırması ya da uygulama değişikliği yapmaz.

Çıktı yalnız sonraki PoC/ADR incelemesine girdi olabilir. Eksik, boş veya hata dönen metadata
satırı başarı ya da başarısızlık olarak yorumlanmaz; gözlem olarak kaydedilir.

## 2. Çalıştırma kaydı

| Alan | Kanıt |
|---|---|
| Tarih/saat (UTC) | `<DOLDURULACAK>` |
| Çalıştıran rol | `<DOLDURULACAK>` |
| Yetkili ortamın mantıksal etiketi | `<DOLDURULACAK>` |
| Repo commit SHA | `<DOLDURULACAK>` |
| SQL betiği SHA-256 | `<DOLDURULACAK>` |
| Ham çıktı dosyası SHA-256 | `<DOLDURULACAK>` |
| Statik doğrulayıcı sonucu | `<DOLDURULACAK>` |
| İnceleyen | `<DOLDURULACAK>` |

Gerçek sunucu adı, kullanıcı adı, parola, token veya bağlantı dizesi bu belgeye yazılmaz.
Yetkili salt-okunur bağlantı çalışma anında repository dışından sağlanır.

Betik yalnız SQLCMD ön işlemesi kapalı bir sorgu çalıştırma modunda yürütülür. `:r`, `!!`,
`$(...)` veya eşdeğer include, shell ve değişken substitution özelliğini işleyen bir istemci
modu kullanılamaz. Çalıştırmadan önce kanonik normalize SQL SHA-256 değeri statik
doğrulayıcı ve repository guard ile doğrulanır; hash uyuşmazlığı fail-closed sonuçtur.

## 3. Ham metadata çıktısı

Çıktı satırları değiştirilmeden ayrı bir kanıt dosyasında saklanır. Bu bölümlerde yalnız
dosya adı, hash ve satır/aralık referansı tutulur.

### 3.1 Sürüm, edition, engine ve full-text kurulum göstergesi

| Alan | Kanıt referansı |
|---|---|
| `product_version` | `<DOSYA/HASH/SATIR>` |
| `product_level` | `<DOSYA/HASH/SATIR>` |
| `edition` | `<DOSYA/HASH/SATIR>` |
| `engine_edition` | `<DOSYA/HASH/SATIR>` |
| `is_full_text_installed` | `<DOSYA/HASH/SATIR>` |

### 3.2 Geçerli veritabanı collation metadata değeri

| Alan | Kanıt referansı |
|---|---|
| `database_collation` | `<DOSYA/HASH/SATIR>` |

Bu değer yalnız gözlemdir; hedef collation seçimi veya Türkçe arama uygunluğu kararı değildir.

### 3.3 Full-text dil metadata satırları

| Alanlar | Kanıt referansı |
|---|---|
| `language_lcid`, `language_name` | `<DOSYA/HASH/SATIR_ARALIĞI>` |

Listede görünmek bir LCID seçildiği veya uygulama davranışının kabul edildiği anlamına gelmez.

### 3.4 Full-text katalog metadata satırları

| Alanlar | Kanıt referansı |
|---|---|
| `catalog_id`, `catalog_name`, `is_default_catalog`, `is_accent_sensitive` | `<DOSYA/HASH/SATIR_ARALIĞI>` |

### 3.5 Full-text stoplist metadata satırları

| Alanlar | Kanıt referansı |
|---|---|
| `stoplist_id`, `stoplist_name` | `<DOSYA/HASH/SATIR_ARALIĞI>` |

## 4. Çalıştırma anomalileri

| Sorgu bölümü | Hata/boş sonuç | Ham hata kanıtı | Karar verilmedi notu |
|---|---|---|---|
| Sürüm/engine/full-text göstergesi | `<DOLDURULACAK>` | `<DOSYA/HASH/SATIR>` | `<DOLDURULACAK>` |
| Collation metadata | `<DOLDURULACAK>` | `<DOSYA/HASH/SATIR>` | `<DOLDURULACAK>` |
| Diller | `<DOLDURULACAK>` | `<DOSYA/HASH/SATIR>` | `<DOLDURULACAK>` |
| Kataloglar | `<DOLDURULACAK>` | `<DOSYA/HASH/SATIR>` | `<DOLDURULACAK>` |
| Stoplistler | `<DOLDURULACAK>` | `<DOSYA/HASH/SATIR>` | `<DOLDURULACAK>` |

## 5. Bütünlük ve inceleme

- [ ] Kullanılan commit ve SQL betiği hash'i kaydedildi.
- [ ] Statik doğrulayıcı başarılı çalıştı.
- [ ] Ham çıktı değişmeden saklandı ve hash'i kaydedildi.
- [ ] Secret veya bağlantı bilgisi kanıt paketine alınmadı.
- [ ] İş verisi ya da kullanıcı tablosu sorgulanmadı.
- [ ] Çıktıya göre sağlayıcı, collation, LCID veya eşik kararı verilmedi.
- [ ] Sonuç ERP-000I Search PoC kabulü olarak işaretlenmedi.

## 6. Sonuç

Envanter durumu: `<ÇALIŞTIRILMADI | ÇIKTI_ALINDI | EKSİK_KANIT | HATA_KAYDEDİLDİ>`

Karar durumu: **AÇIK — BU ŞABLON KARAR KAPATMAZ**
