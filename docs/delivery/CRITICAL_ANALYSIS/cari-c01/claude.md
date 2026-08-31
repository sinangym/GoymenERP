# C-01 Cari Kartlar (liste) — frontend implementation plan

- Dilim: `W1-CARI` / `CARI-ROUTE-01` → kanonik `C-01`. Issue #78/#94.
- Backend: `GET /api/v1/counterparties` (`GetCounterpartiesV1`, PR #141 merged, `contracts/openapi/goymen-erp-v1.json`).
- Bağımlı: `SHELL-PILOT-01B` (PR #139) — bu ekran shell'in `<Outlet/>`'ine ilk business mount.
- Kod bu plandan sonra gelir (aynı branch); generated TS client (Issue #122) landmadan **HANDOFF** işaretli ince client.
- Analizi yapan: `CLAUDE-FRONTEND`.

## 1. Backend contract (özet)

`GET /api/v1/counterparties` — header `X-Company-Id` (aktif şirket), query:

| Param | Değer | Not |
|---|---|---|
| `q` | string ≤200 | code/legalName/tradeName contains; Türkçe CI/AS, Unicode trim+NFC |
| `kind` | `LegalEntity` \| `NaturalPerson` | tekil |
| `role` | `Customer`\|`Supplier`\|`Tolling`\|`Service` | repeatable |
| `roleMatch` | `any` \| `all` | varsayılan `any` |
| `partyState` | `active` \| `inactive` \| `all` | varsayılan `active` |
| `accountState` | `active` \| `inactive` \| `all` | — |
| `sort` | `code` \| `legalName` \| `updatedAt` | — |
| `direction` | `asc` \| `desc` | — |
| `pageSize` | integer | — |
| `cursor` | string | keyset devamı |
| `includeCount` | boolean | `totalCount` istenir |

Yanıt `CounterpartyPage`:

```
items: CounterpartyListItem[]
nextCursor: string | null
hasMore: boolean
totalCount: int64 | string | null    // string = JS güvenli tamsayı dışı; sayı gibi parse etme, göster
countScope: string | null            // sayının neyi kapsadığı
```

`CounterpartyListItem`: `{ id, code, kind: PartyKind, legalName, tradeName: string|null, isActive, version: int64, companyAccounts: CounterpartyListAccount[] }`
`CounterpartyListAccount`: `{ id, companyId, roles: PartyRole, isActive, version: int64 }`

Enum'lar backend'de **integer**: `PartyKind` 1=LegalEntity 2=NaturalPerson · `PartyRole` **flags** 1=Customer 2=Supplier 4=Tolling 8=Service (bitwise OR, max 15). Frontend bu int'leri okunur etikete map eder; **UI'da sabit rol adı yeniden yazmaz** — map merkezi `cari/enums.ts`.

Yanıtlar: 200 · 400 · 401 · 403 · 500 · 503.

## 2. Ekran kapsamı (C-01 = LİSTE, başka bir şey değil)

Referans: `docs/ui/screens/cari/CARI-ROUTE-01.md`, `CARI_UI_VERTICAL_SLICE_v1.md`.

**Var:**
- Liste tablosu: `code` · `legalName` (+ altında `tradeName`) · `kind` rozeti · **aktif şirket bağlamındaki hesabın rolleri** (companyAccounts içinden `companyId === activeCompanyId` olan) · aktif/pasif durumu
- Filtre satırı: `q` (arama), `kind`, `role` (çoklu) + `roleMatch`, `partyState`
- Sıralama: `code` \| `legalName` \| `updatedAt` × `asc`/`desc` (kolon başlığı tıklanır)
- Cursor pagination: `hasMore` → "Daha fazla yükle"; `includeCount=false` varsayılan (performans), kullanıcı "toplam" isterse `includeCount=true`
- Satır tıklama → C-03 Cari 360 route'una git (`/cari/:id`) — C-03 ayrı dilim; şimdilik route placeholder

**Yok (bilinçli):**
- Bakiye / risk / açık kalem / KPI kolonu — `DEC-CAR-002` + `AT-011`: para birimleri sessizce netleşmez, liste bakiye göstermez
- "Yeni Cari" formu → C-02 (ayrı dilim)
- Cari 360 içeriği → C-03
- Birleştirme / mükerrer → C-11
- Mock AI paneli, sahte veri

## 3. §13 fence + §12 durumlar

- Liste query'si `queryKey = [contextKey, 'counterparties', filters]` — company-switch'te otomatik namespace değişir (shell fence, PR #139/#119).
- İstek `X-Company-Id` = `activeContext.companyId`; `activeContext` yoksa liste **çağrılmaz**.
- `capabilityFailClosed(state)` veya ilgili capability yoksa `/cari` route mount edilmez (shell route registry — Codex).
- Durumlar (`uiState.ts` `RemoteViewState`): `initial-loading` (skeleton tablo) · `empty-no-data` ("Bu şirkette cari kaydı yok") · `empty-filtered` ("Filtreye uyan cari yok" + filtre temizle) · `forbidden` (403 non-disclosing) · `unauthenticated` (401 → shell session-ended) · `service-unavailable` (500/503 + retry) · `malformed-response` (fail-closed) · `offline` · `stale-rejected` (company-switch sırasında gelen eski yanıt boyanmaz)

## 4. Pasif cari (`AT-013`)

- `partyState=all` seçilirse pasif cariler görünür, satırda "Pasif" rozeti + soluk stil.
- Pasif cari satırından C-03'e gidilebilir (geçmiş görünür) ama "yeni işlem" aksiyonu (C-02'de) pasif için kapalı — bu C-01'in değil C-02/işlem dilimlerinin sorumluluğu; C-01 yalnız işaretler.

## 5. Client (HANDOFF)

`src/cari/api.ts` — `fetchCounterparties(params, signal)` → `ViewOutcome<CounterpartyListItem>` benzeri (shell `api.ts` deseni). Tipler elle; `contracts/openapi/goymen-erp-v1.json`'u birebir yansıtır; generated client (Issue #122 / orval PoC) landınca `src/api/generated/`'e taşınır, drift check sınırı olur. `totalCount` **string olarak tutulur** (JS `number` precision).

## 6. Testler (C-01 kabul)

Component/browser (Playwright #118/#122 kapanınca):
- liste gerçek API verisiyle render; `X-Company-Id` her istekte gönderiliyor
- **AT-010**: tek cari kimliği, aktif şirket bağlamında müşteri + tedarikçi rollerini ayrı account satırlarından türetiyor (companyAccounts filtresi doğru)
- `q` arama → server-side (client-side filtreleme YOK); Türkçe "İ/ı" testi
- filtre sonucu boş → `empty-filtered` ≠ `empty-no-data`
- 403 → non-disclosing; yabancı şirket cari sayısı sızmıyor
- company-switch sırasında eski şirket listesi boyanmıyor (`stale-rejected`) — shell fence
- pasif cari `partyState=all` ile görünür + işaretli (`AT-013`)
- cursor "daha fazla yükle" → `nextCursor` ile ikinci sayfa, mükerrer satır yok
- keyboard: kolon sıralama, satır navigasyonu, filtre focus
- `totalCount` string değeri bozulmadan gösteriliyor

## 7. ChatGPT'den beklenen (spec)

- `AT-009…AT-014`'ün gerçek veri/rol/adım ayrıntısı (özellikle AT-010 account türetme örneği)
- Liste kolonlarının kesin sırası/başlığı + `tradeName` gösterim kuralı
- `pageSize` varsayılanı + "toplam" davranışı (her zaman mı, isteğe bağlı mı)
- Pasif cari görünürlük varsayılanı (`partyState` başlangıç değeri — `active` öneriyorum)

ChatGPT bu spec'i vermeden C-01 kodu, yukarıdaki contract + `DEC-CAR-001..003` + `AT-010/011/013`'ten türetilebilir; belirsiz kalan kolon sırası/başlık `ASSUMPTION` + ChatGPT review'da düzeltilir.
