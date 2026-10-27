/**
 * The S-01 entry gate: the shared `CapabilityRoute` (fresh capability verification per route entry, the neutral
 * not-found block when ungranted) over the pool's capability list for the screen: the id the backend publishes for it
 * (./poolCapabilities.ts, Sales shell 1.8). An empty list would open nothing.
 */

import { useContext, type ReactNode } from 'react'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { SalesPoolCapabilityContext, type SalesPoolCapabilityKey } from './poolCapabilities'

export function SalesPoolRoute({ capability, children }: { readonly capability: SalesPoolCapabilityKey; readonly children: ReactNode }) {
  const ids = useContext(SalesPoolCapabilityContext)[capability]
  return <CapabilityRoute capability={ids}>{children}</CapabilityRoute>
}
