import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchReportCatalog } from './reportingApi'

/** The active company's catalogue as the server filters it; shared by the centre and the report screens. */
export function useReportCatalog() {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'reporting-catalog'],
    enabled: namespace !== null && company !== null && !capabilityFailClosed(shell),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchReportCatalog(company!, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    },
  })
  return { shell, company, namespace, query, catalog: query.data?.type === 'ok-single' ? query.data.data : null }
}
