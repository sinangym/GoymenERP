import { isDateText, isUuid } from './discountLimitContract'

export type ManufacturingProfileKind = 'parameters' | 'tolerances'
export const MANUFACTURING_PROCESSES = ['Slitting', 'ColdRolling', 'TubeProfile', 'Paint', 'CutToLength'] as const
export type ManufacturingProcess = typeof MANUFACTURING_PROCESSES[number]
export const manufacturingProfilePath = (kind: ManufacturingProfileKind) => `/settings/manufacturing-${kind === 'parameters' ? 'parameter' : 'tolerance'}-profiles`
export interface ParameterKey { readonly key: string; readonly unit: string; readonly kind: string; readonly maximumScale: number; readonly processes: readonly ManufacturingProcess[]; readonly choices: readonly string[] }
export interface ParameterValue { readonly key: string; readonly unit: string; readonly value: string }
export interface ManufacturingProfile {
  readonly profileId: string; readonly source: 'Catalog' | 'Settings'; readonly process: ManufacturingProcess; readonly workCenterId: string | null
  readonly profileVersion: string; readonly stateVersion: string; readonly status: 'Draft' | 'Approved' | 'Published' | 'StartingValue'
  readonly effectiveFrom: string; readonly effectiveThrough: string | null; readonly preparedBy: string | null; readonly preparedAtUtc: string | null
  readonly preparationReason: string; readonly approvedBy: string | null; readonly approvedAtUtc: string | null; readonly approvalReason: string | null
  readonly publishedBy: string | null; readonly publishedAtUtc: string | null; readonly publicationReason: string | null
  readonly values: readonly ParameterValue[]; readonly maximumFirePercent: string | null; readonly massBalanceDifferenceTolerancePercent: string | null
}
export interface ManufacturingProfileList { readonly companyId: string; readonly businessDate: string; readonly keys: readonly ParameterKey[]; readonly profiles: readonly ManufacturingProfile[]; readonly startingValues: readonly ManufacturingProfile[] }
export interface ManufacturingProfileReceipt { readonly profileId: string; readonly profileVersion: string; readonly stateVersion: string; readonly status: 'Draft' | 'Approved' | 'Published' }
export interface PrepareManufacturingProfileBody { readonly expectedVersion: string; readonly process: ManufacturingProcess; readonly workCenterId: string | null; readonly reason: string; readonly effectiveFrom: string; readonly effectiveThrough: string | null; readonly values?: readonly { readonly key: string; readonly value: string }[]; readonly maximumFirePercent?: string; readonly massBalanceDifferenceTolerancePercent?: string }
export interface PublishManufacturingProfileBody { readonly expectedVersion: string; readonly reason: string }

const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const nullableText = (value: unknown): value is string | null => value === null || text(value)
const timestamp = (value: unknown): value is string | null => value === null || (typeof value === 'string' && /^\d{4}-\d\d-\d\dT/.test(value) && Number.isFinite(Date.parse(value)))
export const isManufacturingVersion = (value: unknown): value is string => typeof value === 'string' && value.length <= 19 && /^(0|[1-9]\d*)$/.test(value) && BigInt(value) <= 9223372036854775807n
const process = (value: unknown): value is ManufacturingProcess => typeof value === 'string' && MANUFACTURING_PROCESSES.some((item) => item === value)
const invariant = (value: unknown): value is string => typeof value === 'string' && /^(0|[1-9]\d*)(\.\d+)?$/.test(value)

export function decodeManufacturingProfileReceipt(raw: unknown): ManufacturingProfileReceipt | null {
  if (!record(raw) || !isUuid(raw.profileId) || !isManufacturingVersion(raw.profileVersion) || !isManufacturingVersion(raw.stateVersion)
    || (raw.status !== 'Draft' && raw.status !== 'Approved' && raw.status !== 'Published')) return null
  return { profileId: raw.profileId, profileVersion: raw.profileVersion, stateVersion: raw.stateVersion, status: raw.status }
}

function profile(raw: unknown, kind: ManufacturingProfileKind): ManufacturingProfile | null {
  if (!record(raw) || !isUuid(raw.profileId) || (raw.source !== 'Catalog' && raw.source !== 'Settings') || !process(raw.process)
    || (raw.workCenterId !== null && !isUuid(raw.workCenterId)) || !isManufacturingVersion(raw.profileVersion) || !isManufacturingVersion(raw.stateVersion)
    || !['Draft', 'Approved', 'Published', 'StartingValue'].includes(String(raw.status)) || !isDateText(raw.effectiveFrom)
    || (raw.effectiveThrough !== null && (!isDateText(raw.effectiveThrough) || raw.effectiveThrough < raw.effectiveFrom))
    || !nullableText(raw.preparedBy) || !timestamp(raw.preparedAtUtc) || !text(raw.preparationReason)
    || !nullableText(raw.approvedBy) || !timestamp(raw.approvedAtUtc) || !nullableText(raw.approvalReason)
    || !nullableText(raw.publishedBy) || !timestamp(raw.publishedAtUtc) || !nullableText(raw.publicationReason)) return null
  const values: ParameterValue[] = []
  if (kind === 'parameters') {
    if (!Array.isArray(raw.values)) return null
    for (const value of raw.values) {
      if (!record(value) || !text(value.key) || !text(value.unit) || !text(value.value) || values.some((item) => item.key === value.key)) return null
      values.push({ key: value.key, unit: value.unit, value: value.value })
    }
  } else if (!invariant(raw.maximumFirePercent) || (raw.massBalanceDifferenceTolerancePercent !== null && !invariant(raw.massBalanceDifferenceTolerancePercent))) return null
  return { profileId: raw.profileId, source: raw.source, process: raw.process, workCenterId: raw.workCenterId as string | null,
    profileVersion: raw.profileVersion, stateVersion: raw.stateVersion, status: raw.status as ManufacturingProfile['status'],
    effectiveFrom: raw.effectiveFrom, effectiveThrough: raw.effectiveThrough as string | null, preparedBy: raw.preparedBy,
    preparedAtUtc: raw.preparedAtUtc, preparationReason: raw.preparationReason, approvedBy: raw.approvedBy,
    approvedAtUtc: raw.approvedAtUtc, approvalReason: raw.approvalReason, publishedBy: raw.publishedBy,
    publishedAtUtc: raw.publishedAtUtc, publicationReason: raw.publicationReason, values,
    maximumFirePercent: kind === 'tolerances' ? raw.maximumFirePercent as string : null,
    massBalanceDifferenceTolerancePercent: kind === 'tolerances' ? raw.massBalanceDifferenceTolerancePercent as string | null : null }
}

export function decodeManufacturingProfiles(raw: unknown, company: string, kind: ManufacturingProfileKind): ManufacturingProfileList | null {
  if (!record(raw) || raw.companyId !== company || !isDateText(raw.businessDate) || !Array.isArray(raw.profiles)) return null
  const keys: ParameterKey[] = []
  if (kind === 'parameters') {
    if (!Array.isArray(raw.keys)) return null
    for (const key of raw.keys) {
      if (!record(key) || !text(key.key) || !text(key.unit) || typeof key.kind !== 'string' || !['Positive', 'NonNegative', 'RatioUpToOne', 'WholeCount', 'Choice'].includes(key.kind)
        || typeof key.maximumScale !== 'number' || !Number.isInteger(key.maximumScale) || key.maximumScale < 0 || key.maximumScale > 28
        || !Array.isArray(key.processes) || !key.processes.every(process) || !Array.isArray(key.choices) || !key.choices.every(text)
        || keys.some((item) => item.key === key.key)) return null
      keys.push({ key: key.key, unit: key.unit, kind: key.kind, maximumScale: key.maximumScale, processes: key.processes, choices: key.choices })
    }
  } else if (!Array.isArray(raw.startingValues)) return null
  const decode = (items: readonly unknown[]) => {
    const decoded: ManufacturingProfile[] = []
    for (const item of items) { const value = profile(item, kind); if (value === null || decoded.some((entry) => entry.profileId === value.profileId)) return null; decoded.push(value) }
    return decoded
  }
  const profiles = decode(raw.profiles)
  const startingValues = kind === 'parameters' ? [] : decode(raw.startingValues as unknown[])
  return profiles === null || startingValues === null ? null : { companyId: company, businessDate: raw.businessDate, keys, profiles, startingValues }
}
