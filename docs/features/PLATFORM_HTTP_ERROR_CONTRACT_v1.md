# Platform HTTP Error Contract v1

- Status: `DRAFT / HUMAN SECURITY REVIEW REQUIRED`
- Owner: `CODEX-PLATFORM`
- Tracking: GitHub Issue `#96`
- Source boundaries: ERP-000K shell contract, ERP-000L observability readiness
- Production activation: `NO-GO`; this contract does not authorize deployment

## 1. Purpose

The API must expose shared authentication, authorization and platform-failure outcomes
without requiring clients to parse localized `title` or `detail` text. Public error bodies
use `application/problem+json` and a stable top-level `code` extension. Platform-owned
responses remain JSON even when the request `Accept` header does not advertise JSON.

This contract owns only platform outcomes. Domain modules continue to own their validation,
not-found and conflict discriminators. In particular, Counterparty `400`, `404` and `409`
codes remain in the ChatGPT-CARI single-writer boundary tracked by Issue `#93`.

## 2. Stable platform codes

| HTTP | `code` | Client meaning |
|---:|---|---|
| 401 | `authentication_required` | Authentication is missing, expired or unusable. |
| 403 | `forbidden` | Authentication exists but the operation is not authorized. |
| 500 | `internal_error` | An unexpected platform failure was safely redacted. |
| 503 | `service_unavailable` | The `/health/live` health result is unhealthy. |

The `code` values are presentation contracts, not authorization evidence. A client cannot
gain permission, CompanyContext or record visibility by sending or replaying one of them.

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

## 5. Ownership boundary

- The ASP.NET API composition root registers the platform ProblemDetails customizer and
  an `Accept`-independent platform writer plus the authorization middleware result handler.
- The `503` contract is bound to the real `/health/live` unhealthy path; it is not a generic
  promise for arbitrary dependency failures that have no explicit producer.
- Domain modules do not reference authentication/session implementation details.
- Statuses outside `401`, `403`, `500` and `503` are not assigned a platform code by this
  contract.
- A future module-owned code manifest must be consumed through its owner contract; this
  platform contract must not duplicate its values.

## 6. Acceptance gate

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
- fresh runner-backed CI, exact-head independent review and human security merge approval.

Issue `#87` remains an external acceptance blocker while GitHub Actions jobs terminate with
zero runner steps. Local evidence cannot replace that gate.
