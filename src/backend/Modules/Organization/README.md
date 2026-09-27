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
