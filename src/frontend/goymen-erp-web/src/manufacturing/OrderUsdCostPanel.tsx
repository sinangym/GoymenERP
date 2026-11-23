import { useQuery } from '@tanstack/react-query'
import { MANUFACTURING_BASE, devHeaders } from './api'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './OrderDetailPage.module.css'
import { USD_COST_TEXT } from './orderUsdCostText'
interface UsdLine { readonly outputLabelId: string; readonly outputClass: string; readonly kilograms: string; readonly unitCostPerKgUsd: string | null; readonly amountUsd: string | null; readonly separateScrapExpenseUsd: string | null; readonly remainderUsd: string }
interface UsdRate { readonly weighingDay: string; readonly bulletinDay: string; readonly tryPerUsd: string; readonly recordId: string; readonly recordVersion: string; readonly source: string }
interface UsdCost { readonly orderId: string; readonly orderVersion: number; readonly status: string; readonly costPerKgUsd: string | null; readonly inputTotalUsd: string | null; readonly distributedTotalUsd: string | null; readonly lines: readonly UsdLine[]; readonly scrapRates: readonly UsdRate[]; readonly reasonCode: string | null }
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const amount = (value: unknown): value is string => typeof value === 'string' && /^(0|[1-9]\d*)(\.\d+)?$/.test(value)
const optionalAmount = (value: unknown) => value === null || amount(value)
const guid = (value: unknown) => typeof value === 'string' && /^(?!00000000-0000-0000-0000-000000000000)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
const day = (value: unknown): boolean => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)
  && Number.isFinite(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value
// Key sets mirror ManufacturingProductionUsdCostViewV1 / ManufacturingUsdCostLineViewV1 / ManufacturingUsdCostRateViewV1 one-to-one.
const exact = (value: Record<string, unknown>, names: readonly string[]) => Object.keys(value).length === names.length && names.every(key => Object.hasOwn(value, key))
const COST_KEYS = ['orderId', 'orderVersion', 'status', 'costPerKgUsd', 'inputTotalUsd', 'distributedTotalUsd', 'lines', 'scrapRates', 'reasonCode'] as const
const LINE_KEYS = ['outputLabelId', 'outputClass', 'kilograms', 'unitCostPerKgUsd', 'amountUsd', 'separateScrapExpenseUsd', 'remainderUsd'] as const
const RATE_KEYS = ['weighingDay', 'bulletinDay', 'tryPerUsd', 'recordId', 'recordVersion', 'source'] as const
export function decodeUsdCost(value: unknown, orderId: string): UsdCost | null {
  if (!object(value) || !exact(value, COST_KEYS) || value.orderId !== orderId || !Number.isSafeInteger(value.orderVersion) || (value.orderVersion as number) < 1
    || !['available', 'pending', 'unreadable', 'unavailable'].includes(String(value.status)) || !Array.isArray(value.lines) || !Array.isArray(value.scrapRates)
    || ![value.costPerKgUsd, value.inputTotalUsd, value.distributedTotalUsd].every(optionalAmount)) return null
  if (value.status === 'available') {
    if (![value.costPerKgUsd, value.inputTotalUsd, value.distributedTotalUsd].every(amount) || value.reasonCode !== null || value.lines.length === 0
      || value.inputTotalUsd !== value.distributedTotalUsd) return null
  } else if ([value.costPerKgUsd, value.inputTotalUsd, value.distributedTotalUsd].some(v => v !== null)
    || value.lines.length !== 0 || value.scrapRates.length !== 0 || typeof value.reasonCode !== 'string' || !value.reasonCode.trim()) return null
  const ids = new Set<string>()
  for (const line of value.lines) {
    if (!object(line) || !exact(line, LINE_KEYS) || !guid(line.outputLabelId) || ids.has(String(line.outputLabelId)) || typeof line.outputClass !== 'string'
      || !amount(line.kilograms) || ![line.unitCostPerKgUsd, line.amountUsd, line.separateScrapExpenseUsd].every(optionalAmount)
      || typeof line.remainderUsd !== 'string' || !/^-?(0|[1-9]\d*)(\.\d+)?$/.test(line.remainderUsd)) return null
    ids.add(String(line.outputLabelId))
  }
  for (const rate of value.scrapRates) {
    if (!object(rate) || !exact(rate, RATE_KEYS) || !guid(rate.recordId) || typeof rate.recordVersion !== 'string' || !rate.recordVersion.trim() || rate.source !== 'Tcmb'
      || !amount(rate.tryPerUsd) || /^0(?:\.0+)?$/.test(rate.tryPerUsd) || !day(rate.weighingDay) || !day(rate.bulletinDay)) return null
  }
  return value as unknown as UsdCost
}
async function fetchUsdCost(company: string, order: string, signal: AbortSignal): Promise<ViewOutcome<UsdCost>> {
  let response: Response
  try { response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(order)}/production-cost-usd`, { headers: { ...devHeaders(), 'X-Company-Id': company }, cache: 'no-store', signal }) }
  catch { return signal.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  let body: unknown; try { body = await response.json() } catch { body = null }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decodeUsdCost(body, order); return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}
export function OrderUsdCostPanel({ namespace, routeEntryGeneration, companyId, orderId, orderVersion }: {
  readonly namespace: string; readonly routeEntryGeneration: number; readonly companyId: string; readonly orderId: string; readonly orderVersion: number
}) {
  const boundary = useAuthBoundary()
  const query = useQuery({ queryKey: [namespace, routeEntryGeneration, 'manufacturing-usd-cost', orderId, orderVersion], queryFn: async ({ signal }) => {
    const snapshot = boundary.capture(); const result = await fetchUsdCost(companyId, orderId, signal)
    if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
    return result
  } })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<UsdCost>
  return <section className={styles.section} aria-label={USD_COST_TEXT.heading}><h2>{USD_COST_TEXT.heading}</h2><p>{USD_COST_TEXT.help}</p>
    {state.kind !== 'ready' ? <StateBlock state={state} subject={USD_COST_TEXT.subject} onRetry={() => void query.refetch()} />
      : state.data.status !== 'available' ? <p role="status">{USD_COST_TEXT.missing}</p> : <>
        <dl><dt>{USD_COST_TEXT.unit}</dt><dd>{state.data.costPerKgUsd}</dd><dt>{USD_COST_TEXT.input}</dt><dd>{state.data.inputTotalUsd}</dd><dt>{USD_COST_TEXT.total}</dt><dd>{state.data.distributedTotalUsd}</dd></dl>
        <div className={styles.tableWrap}><table className={styles.table}><caption>{USD_COST_TEXT.lines}</caption><thead><tr><th scope="col">{USD_COST_TEXT.output}</th><th scope="col">{USD_COST_TEXT.mass}</th><th scope="col">{USD_COST_TEXT.unit}</th><th scope="col">{USD_COST_TEXT.amount}</th></tr></thead>
          <tbody>{state.data.lines.map(row => <tr key={row.outputLabelId}><th scope="row">{row.outputLabelId}</th><td>{row.kilograms}</td><td>{row.unitCostPerKgUsd ?? USD_COST_TEXT.missing}</td><td>{row.amountUsd ?? USD_COST_TEXT.missing}</td></tr>)}</tbody></table></div>
        {state.data.scrapRates.length === 0 ? null : <div><h3>{USD_COST_TEXT.rates}</h3><ul>{state.data.scrapRates.map((rate, index) => <li key={`${rate.recordId}:${index}`}>{USD_COST_TEXT.day}: {rate.weighingDay} · {USD_COST_TEXT.bulletin}: {rate.bulletinDay} · {USD_COST_TEXT.rate}: {rate.tryPerUsd}</li>)}</ul></div>}
      </>}
  </section>
}
