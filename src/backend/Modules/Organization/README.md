# Organization

## Cross-module schema contracts

- The alternate keys `organization.Locations (CompanyId, Id)` (`AK_Locations_CompanyId_Id`, Inventory INV-2) and `organization.WorkCenters (CompanyId, Id)` (`AK_WorkCenters_CompanyId_Id`, Manufacturing MFG-5) are foreign-key targets of other modules' tables; renaming, dropping or re-keying them is a cross-module schema change and needs those owners' migration in the same slice.

## Module reads for other modules

- `IOrganizationLocationStatusReaderV1.ReadAsync(companyId, locationId)` answers another module's own command with the location's own `IsActive` and `IsExternal` (supplier depots, SINAN_KARARLARI_2026-08-31 §3), or null when the resolved company has no such location. It is company-scoped by the server-resolved `ICompanyContext` (the tenant query filter, never bypassed) and applies **no** user permission or row scope: the calling module authorizes its own command, and a caller's Organization read grant must not decide it. `IOrganizationService.GetLocationAsync` stays the user read.
- A `companyId` that is not the resolved company (or no resolved company) throws `OrganizationCompanyContextMismatchException` (`organization_company_context_mismatch`); a source failure is not caught.
- The site's state is deliberately not part of it: no source decides that an inactive site makes its locations unusable. How a caller treats inactive or external locations is the caller's own rule.
