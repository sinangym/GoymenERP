# Cari Core — Readiness ve Güvenli İlk Dilim v1.0

- Tarih: 2026-08-28
- Taban: `main@caff02d313e3a40c998380a645f4aa4238ff8e4f`
- Paralel çalışma hattı: `parallel/cari-core-readiness-v1`
- Durum: **Readiness GO / production implementation PARTIAL-GO**
- Amaç: Codex platform hattına dokunmadan Cari çekirdeğinin ilk güvenli kodlama dilimini kesinleştirmek.

## 1. Bağlayıcı kaynaklar

Kaynak önceliği `AGENTS.md` ile aynıdır.

- `DEC-CAR-001`: tek hukuki karşı taraf = tek `Taraf`; müşteri, tedarikçi, fason ve hizmet rolleri aynı kartta bulunabilir.
- `DEC-CAR-002`: her Göymen şirketiyle ilişki şirket kapsamlı `CariHesap` üzerinden izlenir; grup görünümü hesapları karıştırmaz veya otomatik mahsup etmez.
- `DEC-CAR-003`: kullanılmış cari silinmez; pasife alınır. Mükerrerler yetkili birleştirmeyle, kaynak kimlikleri ve geçmiş bağlantılar korunarak çözülür.
- `DEC-GOV-002/003`: CompanyId izolasyonu ve şirket geçmişinin ayrı taşınabilirliği korunur.
- `AGENTS.md`: kritik command = authorization + expected version + idempotency key + correlation/causation + audit.
- Master v3 §7.5: ana menü `Cari Kartlar | Cari Hesap Takibi`; `Taraf` hukuki kimlik, `CariHesap` belirli Göymen şirketiyle ilişkidir.
- Master v3: müşteri ve tedarikçi hesapları/para birimleri sessiz netleştirilmez; sipariş/irsaliye kesin cari hareket değildir.
- UI `UI-MAP-003`: C-01…C-11 route referansları kesinleştirilmiş; C-12 görseli eksik.

## 2. Kanonik ekran kapsamı

| Kod | Ekran | Bu ilk dilimde durum |
|---|---|---|
| C-01 | Cari Kartlar | **GO** |
| C-02 | Yeni/Düzenle Cari | **GO, karar kapılarıyla** |
| C-03 | Cari 360 | **GO, yalnız Cari-owned core özet** |
| C-04 | Cari Hesap Takibi | **DEFER** — finans/open-item sahibi veri gerekir |
| C-05 | Açık Kalemler | **DEFER** — Finans/Open Item sahibi |
| C-06 | Adres / Kişi / İletişim | **GO** |
| C-07 | Banka / Vergi / Sicil | **GO, hassas alan approval kapısıyla** |
| C-08 | Risk / Limit / Vade | **DEFER** — deterministik risk motoru ve finans/satış girdileri gerekir |
| C-09 | Mutabakat | **DEFER** — açık kalem/finans akışı gerekir |
| C-10 | Belgeler / Sözleşmeler | **DEFER** — ERP-000G Document Vault sahibidir |
| C-11 | Mükerrer ve Birleştirme | **PARTIAL-GO** — aday tespiti/karşılaştırma; gerçek merge ertelenir |
| C-12 | İlişki / Takip Görevleri | **DEFER** — görsel eksik, temel görev sınırı ayrıca kabul edilir |

## 3. İlk güvenli implementation dilimi — `CARI-CORE-1`

İlk kod PR'ı yalnız aşağıdakileri sahiplenebilir:

1. Cari modül iskeleti: Domain / Application / Infrastructure / API.
2. `Party` hukuki kimlik çekirdeği.
3. Şirket kapsamlı `CompanyPartyAccount` (`CariHesap`) ilişkisi.
4. Şirket ilişkisi üzerinde roller: Customer, Supplier, Tolling/Fason, Service.
5. Adres, kişi/iletişim bilgileri.
6. Vergi/sicil bilgileri ve banka/IBAN kayıtları için tarihçeli master yapı.
7. Aktif/pasif yaşam döngüsü; fiziksel silme yok.
8. C-01 liste/search ve C-02 create/edit API sözleşmeleri.
9. C-03 için yalnız Cari-owned özet projection/read model.
10. C-11 için mükerrer **aday tespiti ve karşılaştırma**; başka modül referanslarını yeniden bağlayan merge komutu yok.
11. CompanyId, permission, audit, idempotency ve optimistic concurrency acceptance testleri.

Bu dilim bakiye, açık kalem, risk tutarı, teminat kullanılabilirliği, sipariş toplamı, stok miktarı veya finansal hareket üretmez.

## 4. Domain sahipliği

### Cari modülü sahip olur

- Hukuki taraf kimliği ve tarihçesi.
- Göymen şirketi ↔ Taraf ilişkisinin CariHesap kaydı.
- Cari rolleri ve aktif/pasif durumu.
- Cari adresleri ve iletişim/muhatap bilgileri.
- Vergi/sicil ana verisi.
- Cari banka hesabı/IBAN master verisi ve değişiklik geçmişi.
- Mükerrer aday sinyali/karşılaştırma verisi.

### Cari modülü sahip olmaz

- Fatura/open item ledger ve cari hareket: Finance/Accounting sahipliği.
- Satış/satınalma siparişi: ilgili modül sahipliği.
- Stok/tahsis: Stock/Allocation sahipliği.
- Belge/blob/version: Documents sahipliği.
- Deterministik brüt/net risk hesabı: Risk/Finance composition.
- Ticaret sicili dış entegrasyon adaptörü: integration sahibi; Cari yalnız onaylı sonucu uygular.

Cari 360 bu alanları daha sonra read/composition sözleşmeleriyle gösterir; başka modül tablosuna doğrudan yazmaz.

## 5. Kritik mimari değişmez — tek Party + şirket izolasyonu

`DEC-CAR-001` tek Party, `DEC-CAR-002` şirket kapsamlı CariHesap ister. Aynı zamanda CompanyId izolasyonu fail-closed olmalıdır.

Bu nedenle ilk öneri:

- `Party`: grup seviyesinde tek hukuki kimlik ve teknik UUID.
- `CompanyPartyAccount`: zorunlu `CompanyId + PartyId`, şirket kapsamlı ilişki/rol/operasyonel ayarlar.
- Kullanıcı Party'yi doğrudan global listeden okuyamaz; erişim yetkili `CompanyPartyAccount` üzerinden çözülür.
- Bir Party yalnız başka şirkette bağlıysa yetkisiz kullanıcı için varlığı/count'u bile sızmamalıdır.
- Grup görünümü yalnız kullanıcının yetkili şirket bağlarının birleşimidir.
- Şirket export/restore paketi ilgili `CompanyPartyAccount` kayıtlarıyla ihtiyaç duyduğu Party snapshot/identity verisini taşır.

**Bu model `CARI-GATE-01` olarak insan/mimari review ile kilitlenmeden production schema oluşturulmaz.**

## 6. Yaşam döngüsü

Önerilen durumlar:

- `Active`
- `Inactive`

Taslak master kart zorunlu değildir; create command validasyon + permission sonrası atomik oluşturur. AI/OCR dışından gelen veri önce application-level draft/preview olarak tutulur ve ana karta kullanıcı onayıyla yazılır.

Silme yoktur. Pasife alma yeni ticari kullanımın önünü keser; tarihsel sipariş/fatura/stok/finans referansları geçerli kalır.

## 7. CompanyId ve yetki sınırı

Her CompanyPartyAccount command'i aktif CompanyContext ile birebir eşleşmelidir.

Minimum permission aileleri (isimler implementation sırasında mevcut permission naming standardına uydurulur):

- read/list
- create
- edit basic identity/contact
- edit sensitive legal/tax
- edit bank account
- activate/deactivate
- duplicate.review
- merge (ilk dilimde **kapalı**)

UI buton gizleme güvenlik sayılmaz. Backend her request'te permission + CompanyContext doğrular.

## 8. Kritik alanlar

Kaynak UI; VKN, unvan, MERSİS, IBAN ve limit gibi alanları kritik değişiklik olarak gösterir.

İlk dilimde:

- Unvan/resmi ad
- Vergi/kimlik numarası
- MERSİS / resmi sicil kimliği
- IBAN / banka hesabı kimliği

kritik olarak sınıflandırılmaya adaydır. Limit/risk alanı ilk dilimde yoktur.

Kritik değişiklik:

`current value → proposed value → impact preview → reason → authorization/approval → command → audit`

zincirinden geçmelidir.

Kesin tek/çift onay matrisi `CARI-GATE-04` altında kullanıcı kararı bekler.

## 9. Audit / idempotency / concurrency

Create/update/deactivate/sensitive-change command'leri:

- idempotency key
- expected version (create hariç)
- correlation/causation
- actor + CompanyContext
- before/after audit snapshot
- reason (kritik değişiklik ve pasife alma)

kullanır.

Aynı idempotency key + aynı actor + aynı payload güvenli replay; farklı payload/actor conflict olmalıdır. Stale version 409 conflict üretir. Business write + audit + kalıcı command result aynı transaction sınırında olmalıdır.

## 10. Mükerrerlik ve birleştirme

### İlk dilimde GO

- normalize edilmiş resmi ad benzerliği için **yalnız aday sinyali**,
- exact vergi/kimlik numarası eşleşmesi,
- exact resmi sicil/MERSİS eşleşmesi,
- exact IBAN gibi hassas eşleşmenin yetkili kullanıcıya uyarı olarak sunulması,
- iki kartın alan bazlı karşılaştırması.

### İlk dilimde NO-GO

- başka modüllerdeki FK'leri doğrudan yeniden yazmak,
- event/outbox olmadan cross-module merge etkisi,
- kaynak Party'yi silmek,
- otomatik merge.

Gerçek merge; canonical Party + alias/redirect + her owner modülün kendi referansını kontrollü güncellemesi modeliyle ayrıca tasarlanmalı ve ERP-000F/outbox kapısıyla değerlendirilmelidir.

## 11. Cari Hesap / bakiye / açık kalem sınırı

Master'a göre müşteri ve tedarikçi hesapları ile para birimleri sessiz netleştirilemez.

Bu nedenle `CARI-CORE-1`:

- herhangi bir bakiye kolonu hesaplamaz,
- TL'ye zorla tek bakiye üretmez,
- sipariş veya irsaliyeyi cari hareket yazmaz,
- mock bakiye/risk göstermez.

C-03/C-04/C-05/C-08 verisi ilgili owner modüller hazır olduğunda versioned read contracts/projections ile eklenir.

## 12. API sözleşmesi ilkeleri

İlk public API ailesi önerisi:

- `GET /api/v1/cari/parties`
- `GET /api/v1/cari/parties/{partyId}`
- `POST /api/v1/cari/parties`
- `PUT/PATCH /api/v1/cari/parties/{partyId}` — gerçek HTTP şekli implementation review'da tekleştirilir
- `POST /api/v1/cari/parties/{partyId}/deactivate`
- address/contact/bank/legal subresource command'leri
- duplicate candidates read endpoint

Route isimleri kullanıcı UI kimlikleri değildir. API sözleşmesi implementation PR'ında contract testle kilitlenir.

## 13. UI uygulama sınırı

Referanslar `DESIGNED_NOT_LOCKED / PROTOTYPE_REFERENCE` olduğundan pixel-copy yapılmaz.

İlk frontend PR:

- C-01 list + server-side search/filter/keyset
- C-02 full-screen guided workspace
- C-03 core summary
- C-06 adres/kişi/iletişim sekmeleri
- C-07 hassas alanlar ve kritik değişiklik preview
- C-11 duplicate candidate compare read-only

uygulayabilir.

Loading, true-empty, filtered-empty, unauthorized, validation, conflict, saving, idempotent replay ve service unavailable durumları ayrı tasarlanır. Prototipteki `innerHTML`/inline handler kopyalanmaz.

## 14. İlk dilimde açık karar kapıları

### `CARI-GATE-01` — Party tenancy/access modeli — **BLOCKER**

Tek Party'nin grup seviyesinde tutulup CompanyPartyAccount üzerinden görünür olması önerilir. CompanyId invariant ve şirket export/restore ile çelişmediği mimari review ile kilitlenmeli.

### `CARI-GATE-02` — Taraf türü — **BLOCKER for create model**

İlk sürüm yalnız tüzel/kurumsal taraf mı destekleyecek, yoksa gerçek kişi/şahıs tarafı da aynı modelde ilk günden bulunacak mı?

### `CARI-GATE-03` — Görünen cari kodu — **BLOCKER for public card contract**

Global search cari kodu bekliyor; fakat cari görünen kodunun formatı/sırası kaynaklarda kilitli değil. Company-scope veya group-scope kod formatı kullanıcı kararı olmadan uydurulmaz.

### `CARI-GATE-04` — Kritik master değişikliği onayı — **BLOCKER for sensitive writes**

Unvan, VKN/TCKN, MERSİS ve IBAN değişikliklerinde hangi alanların tek yetki + audit, hangilerinin ikinci kullanıcı onayı gerektirdiği kilitlenmeli.

### `CARI-GATE-05` — C-12 takip görevi minimumu — **non-blocking**

UI-MAP-003; görev CRUD, sorumlu, termin, sonuç, hatırlatma ve audit ihtiyacını işaretliyor. CRM ertelendiği için bunun yalnız temel Cari takip görevi olup olmadığı kullanıcı tarafından onaylanmalı.

## 15. Kodlamaya hemen başlanabilecek hazırlıklar

Karar kapıları kapanmadan dahi aşağıdakiler güvenlidir:

- Cari module project skeleton ve dependency test taslağı (schema yaratmadan),
- API contract taslağı,
- permission katalog önerisi,
- domain value object testleri (ör. normalized identifier/IBAN biçim doğrulama; ülkeye özgü kural uydurmadan),
- acceptance fixtures,
- UI state contract/component skeleton,
- duplicate comparison read-model taslağı.

Production schema ve create/edit write akışı `CARI-GATE-01…04` kapanmadan merge edilmemelidir.

## 16. Definition of Done — CARI-CORE-1

- CARI-GATE-01…04 kabul edilmiş ve repoda bağlayıcı kaynağa dönüştürülmüş.
- Party + CompanyPartyAccount invariantları gerçek SQL Server'da testli.
- CompanyId/list/count/detail sızıntı testleri yeşil.
- Aynı Party farklı yetkili şirket bağlarında kopyalanmadan çalışıyor.
- Aynı şirket ilişkisinde müşteri+tedarikçi rolleri tek kartta bulunabiliyor.
- Kullanılmış kart fiziksel silinmiyor; deactivate auditli.
- Hassas alan değişikliği preview/reason/approval politikasına uyuyor.
- Idempotency replay ve stale-version conflict gerçek SQL/HTTP acceptance ile kanıtlı.
- C-01/C-02/C-03/C-06/C-07 ve C-11 partial UI state'leri testli.
- Bakiye/risk/open-item/doc mock'u yok.
- P0/P1 açık bulgu yok.
