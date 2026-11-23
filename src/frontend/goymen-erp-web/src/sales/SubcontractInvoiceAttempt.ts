/**
 * F2: the subcontract service invoice create attempt on the order detail, on the Sales attempt machine (./orderAttempt.ts via
 * ./poolAttempt.ts) — one Idempotency-Key per submitted snapshot; an unknown outcome keeps the key, freezes the inputs and
 * offers only "retry the same" or "leave it"; the attempt and the inputs it was sent from outlive the panel
 * (./pendingPoolAttempts.ts). 401/403 go to the auth boundary. Kept apart from the component so it exports components only.
 */
import { SALES_PROBLEM_CODE, type SalesProblem } from './contract'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import type { OrderAttemptState } from './orderAttempt'
import { usePoolAttempt, type PoolAttempt } from './poolAttempt'
import { problemCodeText } from './salesText'
import { createSubcontractInvoice, type CreateSubcontractInvoicePayload } from './SubcontractInvoiceApi'
import type { SubcontractInvoiceDetail } from './SubcontractInvoiceContract'
import {
  SUBCONTRACT_INVOICE_TEXT as S,
  subcontractInvoiceFieldText,
  subcontractInvoiceReasonText,
} from './SubcontractInvoiceText'
import { useAuthBoundary } from '../shell/useAuthBoundary'

/** 409: the weighing cannot be invoiced now (not recorded under the key; the same key asks again). */
export const SUBCONTRACT_INVOICE_NOT_READY_CODE = 'invoice_not_ready'

export type SubcontractInvoiceAttemptState = OrderAttemptState<CreateSubcontractInvoicePayload, SubcontractInvoiceDetail>
export type SubcontractInvoiceAttempt = PoolAttempt<CreateSubcontractInvoicePayload, SubcontractInvoiceDetail>

/** The attempt is held per order: one panel per subcontract order. */
export const subcontractInvoiceScope = (salesOrderId: string) => `sales.subcontract-invoice.create:${salesOrderId.toLowerCase()}`

/** The form values the attempt was sent from (recalled with the attempt). */
export interface SubcontractInvoiceFormValues {
  readonly loadingInstructionId: string
  readonly weighingId: string
}

export const isSubcontractInvoiceFormValues = (value: unknown): value is SubcontractInvoiceFormValues =>
  typeof value === 'object' && value !== null
  && typeof (value as { loadingInstructionId?: unknown }).loadingInstructionId === 'string'
  && typeof (value as { weighingId?: unknown }).weighingId === 'string'

export function useSubcontractInvoiceCreate(
  salesOrderId: string,
  values: SubcontractInvoiceFormValues,
  onSettled: (next: SubcontractInvoiceAttemptState) => void,
): SubcontractInvoiceAttempt {
  const boundary = useAuthBoundary()
  return usePoolAttempt<CreateSubcontractInvoicePayload, SubcontractInvoiceDetail>(
    subcontractInvoiceScope(salesOrderId),
    async (snapshot) => {
      const security = boundary.capture()
      const outcome = await createSubcontractInvoice(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId })
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
      return outcome
    },
    onSettled,
    { values },
  )
}

export interface SubcontractInvoiceNotice {
  readonly message: string
  readonly role: 'alert' | 'status'
  /** 409 `invoice_not_ready`: the server's reasons, one per field and code, verbatim and in the server's order. */
  readonly reasons: readonly string[]
}

const UNKNOWN_TEXT = {
  offline: T.unknownOffline,
  'network-error': T.unknownNetwork,
  'service-unavailable': T.unknownServiceUnavailable,
  'malformed-response': T.unknownMalformed,
} as const

function failureBase(failure: string): string {
  switch (failure) {
    case 'unauthenticated': return T.unauthenticated
    case 'forbidden': return T.forbidden
    case 'not-found': return S.notFound
    case 'conflict': return T.conflict
    case 'validation': return T.validation
    default: return T.unexpected
  }
}

/** The server's `invoice_not_ready` field errors as "field: reason". */
export function subcontractInvoiceNotReadyItems(problem: SalesProblem | null): readonly string[] {
  if (problem === null) return []
  return problem.errors.map((error) => S.notReadyItem(subcontractInvoiceFieldText(error.field), subcontractInvoiceReasonText(error.code)))
}

export function subcontractInvoiceNotice(state: SubcontractInvoiceAttemptState, blockedChange: boolean): SubcontractInvoiceNotice | null {
  switch (state.phase) {
    case 'idle': return null
    case 'submitting': return { message: S.submitting, role: 'status', reasons: [] }
    case 'unknown': {
      const base = UNKNOWN_TEXT[state.reason]
      return { message: blockedChange ? `${base} ${S.editAfterUnknown}` : base, role: 'alert', reasons: [] }
    }
    case 'abandoned': return { message: T.abandoned, role: 'status', reasons: [] }
    case 'completed':
      return {
        message: state.result.isReplay ? S.replayed(state.result.documentNo) : S.completed(state.result.documentNo),
        role: 'status',
        reasons: [],
      }
    case 'failed': {
      const code = state.problem?.code ?? null
      let text: string
      let reasons: readonly string[] = []
      if (state.failure === 'conflict' && code === SUBCONTRACT_INVOICE_NOT_READY_CODE) {
        text = S.notReady
        reasons = subcontractInvoiceNotReadyItems(state.problem)
      } else if (state.failure === 'conflict') {
        const codeText = code === SALES_PROBLEM_CODE.sameKeyPayloadMismatch ? problemCodeText(code) : null
        text = codeText === null ? S.createConflict : `${S.createConflict} ${codeText}`
      } else {
        const codeText = problemCodeText(code)
        const base = failureBase(state.failure)
        text = codeText === null ? base : `${base} ${codeText}`
      }
      if (state.unresolvedSince !== undefined) text = `${text} ${T.unknown}`
      if (blockedChange) text = `${text} ${S.editAfterUnknown}`
      return { message: text, role: 'alert', reasons }
    }
    default: return null
  }
}
