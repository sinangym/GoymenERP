# Finance → shared shell mount handoff (single package)

Owner of this file: FİNANS (`src/finance/**`). Recipient: the shared shell/router owner (CARİ), via
the coordinator. Nothing here is applied to a shared file by this lane.

Status: **READY TO MOUNT (FIN-UI-1b).** Both earlier blockers are resolved:
1. The Finance shell capability provider exists (FIN-UI-1a, #325):
   `FinanceShellCapabilityProvider`, six STAGED declarations (`ImplementationAvailable: false`).
2. The HTTP port covers every published route (`./httpPort.ts`); Record and Reverse have no route
   (T05, OPEN-009) and are not offered on the mounted detail page.

The declarations stay staged until the FIN-01 browser acceptance (card FIN-UI-1 §6). In the normal
runtime every Finance route therefore closes onto the neutral not-found block and calls nothing; the
disposable Finance staged profile (central, mfg precedent) opens only their availability for FIN-01.

## What exists in `src/finance/**`

| Export | File | Purpose |
|---|---|---|
| `financeRoutes` | `financeRoutes.tsx` | The five routes, each in `CapabilityRoute` + `FinancePortProvider` (HTTP) |
| `FINANCE_CAPABILITY`, `FINANCE_ROUTE` | `capabilities.ts` | The provider's ids and paths (pinned by `FinanceShellCapabilityProviderTests`) |
| `BankAccountsPage` / `BankAccountProposePage` | `BankAccountsPage.tsx` / `BankAccountProposePage.tsx` | F-04 list (+ approval control) / propose |
| `ReceiptListPage` / `ReceiptDraftPage` / `ReceiptDetailPage` | `ReceiptPages.tsx` | F-02 list / new draft (entered from a Cari account) / one draft |

## The mount change (shell owner, one serial change)

1. `src/shell/capabilities.ts` — add the five ROUTE ids with their paths, exactly as `FINANCE_ROUTE`:

   | Capability id | Route |
   |---|---|
   | `finance.receipts.list` | `/finans/tahsilatlar` |
   | `finance.receipts.draft` | `/finans/tahsilatlar/yeni` (static, before `:sourceId`) |
   | `finance.receipts.detail` | `/finans/tahsilatlar/:sourceId` |
   | `finance.bank-accounts.list` | `/finans/banka-hesaplari` |
   | `finance.bank-accounts.propose` | `/finans/banka-hesaplari/yeni` |

   `finance.bank-accounts.approve` is a CONTROL of the F-04 list (`ParentCapabilityId`), not a route:
   it belongs in no route registry; the list checks it with `hasCapability`.
2. `src/shell/router.tsx` — import and spread `financeRoutes` before the catch-all.
3. `src/shell/AppShell.tsx` — nav entries for `finance.receipts.list` ("Tahsilat Taslakları") and
   `finance.bank-accounts.list` ("Banka Hesapları"). No nav entry for the new-draft route (it is
   entered from a Cari account with `?partyId=&companyAccountId=`), the detail, or the propose route
   (linked from the F-04 list).
4. `src/shell/financeMountDrift.test.ts` — the `salesMountDrift.test.ts` pattern: shell ids ==
   `FINANCE_ROUTE` keys' ids, routes/labels equal, the control id not a route.

Serialize with the other lanes' mounts; two lanes must not edit `router.tsx` in parallel.

## Evidence boundary

Component/DOM tests with a recording port and real pending promises (SENTETIK/NOT_ENV), and unit
tests of the HTTP mapping with a stubbed `fetch`. Not API, SQL or browser evidence: that is FIN-01's
labelled run on the disposable Finance staged profile.
