# Organization

## Cross-module schema contracts

- The alternate keys `organization.Locations (CompanyId, Id)` (`AK_Locations_CompanyId_Id`, Inventory INV-2) and `organization.WorkCenters (CompanyId, Id)` (`AK_WorkCenters_CompanyId_Id`, Manufacturing MFG-5) are foreign-key targets of other modules' tables; renaming, dropping or re-keying them is a cross-module schema change and needs those owners' migration in the same slice.

## Module reads for other modules

- `IOrganizationLocationStatusReaderV1.ReadAsync(companyId, locationId)` answers another module's own command with the location's own `IsActive` and `IsExternal` (supplier depots, SINAN_KARARLARI_2026-08-31 §3), or null when the resolved company has no such location. It is company-scoped by the server-resolved `ICompanyContext` (the tenant query filter, never bypassed) and applies **no** user permission or row scope: the calling module authorizes its own command, and a caller's Organization read grant must not decide it. `IOrganizationService.GetLocationAsync` stays the user read.
- A `companyId` that is not the resolved company (or no resolved company) throws `OrganizationCompanyContextMismatchException` (`organization_company_context_mismatch`); a source failure is not caught.
- The site's state is deliberately not part of it: no source decides that an inactive site makes its locations unusable. How a caller treats inactive or external locations is the caller's own rule.

## ORG-04 — one work center's read-only card (W1-ORG / AV-01, Business #66 5702892195)

- `GET /api/v1/organization/work-centers/{id}` (`GetOrganizationWorkCenterV1`) → `WorkCenterDetail`: `id`, `locationId`, `code`, `name`, canonical `kind`, `isActive`, the record's lossless `version`, and `canOpenLocationDetail`.
  - The work-centre read's own grant (`organization.work-centers.read`) and the ORG-03 list's own scope predicate (`ApplyWorkCenterScope`, shared by both reads), under the tenant filter. Another company's, an out-of-scope and an unknown id are the same code-less 404, never a 403.
  - `canOpenLocationDetail` is the list's flag, by the same rule: the location card's own read predicate on `organization.locations.read`; without that permission no location query is issued and the flag is false. The card never shows a location name it was not given.
  - A read: no command, audit event or idempotency record. `version` is carried for a later stale check; the card writes nothing.
- Shell: `organization.work-centers.detail` → `/ayarlar/organizasyon/is-merkezleri/:workCenterId` (`ORG-04`, parameterized, `AnyGrantedScope`, `organization.work-centers.read` alone, like ORG-03). Owner contract 1.1 → 1.2; the accepted authority revisions are the central writer's round.
- Not here: no status change, capacity, shift, load, ownership, production or location label, no new permission, grant, migration or policy (OPEN-001/OPEN-030 untouched).
- `IOrganizationService.GetWorkCenterAsync` has a default body that throws: it keeps the shell registry's central in-memory doubles compiling without an edit outside this module. The registered `OrganizationService` answers it; the default can go once those doubles implement the read.

## Master edit — create and edit sites, locations and work centres (first-release outcome)

- Commands (`IOrganizationMasterDataService`, kept apart from `IOrganizationService` so the reads' central test doubles are not widened):
  - `POST /api/v1/organization/sites` `{code, name, type, reason}` → 201 `{id, version}`; `PUT …/sites/{id}` `{code, name, expectedVersion, reason}` → 204. Permission `organization.sites.write` — a site has no parent inside the company, so only a company-wide grant reaches it (a narrower scope is the code-less 404).
  - `POST /api/v1/organization/locations` `{siteId, code, name, reason}` → 201; `PUT …/locations/{id}` → 204. Permission `organization.locations.write`: create needs the parent site in scope (company-wide or that site); edit uses the location read predicate. The external flag is NOT a request field: it follows the parent site's type (`ExternalWarehouse` → external), the seed's own derivation (DEC-ORG-004, OPEN-002).
  - `POST /api/v1/organization/work-centers` `{locationId, code, name, kind, reason}` → 201; `PUT …/work-centers/{id}` → 204. Permission `organization.work-centers.write`: create needs the parent location in scope (company-wide, its site, or the location); edit uses the work-centre read predicate.
  - Edits change code and name only. A site's type, a location's site and external flag, a work centre's location and kind (the Manufacturing planning reference's immutable fact) are fixed at creation.
- Protocol (the active-state command's): `Idempotency-Key` required; a repeated key with the same payload and actor replays the stored terminal answer (`Idempotency-Replayed`, original correlation) and applies nothing again; same key + other payload/actor is the code-less 409 without `conflict`. Every terminal answer is stored: 201/204, 404 (another company's / out-of-scope / unknown record or parent — never a 403), 409 with `conflict {expectedVersion, currentVersion}` for a stale version, 422 `organization_code_in_use` for a code another record of the same kind holds in the company (active or not; the `(CompanyId, Code)` unique index also decides a lost race). A reason (1–1000) is required: each applied command writes exactly one audit event (`organization.{site|location|work-center}.{created|updated}.v1`, operation `Create` / `Update` / `NoChange`, the request's source channel — `Web` for the UI) in the same transaction.
- Company fence: the resolved `ICompanyContext` (tenant query filter + the DbContext write guard); metadata naming another company never runs.
- Reads: `SiteSummary` now publishes the site's `version` (the site edit's `expectedVersion`). A created location is immediately a candidate of `IOrganizationLocationCandidatesV1` when internal and active (Inventory's location selector), and a created work centre is answered by `IOrganizationWorkCenterPlanningReferenceV1` (Manufacturing).
- Seed: the runtime seed recognises its records by their deterministic id as well as by code, so renaming a seeded record's code does not make the next start re-add it under the old code (which would collide with the id).
- Shell (provider 1.2 → 1.3), controls only: `organization.sites.manage` and `organization.locations.create` (ORG-01), `organization.locations.edit` (ORG-02), `organization.work-centers.create` (ORG-03), `organization.work-centers.edit` (ORG-04). Registration of the three write names, the authority revisions, OpenAPI and pins are the central round.
- Not here: site / work-centre active state, deletes, operator/owner party, address, capacity, responsible role or any other depot-card attribute (ORGANIZASYON_MODELI §Depo kartı) — the runtime model carries none and OPEN-002 keeps the operator/owner relation in the backlog; VKN / company edit (OPEN-030).
