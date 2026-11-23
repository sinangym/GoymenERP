/**
 * FNS-D121 transport: `GET/POST /api/v1/counterparties/{partyId}/ibans` and `POST …/ibans/{id}/deactivate`. A side read of the
 * Cari card (it sends the client-contract declaration and the selected company). Commands carry one Idempotency-Key per
 * attempt; an answer that cannot be decoded, or whose replay header disagrees, is `unknown` and keeps the key.
 */
import { API_BASE, contractRequestHeader } from './clientContract'
import { decodePartyIbanCommandResult, decodePartyIbanList, type PartyIbanCommandResult, type PartyIbanList } from './partyIban'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'
const devHeaders = (): Record<string, string> => DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
const path = (partyId: string) => `${API_BASE}/counterparties/${encodeURIComponent(partyId)}/ibans`

export type PartyIbanRead =
  | { readonly type: 'ok'; readonly data: PartyIbanList }
  | { readonly type: 'http'; readonly status: number }
  | { readonly type: 'malformed' } | { readonly type: 'offline' } | { readonly type: 'network-error' } | { readonly type: 'aborted' }

export async function fetchPartyIbans(partyId: string, companyId: string, signal?: AbortSignal): Promise<PartyIbanRead> {
  let response: Response
  try {
    response = await fetch(path(partyId), { headers: { ...devHeaders(), ...contractRequestHeader(), 'X-Company-Id': companyId },
      cache: 'no-store', signal })
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  if (response.status !== 200) return { type: 'http', status: response.status }
  let body: unknown
  try { body = await response.json() } catch { return { type: 'malformed' } }
  const data = decodePartyIbanList(body, companyId, partyId)
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

export type PartyIbanReply =
  | { readonly kind: 'answered'; readonly result: PartyIbanCommandResult }
  | { readonly kind: 'refused'; readonly status: number }
  | { readonly kind: 'unknown' }

export interface AddPartyIbanRequest { readonly ibanId: string; readonly iban: string; readonly bankName: string; readonly description: string | null }
export interface DeactivatePartyIbanRequest { readonly expectedVersion: string; readonly reason: string }

async function send(url: string, companyId: string, partyId: string, key: string, body: unknown): Promise<PartyIbanReply> {
  try {
    const response = await fetch(url, { method: 'POST', cache: 'no-store', body: JSON.stringify(body), headers: {
      ...devHeaders(), ...contractRequestHeader(), 'X-Company-Id': companyId, 'Content-Type': 'application/json', 'Idempotency-Key': key } })
    if ([400, 401, 403, 404, 415].includes(response.status)) return { kind: 'refused', status: response.status }
    if (![200, 409, 422].includes(response.status)) return { kind: 'unknown' }
    const result = decodePartyIbanCommandResult(await response.json(), companyId, partyId)
    if (result === null || result.replayed !== (response.headers.get('Idempotency-Replayed') === 'true')
      || (response.status === 200) !== (result.outcome === 'Succeeded')
      || (response.status === 422) !== (result.outcome === 'ValidationFailed')) return { kind: 'unknown' }
    return { kind: 'answered', result }
  } catch { return { kind: 'unknown' } }
}

export const addPartyIban = (partyId: string, companyId: string, key: string, request: AddPartyIbanRequest) =>
  send(path(partyId), companyId, partyId, key, request)

export const deactivatePartyIban = (partyId: string, companyId: string, ibanId: string, key: string, request: DeactivatePartyIbanRequest) =>
  send(`${path(partyId)}/${encodeURIComponent(ibanId)}/deactivate`, companyId, partyId, key, request)
