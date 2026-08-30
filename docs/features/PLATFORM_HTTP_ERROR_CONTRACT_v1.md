# Platform HTTP Error Contract v1

- Status: `DRAFT / HUMAN SECURITY REVIEW REQUIRED`
- Owner: `CODEX-PLATFORM`
- Tracking: GitHub Issue `#96`
- Source boundaries: ERP-000K shell contract, ERP-000L observability readiness
- Production activation: `NO-GO`; this contract does not authorize deployment

## 1. Purpose

The API must expose shared authentication, authorization and platform-failure outcomes
without requiring clients to parse localized `title` or `detail` text. Public error bodies
use `application/problem+json` and a stable top-level `code` extension.

This contract owns only platform outcomes. Domain modules continue to own their validation,
not-found and conflict discriminators. In particular, Counterparty `400`, `404` and `409`
codes remain in the ChatGPT-CARI single-writer boundary tracked by Issue `#93`.

## 2. Stable platform codes

| HTTP | `code` | Client meaning |
|---:|---|---|
| 401 | `authentication_required` | Authentication is missing, expired or unusable. |
| 403 | `forbidden` | Authentication exists but the operation is not authorized. |
| 500 | `internal_error` | An unexpected platform failure was safely redacted. |
| 503 | `service_unavailable` | A required service is temporarily unavailable. |

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

The unhandled-exception handler does not pass the exception object, message or stack to the
logger. It records only the stable platform error code and the already-established safe
correlation scope. A telemetry sink cannot be treated as a reason to export the raw
exception.

Unhandled `500` and shared `503` results discard pre-existing public extensions before the
stable code and safe correlation are applied. The same implementation also normalizes
shared `401` and `403` results, so middleware and authorization-policy failures do not drift
into different client contracts.

## 5. Ownership boundary

- The ASP.NET API composition root registers the platform ProblemDetails customizer and
  authorization middleware result handler.
- Domain modules do not reference authentication/session implementation details.
- Statuses outside `401`, `403`, `500` and `503` are not assigned a platform code by this
  contract.
- A future module-owned code manifest must be consumed through its owner contract; this
  platform contract must not duplicate its values.

## 6. Acceptance gate

- anonymous protected request returns `401/authentication_required`;
- authenticated but unauthorized request returns `403/forbidden`;
- CompanyContext access denial uses the same safe `403` envelope;
- injected sensitive `500/503` input is absent from the serialized response;
- safe server correlation is returned consistently;
- module-owned `409` receives no platform code;
- full API integration and architecture regressions pass;
- fresh runner-backed CI, exact-head independent review and human security merge approval.

Issue `#87` remains an external acceptance blocker while GitHub Actions jobs terminate with
zero runner steps. Local evidence cannot replace that gate.
