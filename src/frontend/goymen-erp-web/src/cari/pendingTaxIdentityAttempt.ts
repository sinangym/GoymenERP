import type { SecuritySnapshot } from '../shell/authBoundary'
import type { ShellState } from '../shell/shellState'
import type { CommandContext, CommandOutcome } from './commandApi'
import type { AttemptState } from './commandAttempt'
import type { ContractContext } from './clientContract'

export type TaxIdentitySender = (context: CommandContext) => Promise<CommandOutcome<null>>

export interface FrozenTaxIdentityAttempt {
  readonly operation: 'enter' | 'request' | `decide:${string}`
  readonly fingerprint: string
  readonly key: string
  readonly binding: string
  readonly companyId: string
  readonly contract: ContractContext
  readonly send: TaxIdentitySender
  readonly okText: string
}

export type TaxIdentityAttemptState = AttemptState<FrozenTaxIdentityAttempt, null>

export interface TaxIdentityAttemptBinding {
  readonly origin: string
  readonly actorId: string
  readonly sessionEpoch: number
  readonly companyId: string
  readonly partyId: string
  readonly key: string
}

function binding(
  origin: string | null,
  actorId: string | null,
  sessionEpoch: number,
  companyId: string | null,
  partyId: string,
): TaxIdentityAttemptBinding | null {
  if (origin === null || actorId === null || companyId === null || partyId === '') return null
  return {
    origin,
    actorId,
    sessionEpoch,
    companyId,
    partyId,
    key: JSON.stringify([origin, actorId, String(sessionEpoch), companyId, partyId, 'counterparty.tax-identity']),
  }
}

export function taxIdentityBindingForSnapshot(
  snapshot: SecuritySnapshot,
  partyId: string,
  expectedCompanyId: string,
  origin: string | null,
): TaxIdentityAttemptBinding | null {
  if (snapshot.companyId !== expectedCompanyId) return null
  return binding(origin, snapshot.actorId, snapshot.sessionEpoch, snapshot.companyId, partyId)
}

export function taxIdentityBindingForShell(
  shell: ShellState,
  partyId: string,
  origin: string,
): TaxIdentityAttemptBinding | null {
  const context = shell.activeContext
  return binding(origin, context?.userId ?? null, shell.sessionEpoch, context?.companyId ?? null, partyId)
}

type Held = Exclude<TaxIdentityAttemptState, { phase: 'idle' }>
const slots = new Map<string, Held>()
const settled = new Set<string>()

function settledKey(bindingValue: TaxIdentityAttemptBinding, attemptKey: string): string {
  return `${bindingValue.key}|${attemptKey}`
}

export function rememberTaxIdentityAttempt(
  bindingValue: TaxIdentityAttemptBinding | null,
  state: TaxIdentityAttemptState,
  seen = true,
): void {
  if (bindingValue === null || state.phase === 'idle') return
  const key = settledKey(bindingValue, state.snapshot.key)
  if (settled.has(key)) return
  if (
    state.phase === 'submitting' ||
    state.phase === 'unknown' ||
    state.phase === 'abandoned' ||
    (state.phase === 'completed' && !seen)
  ) {
    slots.set(bindingValue.key, state)
    return
  }
  settled.add(key)
  if (slots.get(bindingValue.key)?.snapshot.key === state.snapshot.key) slots.delete(bindingValue.key)
}

export function recallTaxIdentityAttempt(
  bindingValue: TaxIdentityAttemptBinding | null,
): TaxIdentityAttemptState | null {
  if (bindingValue === null) return null
  const held = slots.get(bindingValue.key)
  if (held === undefined) return null
  if (held.phase === 'submitting') {
    return {
      phase: 'unknown',
      snapshot: held.snapshot,
      reason: held.unresolvedSince ?? 'network-error',
      resends: held.resends,
    }
  }
  return held
}

export function forgetTaxIdentityAttempt(bindingValue: TaxIdentityAttemptBinding | null): void {
  if (bindingValue === null) return
  const held = slots.get(bindingValue.key)
  if (held === undefined) return
  settled.add(settledKey(bindingValue, held.snapshot.key))
  slots.delete(bindingValue.key)
}

interface ArmedDiscard {
  readonly binding: TaxIdentityAttemptBinding
  readonly revision: number
}
const armedDiscards = new Map<string, ArmedDiscard>()

export function armTaxIdentityDiscard(bindingValue: TaxIdentityAttemptBinding | null, revision: number): void {
  if (bindingValue !== null) armedDiscards.set(bindingValue.key, { binding: bindingValue, revision })
}

function sameSession(left: TaxIdentityAttemptBinding, right: TaxIdentityAttemptBinding): boolean {
  return (
    left.origin === right.origin &&
    left.actorId === right.actorId &&
    left.sessionEpoch === right.sessionEpoch &&
    left.partyId === right.partyId
  )
}

export function reconcileTaxIdentityDiscards(shell: ShellState, partyId: string, origin: string): void {
  const current = taxIdentityBindingForShell(shell, partyId, origin)
  if (current === null) return
  for (const [key, armed] of armedDiscards) {
    if (armed.binding.partyId !== partyId || armed.binding.origin !== origin) continue
    if (!sameSession(armed.binding, current)) {
      if (armed.binding.sessionEpoch < current.sessionEpoch) armedDiscards.delete(key)
      continue
    }
    if (shell.committedContextRevision > armed.revision) {
      forgetTaxIdentityAttempt(armed.binding)
      armedDiscards.delete(key)
    } else if (armed.binding.key === current.key) {
      armedDiscards.delete(key)
    }
  }
}

export function resetPendingTaxIdentityAttempts(): void {
  slots.clear()
  settled.clear()
  armedDiscards.clear()
}
