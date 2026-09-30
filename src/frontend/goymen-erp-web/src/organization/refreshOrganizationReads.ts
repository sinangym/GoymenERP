import type { QueryClient } from '@tanstack/react-query'

/**
 * After an accepted master-data command: drop every read bound to THIS context namespace, so the lists behind the
 * card and any picker another screen keeps (a location or work-centre selector) are re-read on their next view
 * instead of painting the pre-change rows from the 30 s cache. Nothing outside the namespace is touched, and nothing
 * is predicted: the fresh values come from the server.
 */
export async function refreshOrganizationReads(queryClient: QueryClient, namespace: string | null): Promise<void> {
  if (namespace === null) return
  await queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] === namespace })
}
