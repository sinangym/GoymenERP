/**
 * W1-B — remembers a screen when the actor lands on it, from the sidebar, the palette, a list link or Back alike. Only a
 * pathname that IS one of the actor's granted screen/create commands is remembered; a record page, a search URL or an
 * ungranted path is not.
 */

import { useEffect } from 'react'
import type { ShellState } from '../shellState'
import { grantedCommands } from './paletteCommands'
import { PALETTE_CREATE_LABEL } from './paletteText'
import { recentKey, rememberRecent, type StorageAccessor } from './recentScreens'

export function paletteRecentKey(state: ShellState): string | null {
  return recentKey(state.sessionEpoch, state.activeContext?.userId ?? null, state.activeContext?.companyId ?? null)
}

export function useRecentScreenTracker(state: ShellState, pathname: string, storage: StorageAccessor): void {
  const key = paletteRecentKey(state)
  const match = grantedCommands(state, PALETTE_CREATE_LABEL).find((command) => command.path === pathname)?.id ?? null
  useEffect(() => {
    if (match !== null) rememberRecent(storage, key, match)
  }, [storage, key, match, pathname])
}
