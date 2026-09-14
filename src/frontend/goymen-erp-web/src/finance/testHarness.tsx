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
import { Fragment, useEffect, useReducer, type ReactNode } from 'react'
import { ShellDispatchContext, ShellStateContext } from '../shell/shellContext'
import { initialShellState, shellReducer, type ShellEvent, type ShellState } from '../shell/shellState'
import { FinancePortProvider } from './financePort'
import type { FinanceReceiptPort, FinanceScope, PortReply } from './port'
import type { HttpReadReply } from './http'
import type { DraftContextRequest } from './draftContext'
import type { DraftSaveRequest, RecordRequest, ReverseRequest } from './contract'
import { clearAttemptStore } from './receiptCommand'
import { DRAFT_CONTEXT_EXAMPLE, EXAMPLE_BANK, EXAMPLE_COMPANY, EXAMPLE_CURRENCY, NEGATIVE_EXAMPLE, NORMAL_EXAMPLE } from './contractExamples'

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
type ReadName = 'listBankAccounts' | 'listSources' | 'sourceDetail' | 'draftContext'

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

export type ReadResponder = (
  method: ReadName,
  scope: FinanceScope,
  request: unknown,
) => PortReply | HttpReadReply | Promise<PortReply | HttpReadReply>

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
    listBankAccounts: read('listBankAccounts') as FinanceReceiptPort['listBankAccounts'],
    listSources: read('listSources') as FinanceReceiptPort['listSources'],
    sourceDetail: read('sourceDetail') as FinanceReceiptPort['sourceDetail'],
    draftContext: read('draftContext') as FinanceReceiptPort['draftContext'],
    draftSave: command('draftSave'),
    record: command('record'),
    reverse: command('reverse'),
  }
  return { port, commands, reads }
}

type HarnessEvent = ShellEvent | { readonly type: '__COMMIT'; readonly state: ShellState }

/** The real shell reducer, plus a test-only commit that installs a verified context. */
function harnessReducer(state: ShellState, event: HarnessEvent): ShellState {
  return event.type === '__COMMIT' ? event.state : shellReducer(state, event)
}

let dispatchShell: ((event: HarnessEvent) => void) | null = null
let latestShell: ShellState = initialShellState

function ControlledShell({ initial, children }: { readonly initial: ShellState; readonly children: ReactNode }) {
  const [state, dispatch] = useReducer(harnessReducer, initial)
  useEffect(() => {
    dispatchShell = dispatch
  }, [])
  useEffect(() => {
    latestShell = state
  }, [state])
  return (
    <ShellDispatchContext.Provider value={dispatch}>
      <ShellStateContext.Provider value={state}>{children}</ShellStateContext.Provider>
    </ShellDispatchContext.Provider>
  )
}

function tree(ui: ReactNode, port: FinanceReceiptPort | null, shell: ShellState, queryClient: QueryClient) {
  return (
    <QueryClientProvider client={queryClient}>
      <ControlledShell initial={shell}>
        {port === null ? ui : <FinancePortProvider port={port}>{ui}</FinancePortProvider>}
      </ControlledShell>
    </QueryClientProvider>
  )
}

export function renderFinance(
  ui: ReactNode,
  options: { readonly port: FinanceReceiptPort | null; readonly shell?: ShellState },
) {
  clearAttemptStore()
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const shell = options.shell ?? shellFor(COMPANY_A)
  let generation = 0
  const keyed = (node: ReactNode) => <Fragment key={generation}>{node}</Fragment>
  const view = render(tree(keyed(ui), options.port, shell, queryClient))
  /**
   * Unmount the Finance subtree and mount `next` in its place while the SHELL stays mounted
   * — what a route change or a closed gate does in the app. Cleanups reach the live shell.
   */
  const remount = (next: ReactNode) => {
    generation += 1
    view.rerender(tree(keyed(next), options.port, shell, queryClient))
  }
  return { ...view, queryClient, remount }
}

/** Commit a new shell context (e.g. a company switch that the shell already confirmed). */
export async function commitContext(next: ShellState): Promise<void> {
  await act(async () => {
    dispatchShell?.({ type: '__COMMIT', state: next })
  })
}

/** Send a real shell event (e.g. REQUEST_SWITCH) through the real reducer. */
export async function shellEvent(event: ShellEvent): Promise<void> {
  await act(async () => {
    dispatchShell?.(event)
  })
}

/** The shell state as last committed. */
export function currentShell(): ShellState {
  return latestShell
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

/** A 200 draft read context answering `request` in `companyId` (verbatim #261 example shape). */
export function draftContextBody(companyId: string, request: DraftContextRequest, overrides: Json = {}): Json {
  const example = DRAFT_CONTEXT_EXAMPLE.response
  return {
    ...example,
    companyId,
    occurredOn: request.occurredOn,
    bankAccount: { ...example.bankAccount, id: request.treasuryAccountId, companyId },
    currency: {
      ...example.currency,
      profile: { ...example.currency.profile, effectiveFrom: request.occurredOn },
    },
    party: { ...example.party, partyId: request.partyId, companyAccountId: request.companyAccountId },
    ...overrides,
  }
}

export function contextOk(body: unknown): HttpReadReply {
  return { kind: 'ok', body }
}

export function contextStatus(status: number): HttpReadReply {
  return { kind: 'status', status }
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
