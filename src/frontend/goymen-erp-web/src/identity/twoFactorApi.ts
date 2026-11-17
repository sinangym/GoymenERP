import { API_BASE } from './api'
import type { ViewOutcome } from '../shell/uiState'
import type { CommandOutcome } from '../settings/discountLimitApi'
import { accessGrantVersion } from './accessGrantContract'
export interface TwoFactorState { readonly credentialVersion: string; readonly enabled: boolean; readonly recoveryCodesRemaining: number }
export interface TwoFactorEnrollment { readonly credentialVersion: string; readonly secretKey: string; readonly authenticatorUri: string; readonly recoveryCodes: readonly string[]; readonly enrollmentToken: string }
export interface TwoFactorReceipt { readonly credentialVersion: string; readonly enabled: boolean; readonly recoveryCodes: readonly string[] | null }
export interface TwoFactorBody { readonly expectedVersion: string; readonly currentPassword: string; readonly code: string | null; readonly enrollmentToken: string | null }
const object = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null && !Array.isArray(raw)
const codes = (raw: unknown): raw is readonly string[] => Array.isArray(raw) && raw.length === 10 && raw.every((code) => typeof code === 'string' && /^[A-F0-9]{4}(?:-[A-F0-9]{4}){7}$/.test(code)) && new Set(raw).size === 10
export function decodeTwoFactorState(raw: unknown): TwoFactorState | null {
  return object(raw) && Object.keys(raw).sort().join(',') === 'credentialVersion,enabled,recoveryCodesRemaining'
    && accessGrantVersion(raw.credentialVersion) && typeof raw.enabled === 'boolean' && Number.isInteger(raw.recoveryCodesRemaining)
    && (raw.recoveryCodesRemaining as number) >= 0 && (raw.recoveryCodesRemaining as number) <= 10 && (raw.enabled || raw.recoveryCodesRemaining === 0) ? raw as unknown as TwoFactorState : null
}
export function decodeTwoFactorEnrollment(raw: unknown): TwoFactorEnrollment | null {
  if (!object(raw) || Object.keys(raw).sort().join(',') !== 'authenticatorUri,credentialVersion,enrollmentToken,recoveryCodes,secretKey'
    || !accessGrantVersion(raw.credentialVersion) || typeof raw.secretKey !== 'string' || !/^[A-Z2-7]{32}$/.test(raw.secretKey)
    || typeof raw.authenticatorUri !== 'string' || typeof raw.enrollmentToken !== 'string' || raw.enrollmentToken.length < 20 || !codes(raw.recoveryCodes)) return null
  let uri: URL; try { uri = new URL(raw.authenticatorUri) } catch { return null }
  if (uri.protocol !== 'otpauth:' || uri.hostname !== 'totp' || uri.searchParams.get('secret') !== raw.secretKey
    || uri.searchParams.get('digits') !== '6' || uri.searchParams.get('period') !== '30' || uri.searchParams.get('algorithm') !== 'SHA1') return null
  return raw as unknown as TwoFactorEnrollment
}
export function decodeTwoFactorReceipt(raw: unknown): TwoFactorReceipt | null {
  return object(raw) && Object.keys(raw).sort().join(',') === 'credentialVersion,enabled,recoveryCodes' && accessGrantVersion(raw.credentialVersion)
    && typeof raw.enabled === 'boolean' && (raw.recoveryCodes === null || raw.enabled && codes(raw.recoveryCodes)) ? raw as unknown as TwoFactorReceipt : null
}
export async function readTwoFactor(company: string, signal?: AbortSignal): Promise<ViewOutcome<TwoFactorState>> {
  try {
    const response = await fetch(`${API_BASE}/identity/session/two-factor`, { headers: { 'X-Company-Id': company }, credentials: 'same-origin', cache: 'no-store', signal })
    let raw: unknown = null; try { raw = await response.json() } catch { /* decode below */ }
    if (response.status !== 200) return { type: 'http', status: response.status, body: raw }
    const data = decodeTwoFactorState(raw); return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export async function prepareTwoFactor(company: string, body: TwoFactorBody): Promise<ViewOutcome<TwoFactorEnrollment>> {
  try {
    const response = await fetch(`${API_BASE}/identity/session/two-factor/enrollment`, { method: 'POST', headers: { 'X-Company-Id': company, 'Content-Type': 'application/json' }, credentials: 'same-origin', cache: 'no-store', body: JSON.stringify(body) })
    let raw: unknown = null; try { raw = await response.json() } catch { /* decode below */ }
    if (response.status !== 200) return { type: 'http', status: response.status, body: raw }
    const data = decodeTwoFactorEnrollment(raw); return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export async function changeTwoFactor(company: string, action: string, body: TwoFactorBody, key: string): Promise<CommandOutcome<TwoFactorReceipt>> {
  try {
    const response = await fetch(`${API_BASE}/identity/session/two-factor/${action}`, { method: 'POST', headers: { 'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': key }, credentials: 'same-origin', cache: 'no-store', body: JSON.stringify(body) })
    let raw: unknown = null; try { raw = await response.json() } catch { /* decode below */ }
    const replayed = response.headers.get('Idempotency-Replayed')
    if (response.status === 200) {
      const data = decodeTwoFactorReceipt(raw)
      if (data === null || replayed !== null && replayed !== 'true' && replayed !== 'false'
        || replayed === 'true' && !response.headers.get('Idempotency-Original-Correlation-Id')?.trim()) return { type: 'unknown', reason: 'unexpected' }
      return { type: 'applied', data, replayed: replayed === 'true' }
    }
    if (response.status >= 500 || response.status < 400) return { type: 'unknown', reason: 'server' }
    return { type: 'refused', status: response.status, code: object(raw) && typeof raw.code === 'string' ? raw.code : null, companyId: null, groupCode: null, replayed: false }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
