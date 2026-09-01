# CARI-C01-C03-ACCEPTANCE-001 — Çalıştırılabilir kabul matrisi

- Süreç: Hızlı Teslim v3.0, JIT acceptance
- İş sahibi: `CHATGPT-BUSINESS`
- Backend kanıt sahibi: `CODEX`
- Frontend/browser kanıt sahibi: `CLAUDE-FRONTEND`
- Kaynak spec: `docs/features/CARI_C01_C03_JIT_SPEC_v1.md`
- Kanonik kabul bağlantıları: `AT-001`, `AT-010`, `AT-013`
- Bilinçli kapsam dışı kanonik kabuller: `AT-009`, `AT-011`, `AT-012`, `AT-014`
- Production activation: **NO-GO**

Bu dosya yeni `AT-*` kimliği üretmez. Aşağıdaki `CASE-*` kimlikleri yalnız mevcut kanonik kabullerin C-01/C-03 test vakalarıdır.

## 1. Ortak test verisi

### Şirketler

| Kimlik | Kod | Görünen ad | Aktör erişimi |
|---|---|---|---|
| `aaaaaaaa-0000-0000-0000-000000000001` | `GDC` | Göymen Demir Çelik | Actor-AB ve Actor-A |
| `bbbbbbbb-0000-0000-0000-000000000002` | `GSP` | Göymen Sac Profil | Actor-AB ve Actor-B |

Gerçek test seed’i farklı UUID kullanabilir; test çıktısı UUID’yi kullanıcı etiketi yapamaz.

### Aktörler

| Aktör | Erişim ve izin |
|---|---|
| `Actor-A` | Yalnız GDC; `counterparty.parties.read` company-wide |
| `Actor-B` | Yalnız GSP; `counterparty.parties.read` company-wide |
| `Actor-AB` | GDC ve GSP; ikisinde de `counterparty.parties.read` company-wide |
| `Actor-A-Narrow` | GDC erişimi var fakat company-wide Cari read izni yok |
| `Actor-None` | Cari read izni veya erişilebilir şirketi yok |

### Party ve şirket hesapları

| Party | Global alanlar | GDC hesabı | GSP hesabı |
|---|---|---|---|
| `P-001` | `C0001`, Tüzel, `Örnek Metal A.Ş.`, aktif | aktif, roles=Customer\|Supplier | yok |
| `P-002` | `C0002`, Tüzel, `Pasif İlişki Sanayi A.Ş.`, aktif | pasif, roles=Customer | yok |
| `P-003` | `C0003`, Gerçek Kişi, `Ayşe Örnek`, aktif | aktif, roles=Service | aktif, roles=Supplier |
| `P-004` | `C0004`, Tüzel, `Tarihsel Cari A.Ş.`, pasif | aktif, roles=Tolling | yok |
| `P-005` | `C0005`, Tüzel, `Yalnız GSP A.Ş.`, aktif | yok | aktif, roles=Supplier |

Test seed’inde Party ve account ID’leri boş olmayan UUID’dir. Bir şirket içinde aynı Party için ikinci account bulunmaz.

## 2. Kanıt standardı

Her vaka en az şu çıktıları üretir:

- HTTP request/response ve stable problem code.
- Gerçek SQL Server sonucunda görünür Party/account kümesi.
- Frontend component veya browser sonucu.
- DOM/accessibility ağacında foreign/hidden veri bulunmadığına ilişkin negatif assertion.
- DB write cardinality: `0`.
- `CommandExecution`/idempotency cardinality: `0`.
- Business audit cardinality: `0`; yalnız mevcut request correlation/log/trace kabul edilir.
- Skip sayısı: `0`.

Mock/InMemory testi gerçek SQL veya browser kanıtının yerine geçmez.

## 3. C-01 Cari Kartlar vakaları

### CASE-C01-001 — birleşik roller, tek Party

**Kaynak:** `DEC-CAR-001`, `AT-010` read bölümü.

**Actor/context:** Actor-A, GDC.

**İstek:** varsayılan filtrelerle `GET /api/v1/counterparties`, `X-Company-Id=GDC`.

**Beklenen:**

- P-001 tam bir satırdır.
- `Müşteri` ve `Tedarikçi` iki etiket olarak aynı satırda görünür.
- P-001 ikinci kez listelenmez.
- Tek GDC account’u görünür; role başına account yaratılmaz.
- `Taraf Aktif` ve `Şirket hesabı Aktif` ayrı erişilebilir metindir.

**Beklenmeyen:** bakiye/risk/limit/sipariş/stok/AI alanı; GSP account bilgisi; DB write/audit/idempotency.

### CASE-C01-002 — global aktif, şirket hesabı pasif

**Kaynak:** `DEC-CAR-002`, `DEC-CAR-003`, `AT-013` read bölümü.

**Actor/context:** Actor-A, GDC.

**Matris:**

| Query | P-002 |
|---|---|
| default `partyState=active`, `accountState=all` | görünür |
| `partyState=active&accountState=active` | görünmez |
| `partyState=active&accountState=inactive` | görünür |

Görünür durumda `Taraf Aktif` ve `Şirket hesabı Pasif` ayrı gösterilir. Tek “Cari pasif” etiketi yoktur.

### CASE-C01-003 — global pasif Party

**Kaynak:** `DEC-CAR-003`, `AT-013` read bölümü.

- Varsayılan `partyState=active` ile P-004 görünmez.
- `partyState=inactive` veya `all` ile P-004 görünür.
- Görünür durumda global pasiflik açıkça gösterilir.
- Bu read ekranı “yeni işleme seçilemez” kısmını tamamlamış sayılmaz; o assertion tüketen write ekranındadır.

### CASE-C01-004 — seçili şirket izolasyonu

**Kaynak:** `AT-001`, `DEC-CAR-002`.

**Actor/context:** Actor-AB, shell active GDC.

**Beklenen:**

- Request yalnız `X-Company-Id=GDC` taşır.
- P-003 varsa yalnız GDC account’u `Hizmet/Aktif` olarak görünür.
- GSP `Tedarikçi` rolü, GSP account durumu, GSP ID/adı ve GSP’ye ait count DOM/state/accessibility ağacında bulunmaz.
- P-005 görünmez.

Aynı actor shell active GSP’ye geçince fresh request yapılır; geç GDC cevabı GSP listesine yazamaz.

### CASE-C01-005 — izin yok ve non-disclosure

| Actor/context | Beklenen HTTP/UI |
|---|---|
| Actor-A-Narrow / GDC | `403 forbidden`, liste/count/isim yok |
| Actor-None | güvenli permission/no-company state, kayıt varlığına ilişkin ipucu yok |
| Actor-A / yabancı GSP header | `403 forbidden`, GSP Party/count yok |

403 ekranı localized `detail` parse etmez ve probing sonucu üretmez.

### CASE-C01-006 — arama ve tür

**Actor/context:** Actor-A, GDC.

- `q` code/legalName/tradeName contains davranışını yalnız server sonucu belirler.
- UI trim/NFC dışında client-side Turkish case/accent algoritması uygulamaz.
- `kind=LegalEntity` yalnız tüzel; `NaturalPerson` yalnız gerçek kişi döndürür.
- 200 rune üzeri, bilinmeyen veya çoklu tekil parametre `400 validation_failed` olur; partial data yoktur.

### CASE-C01-007 — rol any/all aynı account içinde

Fixture’a GDC’de roles=`Customer|Supplier` olan P-001 ve yalnız Customer olan başka Party eklenir.

| Query | Beklenen |
|---|---|
| `role=Customer&role=Supplier&roleMatch=any` | en az bir biti taşıyanlar |
| `role=Customer&role=Supplier&roleMatch=all` | iki biti aynı GDC account’unda taşıyan P-001 |

Ayrı şirketlerdeki roller birleştirilerek `all` sağlanamaz.

### CASE-C01-008 — sort görünür seçenekleri

- `sort=code` ve `sort=legalName`, `asc/desc` server sırasıyla gösterilir.
- UI satırları yeniden sıralamaz.
- Backend `updatedAt` kabul etse de response satırı timestamp taşımadığından görünür “Güncellenme” kolonu veya sıralaması yoktur.
- Tarih uydurulmaz.

### CASE-C01-009 — cursor continuation

**Fixture:** pageSize sınırını aşan görünür Party kümesi.

1. İlk sayfa `hasMore=true` ve non-empty opaque `nextCursor` döndürür.
2. “Daha fazla göster” aynı filtre/context ile cursor’ı aynen gönderir.
3. Doğrulanmış ikinci sayfa append edilir.
4. Aynı Party ID devam sayfasında yeniden gelirse sayfa malformed sayılır; sessiz dedup yoktur.
5. Filter/sort/company/actor/session/generation değişirse eski cursor kullanılmaz.
6. Cursor localStorage/sessionStorage veya URL paylaşım otoritesi değildir.

### CASE-C01-010 — invalid cursor recovery

Tampered, expired veya scope-mismatch cursor:

- HTTP `400`, code=`invalid_cursor`.
- Reddedilen devam sayfası append edilmez.
- Cursor zinciri silinir.
- İlk sayfa fresh alınır.
- Kullanıcıya `Liste güncellendi; ilk sayfaya dönüldü.` gösterilir.
- Localized `title/detail` üzerinden branching yapılmaz.

### CASE-C01-011 — count scope parity

Runtime decoder yalnız aşağıdaki şekilleri kabul eder:

| totalCount | countScope | Sonuç |
|---|---|---|
| `null` | `null` | kabul |
| non-negative kayıpsız int64 | `filtered-authorized-visible-parties` | kabul |
| field eksik | herhangi | malformed |
| count var | `null`/bilinmeyen | malformed |
| count yok | non-null | malformed |
| negatif/ondalık/geçersiz string | herhangi | malformed |

Default `includeCount=false`; geçerli count olmadan üst bilgi toplam göstermez.

### CASE-C01-012 — malformed company binding

Aşağıdakilerin her biri bütün sayfayı reddeder:

- Party’de zero account.
- Active company dışında foreign account.
- Aynı selected company için iki account.
- Duplicate Party ID veya account ID.
- Nil/malformed UUID.
- `roles=0`, unknown bit veya 15 dışı değer.
- Boş code/legalName.
- `hasMore`/`nextCursor` uyuşmazlığı.

Hiçbir kısmi satır veya foreign değer render edilmez.

### CASE-C01-013 — ilk/sonraki sayfa hata davranışı

- İlk sayfa 500/503/offline: blocking retry; eski şirket satırı yok.
- Sonraki sayfa 500/503/offline: önceki doğrulanmış sayfalar korunur, yeni append yok, retry vardır.
- Malformed body: raw body/HTML render edilmez.
- Stale generation sonucu: sessiz discard; başarı mesajı yok.

### CASE-C01-014 — erişilebilirlik ve responsive

Browser kabulü:

- semantic table/list ve başlık ilişkileri;
- sort kontrolünde mevcut alan ve yön erişilebilir;
- filtreler ile continuation yalnız klavyeyle çalışır;
- görünür focus;
- status yalnız renkle anlatılmaz;
- error/empty değişimi kontrollü live region;
- %200 zoom ve mobil reflow’da arama/filtre/liste/continuation tamamlanabilir;
- foreign data accessibility tree’de yoktur.

## 4. C-03 Cari 360 çekirdek vakaları

### CASE-C03-001 — tek kimlik ve roller

**Kaynak:** `DEC-CAR-001`, `AT-010` read bölümü.

**Actor/context:** Actor-A, GDC; P-001.

**Beklenen:** tek Party başlığı, tek GDC account özeti, Müşteri+Tedarikçi etiketleri, global ve account durumu ayrı. DB write/business audit/idempotency `0`.

### CASE-C03-002 — aktiflik ayrımı

P-002 detail:

```text
Taraf: Aktif
Şirket hesabı: Pasif
```

Capability olmadan create/link/status aksiyonu gösterilmez. Account pasifliği global Party’yi pasif göstermez.

### CASE-C03-003 — şirket izolasyonu

Actor-AB active GDC ile P-003 detail:

- Request GDC header taşır.
- Yalnız GDC account `Hizmet/Aktif` görünür.
- GSP Supplier/account/ID/ad/count yoktur.

### CASE-C03-004 — direct-ID non-disclosure

Aşağıdaki üç durum aynı `404 not_found` görünümüne sahiptir:

1. Olmayan Party ID.
2. Yalnız GSP’de görünür P-005’e GDC context’inden istek.
3. Policy/permission nedeniyle gizli Party.

İsim, rol, account sayısı, foreign şirket veya ayrıştırıcı açıklama gösterilmez.

### CASE-C03-005 — stale detail

1. GDC/P-001 detail isteği geciktirilir.
2. Shell GSP context’ine geçer ve fresh capability commit eder.
3. Eski GDC cevabı bırakılır.

Beklenen: GDC Cari adı, title, breadcrumb, rol veya page cache GSP ekranına yazılmaz. GSP içeriği fresh request olmadan açılmaz.

### CASE-C03-006 — hassas alan

Geçerli backend body `externalIntegrationCode` taşısa bile:

- görünür metin, DOM attribute, accessible name, log, telemetry, localStorage veya sessionStorage’da bulunmaz;
- tercih edilen backend projection bu alanı hiç yayınlamaz;
- runtime decoder alanı bilinçli discard eder.

### CASE-C03-007 — malformed detail

Zero account, foreign account, duplicate account, nil UUID, unknown role biti veya eksik required Party alanı bütün cevabı reddeder. Partial unvan gösterilmez.

### CASE-C03-008 — owner’sız alanları uydurmama

C-03A’da risk, bakiye, açık kalem, zaman çizgisi, sipariş, stok, belge, banka/vergi/sicil veya AI paneli bulunmaz. `0`, `—`, boş kart veya “yakında” sekmesi de gösterilmez.

### CASE-C03-009 — route ve browser

- `/cari/:partyId` yalnız detail capability ile mount edilir.
- Boş/bozuk UUID’de backend çağrısı yok; güvenli not-found.
- Deep-link, refresh, back/forward ve yeni sekme fresh capability + backend authorization ister.
- Main heading/focus ve keyboard-only `Cari Kartlar’a dön` çalışır.
- %200 zoom ve %400 reflow’da çekirdek kimlik/account özeti kullanılabilir.

## 5. Kanonik AT tamamlanma matrisi

| AT | Modül/dilim | Bu dosyadaki kanıt | Statü kuralı |
|---|---|---|---|
| `AT-001` | Shell + C-01 + C-03 | CASE-C01-004/005/012/013, CASE-C03-003/004/005/007 | gerçek SQL+HTTP+browser birlikte yeşil olmadan tamamlanmaz |
| `AT-009` | C-02 create | bu dosyada yok | mükerrer VKN/kimlik write akışına kadar açık |
| `AT-010` | C-01/C-03 read + C-02 write | CASE-C01-001/007, CASE-C03-001 | burada yalnız read görünümü kısmi kanıt |
| `AT-011` | C-04/C-05/finans | API para döndürmez; UI para göstermez | omission completion değildir |
| `AT-012` | C-11 merge | bu dosyada yok | belge/audit bağlarıyla ayrı dilim |
| `AT-013` | C-01/C-03 read + tüketen işlem ekranları | CASE-C01-002/003, CASE-C03-002 | tarihsel görünüm kısmi; yeni işleme seçilememe sonra |
| `AT-014` | C-07 + ilgili işlem | bu dosyada yok | vergi/banka belge kapısı ayrı |

## 6. Merge kabul özeti

### C-01

- PR #139 shell foundation accepted, merged ve new-main green.
- C-01 decoder yalnız kendi dar diff’iyle restack edilmiş.
- Count-scope ve active-company binding negatif testleri yeşil.
- Generated client veya açık geçici handoff; ikinci DTO gerçeği yok.
- Component + browser + a11y + remote CI yeşil.
- Codex exact-head hızlı review; P0/P1=0.

### C-03

- C-01 route modeli main’de.
- Dar detail projection/OpenAPI; `externalIntegrationCode` güvenli.
- Int64 version kayıpsız/opaque.
- A/B/non-disclosure gerçek SQL+HTTP.
- Component + browser + a11y + remote CI yeşil.
- Codex/Claude karşı review; P0/P1=0.
