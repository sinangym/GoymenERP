/**
 * The Finance transport PORT the screens talk to.
 *
 * Why a port and not `fetch` calls: #258 publishes body shapes and module service ports
 * (`IManualReceiptService`, `IFinanceBankAccountReader`) but NO HTTP routes, methods,
 * status mapping or idempotency/replay headers. Writing URLs here would be inventing an
 * API. The screens therefore depend on this interface; the HTTP adapter is written once
 * the Backend publishes the mapping, and nothing above it changes.
 *
 * Contract of every method:
 *  - resolve `{ kind: 'body', body }` when a response body ARRIVED (decoded by the caller
 *    with ./contract.ts — an undecodable body is still "no trustworthy answer");
 *  - resolve `{ kind: 'no-reply' }` (or reject) when nothing trustworthy came back —
 *    network loss, timeout, aborted delivery. For a command that is an UNKNOWN outcome;
 *  - `scope.companyId` is the verified shell company the adapter binds the call to. It is
 *    never put into a request body (company is server-bound, #258 README).
 */

import type { BankAccountApprovalBody, BankAccountIbanChangeBody, BankAccountProposeRequest } from './bankAccountContract'
import type {
  ApproveReversalRequest,
  DraftSaveRequest,
  ProposeReversalRequest,
  RecordRequest,
  RejectReversalRequest,
  ReverseRequest,
} from './contract'
import type { DraftContextRequest } from './draftContext'
import type { HttpReadReply } from './http'
import type {
  ApproveManualBankPaymentRequest,
  ManualBankPaymentDraftSaveRequest,
  RecordManualBankPaymentRequest,
} from './manualBankPaymentContract'

export type PortReply = { readonly kind: 'body'; readonly body: unknown } | { readonly kind: 'no-reply' }

export interface FinanceScope {
  readonly companyId: string
}

export interface FinanceReceiptPort {
  listManualBankPayments(
    scope: FinanceScope,
    request: { readonly cursor: string | null },
    signal: AbortSignal,
  ): Promise<PortReply>
  manualBankPaymentDetail(scope: FinanceScope, paymentId: string, signal: AbortSignal): Promise<PortReply>
  saveManualBankPaymentDraft(
    scope: FinanceScope,
    idempotencyKey: string,
    request: ManualBankPaymentDraftSaveRequest,
  ): Promise<PortReply>
  approveManualBankPayment(
    scope: FinanceScope,
    idempotencyKey: string,
    request: ApproveManualBankPaymentRequest,
  ): Promise<PortReply>
  recordManualBankPayment(
    scope: FinanceScope,
    idempotencyKey: string,
    request: RecordManualBankPaymentRequest,
  ): Promise<PortReply>

  /** FS0 — `IFinanceBankAccountReader.ListAuthorizedAsync` → `ReadResult<BankAccountPage>`. */
  listBankAccounts(scope: FinanceScope, signal: AbortSignal): Promise<PortReply>
  /** `ListAuthorizedAsync(SourceListRequest)` → `ReadResult<SourcePage>`. */
  listSources(scope: FinanceScope, request: { readonly cursor: string | null }, signal: AbortSignal): Promise<PortReply>
  /**
   * PUBLISHED (#261): `GET /finance/manual-receipts/draft-context`. Outcome is the HTTP status,
   * not a body envelope — `http.ts#fetchDraftContext` is the production implementation.
   */
  draftContext(scope: FinanceScope, request: DraftContextRequest, signal: AbortSignal): Promise<HttpReadReply>
  /** `DetailAuthorizedAsync(SourceDetailRequest)` → `ReadResult<SourceDetail>`. */
  sourceDetail(scope: FinanceScope, request: { readonly sourceId: string }, signal: AbortSignal): Promise<PortReply>
  /** → `CommandResult`. A `Succeeded` Draft is draft persistence only, never money. */
  draftSave(scope: FinanceScope, idempotencyKey: string, request: DraftSaveRequest): Promise<PortReply>
  /** → `CommandResult`. The only command that records money. */
  record(scope: FinanceScope, idempotencyKey: string, request: RecordRequest): Promise<PortReply>
  /** → `CommandResult`. Original amount/currency/account are resolved server-side. */
  reverse(scope: FinanceScope, idempotencyKey: string, request: ReverseRequest): Promise<PortReply>
  proposeReversal(scope: FinanceScope, idempotencyKey: string, request: ProposeReversalRequest): Promise<PortReply>
  approveReversal(scope: FinanceScope, idempotencyKey: string, request: ApproveReversalRequest): Promise<PortReply>
  rejectReversal(scope: FinanceScope, idempotencyKey: string, request: RejectReversalRequest): Promise<PortReply>
  pendingReversals(scope: FinanceScope, signal: AbortSignal): Promise<PortReply>
  reversalProposal(scope: FinanceScope, proposalId: string, signal: AbortSignal): Promise<PortReply>

  /** FIN-UI-1a: `GET /finance/bank-accounts/master` → `ReadResult<BankAccountMasterPage>` (F-04's list). */
  listBankAccountMaster(scope: FinanceScope, signal: AbortSignal): Promise<PortReply>
  /** FIN-UI-1a: `GET /finance/bank-accounts/currency-candidates` → `ReadResult<BankAccountCurrencyCandidates>`. */
  bankAccountCurrencyCandidates(scope: FinanceScope, signal: AbortSignal): Promise<PortReply>
  /** FIN-UI-1a: `POST /finance/bank-accounts` → `BankAccountCommandResult`. Never a money effect. */
  proposeBankAccount(scope: FinanceScope, idempotencyKey: string, request: BankAccountProposeRequest): Promise<PortReply>
  /** FIN-UI-1a: `POST /finance/bank-accounts/{accountId}/approval` → `BankAccountCommandResult` (Ö4 server-side). */
  approveBankAccount(
    scope: FinanceScope,
    idempotencyKey: string,
    accountId: string,
    body: BankAccountApprovalBody,
  ): Promise<PortReply>

  /** D4: `POST /finance/bank-accounts/{accountId}/iban-change` → `BankAccountCommandResult`. The value waits. */
  proposeBankAccountIbanChange(
    scope: FinanceScope,
    idempotencyKey: string,
    accountId: string,
    body: BankAccountIbanChangeBody,
  ): Promise<PortReply>
  /** D4: `POST …/iban-change/approval` → `BankAccountCommandResult`. Ö4 against the CHANGE's proposer. */
  approveBankAccountIbanChange(
    scope: FinanceScope,
    idempotencyKey: string,
    accountId: string,
    body: BankAccountApprovalBody,
  ): Promise<PortReply>
}
