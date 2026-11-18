# Identity migration notes

### KIM-S8 / KIM-D3 optional authenticator and recovery codes

EF-generated `20261117004612_AddLocalTwoFactorSecrets` adds encrypted local authenticator secrets,
one-use recovery hashes and nullable original correlation evidence in the existing command journal.
It never enables two-factor authentication or assigns access. Credentials remain global Identity-owned
accounts; each self-service request verifies its current user, security stamp and live company membership.
Credential, factor, command receipt and typed Audit-owner append share one Serializable transaction.
Down first refuses with **THROW 51967** while any factor, enabled credential or MFA replay evidence exists.
Login consumes the factor and any one-use temporary password in one transaction; a failed write restores both.
Enrollment and regenerated recovery plaintext is returned once with NoStore; replay returns no secret.
Unit, HTTP, SQL and frontend test sources are separate from runtime acceptance, which remains NOT_RUN.


### KIM-S5 W7 lifecycle foundation (KIM-D18/D20/D21; historical source checkpoint)

- Generated migration `20261116185533_AddCompanySetupInboxAndOutboxPosition`: owner inbox/outbox and lifecycle source; Up DDL only. Existing companies remain Active.
- Down first refuses with **51944** while company setup lifecycle/history would lose meaning (forward fix). No database application was performed.
- Only the two exact setup events pass the pending-company dispatcher gate; business events stay held. Dispatcher activation remains human-owned.
- The W7 checkpoint had no opening endpoint or group authority source. KIM-D56 and the W8 implementation below supersede that source gap; production grants, dispatcher activation and runtime acceptance remain separate human/verification gates.
- Unit/contract and real SQL facts are written; runtime acceptance is NOT_RUN.


### KIM-S5 / KIM-D56 explicit group setup

`identity.company-setup.first-admin.assign` is registered only. It is not placed in `Critical`, a role,
a seed, or a real user's grants by this change. Existing `organization.companies.open` remains Critical.
Company roles and Product master grants do not imply either group operation. Human-approved group eligibility
is read from Identity-owned `CompanySetupGroupAssignments`, with explicit operation, decision, validity,
revocation and SQL rowversion. There is no production grant/role-assignment API or automatic assignment writer.
Its CompanyId-less global authorization scope requires K3 review; it is not a wildcard company or a filter bypass.

Opening events now append only an immutable dedup receipt; they never grant the opener rights, including late
or repeated events. The explicit first-Admin command selects its user under fresh group authority and the shared
company Admin lock. `CompanySetupAuthority` records the one-authority KIM-D56 approval; historical
`CompanyOpener` rows remain, but new writes in that mode are refused. Existing-company founder bootstrap is unchanged.

EF-generated `20261116205256_AddExplicitCompanySetupAuthority` adds the global eligibility model and widens only
the approval-mode checks. Its Up is DDL-only and seeds nothing. Down first refuses with **THROW 51947** while a
group authority or new approval mode exists. SQL/browser/CI acceptance and real grants remain separate gates.

### KIM-S9: explicitly targeted server operator 2FA recovery

KIM-D27 and the human answer relayed at #1189 / 6951606530 authorize the separate interactive
`identity recover-two-factor` command for every local user, including an inactive account or a user with no
company memberships. The exact two command words are the entire argv. Target name, retry key and reason
are read at the terminal; the confirmation displays the real target and confirmed versions. No password,
authenticator seed or recovery code is requested. `recover-founder` retains its existing separate semantics.

Recovery deactivates the confirmed factor and renews the credential security stamp. It never changes the
password, user activity, roles or company access. An inactive user remains inactive. A target's changed
user/credential/factor versions produce a recorded refusal instead of adopting a fresh expected version.

`LocalTwoFactorOperatorRecoveryAttempts` is a global immutable command journal for global local credentials;
it has no invented CompanyId. Its unique hashed retry key binds target/operator/reason/original versions.
Reuse the same key after a lost answer: its original result is returned without removing a later enrollment.
Another target/operator/reason cannot read that original result. A genuinely new action needs a new key and
a new explicit confirmation. The server operator's real OS/machine declaration is recorded only by typed Audit.

Identity owns the Serializable transaction. Audit and SecurityEvents borrow its exact connection/transaction
through their closed owner ports, append, and never commit it. Their failure rolls the credential, factor and
journal back. No target company is invented and no membership/role is assigned. Normal HTTP self-service
retains its current-session, actor-equals-target and company-membership checks.

EF generated `20261117160905_AddOperatorTwoFactorRecoveryAttempts`; Down first refuses with **51922** while
history exists. The reviewed UPDATE/DELETE trigger refuses with **51925**, in addition to the tracker guard.
It has no INSERT trigger: SQL Server's [OUTPUT restriction applies to the corresponding DML action](https://learn.microsoft.com/sql/t-sql/queries/output-clause-transact-sql?view=sql-server-ver17#triggers).
No database application, command execution or runtime acceptance is implied by source/EF compilation.


### W8 access grant approval-mode SQL NULL repair

EF-generated `20261118211933_RejectNullAccessGrantApprovalModes` replaces only the two existing
`AccessGrantRequests` approval-mode/second-approval CHECK constraints. An Approved row must carry a
non-NULL existing approval mode; SQL UNKNOWN cannot admit a missing mode. The accepted mode names,
second-person rules and existing founder/company setup exceptions do not change. Up performs DDL
only: it creates no request, assignment, role, grant or default and does not rewrite historical rows.

Down first refuses with **THROW 51798** while any Approved request exists, preserving the repaired
history contract; otherwise it restores the two previous predicates. The existing real-SQL constraint
fact also executes that exact first Down guard with no approved history and with a valid synthetic
approval inside a rolled-back transaction. This is guard evidence, not full downgrade/reapply evidence.
Migration-operation unit assertions, actual SQL execution and final K3 acceptance remain distinct;
new test sources alone do not imply that the SQL verification has run.
