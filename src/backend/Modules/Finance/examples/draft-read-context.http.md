# Draft read context: central HTTP integration handoff

Owner: Finance consumer; API/DI/capability/OpenAPI writer: central Backend task 01a0865c-1990-7783-8aee-4aee15618b31.
Source: main cb11d5de6f718f3f1389f41628b543ba0581ad7f, including Finance #258 and Settings #256; Business #254 at 6f0cd10259aeb2a44d32a92535464ed2ce096cf7 sections 2-5.

DraftReadContextReader.GetAsync(DraftReadContextRequest request, string? acceptedMoneyOperation, string correlationId, CancellationToken cancellationToken) is implemented in Finance.Contracts. Company and actor come from ICompanyContext/ICurrentUser; no body/query company, actor, currency, profile or label is trusted. The operation argument must come from an accepted server mapping. Null mapping fails unavailable. The existing IFinancePermissionPort.ReadBankAccounts action gates this read and is rechecked before response disclosure; it is not an Identity grant name. Counterparty.GetAuthorizedPartyAsync supplies labels and the selected active company account, including valid roles0. Settings.GetMoneyProfileAsync supplies currency code and effective profile for the selected bank currency and occurredOn. Response contains no amount, balance, source posting, approval or write capability.

Central wrapper proposal (not a published route until central contribution): GET /api/v1/finance/manual-receipts/draft-context?treasuryAccountId=<uuid>&partyId=<uuid>&companyAccountId=<uuid>&occurredOn=YYYY-MM-DD. All four query fields are required. Selected bank currency is authoritative; date must bind as date-only. Serialize only Resource on success, using FinanceJson.CreateOptions semantics; do not serialize internal outcome/code envelopes as success. Required nullable fields remain present. No request body and no idempotency key/replay headers are required for this read.

Outcome mapping for central implementation:

| Consumer outcome | HTTP | Wire body |
|---|---|---|
| Found | 200 | Non-null DraftReadContextView with company/date/bank/currency/profile/party identity |
| InvalidRequest / invalid query binding | 400 | Central validation problem; no partial resource |
| Denied | 403 | Existing platform problem code forbidden |
| NotFound | 404 | Central not-found problem; no cross-company label disclosure |
| Unavailable | 503 | Existing platform problem code service_unavailable; never empty200 or zero balance |
| Unauthenticated middleware | 401 | Existing platform problem code authentication_required |
| Unexpected exception | 500 | Existing platform problem code internal_error |

The current main API Errors/ErpProblemCodes.cs and ErpProblemDetailsExtensions.cs actually define/normalize forbidden and service_unavailable. Internal settings_access_denied and settings_provider_unavailable remain distinct consumer outcomes but must not be promised as 403/503 wire codes because the platform writer normalizes those responses. Internal finance_* diagnostic codes in DraftReadContextResult are not new entries in the shared public problem-code registry. Central owns exact 400/404 codes and query binding tests.

Missing sources, exactly:

- Finance bank owner: no persisted TreasuryAccount projection/data/lifecycle source implementing the existing IFinanceBankAccountReader exists. The supplied UnavailableFinanceBankAccountReader returns unavailable (or denied for wrong company/actor), never an empty catalog success.
- Settings owner: ISettingsPrerequisiteProvider exists, but accepted company/currency/code/profile ID/version/scale/effective-date data does not. UnavailableSettingsPrerequisiteProvider remains unavailable.
- Identity/Business owner: no accepted Finance read grant/action mapping or manual-receipt MoneyProfile operation identifier exists. The supplied UnavailableFinancePermissionPort never grants access; central may register this adapter until an accepted mapping arrives. Read metadata preparation does not resolve OPEN-009.

No second catalog, seed data, grant, role, operation constant, period-open fallback or database write is introduced. Central can expose an honest unavailable read contract without asserting that a user can prepare/save real bank receipts.

Command boundary grounded in existing platform source, not implemented by this GET: Api/OpenApi/ErpOpenApiContract.cs names Idempotency-Key, X-Correlation-Id, X-Causation-Id, Idempotency-Replayed and Idempotency-Original-Correlation-Id. Idempotency.Domain/Executions/CommandExecution.cs stores CompanyId, CommandName, IdempotencyKeyHash, RequestHash, ActorId, HttpStatusCode, ResponseJson and OriginalCorrelationId with the durable terminal. CounterpartyCommandService.CreateAsync checks company permission before finding/replaying a terminal. A future persistent DraftSave must use the platform transaction/audit/terminal behavior and pinned concrete status mapping; this change does not fabricate a generic Finance command executor or publish Record/Reverse endpoints. Existing preparation CommandResult remains a model, not an HTTP execution receipt. Unknown command outcomes retain the same key/company/actor/payload; a successful read context is never a command replay or financial success.

Targeted consumer evidence: DraftReadContextTests, 19 passed / 0 failed / 0 skipped, finance-draft-read-context.trx. Synthetic owner responses test composition and fail-closed behavior, not real bank/profile data or HTTP execution. Central wrapper HTTP tests and generated OpenAPI are still required before claiming a published HTTP contract.
