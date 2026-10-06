import { useQuery } from '@tanstack/react-query'
import { fetchDraftLinePricing } from './api'
import type { DraftDetail } from './contract'
import { contextKey, contextKeyString, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

/**
 * AT-018 / AT-021: the line pricing of THIS draft version (reference, adjustments, the server's net), read only when the
 * detail discloses prices to the caller (a withheld line means the price read is not held: nothing is asked). Keyed by
 * the draft version, so a recorded revision reads the new pricing.
 */
export function useDraftLinePricing(shell: ShellState, detail: DraftDetail, companyId: string | null) {
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const disclosed = detail.lines.length > 0 && detail.lines.every((line) => line.priceDisclosure === 'Visible')
  return useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-line-pricing', detail.draft.id, detail.draft.version],
    enabled: namespace !== null && companyId !== null && disclosed,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftLinePricing(detail.draft.id, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
}
