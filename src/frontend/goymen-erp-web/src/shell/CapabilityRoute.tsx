/**
 * Mounts a feature only when the server granted its capability.
 *
 * ERP-000K §10.2: this is NOT authorization. A direct URL, a refresh, a new tab and
 * browser back/forward all land here, and the backend still re-authorizes every request.
 * What this guarantees is the shell-side half: an ungranted or unresolved capability
 * mounts nothing, and it does so WITHOUT disclosing whether the feature or any record
 * exists (§10.2 non-disclosure) — the same neutral block either way.
 */

import type { ReactNode } from 'react'
import { hasCapability } from './shellState'
import { useShellState } from './shellContext'
import { StateBlock } from './StateBlock'
import type { CapabilityId } from './capabilities'

export function CapabilityRoute({
  capability,
  children,
}: {
  readonly capability: CapabilityId
  readonly children: ReactNode
}) {
  const state = useShellState()
  if (!hasCapability(state, capability)) {
    return <StateBlock state={{ kind: 'not-found' }} subject="sayfa" />
  }
  return <>{children}</>
}
