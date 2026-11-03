---
name: permission-capability-registration
description: Use when a slice needs a NEW permission name (e.g. purchasing.requests.withdraw) or a new shell capability/route/control — registers the name in ErpPermissions and the module manifest, bumps the module's shell provider version, re-derives ShellCapabilityRegistryAuthorities, wires the frontend capability id, and keeps grants out (grant/rol/Critical is a human gate).
---

# Permission + shell capability registration

`src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs` (12 commits) and
`src/backend/GoymenERP.Api/Shell/ShellCapabilityRegistryAuthorities.cs` (7 commits) change in most feature slices
since 2026-10-20. Canonical example: #907 `06c34c0` (`purchasing.requests.withdraw`).

**Human gate:** registering a name is allowed; giving it to a role, seed or real user, or marking it Critical, is
not (AGENTS.md "grant/rol/Critical … somut kullanıcı kararı"). Only SENTETIK browser-suite personas in disposable
stacks may hold it.

## Steps

1. `ErpPermissions.cs`: add `public const string <Module><Thing><Verb> = "<module>.<things>.<verb>";` with the XML
   comment style of the neighbours (decision ids, "Registered, not granted", "Company-wide only", "Not Critical").
   Add the constant to the `All` set and, when company-wide, to `CompanyWideOnly` (same order as neighbours). Do not
   add to `Critical` without a user decision.
2. Module manifest `src/backend/Modules/<M>/…Application/Contracts/<M>PermissionManifestV1.cs`: add the const and
   append it to `Names`.
3. Registration tests: `tests/GoymenERP.Identity.UnitTests/<M>PermissionRegistrationTests.cs` (exact name set,
   company-wide, not critical) and, if Settings-owned, `tests/GoymenERP.Api.IntegrationTests/Settings…PermissionRegistrationTests.cs`.
4. Shell provider `src/backend/Modules/<M>/…Infrastructure/Shell/<M>ShellCapabilityProvider.cs`: add the
   declaration (`requestDetail with { CapabilityId = …, ParentCapabilityId = …, RequiredPermissions = [...] }` for a
   control), bump `OwnerContractVersion` (e.g. `"1.5"` → `"1.6"`) and the `<para>` history note.
5. `ShellCapabilityRegistryAuthorities.cs`: every host revision that contains this provider moves. Run the shell
   registration tests; each prints `"<M> normal-host authority drifted. Actual: sha256:…"`
   (`tests/GoymenERP.Api.IntegrationTests/*ShellRegistrationTests.cs`, `*PermissionAndShellRegistrationTests.cs`
   such as `PurchasingPermissionAndShellRegistrationTests.cs`, `*ShellCapabilityProviderTests.cs`,
   `ShellControlCapabilityRegistryTests.cs`). Replace each constant **in place** with the printed value and add one
   NOTE line ("<slice>: <M> x.y adds …; rows re-derived in place by ShellCapabilityRegistry.ComputeCandidateRevision").
   Without local .NET, push and take the `Actual:` values from the CI log of the api group.
6. Frontend: `src/frontend/goymen-erp-web/src/<m>/capabilities.ts` (capability id), its `capabilities.test.ts`, the
   shell mount-drift test `src/shell/<m>MountDrift.test.ts` if a **route** (not a control) was added, and the screen
   gate (`hasCapability(shell, …)`).
7. Browser suite (only if the E2E journey needs it): `infra/browser-suites/<suite>.override.yml` gives the
   permission to the SENTETIK persona, with the comment that no real role receives it.
8. Docs: module README permission table; `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` row is written only from
   a Sinan decision (persona → grant). If the decision is missing: `NEEDS-SINAN` on Issue #66, keep the name
   registered-only.
9. Re-pin: `recovery-inventory-repin` (backend surface moved), `openapi-regenerate` if an endpoint uses it.

## Pitfalls

- A permission/rol change is **K3** (two independent exact-head reviews).
- Never check role names in code; check the permission. `ErpPersonaRoleCodes` is only for persona facts the
  decisions name explicitly (e.g. Sales `IsSalesSupport`).
- Service-side rules such as "only the preparer may withdraw" stay in the module service/domain, not in the
  permission catalogue (see the #907 XML comments).
- Changing a provider without bumping `OwnerContractVersion` makes the registry revision lie; reviewers reject it.
