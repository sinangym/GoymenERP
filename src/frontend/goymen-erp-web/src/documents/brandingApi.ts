import { DOCUMENTS_API_BASE, documentHeaders } from './api'
import { TEMPLATE_CATALOG, decodeBrandingCurrent, decodeBrandingMutation, decodeCompanyBranding, decodeTemplateCurrent,
  decodeTemplateDraft, decodeTemplatePublication, type BrandingCurrent, type BrandingMutation, type CompanyBranding,
  type PrepareTemplate, type PublishTemplate, type SetBranding, type TemplateCurrent, type TemplateDraft,
  type TemplateModule, type TemplatePublication } from './brandingContract'
import type { ViewOutcome } from '../shell/uiState'

const path = '/document-branding'
export type BrandingCommand = { readonly kind: 'branding'; readonly payload: SetBranding }
  | { readonly kind: 'prepare'; readonly payload: PrepareTemplate }
  | { readonly kind: 'publish'; readonly payload: PublishTemplate }
export type BrandingWritten = CompanyBranding | TemplateDraft | TemplatePublication
export type BrandingWriteOutcome = { readonly type: 'applied'; readonly result: BrandingMutation<BrandingWritten> }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }

async function read<T>(suffix: string, company: string, decode: (v: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(`${DOCUMENTS_API_BASE}${path}${suffix}`, { headers: documentHeaders(company), signal })
    if (signal?.aborted) return { type: 'fence-stale' }
    if (response.status !== 200) return { type: 'http', status: response.status }
    let raw: unknown
    try { raw = await response.json() } catch { return { type: 'malformed' } }
    const data = decode(raw)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export const fetchBranding = (company: string, signal?: AbortSignal): Promise<ViewOutcome<BrandingCurrent>> =>
  read('/current', company, (v) => decodeBrandingCurrent(v, company), signal)
export const fetchTemplate = (company: string, module: TemplateModule, signal?: AbortSignal): Promise<ViewOutcome<TemplateCurrent>> =>
  read(`/templates/${TEMPLATE_CATALOG[module].slug}`, company, (v) => decodeTemplateCurrent(v, company, module), signal)

export async function writeBranding(company: string, key: string, command: BrandingCommand): Promise<BrandingWriteOutcome> {
  const suffix = command.kind === 'branding' ? '/current'
    : `/templates/${TEMPLATE_CATALOG[command.payload.module].slug}/${command.kind === 'prepare' ? 'drafts' : 'publications'}`
  try {
    const response = await fetch(`${DOCUMENTS_API_BASE}${path}${suffix}`, {
      method: command.kind === 'branding' ? 'PUT' : 'POST',
      headers: { ...documentHeaders(company), 'Content-Type': 'application/json', 'Idempotency-Key': key },
      body: JSON.stringify(command.payload),
    })
    if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
    if (response.status !== 200) {
      if (![400, 401, 403, 404, 409, 415, 422].includes(response.status)) return { type: 'unknown', reason: 'malformed-response' }
      let raw: unknown = null
      try { raw = await response.json() } catch { /* code-less authority refusals are still definite */ }
      const code = raw !== null && typeof raw === 'object' && 'code' in raw && typeof raw.code === 'string' ? raw.code : null
      return { type: 'refused', status: response.status, code }
    }
    let raw: unknown
    try { raw = await response.json() } catch { return { type: 'unknown', reason: 'malformed-response' } }
    const result: BrandingMutation<BrandingWritten> | null = command.kind === 'branding'
      ? decodeBrandingMutation(raw, 'branding', (v) => decodeCompanyBranding(v, company))
      : command.kind === 'prepare' ? decodeBrandingMutation(raw, 'draft', (v) => decodeTemplateDraft(v, company, command.payload.module))
        : decodeBrandingMutation(raw, 'publication', (v) => decodeTemplatePublication(v, company, command.payload.module))
    const replay = response.headers.get('Idempotency-Replayed'), original = response.headers.get('Idempotency-Original-Correlation-Id')
    if (result === null || (result.isReplay ? replay !== 'true' || original !== result.originalCorrelationId : replay !== null || original !== null))
      return { type: 'unknown', reason: 'malformed-response' }
    return { type: 'applied', result }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
