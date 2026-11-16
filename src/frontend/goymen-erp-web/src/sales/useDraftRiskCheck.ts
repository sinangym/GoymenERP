/**
 * MOD-01 / API-06: the S-04 risk read (`GET …/drafts/{id}/risk-check`) as one query shared by the risk card and the risk
 * exception section. Both observers use this key, so a single open reads the risk once; it is read afresh on every open
 * (`gcTime` 0, `staleTime` 0) and keyed by the draft version.
 */
import { useQuery } from '@tanstack/react-query'
import { fetchDraftRiskCheck } from './api'
import type { DraftDetail } from './contract'
import type { RiskCheck } from './riskCheck'
import type { ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

export function useDraftRiskCheck(input: {
  readonly detail: DraftDetail
  readonly namespace: string | null
  readonly routeEntryGeneration: number
  readonly companyId: string | null
  readonly enabled: boolean
}) {
  const { detail, namespace, routeEntryGeneration, companyId, enabled } = input
  const boundary = useAuthBoundary()
  return useQuery<ViewOutcome<RiskCheck>>({
    queryKey: [namespace ?? 'no-context', routeEntryGeneration, 'sales-draft-risk-check', detail.draft.id, detail.draft.version],
    enabled: enabled && companyId !== null,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftRiskCheck(detail, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
}
