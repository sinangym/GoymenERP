# Cari Core — Domain Model Proposal v1.0

Bu belge karar değil, `CARI-GATE-01…04` kapanışını hızlandırmak için teknik model önerisidir. Kabul edilmeden migration/schema sözleşmesi sayılmaz.

## 1. Model hedefi

Aynı hukuki karşı tarafı Göymen grup şirketlerinde kopyalamadan yönetirken:

- CompanyId izolasyonunu,
- şirket bazlı cari ilişki/rol/ayarları,
- şirket bazlı veri export/restore'u,
- kritik master alanı tarihçesini,
- idempotency/concurrency/audit'i,
- gelecekte cross-module referansları

korumak.

## 2. Önerilen aggregate sınırları

### 2.1 `Party`

Grup seviyesinde hukuki kimliğin canonical kaydı.

Önerilen alan sınıfları:

- `PartyId` — immutable UUID
- `PartyKind` — CARI-GATE-02 sonrası
- `LegalName`
- `TradeName` / kısa ad — opsiyonel
- resmi vergi/kimlik tanımlayıcıları — türü açık kayıtlar
- resmi sicil/MERSİS tanımlayıcıları — türü açık kayıtlar
- `Status`
- `Version` + SQL rowversion
- created/updated metadata

`Party` kullanıcıya doğrudan global collection olarak açılmaz. Görünürlük CompanyPartyAccount bağları üzerinden çözülür.

### 2.2 `CompanyPartyAccount`

Belirli Göymen şirketi ile Party arasındaki şirket kapsamlı CariHesap.

Önerilen temel alanlar:

- `CompanyPartyAccountId`
- `CompanyId`
- `PartyId`
- `CariCode` — CARI-GATE-03 sonrası
- `Status`
- roller
- şirket-özel ticari temel ayarlar için genişleme alanı
- `Version` + SQL rowversion

Önerilen unique invariant:

`(CompanyId, PartyId)` tek aktif/kanonik CariHesap ilişkisini temsil eder.

Aynı Party'nin aynı Göymen şirketinde müşteri ve tedarikçi olması ikinci CariHesap oluşturmaz; roller aynı ilişki üzerinde tutulur.

### 2.3 `PartyRole`

İlk rol kümesi bağlayıcı kaynaklardan gelir:

- Customer
- Supplier
- Tolling / Fason
- Service

Rol, hukuki Party'nin evrensel özelliği değil; şirket ilişkisinin ticari rolüdür. Böylece bir Party Göymen şirket A'da müşteri, şirket B'de tedarikçi olabilir.

### 2.4 `PartyAddress`

Adres kaydı için önerilen alanlar:

- identity
- address type/purpose
- structured address components + freeform line gerektiğinde
- country
- active/inactive
- validity metadata

Kesin ülke/adres validation kuralı ayrı policy olmadan uydurulmaz.

Adres Party-level veya CompanyPartyAccount-level ihtiyaç gösterebilir. İlk öneri:

- hukuki/resmi merkez adresi Party-level,
- teslimat/fatura/operasyon adresleri CompanyPartyAccount-level bağ taşıyabilir.

Bu ayrım implementation öncesi acceptance fixture ile doğrulanmalıdır.

### 2.5 `PartyContact`

- ContactId
- kişi adı/soyadı
- görev/unvan
- iletişim kanalları
- kullanım amacı
- active/inactive
- CompanyPartyAccount bağı opsiyonel/explicit

Kişisel veri log/telemetry label'larına yazılmaz.

### 2.6 `PartyBankAccount`

Cari master verisidir; para hareketi değildir.

Önerilen alanlar:

- BankAccountId
- PartyId veya CompanyPartyAccount kapsamı — implementation kararı
- Bank/Branch display metadata
- IBAN / hesap kimliği
- Currency — varsa ve kaynaktan doğrulanıyorsa
- active/inactive
- validity/history
- version

IBAN değişikliği ayrı kritik command olmalıdır. Banka hesabının varlığı, ödeme yetkisi veya banka doğrulaması anlamına gelmez.

### 2.7 `PartyOfficialIdentifier`

Vergi/kimlik/sicil alanlarını tek sabit kolona yığmak yerine türü açık kayıt önerilir:

- IdentifierType
- Country/Jurisdiction
- Value (uygun güvenlik sınıflamasıyla)
- ValidFrom/ValidTo veya active flag
- source/reference metadata

CARI-GATE-02 sonrası VKN/TCKN veya başka kimlik türleri kesinleştirilir.

## 3. Tenant-safe query modeli

### 3.1 Liste

C-01 başlangıç query'si `CompanyPartyAccount`tan başlar, Party bilgisine join eder.

Şirket filtresi önce uygulanır:

`Authorized CompanyContext → CompanyPartyAccount → Party`

Party tablosundan global scan edip sonra UI'da filtreleme yapılmaz.

### 3.2 Detay

`PartyId` bilinse dahi backend önce kullanıcının Party'ye erişim sağlayan en az bir yetkili CompanyPartyAccount bağını doğrular.

### 3.3 Grup görünümü

Birden çok şirkete yetkili kullanıcı için authorized CompanyId seti server-side üretilir. Aynı Party sonuçta tek üst kart; şirket ilişkileri alt kayıt/rozet olarak gösterilebilir. Finansal hesaplar şirket/para bazında ayrı kalır.

## 4. Şirket export/restore

DEC-GOV-003 ile uyum için şirket veri paketi:

- ilgili CompanyPartyAccount kayıtlarını,
- bunların referans verdiği Party kimlik snapshot'ını,
- gerekli adres/contact/official identifier/bank master parçalarını,
- audit ve alias/history referanslarını

paketleyebilmelidir.

Restore, başka şirketin CariHesap/verisini yanlışlıkla içeri almamalıdır. Aynı Party hedefte zaten varsa deterministic identity matching + kullanıcı/approval politikası gerekebilir; otomatik merge varsayılmaz.

## 5. Tarihçe stratejisi

Kritik alan için yalnız audit JSON'una güvenmek yerine business history/read model ihtiyacı olabilir.

İlk öneri:

- current canonical state ana aggregate üzerinde,
- her write append-only audit olayı,
- resmi kimlik/banka gibi çok-değerli alanlarda validity/status kayıtları,
- silme yerine `Inactive` / `ValidTo`.

Full temporal table veya ayrı snapshot tablosu gerektiği performans/rapor ihtiyacı kanıtlanmadan seçilmez.

## 6. Duplicate modeli

### Candidate detection

Cari modülü kendi master alanlarından candidate score/signal üretir:

- exact official identifier
- exact registry identifier
- exact IBAN (yetki dahilinde)
- normalized legal-name similarity

İlk sürümde skor threshold'u business magic number olarak hard-code edilmez. Exact sinyaller deterministic; fuzzy benzerlik yalnız yardımcı/sıralama sinyalidir.

### Merge

Gerçek merge için öneri:

- `CanonicalPartyId`
- kaynak Party için retained alias/redirect/history
- owner modüller kendi referanslarını kendi command/event sözleşmeleriyle günceller
- cross-module direct SQL/FK rewrite yok
- ERP-000F/outbox sonrası orchestration
- kullanıcı preview + approval + audit

Bu nedenle actual merge `CARI-CORE-1` dışında kalır.

## 7. Permission/approval önerisi

Permission kategorileri:

- `cari.party.read`
- `cari.party.create`
- `cari.party.update.basic`
- `cari.party.update.official`
- `cari.party.bank.manage`
- `cari.party.status.change`
- `cari.party.duplicate.review`
- `cari.party.merge` — ilk sürümde route/command kapalı

Bunlar yalnız isim önerisidir; mevcut Identity permission naming standardına implementation review'da adapte edilir.

Critical approval adayları:

| Değişiklik | Öneri |
|---|---|
| iletişim/adres düzeltmesi | yetki + audit; ikinci onay gerekmeyebilir |
| resmi unvan | ikinci onay adayı |
| VKN/TCKN/official identifier | ikinci onay adayı |
| MERSİS/sicil identity | ikinci onay adayı |
| IBAN/banka hesabı ekle/değiştir/pasife al | ikinci onay adayı |
| Cari status deactivate/reactivate | reason + elevated permission; ikinci onay kullanıcı kararı |

Kesin matris CARI-GATE-04 ile kullanıcı tarafından kilitlenir.

## 8. Cross-module read contracts — gelecek

Cari 360 ileride şu owner contract'ları tüketebilir:

- Sales: orders summary
- Purchasing: orders summary
- Finance: balances/open items/risk inputs
- Stock: ownership/stock/allocation summary
- Documents: document references

Cari bu modüllerin tablolarına doğrudan query/write yapmaz. UI composition API veya versioned Application read contract kullanılır.

## 9. İlk migration için NO-GO koşulları

Aşağıdakiler kapanmadan migration yazılmaz:

1. Party group-level tenancy/access modeli kabulü.
2. PartyKind kapsamı.
3. CariCode policy.
4. Kritik master approval policy.
5. Hassas identifier storage/indexing/log-redaction yaklaşımının security review'u.

## 10. İlk kod PR'ına önerilen sıra

1. Domain/value objects ve unit tests.
2. Application contracts + permissions.
3. EF model + real SQL constraints after gates.
4. create/read/update/deactivate commands.
5. idempotency/audit/concurrency integration.
6. C-01/C-02/C-03 core API.
7. React C-01/C-02/C-03/C-06/C-07/C-11 partial.
8. security + relational acceptance.

Bu sıra ERP-000F/G/Finance/Stock implementation'ına bağımlı değildir; deferred composition ekranları feature-gated kalır.
