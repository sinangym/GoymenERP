# Muhasebe ekranlarının ortak shell'e ve host'a bağlanması — hazır delta (ACC-V0b)

**Kimden:** FİNANS (Accounting sahibi; `src/accounting/**` ile Accounting modülünün tek yazarı)
**Kime:** CARİ (merkezi yazar: `src/shell/**`, `Program.cs`, shell registry / authority, BROWSER-CI)
**Kaynak:** ACC-V0 kartı (koordinatör onayı, 2026-09-19). Backend yüzeyi ACC-V0a'dadır.

Bu dosya bir istek değil, **uygulanmaya hazır kaynak**. Delta tamamen **ekleyicidir**.

Delta uygulansa da ekranlar açılmaz. Sunucu tüm bildirimleri **staged** (`ImplementationAvailable: false`) ilan ediyor; `CapabilityRoute` yayımlanmamış bir kabiliyeti fail-closed kapatır. Açma, `acc01` browser kabulünden sonra ayrı bir flip PR'dır (FIN-01 emsali).

## Frontend (`src/shell/**`)

### 1. `src/shell/capabilities.ts`

`CAPABILITY` nesnesine (değerler `src/accounting/capabilities.ts` ile birebir aynı):

| Anahtar | Kabiliyet | Route | Nav etiketi |
|---|---|---|---|
| `accountingPeriodsList` | `accounting.periods.list` | `/muhasebe/donemler` | Muhasebe Dönemleri |
| `accountingPeriodsPropose` | `accounting.periods.propose` | `/muhasebe/donemler/yeni` | (yok) |
| `accountingJournalsList` | `accounting.journals.list` | `/muhasebe/fisler` | Muhasebe Fişleri |
| `accountingJournalsDetail` | `accounting.journals.detail` | `/muhasebe/fisler/:journalId` | (yok) |
| `accountingPostingQueues` | `accounting.posting-queues` | `/muhasebe/kontrol` | Muhasebe Kontrol |

`accounting.periods.approve` (Ö4) ve `accounting.periods.close` (Ö5) **route değil, kontroldür** (sağlayıcıdaki `ParentCapabilityId`). Shell route tablosuna girmez; M-07 listesi onları `hasCapability` ile okur. Sunucu kendi önerisini onaylamak isteyene her durumda 403 verir.

Statik `donemler/yeni` route'u listeden ve her parametreli route'tan önce tanımlıdır (`/cari/yeni` kuralı).

### 2. `src/shell/router.tsx`

```ts
import { accountingRoutes } from '../accounting/accountingRoutes'
// ...
      ...accountingRoutes,
```

Bundan sonra `accountingRoutes.tsx` içindeki `as CapabilityId` cast'i kaldırılabilir (Accounting şeridinin takibi).

### 3. `AppShell.tsx` nav

`CAPABILITY.accountingPeriodsList`, `CAPABILITY.accountingJournalsList`, `CAPABILITY.accountingPostingQueues`.

### 4. Drift testi (önerilen; `financeMountDrift.test.ts` emsali)

Shell'in mount ettiği Accounting route kümesi `ACCOUNTING_ROUTE` ile birebir aynı olmalı; `accounting.periods.approve` ve `accounting.periods.close` shell tablosunda yer almamalı.

## Backend (merkezi)

- **`Program.cs`:** `AccountingShellCapabilityProvider` (`GoymenERP.Modules.Accounting.Infrastructure.Shell`) `IShellCapabilityProvider` olarak (singleton), ACC-V0a host bağlamasıyla birlikte.
- **Normal host authority:** yeni satır (mevcut küme + Accounting 1.0 staged).
- **Disposable profil önerisi:** `accounting-periods-and-journals` (profil `acc-staged`): C-01 list-only Counterparty sağlayıcısı + Accounting'in kendi 7 bildirimi, yalnız `ImplementationAvailable` açılmış.
- **`acc01` suite:** `SUITES` / `SUITE_PROFILES` / workflow seçeneği (yazan suite; read-only değil). Aktörler: bir hazırlayan (`accounting.periods.read/propose`), ayrı bir onaylayan (`accounting.periods.read/approve/close`, `accounting.journals.read`, `accounting.posting-queues.read`). Seed ve spec FİNANS'tan gelir.

## Yapılmayanlar

Fiş oluşturma / onay / ters kayıt yok (ACC-V1; NEEDS-BUSINESS 5740501573, 5740501655, OPEN-009). Hesap planı yok; eksik eşleme kuyrukta kalır.

---

# ACC-V0c — staged → normal flip (hazır delta, CARİ için)

**Kimden:** FİNANS (Accounting sahibi). **Kime:** CARİ (merkezi yazar). **Emsal:** FIN-UI-1c, MFG-UI-F.

ACC-V0b indi (#362) ve `acc01` o head'de **acc-staged** profilinde yeşil oldu (run 35479050164, 8 vaka, failure=0). Bu delta ekranları normal runtime'da açar. Ekran, metin, rota, izin ya da iş kuralı DEĞİŞMİYOR — yalnız availability.

## Bu dalda olan (benim dosyalarım)

- `AccountingShellCapabilityProvider`: **yedi bildirimden ALTISI** `ImplementationAvailable: true` — M-07 dönem listesi, M-07-NEW öneri ekranı, onay ve kapatma **kontrolleri**, M-04 fiş listesi, M-01 kuyruklar.
- **`accounting.journals.detail` STAGED KALIYOR.** Gerekçe kanıt: `acc01` bu rotayı hiç açmadı — spec yalnız `/muhasebe/donemler`, `/muhasebe/donemler/yeni`, `/muhasebe/fisler`, `/muhasebe/kontrol` adreslerine gidiyor; seed hiçbir şey yazmadığı ve ACC-V1'den önce fiş üreten bir akış olmadığı için açılacak fiş yok. Kabulü olmayan ekranı yayımlamıyorum.
- `JournalPages`: M-04 listesindeki satır bağlantısı artık `accounting.journals.detail` kabiliyetine bağlı. Detay yayımlanmamışken satır fişi **etiketli referansıyla** gösteriyor ve kapalı bir rotaya bağlantı vermiyor (shell onu nötr not-found bloğuna kapatırdı). Component testi iki durumu da sabitliyor.
- Modülün kendi sağlayıcı testi: yayımlanan altı kimlik tek tek pinlendi, detayın kapalı olduğu ayrıca ölçülüyor.

## Merkezi satırlar (CARİ)

1. **Normal host authority sabiti yeniden ölçülür.** Girdiler: Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 (yayımlı, approve kontrolüyle) + Finance 1.0 (yayımlı) + Manufacturing 1.0 (yayımlı, MFG-ORD'un dört bildirimi) + Logistics 1.0 (staged) + Purchasing 1.0 (staged) + **Accounting 1.0 (altı yayımlı + `accounting.journals.detail` staged)**. Accounting'in bildirim listesi, route/screen/precedence/context/scope ve izinleri bu dalda değişmedi; yalnız altı bildirimin availability'si `false` → `true` oldu. **Değeri bu dosyaya yazmıyorum** — kör türetme kuralı: değeri görmemiş bir şerit türetir, koordinatör karşılaştırır.
2. **`acc-staged` profili emekliye ayrılır** (mfg-staged / fin-staged deseni): `C01E2eShellCapabilityActivation`'daki profil dalı ve `C01E2eAccountingShellCapabilityProvider`, `ShellCapabilityRegistryAuthorities`'teki `C01DisposableAccountingPeriodsAndJournalsV1` (yorumla "retired"), `infra/docker-compose.browser-acc-staged.yml`, validator tabloları (`PROFILES`, `PROFILE_COMPOSE_FILES`, `PROFILE_SUITE_LABELS`, `STAGED_PROFILE_VALUES`) ve `test_suite_profiles.py`'deki acc-staged satırları.
3. **`acc01` normal profile taşınır**: `tools/validate_browser_acceptance.py` → `SUITE_PROFILES["acc01"] = "normal"` ve `SUITE_FOR` haritasındaki acc-staged girdisi kalkar.

## `acc01` bu taşımada ne ister?

Spec ve seed **değişmeden** koşar; şunları kontrol ettim: suite hiçbir yerde `acc-staged` profiline ya da disposable zarfa ad vermiyor, kabiliyet anlık görüntüsünü gerçek host'tan okuyor ve aktörleri kendi grant'leriyle doğruluyor. Tek koşul: normal host'ta Accounting sağlayıcısı yayımlı olmalı — bu deltanın kendisi. **Kabul, final head'de `acc01`'in NORMAL profilde yeniden koşmasıdır; acc-staged'deki yeşil buraya taşınmaz** ve PR gövdesinde bunu böyle yazacağım.

## İddia (büyütmeden)

Flip ekranları **ulaşılabilir** yapar, **dolu** yapmaz: M-04 ve M-01 gerçek ama BOŞ okuma görünümleridir, fiş üreten akış ACC-V1'dir (NEEDS-BUSINESS 5740501655, OPEN-009). Hesap planı hâlâ açık karar (5740501573); ekran "Hesap referansı" gösteriyor, ad uydurmuyor.
