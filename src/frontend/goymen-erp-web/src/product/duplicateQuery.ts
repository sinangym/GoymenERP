/**
 * The query side of the §10.2 duplicate warning (see ./DuplicateCandidates.tsx for what it shows and why).
 * The read is fresh on every mount (`staleTime: 0`), so closing and reopening a card shows what the server
 * holds now, never a remembered answer.
 */

import { useQuery } from '@tanstack/react-query'
import { fetchDuplicateCandidates } from './api'
import type { ProductDuplicateCandidates } from './contract'
import type { ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

export type DuplicateOutcome = ViewOutcome<ProductDuplicateCandidates>

export function duplicateQueryKey(
  namespace: string | null,
  entryGeneration: number,
  name: string,
  excludeProductId: string | null,
) {
  return [namespace ?? 'no-context', entryGeneration, 'product-duplicates', name, excludeProductId ?? ''] as const
}

/** The ids the server listed, in its order; null when there is no successful answer to compare. */
export function duplicateIds(outcome: DuplicateOutcome | undefined): string | null {
  if (outcome === undefined || outcome.type !== 'ok-single') return null
  return `${outcome.data.exactName.map((item) => item.id).join(',')}|${outcome.data.hasMore ? 'more' : ''}`
}

export function useDuplicateCandidates({
  name,
  excludeProductId,
  namespace,
  entryGeneration,
  origin,
  enabled,
}: {
  /** Already trimmed by the caller; an empty name is not checked. */
  readonly name: string
  readonly excludeProductId: string | null
  readonly namespace: string | null
  readonly entryGeneration: number
  readonly origin: string | null
  readonly enabled: boolean
}) {
  const boundary = useAuthBoundary()
  return useQuery({
    queryKey: duplicateQueryKey(namespace, entryGeneration, name, excludeProductId),
    enabled: enabled && namespace !== null && origin !== null && name.length > 0,
    staleTime: 0,
    refetchOnMount: 'always',
    queryFn: async ({ signal }): Promise<DuplicateOutcome> => {
      const snapshot = boundary.capture()
      const outcome = await fetchDuplicateCandidates(name, excludeProductId, { companyId: origin as string }, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
}
