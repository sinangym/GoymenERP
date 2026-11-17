# Audit owner — typed founder recovery source

KIM-S7 / KIM-D22, KIM-D23, KIM-D43 and KLN-D113 TK-44 add a separate typed company System audit port.
Normal `AuditEventDraft` still requires a permission for every actor type. The founder permission-null variant is
the successful founder recovery CLI with a closed event/operation pair, operator declaration and company subject.
The Audit-owned context joins Identity's exact open Serializable transaction; it never commits the caller's handles.
Each adminless or restored-assignment company is audited on reactivation; password reset audits every current
membership company. Platform refusal/success security events remain separate. No password, hash or stamp is audited.

EF-generated `20261116220434_AddTypedFounderRecoverySystemAudit` changes only Audit-owned nullability and adds the
closed CHECK. Down first throws **51952** while permission-null history exists; it does not rewrite canonical history.
Legacy command contexts retain required permission metadata and do not own Audit DDL. No production CLI was run,
no migration was applied, and runtime SQL/CLI/browser/CI acceptance remains **NOT_RUN**.

## KIM-S8 authenticated self-service audit

KIM-D3 permits optional personal two-factor setup. The typed
`ILocalCredentialSelfServiceAuditWriterV1` records only the three closed TOTP
enable/disable/recovery-code-regeneration transitions, with actor equal to target,
sequential credential versions and the real command metadata. It invents no
administrator permission or role. Arbitrary Audit drafts still require their
existing permission; founder System recovery retains its separate typed boundary.

Identity proves the current session and company membership, then lends its open
Serializable transaction. Audit opens its own company-scoped owner context and
appends in that transaction without committing it. A failed Identity command
rolls the audit back. No token, password, seed, recovery code or credential hash
is carried in the audit payload. The Audit screen recognizes this exact typed
null-permission variant as a personal security action.

EF CLI generated `20261117003912_AddLocalCredentialSelfServiceAudit`; Up changes only
the CHECK and Down first refuses with **51970** while personal security rows exist.
API, Audit.UnitTests and Api.IntegrationTests source builds passed with
`OpenApiGenerateDocumentsOnBuild=false`; TypeScript compiled. The normal API build
still stops at the pending central Shell registry pin, so OpenAPI generation is not PASS.
SQL and unit test sources cover transaction
rollback/commit, company isolation, wrong actor/target, unknown transitions and
the first Down refusal. Runtime tests and DB application are **NOT_RUN**.
The fresh owner context-construction surface awaits K3 review; it is not silently
added to any guard allowlist.
