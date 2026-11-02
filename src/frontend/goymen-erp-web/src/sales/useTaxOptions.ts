import { useQuery } from '@tanstack/react-query'
import { fetchTaxOptions } from './api'
import { contextKey, contextKeyString, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

/**
 * MOD-03 → Sales (SAT-D158): the Settings tax lists (buyer situations, exemption codes, item types) in force on the order's
 * business date `on`, read through Sales under the draft read. Nothing is asked without a bound company or a date. An empty list
 * is data with its reason (SAT-D148); the forms then offer nothing and say why — never a guessed option.
 */
export function useTaxOptions(shell: ShellState, companyId: string | null, on: string | null) {
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  return useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-tax-options', on],
    enabled: namespace !== null && companyId !== null && on !== null && on !== '',
    retry: false,
    refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchTaxOptions(on as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
}
