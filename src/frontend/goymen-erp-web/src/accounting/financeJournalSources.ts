import { isDateOnly } from '../finance/dateOnly'
import type { ViewOutcome } from '../shell/uiState'
import { ACCOUNTING_BASE, devHeaders } from './api'

export type FinanceJournalSource = {
  eventId: string; companyId: string; sourceId: string; sourceKind: string; sourceVersion: string
  currencyId: string; amount: string; ownerProfileId: string; ownerProfileVersion: string
  occurredOn: string; recordedAtUtc: string; receivedAtUtc: string; status: 'Received'
  mappingStatus: 'BindingAvailable' | 'BindingUnavailable'; preparationVersion: string | null
  cashExchange?: { transferId: string; bookValueId: string | null; sourceCurrencyId: string; sourceAmount: string
    targetCurrencyId: string; targetAmount: string; sourceBookValueTl: string | null
    postingState: 'SourceBookValuePending' | 'TargetBookValueUnavailable' | 'ApprovedSourceBookValue' }
}
export type FinanceJournalSources = { companyId: string; items: FinanceJournalSource[] }
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const HASH = /^[0-9a-f]{64}$/i
const UTC = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/
const fields = ['eventId', 'companyId', 'sourceId', 'sourceKind', 'sourceVersion', 'currencyId', 'amount',
  'ownerProfileId', 'ownerProfileVersion', 'occurredOn', 'recordedAtUtc', 'receivedAtUtc', 'status', 'mappingStatus', 'preparationVersion']
function record(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }
function text(value: unknown, maximum = 128): value is string { return typeof value === 'string' && value.trim() === value && value.length > 0 && value.length <= maximum }
function guid(value: unknown): value is string { return typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000' }
function utc(value: unknown): value is string { return typeof value === 'string' && UTC.test(value) && Number.isFinite(Date.parse(value)) }

export function decodeFinanceJournalSources(value: unknown, company: string): FinanceJournalSources | null {
  if (!record(value) || Object.keys(value).sort().join(',') !== 'companyId,items' || value.companyId !== company || !guid(company) || !Array.isArray(value.items)) return null
  const events = new Set<string>(), sources = new Set<string>()
  for (const item of value.items) {
    if (!record(item) || Object.keys(item).sort().join(',') !== [...fields, ...('cashExchange' in item ? ['cashExchange'] : [])].sort().join(',')
      || !guid(item.eventId) || item.companyId !== company || !guid(item.sourceId) || !guid(item.currencyId)
      || !['finance.expense-payment.recorded.v1', 'finance.cash-transfer.recorded.v1', 'finance.cash-exchange.recorded.v1', 'finance.cash-exchange.book-value-approved.v1'].includes(item.sourceKind as string)
      || !text(item.sourceVersion, 19) || !/^[1-9][0-9]*$/.test(item.sourceVersion)
      || !text(item.amount) || !/^[0-9]+(?:\.[0-9]+)?$/.test(item.amount) || !/[1-9]/.test(item.amount)
      || !text(item.ownerProfileId) || !text(item.ownerProfileVersion) || typeof item.occurredOn !== 'string' || !isDateOnly(item.occurredOn)
      || !utc(item.recordedAtUtc) || !utc(item.receivedAtUtc) || item.status !== 'Received'
      || (item.mappingStatus !== 'BindingAvailable' && item.mappingStatus !== 'BindingUnavailable')
      || (item.mappingStatus === 'BindingAvailable' ? typeof item.preparationVersion !== 'string' || !HASH.test(item.preparationVersion) : item.preparationVersion !== null)) return null
    const exchangeKind = (item.sourceKind as string).startsWith('finance.cash-exchange.')
    const exchange = item.cashExchange
    if (exchangeKind) {
      if (!record(exchange) || Object.keys(exchange).sort().join(',') !== ['transferId', 'bookValueId', 'sourceCurrencyId', 'sourceAmount', 'targetCurrencyId', 'targetAmount', 'sourceBookValueTl', 'postingState'].sort().join(',')
        || !guid(exchange.transferId) || !guid(exchange.sourceCurrencyId) || !guid(exchange.targetCurrencyId)
        || exchange.sourceCurrencyId === exchange.targetCurrencyId || !positive(exchange.sourceAmount) || !positive(exchange.targetAmount)) return null
      if (item.sourceKind === 'finance.cash-exchange.recorded.v1') {
        if (item.sourceVersion !== '1' || exchange.bookValueId !== null || exchange.sourceBookValueTl !== null || exchange.postingState !== 'SourceBookValuePending'
          || exchange.transferId !== item.sourceId || exchange.sourceCurrencyId !== item.currencyId || exchange.sourceAmount !== item.amount
          || item.mappingStatus !== 'BindingUnavailable') return null
      } else if (item.sourceVersion !== '2' || !guid(exchange.bookValueId) || exchange.bookValueId !== item.sourceId || !positive(exchange.sourceBookValueTl)
        || exchange.sourceBookValueTl !== item.amount || !['TargetBookValueUnavailable', 'ApprovedSourceBookValue'].includes(exchange.postingState as string)
        || (exchange.postingState === 'ApprovedSourceBookValue' ? exchange.targetCurrencyId !== item.currencyId
          : exchange.targetCurrencyId === item.currencyId || item.mappingStatus !== 'BindingUnavailable')) return null
    } else if (exchange !== undefined) return null
    const identity = `${item.sourceKind}:${item.sourceId.toLowerCase()}:${item.sourceVersion}`
    if (events.has(item.eventId.toLowerCase()) || sources.has(identity)) return null
    events.add(item.eventId.toLowerCase()); sources.add(identity)
  }
  return value as FinanceJournalSources
}

function positive(value: unknown): value is string { return text(value) && /^[0-9]+(?:\.[0-9]+)?$/.test(value) && /[1-9]/.test(value) }

export async function fetchFinanceJournalSources(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<FinanceJournalSources>> {
  let response: Response
  try {
    response = await fetch(`${ACCOUNTING_BASE}/finance-journal-sources`, { headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' }, signal })
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  let body: unknown = null
  try { body = await response.json() } catch { if (signal?.aborted) return { type: 'fence-stale' } }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decodeFinanceJournalSources(body, companyId)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}
