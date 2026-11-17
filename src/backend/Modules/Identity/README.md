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
