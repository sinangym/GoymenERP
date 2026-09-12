# U-01 / U-02 backend handoff

This is the isolated Product preparation branch, not a deployed API. The Domain
and Application projects are not yet registered in the shared solution or host.
The current host cannot read or create Products.

Sources: DEC-PRD-003/004/010, Master §7.4,
[locked global identity](https://github.com/sinangym/GoymenERP/issues/98#issuecomment-5469061415),
[locked pilot access](https://github.com/sinangym/GoymenERP/issues/99#issuecomment-5469937658),
and [U-01/U-02 JIT at cc2ea1e](https://github.com/sinangym/GoymenERP/blob/cc2ea1ee4b29b05f98d06158165c9bbf9f4b2220/docs/features/PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1.md).

## Implemented independently

- One immutable master identity; no owning CompanyId or physical lot fields.
- Basic construction starts Draft with server-supplied identity/code/time.
- Group-prefix/positive-sequence code value; no implicit group or production seed.
- Candidate JSON DTO rejects unknown ownership, lifecycle, code and physical fields.
- Read projection serializes the version as a decimal string.

## Contract candidate for the existing frontend pair

The minimum field set awaits the existing Business owner's narrow JIT handoff.
`name` + `groupId` is a proposal, not an approved group catalogue or create policy.
The group prefix and unpadded sequence formatting are reversible technical choices;
no server sequence allocator or persistent code namespace exists yet.

Candidate request:

```json
{"name":"Siyah sac","groupId":"f4fc7672-0b46-41ba-a017-393401ba61dd"}
```

Candidate core response (synthetic example):

```json
{"id":"942e1d53-857b-49dc-80cb-66e5b04128a2","code":"SAC-42","name":"Siyah sac","lifecycle":"draft","version":"1","createdAtUtc":"2026-09-12T07:00:00+00:00"}
```

No route, list pagination, error envelope, capability or grant is advertised as
available. Existing platform ProblemDetails should be reused after integration.
Unknown/unavailable unit, template or production-role information must not become
zero, false or a fabricated stock value in the frontend.

## Remaining owners and dependencies

1. Existing ChatGPT-BUSINESS: minimum create fields and approved group/prefix source.
2. Existing platform Backend: explicit global authorization subject/scope, canonical
   audit subject/origin separation and durable command/replay scope under #103/#104.
   Company permissions do not confer global authority. No origin-replay model has
   been silently chosen by this branch.
3. Product Backend: persistence, unique code/alias namespace and atomic Draft create
   after those contracts are accepted; list/detail/HTTP and isolated SQL acceptance.
4. Existing platform Backend: separate central integration of solution/project
   references, DI/host, OpenAPI, approved permission names and migration runner.
5. Product Frontend: real API journey on reserved API 26080 / SQL 26433 / UI 26173.

The candidate DTO alone is not validation, authorization or persistence. There is
no Product DbContext, migration, runtime seed, activation command or SQL evidence.
Solution CI cannot cover these new projects until the sole integration writer adds
them; green existing CI must not be reported as Product test execution.

Direct verification:

```powershell
dotnet test tests/GoymenERP.Product.UnitTests/GoymenERP.Product.UnitTests.csproj
```
