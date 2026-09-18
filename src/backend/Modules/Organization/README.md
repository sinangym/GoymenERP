# Organization

## Cross-module schema contracts

- The alternate keys `organization.Locations (CompanyId, Id)` (`AK_Locations_CompanyId_Id`, Inventory INV-2) and `organization.WorkCenters (CompanyId, Id)` (`AK_WorkCenters_CompanyId_Id`, Manufacturing MFG-5) are foreign-key targets of other modules' tables; renaming, dropping or re-keying them is a cross-module schema change and needs those owners' migration in the same slice.
