/**
 * Test-only harness for the Finance screens.
 *
 * - The SHELL STATE is controlled: a real `ShellState` (from `initialShellState`) is put
 *   into the real `ShellStateContext`, and a test can commit a company switch the way the
 *   reducer's ATOMIC-COMMIT does (new company, higher `committedContextRevision`). The
 *   switch UI itself is the shell owner's and is tested there.
 * - The PORT is a recorder whose commands return REAL pending promises. Nothing resolves
 *   until the test says so, so "while in flight" is a state the test actually observes.
 *
 * Component/DOM evidence only: no HTTP adapter, API, SQL, provider, profile, period or
 * browser. Fixtures are SENTETIK/NOT_ENV.
 */

import { act, render } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useEffect, useState, type ReactNode } from 'react'
import { ShellStateContext } from '../shell/shellContext'
import { initialShellState, type ShellState } from '../shell/shellState'
import { FinancePortProvider } from './financePort'
import type { FinanceReceiptPort, FinanceScope, PortReply } from './port'
import type { DraftSaveRequest, RecordRequest, ReverseRequest } from './contract'
import { clearAttemptStore } from './receiptCommand'
import { EXAMPLE_BANK, EXAMPLE_COMPANY, EXAMPLE_CURRENCY, NEGATIVE_EXAMPLE, NORMAL_EXAMPLE } from './contractExamples'

export const COMPANY_A = EXAMPLE_COMPANY
export const COMPANY_B = 'bbbbbbbb-0000-4000-8000-000000000002'
export const ACTOR = '99999999-9999-4999-8999-999999999999'

export function shellFor(companyId: string, revision = 1): ShellState {
  return {
    ...initialShellState,
    phase: 'idle',
    activeContext: { userId: ACTOR, scopeMode: 'single', companyId, capabilityGeneration: 'finance-gen-1', capabilities: [] },
    committedContextRevision: revision,
    sessionEpoch: 1,
    bootstrappedSessionEpoch: 1,
  }
}

type CommandName = 'draftSave' | 'record' | 'reverse'
type ReadName = 'listBankAccounts' | 'listSources' | 'sourceDetail'

export interface CommandCall {
  readonly method: CommandName
  readonly scope: FinanceScope
  readonly key: string
  readonly request: DraftSaveRequest | RecordRequest | ReverseRequest
  resolve(reply: PortReply): Promise<void>
  reject(): Promise<void>
}

export interface ReadCall {
  readonly method: ReadName
  readonly scope: FinanceScope
  readonly request: unknown
}

export type ReadResponder = (method: ReadName, scope: FinanceScope, request: unknown) => PortReply

export function body(value: unknown): PortReply {
  return { kind: 'body', body: value }
}

export const NO_REPLY: PortReply = { kind: 'no-reply' }

export function found(resource: unknown): PortReply {
  return body({ outcome: 'Found', resource })
}

export function recordingPort(respond: ReadResponder) {
  const commands: CommandCall[] = []
  const reads: ReadCall[] = []

  const command =
    (method: CommandName) =>
    (scope: FinanceScope, key: string, request: DraftSaveRequest | RecordRequest | ReverseRequest) =>
      new Promise<PortReply>((resolvePromise, rejectPromise) => {
        commands.push({
          method,
          scope,
          key,
          request,
          resolve: (reply) =>
            act(async () => {
              resolvePromise(reply)
            }),
          reject: () =>
            act(async () => {
              rejectPromise(new Error('transport'))
            }),
        })
      })

  const read = (method: ReadName) => async (scope: FinanceScope, ...rest: unknown[]) => {
    const request = rest.length === 2 ? rest[0] : null
    reads.push({ method, scope, request })
    return respond(method, scope, request)
  }

  const port: FinanceReceiptPort = {
    listBankAccounts: read('listBankAccounts'),
    listSources: read('listSources'),
    sourceDetail: read('sourceDetail'),
    draftSave: command('draftSave'),
    record: command('record'),
    reverse: command('reverse'),
  }
  return { port, commands, reads }
}

let commitShell: ((next: ShellState) => void) | null = null

function ControlledShell({ initial, children }: { readonly initial: ShellState; readonly children: ReactNode }) {
  const [state, setState] = useState(initial)
  useEffect(() => {
    commitShell = setState
  }, [])
  return <ShellStateContext.Provider value={state}>{children}</ShellStateContext.Provider>
}

export function renderFinance(
  ui: ReactNode,
  options: { readonly port: FinanceReceiptPort | null; readonly shell?: ShellState },
) {
  clearAttemptStore()
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const tree = (
    <QueryClientProvider client={queryClient}>
      <ControlledShell initial={options.shell ?? shellFor(COMPANY_A)}>
        {options.port === null ? ui : <FinancePortProvider port={options.port}>{ui}</FinancePortProvider>}
      </ControlledShell>
    </QueryClientProvider>
  )
  return { ...render(tree), queryClient }
}

/** Commit a new shell context (e.g. a company switch that the shell already confirmed). */
export async function commitContext(next: ShellState): Promise<void> {
  await act(async () => {
    commitShell?.(next)
  })
}

// ---------------------------------------------------------------------------------
// Synthetic body builders, derived from the verbatim #258 normal example
// ---------------------------------------------------------------------------------


type Json = Record<string, unknown>

export function bankAccount(companyId: string, overrides: Json = {}): Json {
  return {
    id: EXAMPLE_BANK,
    companyId,
    currencyId: EXAMPLE_CURRENCY,
    displayName: 'SENTETIK Banka Hesabı',
    isActive: true,
    isUsableForReceipt: true,
    version: '9007199254740993',
    ...overrides,
  }
}

export function bankPage(companyId: string, items: readonly Json[] = [bankAccount(companyId)]): Json {
  return { companyId, items }
}

export function sourceSummary(companyId: string, overrides: Json = {}): Json {
  return { ...NORMAL_EXAMPLE.draftSaveResult.resource.source, companyId, ...overrides }
}

export function sourceDetail(companyId: string, overrides: { source?: Json } & Json = {}): Json {
  const { source, ...rest } = overrides
  return {
    ...NORMAL_EXAMPLE.draftSaveResult.resource,
    source: sourceSummary(companyId, source ?? {}),
    ...rest,
  }
}

export function succeeded(resource: Json, isReplay = false): PortReply {
  return body({ outcome: 'Succeeded', resource, isReplay, originalCorrelationId: 'SENTETIK-attempt-1', issues: [] })
}

export function failedOutcome(
  outcome: 'Unavailable' | 'Denied' | 'ValidationFailed' | 'VersionConflict' | 'IdempotencyConflict',
  options: { issues?: readonly { field: string; code: string }[]; isReplay?: boolean } = {},
): PortReply {
  return body({
    ...NEGATIVE_EXAMPLE.unavailableResult,
    outcome,
    issues: options.issues ?? [],
    isReplay: options.isReplay ?? false,
  })
}
