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
