# Finance → shared shell mount handoff (single package)

Owner of this file: FİNANS (`src/finance/**`). Recipient: the shared shell/router owner (CARİ), via
the coordinator. Nothing here is applied to a shared file by this lane.

Status: **MOUNTED (FIN-UI-1b, #332); provider 1.1 ready for central authority integration.**
1. `FinanceShellCapabilityProvider` now has nine published declarations (`ImplementationAvailable: true`):
   six routes and three controls.
2. The HTTP port covers every Finance route (`./httpPort.ts`). Record and reversal proposal are controls
   of the receipt detail; reversal approval has its own checker route.

The historical FIN-UI-1c run (#332, run 35444049175) covered the earlier six-declaration provider. It is not
evidence for the provider 1.1 additions. The shared-shell owner must bind the reviewed 1.1 authority first;
fresh SQL/browser/CI evidence remains separate. A route or control opens only for an actor holding all of its
narrow permissions; declarations do not grant them.

## What exists in `src/finance/**`

| Export | File | Purpose |
|---|---|---|
| `financeRoutes` | `financeRoutes.tsx` | The six routes, each in `CapabilityRoute` + `FinancePortProvider` (HTTP) |
| `FINANCE_CAPABILITY`, `FINANCE_ROUTE` | `capabilities.ts` | The provider's ids and paths (pinned by `FinanceShellCapabilityProviderTests`) |
| `BankAccountsPage` / `BankAccountProposePage` | `BankAccountsPage.tsx` / `BankAccountProposePage.tsx` | F-04 list (+ approval control) / propose |
| `ReceiptListPage` / `ReceiptDraftPage` / `ReceiptDetailPage` | `ReceiptPages.tsx` | F-02 list / new draft (entered from a Cari account) / one draft |

## The mount change (shell owner, one serial change)

1. `src/shell/capabilities.ts` — keep the six ROUTE ids with their paths, exactly as `FINANCE_ROUTE`:

   | Capability id | Route |
   |---|---|
   | `finance.receipts.list` | `/finans/tahsilatlar` |
   | `finance.receipts.draft` | `/finans/tahsilatlar/yeni` (static, before `:sourceId`) |
   | `finance.receipts.detail` | `/finans/tahsilatlar/:sourceId` |
   | `finance.receipts.reverse.approve` | `/finans/ters-kayit-onaylari` |
   | `finance.bank-accounts.list` | `/finans/banka-hesaplari` |
   | `finance.bank-accounts.propose` | `/finans/banka-hesaplari/yeni` |

   These are controls, not routes, and belong in no route registry:
   `finance.bank-accounts.approve` (F-04 list), `finance.receipts.record` (F-02 detail), and
   `finance.receipts.reverse.propose` (F-02 detail). Their pages check them with `hasCapability`.
2. `src/shell/router.tsx` — import and spread `financeRoutes` before the catch-all.
3. `src/shell/AppShell.tsx` — nav entries for `finance.receipts.list` ("Tahsilat Taslakları") and
   `finance.bank-accounts.list` ("Banka Hesapları"). No nav entry for the new-draft route (it is
   entered from a Cari account with `?partyId=&companyAccountId=`), the detail, or the propose route
   (linked from the F-04 list).
4. `src/shell/financeMountDrift.test.ts` — the `salesMountDrift.test.ts` pattern: shell ids ==
   `FINANCE_ROUTE` keys' ids, routes/labels equal, the control id not a route.

Serialize with the other lanes' mounts; two lanes must not edit `router.tsx` in parallel.

## Pending dedicated customer-instrument read route

The existing customer cheque / note portfolio no longer relies on the receipt-list route. The Finance-owned route is:

| Capability id | Route | Existing required permission |
|---|---|---|
| `finance.customer-instruments.list` | `/finans/musteri-cek-senetleri` | `finance.customer-instrument.read` |

The shared-shell/authority single writer must register this route as a Company-wide read capability and mount the
updated `financeRoutes`. It must not add `finance.receipts` as a second required permission. The existing receive and
state-change controls remain separate; this route grants no command authority. Add one navigation label such as
"Müşteri Çekleri ve Senetleri" only through the central mount, and refresh the registry/fingerprint/OpenAPI evidence
there. Until that binding lands, the route remains fail-closed.

## Evidence boundary

Component/DOM tests use a recording port and real pending promises (SENTETIK/NOT_ENV); HTTP mapping
tests use a stubbed `fetch`. They are not API, SQL or browser evidence. Provider 1.1 still requires the
central authority integration and fresh labelled acceptance evidence.
