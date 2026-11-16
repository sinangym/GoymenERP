export interface PlanAllocationShortfall {
  readonly companyId: string; readonly planId: string; readonly labelId: string; readonly productId: string
  readonly labelVersion: string; readonly requestedKg: string; readonly availableKg: string; readonly missingKg: string
  readonly freePositionCount: 1
}
const GUID = /^(?!00000000-0000-0000-0000-000000000000)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const KG = /^(0|[1-9][0-9]*)(\.[0-9]{1,3})?$/
export function decodePlanAllocationShortfall(value: unknown, companyId: string, planId: string): PlanAllocationShortfall | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const v = value as Record<string, unknown>
  if (['companyId', 'planId', 'labelId', 'productId'].some((key) => typeof v[key] !== 'string' || !GUID.test(v[key] as string))
    || (v.companyId as string).toLowerCase() !== companyId.toLowerCase() || (v.planId as string).toLowerCase() !== planId.toLowerCase()
    || typeof v.labelVersion !== 'string' || !/^[1-9][0-9]*$/.test(v.labelVersion) || v.freePositionCount !== 1
    || ['requestedKg', 'availableKg', 'missingKg'].some((key) => typeof v[key] !== 'string' || !KG.test(v[key] as string))) return null
  return v as unknown as PlanAllocationShortfall
}
