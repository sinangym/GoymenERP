/**
 * Shell reducer context + hooks. Kept separate from ShellProvider.tsx so the
 * provider file only exports a component.
 */

import { createContext, useContext, type Dispatch } from 'react'
import { initialShellState, type ShellEvent, type ShellState } from './shellState'

export const ShellStateContext = createContext<ShellState>(initialShellState)
export const ShellDispatchContext = createContext<Dispatch<ShellEvent>>(() => {
  throw new Error('ShellDispatchContext used outside ShellProvider')
})

export function useShellState(): ShellState {
  return useContext(ShellStateContext)
}

export function useShellDispatch(): Dispatch<ShellEvent> {
  return useContext(ShellDispatchContext)
}
