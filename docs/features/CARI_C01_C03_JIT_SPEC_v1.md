# CARI-C01-C03-JIT-001 — Cari Kartlar ve Cari 360 çekirdek dilimi

- Süreç: `docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md`
- Belge türü: **şimdi geliştirilen C-01 ve hemen sonraki C-03 için JIT iş/ekran sözleşmesi**
- İş ve kabul sahibi: `CHATGPT-BUSINESS`
- Backend/API yazarı: `CODEX`
- Frontend/browser yazarı: `CLAUDE-FRONTEND`
- İncelenen taban: `main@4cd05d8983786f2eb1a512ae7d3da7e303ec6ba8`
- Üst kapsam: Issues `#78`, `#146`, `#148`
- Backend kaynakları: merged PR `#141`, `GET /api/v1/counterparties`, `GET /api/v1/counterparties/{partyId}`
- Production activation: **NO-GO**

Bu belge readiness paketi veya yeni governance değildir. C-01/C-03 implementation PR’larının alan, durum, hata, tenant ve kabul girdisidir. Kaynakta bulunmayan bakiye, risk, açık kalem, stok, sipariş, belge veya AI verisi eklemez.

## 1. Kaynak ve değişmezler

Bağlayıcı sıra:

1. Son kullanıcı kararları ve `AGENTS.md`.
2. `KARAR_SICILI.md` içindeki `DEC-CAR-001`, `DEC-CAR-002`, `DEC-CAR-003`.
3. Master v3 §7.5 ve §27.5.
4. `docs/features/CARI_UI_VERTICAL_SLICE_v1.md`.
5. Canlı OpenAPI ve merged backend davranışı.
6. `docs/ui/screens/cari/CARI-ROUTE-01.md` ile `CARI-ROUTE-03.md` yalnız `DESIGNED_NOT_LOCKED / PROTOTYPE_REFERENCE` görsel girdisidir.

Değişmezler:

- Bir hukuki karşı taraf tek `Party` kaydıdır; müşteri, tedarikçi, fason ve hizmet rolleri ayrı Party yaratmaz.
- Her Göymen şirketiyle ticari ilişki ayrı `CompanyPartyAccount` kaydıdır.
- Global `Party.IsActive` ile şirket kapsamlı `CompanyPartyAccount.IsActive` farklı anlam taşır.
- Şirketler arası rol, durum, bakiye veya kayıt otomatik birleşmez.
- Kullanılmış Party veya şirket hesabı silinmez; durum değişikliği tarihsel kimliği yok etmez.
- UI görünürlüğü authorization değildir; her API çağrısı backend tarafından yeniden yetkilendirilir.

## 2. Dikey dilim sonucu

### 2.1 C-01 — Cari Kartlar

Kullanıcı aktif şirket bağlamında:

- yetkili Cari kayıtlarını server-side arar, filtreler ve sıralar;
- opaque cursor ile sonraki sayfaya geçer;
- tek Party satırında birleşik rol etiketlerini görür;
- global Taraf durumu ile seçili şirket hesabı durumunu ayrı görür;
- capability varsa C-03 detayına gider;
- yabancı şirket hesabı veya stale şirket sonucu görmez.

### 2.2 C-03 — Cari 360 çekirdek özeti

Kullanıcı seçili Cari için:

- Cari kodu, hukuki/ticari unvanı, türü ve global Taraf durumunu;
- shell’den gelen aktif şirket adını/kodunu;
- yalnız bu şirket için yetkili olduğu tek şirket hesabının durumunu ve rollerini;
- güvenli geri dönüşü

görür.

C-03A; risk, bakiye, açık kalem, sipariş, stok, belge, banka/vergi/sicil, zaman çizgisi veya AI ekranı değildir. Bu alanların owner API’si oluşmadan sıfır değer, boş kart, inert sekme veya sahte KPI gösterilmez.

## 3. C-01 HTTP sözleşmesi

```http
GET /api/v1/counterparties
X-Company-Id: <shell tarafından doğrulanmış aktif şirket>
```

### 3.1 Query alanları

| Parametre | Kabul edilen değer | Varsayılan / UI davranışı |
|---|---|---|
| `q` | trim + NFC, en çok 200 Unicode rune | boşsa gönderilmez; kod/hukuki ad/ticari ad server tarafında aranır |
| `kind` | `LegalEntity`, `NaturalPerson` | Tümü |
| `role` | tekrarlanabilir `Customer`, `Supplier`, `Tolling`, `Service` | seçim yoksa gönderilmez |
| `roleMatch` | `any`, `all` | `any`; yalnız iki veya daha fazla rol seçilince görünür |
| `partyState` | `active`, `inactive`, `all` | `active` |
| `accountState` | `active`, `inactive`, `all` | `all` |
| `sort` | `code`, `legalName`, backend ayrıca `updatedAt` | UI v1 yalnız Kod ve Unvan; DTO tarih taşımadığı için `updatedAt` görünür seçenek değildir |
| `direction` | `asc`, `desc` | `asc` |
| `pageSize` | `1..200` | `50` |
| `cursor` | opaque ve authenticated | browser üretmez, parse etmez, localStorage’a yazmaz |
| `includeCount` | `true`, `false` | ilk dilimde `false` |

Filtre, sıralama, şirket, actor/session veya CompanyContext generation değişirse birikmiş sayfalar ve cursor zinciri atılır; ilk sayfadan başlanır. Browser server sonucunu yeniden sıralamaz, şirketler arası union yapmaz ve Türkçe aramayı client-side taklit etmez.

### 3.2 Response alanları

```text
CounterpartyPage
- items[]
- nextCursor
- hasMore
- totalCount
- countScope

CounterpartyListItem
- id
- code
- kind
- legalName
- tradeName
- isActive          // global Party durumu
- version
- companyAccounts[]

CounterpartyListAccount
- id
- companyId
- roles             // tek account üzerindeki bit flag
- isActive          // şirket hesabı durumu
- version
```

### 3.3 Roller

`roles` ayrı satır veya ayrı hesap değildir:

| Bit | Teknik değer | Kullanıcı etiketi |
|---:|---|---|
| 1 | `Customer` | Müşteri |
| 2 | `Supplier` | Tedarikçi |
| 4 | `Tolling` | Fason |
| 8 | `Service` | Hizmet |

Örnek `roles=3`, aynı şirket hesabında `Müşteri` ve `Tedarikçi` etiketlerini üretir. Party iki kez listelenmez. Gelecekte yetkili çoklu şirket görünümünde roller şirket hesapları arasında union edilmez.

### 3.4 Aktiflik

Aşağıdaki görünüm ayrı ayrı korunur:

```text
Taraf: Aktif | Pasif
Şirket hesabı: Aktif | Pasif
```

Varsayılan `partyState=active`, `accountState=all` olduğundan global aktif Party + pasif şirket hesabı listede bulunabilir. Tek “Cari pasif” etiketi kullanılmaz.

### 3.5 Tek şirket response binding

C-01A yalnız shell’in aktif tek şirket bağlamında çalışır. Her Party için:

- tam olarak bir görünür `companyAccount` olmalı;
- `account.companyId` aktif CompanyContext ile birebir eşleşmeli;
- aynı seçili şirket için duplicate account olmamalı;
- yabancı şirket account’u bulunmamalı;
- Party/account kimlikleri boş olmayan UUID olmalı;
- role bitleri yalnız `1|2|4|8` kümesinden ve en az bir rol içermeli.

Sıfır hesap, foreign account, duplicate selected-company account, unknown role biti veya malformed zorunlu alan **bütün sayfayı** reddettirir. Kısmi güvenilir satır gösterilmez.

### 3.6 Count doğruluğu

İlk dilimde `includeCount=false` kullanılır. Runtime decoder yalnız iki şekli kabul eder:

```text
totalCount = null
countScope = null
```

veya

```text
totalCount = sıfır veya pozitif kayıpsız int64 gösterimi
countScope = "filtered-authorized-visible-parties"
```

Eksik `totalCount`, count varken scope eksikliği, count yokken scope bulunması veya bilinmeyen scope malformed cevaptır. Count hiçbir zaman yetkisiz şirket veya filtreden önceki kayıt sayısını göstermez.

## 4. C-01 ekran sözleşmesi

### 4.1 Üst alan

- Başlık: `Cari Kartlar`.
- Aktif şirket etiketi shell Organization directory’den gelir; ham UUID kullanılmaz.
- `includeCount=true` ile geçerli count dönmedikçe toplam sayı gösterilmez.
- Capability ve route mount yoksa `Yeni Cari`, `Detay`, `Düzenle` gibi inert aksiyon gösterilmez.

### 4.2 Filtreler

- Arama: Cari kodu / hukuki unvan / ticari unvan.
- Tür: Tümü / Tüzel Kişi / Gerçek Kişi.
- Roller: Müşteri / Tedarikçi / Fason / Hizmet, çoklu seçim.
- Rol eşleşmesi: Herhangi biri / Tümü; iki veya daha fazla rol seçildiğinde.
- Taraf durumu: Aktif / Pasif / Tümü.
- Şirket hesabı durumu: Aktif / Pasif / Tümü.
- Sıralama: Kod / Unvan.
- Yön: Artan / Azalan.

Debounce geri alınabilir teknik ayrıntıdır; başlangıç değeri 300 ms olabilir. İş anlamını değiştirmez ve server contractını atlamaz.

### 4.3 Liste kolonları

1. Cari kodu.
2. Hukuki unvan; ticari unvan varsa ikincil metin.
3. Tür.
4. Seçili şirket rol etiketleri.
5. Global Taraf durumu.
6. Şirket hesabı durumu.

Bakiye, para, risk, limit, açık kalem, sipariş, stok, belge sayısı veya AI sütunu yoktur.

### 4.4 Cursor devamı

- İlk sayfa: `pageSize=50`, `includeCount=false`.
- `hasMore=true` ve geçerli `nextCursor` birlikteyse `Daha fazla göster` etkinleşir.
- Sonraki sayfa yalnız doğrulandıktan sonra mevcut doğrulanmış satırlara eklenir.
- Client-side sorting veya sessiz dedup ile server hatası gizlenmez; devam sayfasında duplicate Party ID malformed’dur.
- `invalid_cursor`: cursor zinciri atılır, sayfa 1 yeniden alınır ve `Liste güncellendi; ilk sayfaya dönüldü.` bilgisi gösterilir.

## 5. C-03 HTTP ve görünüm sözleşmesi

```http
GET /api/v1/counterparties/{partyId}
X-Company-Id: <shell tarafından doğrulanmış aktif şirket>
```

C-03A `companyId` query parametresi kullanmaz. `partyId` boş veya geçersiz UUID ise backend çağrısı yapılmaz; güvenli bulunamadı görünümü gösterilir.

### 5.1 Görünür alanlar

- Cari kodu — salt okunur.
- Hukuki unvan.
- Ticari unvan — varsa ikincil.
- Tür.
- Global Taraf durumu.
- Aktif şirket adı/kodu — shell kaynağı.
- Seçili şirket hesabı durumu.
- Birleşik rol etiketleri.

### 5.2 Hassas ve kapsam dışı alanlar

Mevcut `PartyCoreView` içindeki `externalIntegrationCode`, dedicated read permission ve redaction sahibi kabul edilene kadar:

- ekranda gösterilmez;
- aranmaz;
- DOM/accessibility tree’ye konmaz;
- client log/telemetry/localStorage/sessionStorage’a yazılmaz;
- C-03’e özel dar projection yapılırsa API’den çıkarılır.

`version` int64 değeridir; generated TypeScript client kayıpsız temsil sağlamadan frontend write command’ine sokulmaz. C-03A read-only’dir.

### 5.3 Detail response binding

Cevap yalnız şu durumda render edilir:

- Party alanları yapısal olarak geçerli;
- `companyAccounts.length === 1`;
- account CompanyId aktif CompanyContext’e eşit;
- bilinen ve en az bir role sahip;
- Party/account kimlikleri boş olmayan UUID;
- foreign veya duplicate account yok.

Aksi durumda bütün cevap malformed sayılır; partial Cari adı veya yabancı şirket verisi gösterilmez.

## 6. Güvenli durumlar

### C-01 ve C-03 ortak

- `initial-loading`: önceki şirket verisi yok.
- `401 authentication_required`: shell session-ended; business cache temizlenir.
- `403 forbidden`: isim, count, account sayısı veya probing bilgisi yok.
- `500/503/offline`: blocking retry; foreign/stale veri yok.
- `malformed`: generic güvenli hata; raw body veya HTML render edilmez.
- stale CompanyContext generation: sonuç sessizce atılır, başarı bildirimi yok.

### C-01 özel

- ilk sayfa boş + filtre yok: yetkili görünür Cari yok.
- filtreli boş: filtre sonucu bulunamadı; filtreleri temizleme aksiyonu.
- devam sayfası hatası: yalnız önceki doğrulanmış sayfalar korunur; append yok, retry vardır.
- `400 validation_failed`: güvenli istek/filtre hatası; localized `detail` parse edilmez.
- `400 invalid_cursor`: cursor recovery uygulanır.

### C-03 özel

- `404 not_found`: olmayan, foreign, hidden veya policy ile görünmeyen kayıt aynı güvenli sonuçtur.
- Deep-link, refresh, back/forward ve yeni sekme fresh capability + backend authorization ister.
- Şirket değişiminde eski Cari adı, title, breadcrumb, recent/favorite veya cache yeni context’e taşınmaz.

## 7. Durum geçişleri ve yan etkiler

C-01/C-03 salt-okunur dilimdir:

```text
idle
→ request(active actor + active company + generation)
→ loading
→ validated success | safe error | silently discarded stale result
```

Bu read akışları:

- business tablosuna yazmaz;
- `CommandExecution` veya idempotency kaydı üretmez;
- business audit olayı üretmez;
- yalnız mevcut correlation/log/trace politikasını kullanır.

Şirket değişimi, actor/session değişimi veya capability revizyonu tüm company-scoped Cari query namespace’ini değiştirir. Eski sonuç yeni namespace’e commit edilmez.

## 8. AT doğruluk eşlemesi

| AT | Bu dilimdeki gerçek kapsam | Tamamlanma iddiası |
|---|---|---|
| `AT-001` | C-01/C-03 seçili şirket ve permission izolasyonu | Browser + HTTP + gerçek SQL kanıtıyla tamamlanabilir |
| `AT-009` | Aynı VKN/kimlik mükerrer uyarısı | C-02 create işidir; burada tamamlanmaz |
| `AT-010` | Tek Party’de müşteri+tedarikçi rollerinin read görünümü | C-01/C-03 okuma kısmı; write/create kısmı C-02 |
| `AT-011` | Para defterlerinin netlenmemesi | Bu API para döndürmediği için tamamlanmaz; UI para göstermez |
| `AT-012` | Cari birleştirme ve bağların korunması | C-11; burada tamamlanmaz |
| `AT-013` | Pasif Party/account’un ayrı ve tarihsel görünmesi | Read görünümü kısmen; yeni işleme seçilememe tüketen ekranlarda |
| `AT-014` | Eksik vergi/banka belge kapısı | C-07 ve ilgili write akışları; burada tamamlanmaz |

## 9. Implementation kapıları

### Codex

- OpenAPI ve actual DTO parity’si.
- C-01 tenant/cursor/count gerçek SQL + HTTP acceptance.
- C-03 A/B/non-disclosure gerçek SQL + HTTP acceptance.
- C-03’te `externalIntegrationCode` için dar projection veya açık discard sözleşmesi.
- Int64 alanların generated client’ta kayıpsız temsili.
- Migration/permission/yeni iş kuralı yok.

### Claude

- Generated client tüketimi; runtime decoder devam eder.
- Active CompanyContext’e exact-bound account doğrulaması.
- Loading/empty/error/forbidden/404/invalid-cursor/offline/malformed/stale durumları.
- Cursor, filtre ve şirket değişimi davranışı.
- Browser + keyboard + focus + semantic table/list + %200 zoom + mobil reflow.
- Owner’sız KPI/sekme/alan yok.

### Merge

```text
PR #139 shell foundation accepted + merged + fresh new-main green
→ C-01 decoder dar restack
→ C-01 gerçek ekran + browser/a11y + exact-head review
→ C-03 backend daraltma/acceptance
→ C-03 gerçek ekran + exact-head review
```

P0/P1 açıkken veya remote CI kırmızıyken merge yapılmaz.
