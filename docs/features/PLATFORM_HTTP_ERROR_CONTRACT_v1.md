# Platform HTTP Error Contract v1

- Status: `DRAFT / HUMAN SECURITY REVIEW REQUIRED`
- Owner: `CODEX-PLATFORM`
- Tracking: GitHub Issues `#96`, `#112`, `#113`
- Source boundaries: ERP-000K shell contract, ERP-000L observability readiness
- Binding request-metadata decisions:
  - `PLATFORM-CORR-001=A`, Issue `#112`, comment `#issuecomment-5470593914`
  - `PLATFORM-COMPANY-CONTEXT-001=A`, Issue `#113`, comment
    `#issuecomment-5470594079`
- Production activation: `NO-GO`; this contract does not authorize deployment

## 1. Purpose

The API must expose shared authentication, authorization and platform-failure outcomes
without requiring clients to parse localized `title` or `detail` text. Public error bodies
use `application/problem+json` and a stable top-level `code` extension. Platform-owned
responses remain JSON even when the request `Accept` header does not advertise JSON.

This contract owns only platform outcomes. Domain modules continue to own their validation,
not-found and conflict discriminators. In particular, Counterparty `400`, `404` and `409`
codes remain in the ChatGPT-CARI single-writer boundary tracked by Issue `#93`.
The two request-metadata `400` codes below are explicit shared-middleware producers; HTTP
status `400` by itself never transfers a module response to platform ownership.

## 2. Stable platform codes

| HTTP | `code` | Client meaning |
|---:|---|---|
| 400 | `correlation_metadata_invalid` | Correlation or causation request metadata was rejected before endpoint/auth processing. |
| 400 | `company_context_invalid` | An authenticated, CompanyContext-required endpoint did not receive one valid non-empty company identifier. |
| 401 | `authentication_required` | Authentication is missing, expired or unusable. |
| 403 | `forbidden` | Authentication exists but the operation is not authorized. |
| 500 | `internal_error` | An unexpected platform failure was safely redacted. |
| 503 | `service_unavailable` | The `/health/live` health result is unhealthy. |

The `code` values are presentation contracts, not authorization evidence. A client cannot
gain permission, CompanyContext or record visibility by sending or replaying one of them.

`correlation_metadata_invalid` is platform-owned and uniform for public, unknown, anonymous
protected and authenticated routes. Shared middleware never selects Counterparty
`command_metadata_invalid` or another module code from pre-auth endpoint metadata.

`company_context_invalid` is limited to an authenticated endpoint explicitly marked as
requiring CompanyContext. Missing, empty, whitespace-only, multiple, comma-joined, malformed and
`Guid.Empty` selection metadata produce this code. A valid but inaccessible company remains
`403/forbidden`; unauthenticated access remains `401/authentication_required`; optional or
unknown routes do not acquire a CompanyContext requirement from the header alone.

## 3. Public envelope

Required fields:

- `type`
- `title`
- `status`
- `detail`
- `code`

Optional field:

- `correlationId`, only when the server-side correlation boundary resolved a valid value

The correlation identifier is diagnostic. It is not a user, tenant, permission, record,
idempotency or approval identifier.

When hostile correlation/causation metadata is rejected, the hostile identifier is never
accepted or echoed. The server resolves a new safe correlation identifier for the response,
application log scope and response header. The causation value is discarded. Some CR/LF
inputs can be rejected by the HTTP transport before application middleware; this contract
does not claim an application ProblemDetails body for a request the host never delivers.

## 4. Redaction and non-disclosure

Platform-owned responses use fixed safe Turkish titles and details. They do not expose:

- exception type, message or stack;
- token, claim, role or permission names;
- SQL, connection string, provider or dependency identity;
- CompanyId, foreign record identifier or existence information;
- file path, secret, environment value or internal endpoint.

For an unhandled exception caught before the response starts, the application-owned handler
does not pass the exception object, message or stack to its own logger. It records only the
stable platform error code and, when the request correlation boundary has resolved it, the
same safe correlation identifier returned to the client as a structured log field. It does
not invent a correlation identifier when that boundary is unresolved.

If a response has already started, ASP.NET cannot replace it with this contract. The host may
abort the connection, rethrow the exception and emit framework/host telemetry. That telemetry
path is outside this v1 public-response and application-owned-log redaction guarantee and must
not be represented as a universally redacted sink.

Pre-response-start unhandled `500` and `/health/live` unhealthy `503` results discard
pre-existing public extensions before the stable code and safe correlation are applied. The
same implementation also normalizes
shared `401` and `403` results, so middleware and authorization-policy failures do not drift
into different client contracts.

The two explicit request-metadata `400` producers are canonicalized only when both the exact
code and the private server-side producer marker are present. A module cannot gain platform
ownership by copying or constructing a reserved code string in its own ProblemDetails; a
marker-less reserved discriminator is removed before serialization while the module-owned
status, title and detail remain intact.
Their pre-existing extensions are discarded before the accepted code and safe resolved
correlation are returned. Generic or module-owned `400`, `404` and `409` responses are not
canonicalized by the platform writer.

## 5. Ownership boundary

- The ASP.NET API composition root registers the platform ProblemDetails customizer and
  an `Accept`-independent platform writer plus the authorization middleware result handler.
- The `503` contract is bound to the real `/health/live` unhealthy path; it is not a generic
  promise for arbitrary dependency failures that have no explicit producer.
- Domain modules do not reference authentication/session implementation details.
- Status `400` is platform-owned only when shared middleware explicitly produces
  `correlation_metadata_invalid` or `company_context_invalid`. Other `400` responses remain
  with their declared module owner.
- Reserved code literals, constant references and request-metadata producer calls are locked
  to an exact platform source allowlist by an architecture guard. A new producer or duplicate
  declaration fails closed until the ownership contract is reviewed.
- Statuses outside these two explicit `400` producers and `401`, `403`, `500`, `503` are not
  assigned a platform code by this contract.
- A future module-owned code manifest must be consumed through its owner contract; this
  platform contract must not duplicate its values.

## 6. Acceptance gate

- `PLATFORM-META-AC-001`: absent correlation remains valid and produces a 32-hex server
  identifier; safe identifiers of length 1 and 128 remain accepted;
- `PLATFORM-META-AC-002`: empty, whitespace, multiple, comma-joined, over-128, unsafe,
  non-ASCII or application-reachable control correlation/causation values return
  `400/correlation_metadata_invalid`, a new safe correlation and no hostile echo;
- `PLATFORM-META-AC-003`: public, unknown, anonymous protected and authenticated protected
  paths receive the same correlation metadata code and disclose no route, module, permission,
  role, CompanyId or hostile value;
- `PLATFORM-META-AC-004`: authenticated required-context requests with missing, empty,
  whitespace-only, multiple, comma-joined, malformed or `Guid.Empty` company metadata return
  `400/company_context_invalid`;
- `PLATFORM-META-AC-005`: anonymous required-context access remains
  `401/authentication_required`, valid inaccessible company remains `403/forbidden`, and
  optional/unknown routes do not emit `company_context_invalid`;
- `PLATFORM-META-AC-006`: if correlation and company metadata are both invalid, the earlier
  correlation boundary wins; with valid correlation the CompanyContext boundary owns the
  result. Middleware order is not silently changed;
- `PLATFORM-META-AC-007`: rejected Organization and real Counterparty protected writes leave
  business state, total audit and total CommandExecution cardinality unchanged on real SQL
  Server; rejected idempotency keys are not reserved, while a valid follow-up command and
  same-key replay produce exactly one business change, one audit event and one execution;
- `PLATFORM-META-AC-008`: private producer-marker ownership preserves all generic/module-owned
  `400`, `404`, `409` codes and fields, but removes a marker-less copied or dynamically
  constructed reserved discriminator; architecture guards lock literals, constant references
  and producer methods to their exact platform allowlist; shared middleware never emits
  Counterparty `command_metadata_invalid` and platform production source contains no
  Counterparty route, permission or domain-code literal;
- `PLATFORM-META-AC-009`: full middleware paths for both request-metadata `400` outcomes return
  `application/problem+json` with the same canonical envelope when `Accept` requests
  `text/plain` or `application/xml`;
- hostile-`Accept` anonymous protected request returns `401/authentication_required`;
- an invalid production JWT challenge does not expose token-validation error details;
- hostile-`Accept` authenticated but unauthorized request returns `403/forbidden`;
- CompanyContext access denial uses the same safe `403` envelope;
- hostile-`Accept` pre-response-start exception returns redacted `500/internal_error`;
- an unhealthy `/health/live` check returns redacted `503/service_unavailable` without health
  description, exception, data or dependency identity;
- safe server correlation is returned consistently and a pre-response-start `500` application
  log carries that exact value as a structured field;
- real module-owned `400`, `404` and `409` responses receive no platform code and retain their
  module-owned fields;
- full API integration and architecture regressions pass;
- fresh runner-backed CI, exact-head independent Security review, exact-head named
  ChatGPT-BUSINESS/CARI read-only review, and human security merge approval.

Runner-backed GitHub Actions execution is restored. Issue `#87` continues to track
branch-protection and independent-review enforcement. Every new exact head still requires
actual runner-backed backend, frontend, real-SQL, container and UI-golden success; older or
zero-step runs cannot be reused as acceptance evidence.
