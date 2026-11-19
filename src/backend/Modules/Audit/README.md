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

### KIM-D27 platform credential recovery

The separately typed `ILocalTwoFactorOperatorRecoveryAuditV1` writes the closed
`PlatformCredentialRecovery` schema-3 subject for `identity.recover-two-factor`.
A global local credential may have zero company memberships: CompanyId, origin
company and authority revision remain null. This is not a general System/null-permission
draft path. The normal Company/V1 and GroupSharedProduct/V2 constructors, integrity
algorithms and company read filters remain unchanged.

The fixed target entity is `Identity.LocalCredential`; the immutable payload proves
confirmed/before/after user, credential and factor versions, the actual outcome and
server operator declaration. No credential hash, factor seed, recovery code or security
stamp enters the payload. The canonical V3 verifier rejects mismatched envelopes.

Audit opens a fresh unresolved owner scope and borrows the Identity command's open
Serializable transaction without committing it. A failed append rolls the whole
credential command back. Company audit reads do not acquire visibility over this
global platform subject. No company row or company-wide event fan-out is fabricated.

EF generated `20261117160915_AddPlatformCredentialRecoveryAudit`. Up only widens the
closed subject/permission checks; Down first refuses with **51923** while the new
subject exists. This subject and borrowed owner writer require K3 review; no guard
allowlist was silently expanded. SQL and CLI runtime acceptance remain **NOT_RUN**.


## W8 historical Audit rollback boundary

EF CLI generated `20261119131410_ProtectAuditHistoryRollback`; the model and
historical migration files remain unchanged. Down first locks the entire
AuditEvents table with `TABLOCKX, HOLDLOCK` and refuses with **51900** while any
history exists, including legacy Company/schema-1 permission-bearing events.

EF Core 10 commits each migration separately. An empty-store Down therefore
installs an owner-only INSTEAD OF INSERT barrier in the same transaction as the
empty check. **51901** refuses new appends between migration commits. It survives
an interrupted rollback or a rollback stopped at the previous schema; that older
schema intentionally refuses appends until this migration is reapplied. Up drops
only this temporary barrier. A complete Down-to-zero drops the owner table and its
barrier together. No business row, retention choice, permission or activation is
changed. The constant trigger DDL and its two Sql calls require K3 review.

The protection applies after this migration has been applied; it does not amend
previously distributed binaries or SQL scripts. Isolated Audit-only SQL tests
cover a canonical legacy record, both nonempty rollback refusals with unchanged
payload/hash/history, an empty zero/latest cycle, a committed previous-schema
barrier refusing an append, latest reapply, and a separate SQL session blocked
while the guard transaction is open. Build and real-SQL acceptance are NOT_RUN.

Transaction scope reference: [Microsoft EF migration transaction behavior](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes#all-pending-migrations-are-applied-in-a-single-transaction).
