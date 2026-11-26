import { isBoundUuid } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { readJson, type CommandOutcome } from '../settings/discountLimitApi'
import { postWorkCommand } from './collaborationApi'
export interface Delegation {
  readonly id: string; readonly companyId: string; readonly kind: string; readonly approverPermission: string
  readonly delegatorUserId: string; readonly delegateUserId: string; readonly startsAtUtc: string; readonly endsAtUtc: string
  readonly version: string; readonly recordedByUserId: string; readonly recordedAtUtc: string; readonly reason: string
  readonly revokedByUserId: string | null; readonly revokedAtUtc: string | null; readonly revocationReason: string | null
}
export interface DelegationPage { readonly companyId: string; readonly items: readonly Delegation[]; readonly page: number; readonly hasMore: boolean }
export interface PeoplePage { readonly companyId: string; readonly kind: string; readonly items: readonly { readonly userId: string; readonly displayName: string }[]; readonly hasMore: boolean }
export interface CreateDelegation { readonly kind: string; readonly delegateUserId: string; readonly endsAtUtc: string; readonly reason: string; readonly delegatorUserId: string | null; readonly startsAtUtc: string }
export interface RevokeDelegation { readonly expectedVersion: string; readonly reason: string }
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const text = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0
const instant = (v: unknown): v is string => text(v) && /(?:Z|[+-]\d{2}:\d{2})$/.test(v) && Number.isFinite(Date.parse(v))
const same = (a: unknown, b: string) => typeof a === 'string' && a.toLowerCase() === b.toLowerCase()
const fields = ['id','companyId','kind','approverPermission','delegatorUserId','delegateUserId','startsAtUtc','endsAtUtc','version','recordedByUserId','recordedAtUtc','reason','revokedByUserId','revokedAtUtc','revocationReason']
export function decodeDelegation(v: unknown, company: string): Delegation | null {
  if (!record(v) || !hasExactKeys(v, fields) || !same(v.companyId, company)
    || ![v.id,v.delegatorUserId,v.delegateUserId,v.recordedByUserId].every(isBoundUuid)
    || ![v.kind,v.approverPermission,v.reason].every(text) || ![v.startsAtUtc,v.endsAtUtc,v.recordedAtUtc].every(instant)
    || Date.parse(v.startsAtUtc as string) >= Date.parse(v.endsAtUtc as string)
    || !(v.version === '1' && v.revokedByUserId === null && v.revokedAtUtc === null && v.revocationReason === null
      || v.version === '2' && isBoundUuid(v.revokedByUserId) && instant(v.revokedAtUtc) && text(v.revocationReason))) return null
  return v as unknown as Delegation
}
export function decodeDelegationPage(v: unknown, company: string, page: number): DelegationPage | null {
  if (!record(v) || !hasExactKeys(v,['companyId','items','page','hasMore']) || !same(v.companyId,company) || v.page !== page
    || typeof v.hasMore !== 'boolean' || !Array.isArray(v.items) || v.items.length > 200) return null
  const items = v.items.map(row => decodeDelegation(row,company))
  if (items.some(row => row === null) || new Set(items.map(row => row!.id)).size !== items.length) return null
  return { companyId: company, items: items as Delegation[], page, hasMore: v.hasMore }
}
export function decodePeople(v: unknown, company: string, kind: string): PeoplePage | null {
  if (!record(v) || !hasExactKeys(v,['companyId','kind','items','hasMore']) || !same(v.companyId,company) || v.kind !== kind
    || typeof v.hasMore !== 'boolean' || !Array.isArray(v.items) || v.items.length > 50 || !v.items.every(row => record(row)
      && hasExactKeys(row,['userId','displayName']) && isBoundUuid(row.userId) && text(row.displayName))
    || new Set(v.items.map(row => (row as {userId:string}).userId)).size !== v.items.length) return null
  return v as unknown as PeoplePage
}
export const fetchDelegations = (company: string, all: boolean, page: number, signal?: AbortSignal) =>
  readJson(`/work/delegations?all=${all}&page=${page}`, company, v => decodeDelegationPage(v,company,page),signal)
export const fetchDelegationPeople = (company: string, kind: string, q: string, signal?: AbortSignal) =>
  readJson(`/work/delegations/people?${new URLSearchParams({kind,q})}`, company, v => decodePeople(v,company,kind), signal)
export const isCreateDelegation = (v: unknown): v is CreateDelegation => record(v)
  && hasExactKeys(v,['kind','delegateUserId','endsAtUtc','reason','delegatorUserId','startsAtUtc']) && text(v.kind)
  && isBoundUuid(v.delegateUserId) && instant(v.startsAtUtc) && instant(v.endsAtUtc) && text(v.reason)
  && (v.delegatorUserId === null || isBoundUuid(v.delegatorUserId))
export const isRevokeDelegation = (v: unknown): v is RevokeDelegation => record(v)
  && hasExactKeys(v,['expectedVersion','reason']) && v.expectedVersion === '1' && text(v.reason)
async function command(company: string, path: string, key: string, body: object, status: number,
  accepts: (row: Delegation) => boolean): Promise<CommandOutcome<Delegation>> {
  const answer = await postWorkCommand(path,company,key,body,status,v => {
    const row=decodeDelegation(v,company); return row !== null && accepts(row) ? row : null
  })
  return answer.type !== 'refused' ? answer : { ...answer, companyId: null, groupCode: null }
}
export const createDelegation = (company: string, body: CreateDelegation, key: string) => command(company,'/work/delegations',key,body,201,
  row => row.kind === body.kind && same(row.delegateUserId,body.delegateUserId) && row.version === '1'
    && Date.parse(row.startsAtUtc) === Date.parse(body.startsAtUtc) && Date.parse(row.endsAtUtc) === Date.parse(body.endsAtUtc)
    && row.reason === body.reason && (body.delegatorUserId === null || same(row.delegatorUserId,body.delegatorUserId)))
export const revokeDelegation = (company: string, id: string, body: RevokeDelegation, key: string) => command(company,
  `/work/delegations/${encodeURIComponent(id)}/revocation`,key,body,200,row => same(row.id,id) && row.version === '2' && row.revocationReason === body.reason)
