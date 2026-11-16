# Audit owner — typed founder recovery source

KIM-S7 / KIM-D22, KIM-D23, KIM-D43 and KLN-D113 TK-44 add a separate typed company System audit port.
Normal `AuditEventDraft` still requires a permission for every actor type. The sole permission-null variant is
the successful founder recovery CLI with a closed event/operation pair, operator declaration and company subject.
The Audit-owned context joins Identity's exact open Serializable transaction; it never commits the caller's handles.
Each adminless or restored-assignment company is audited on reactivation; password reset audits every current
membership company. Platform refusal/success security events remain separate. No password, hash or stamp is audited.

EF-generated `20261116220434_AddTypedFounderRecoverySystemAudit` changes only Audit-owned nullability and adds the
closed CHECK. Down first throws **51952** while permission-null history exists; it does not rewrite canonical history.
Legacy command contexts retain required permission metadata and do not own Audit DDL. No production CLI was run,
no migration was applied, and runtime SQL/CLI/browser/CI acceptance remains **NOT_RUN**.
