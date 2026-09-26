import sql from 'mssql'

/**
 * Read-only effect counts for ONE goods receipt, straight from the disposable run's database. They measure the
 * atomic Purchasing + Inventory write (one acceptance share, one lot, one label, one receipt movement, one movement
 * line and one Inventory command) so a replay or a denied attempt is proven to add nothing, not merely to answer
 * the same JSON. Identities not yet known (before the browser generated them) are passed as the zero GUID.
 */
export interface ReceiptEffects {
  readonly acceptancesOnOrder: number
  readonly acceptance: number
  readonly lots: number
  readonly labelsByIdOrCode: number
  readonly receiptMovements: number
  readonly receiptMovementById: number
  readonly labelMovementLines: number
  readonly inventoryCommands: number
}

export interface ReceiptIdentity {
  readonly companyId: string
  readonly orderId: string
  readonly acceptanceId: string
  readonly lotId: string
  readonly labelId: string
  readonly movementId: string
  readonly labelCode: string
}

export const ZERO_GUID = '00000000-0000-0000-0000-000000000000'
export const RECEIPT_SOURCE_KIND = 'purchasing.goods-acceptance'
export const INVENTORY_RECEIPT_COMMAND = 'inventory.goods-receipt.stage.v1'

export async function readReceiptEffects(pool: any, identity: ReceiptIdentity): Promise<ReceiptEffects> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, identity.companyId)
    .input('orderId', sql.UniqueIdentifier, identity.orderId)
    .input('acceptanceId', sql.UniqueIdentifier, identity.acceptanceId)
    .input('lotId', sql.UniqueIdentifier, identity.lotId)
    .input('labelId', sql.UniqueIdentifier, identity.labelId)
    .input('movementId', sql.UniqueIdentifier, identity.movementId)
    .input('labelCode', sql.NVarChar, identity.labelCode)
    .input('sourceKind', sql.NVarChar, RECEIPT_SOURCE_KIND)
    .input('commandName', sql.NVarChar, INVENTORY_RECEIPT_COMMAND)
    .query(`
      SELECT
        (SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares
          WHERE CompanyId=@companyId AND OrderId=@orderId) AS acceptancesOnOrder,
        (SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares
          WHERE CompanyId=@companyId AND AcceptanceId=@acceptanceId) AS acceptance,
        (SELECT COUNT(*) FROM inventory.Lots WHERE CompanyId=@companyId AND Id=@lotId) AS lots,
        (SELECT COUNT(*) FROM inventory.Labels
          WHERE CompanyId=@companyId AND (Id=@labelId OR Code=@labelCode)) AS labelsByIdOrCode,
        (SELECT COUNT(*) FROM inventory.Movements
          WHERE CompanyId=@companyId AND SourceKind=@sourceKind AND SourceId=@acceptanceId) AS receiptMovements,
        (SELECT COUNT(*) FROM inventory.Movements
          WHERE CompanyId=@companyId AND Id=@movementId AND SourceKind=@sourceKind AND SourceId=@acceptanceId) AS receiptMovementById,
        (SELECT COUNT(*) FROM inventory.MovementLines WHERE CompanyId=@companyId AND LabelId=@labelId) AS labelMovementLines,
        (SELECT COUNT(*) FROM inventory.Commands
          WHERE CompanyId=@companyId AND CommandName=@commandName AND TargetId=@acceptanceId) AS inventoryCommands`)
  return result.recordset[0] as ReceiptEffects
}

/** The receipt location's Organization facts: the command must have used an active, internal location of this company. */
export async function readLocationFacts(pool: any, companyId: string, locationId: string):
  Promise<{ readonly locations: number; readonly activeInternal: number }> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .input('locationId', sql.UniqueIdentifier, locationId)
    .query(`
      SELECT
        (SELECT COUNT(*) FROM organization.Locations WHERE CompanyId=@companyId AND Id=@locationId) AS locations,
        (SELECT COUNT(*) FROM organization.Locations
          WHERE CompanyId=@companyId AND Id=@locationId AND IsActive=1 AND IsExternal=0) AS activeInternal`)
  return result.recordset[0] as { readonly locations: number; readonly activeInternal: number }
}
