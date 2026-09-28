/**
 * AT-002 — the search route's gate: the shared `CapabilityRoute` entry gate on the first searchable owner list the actor
 * holds (the search has no capability of its own). A direct URL, a refresh or Back re-verifies the snapshot exactly as
 * every owner route does; an actor holding none gets the neutral not-found block and reads nothing.
 */

import type { ReactNode } from 'react'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { SEARCHABLE_LIST_CAPABILITIES } from './searchCapabilities'

export function SearchRoute({ children }: { readonly children: ReactNode }) {
  const shell = useShellState()
  const held = SEARCHABLE_LIST_CAPABILITIES.find((capability) => hasCapability(shell, capability)) ?? SEARCHABLE_LIST_CAPABILITIES[0]
  return <CapabilityRoute capability={held}>{children}</CapabilityRoute>
}
