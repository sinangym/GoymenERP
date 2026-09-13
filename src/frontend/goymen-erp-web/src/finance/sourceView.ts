/**
 * What a Finance source screen may SAY about one manual receipt — as types, so the
 * wrong sentence cannot be built.
 *
 * Source: `docs/features/FINANCE_MANUAL_SOURCE_JIT_SPEC_v1.md` (PR #254 @290624d).
 * Status tokens, the amount-visibility signal and the gate reasons are pinned by the
 * Finance Backend; decoders (not written yet) map the wire onto these types and fail
 * closed on anything else. The rules encoded here are the spec's:
 *
 *  - §1 a draft "para kaynağı, banka bakiyesi veya gerçekleşmiş tahsilat diye
 *    gösterilmez" → a draft has NO unallocated field at all;
 *  - §3 "Dağıtım servisi erişilemiyorsa 0 varsayılmaz" → an unallocated amount is either
 *    the server's text or explicitly unavailable, never a client default;
 *  - §4 "Yetkisiz tutar DOM/JSON/log içinde saklanarak gizlenmez" → a withheld amount
 *    carries no digits to hide; it is a different state, not a masked value;
 *  - §1/§5 definitive recording is closed without profile / period / company authority
 *    / policy → the gate is CLOSED unless the server opened it.
 */

export type AmountView =
  | { readonly kind: 'visible'; readonly amount: string; readonly currencyLabel: string }
  /** The actor may see the record but not its amount. No value exists client-side. */
  | { readonly kind: 'withheld' }

export type UnallocatedView =
  | { readonly kind: 'server'; readonly amount: string; readonly currencyLabel: string }
  /** The owner could not answer. Shown as unavailable — never as 0. */
  | { readonly kind: 'unavailable' }
  | { readonly kind: 'withheld' }

export type SourceSummaryView =
  /** Preparation only. No money event, so no unallocated amount exists to show. */
  | { readonly stage: 'draft'; readonly amount: AmountView }
  | { readonly stage: 'recorded'; readonly amount: AmountView; readonly unallocated: UnallocatedView }
  /** Corrected by a linked reversal; the original stays visible with its amount. */
  | { readonly stage: 'reversed'; readonly amount: AmountView; readonly unallocated: UnallocatedView }

export type RecordGateReason =
  | 'precision-profile-missing'
  | 'period-not-open'
  | 'authority-missing'
  | 'policy-missing'
  /** The server has not declared the gate at all — the default, and closed. */
  | 'not-declared'

export type RecordGate =
  | { readonly kind: 'open' }
  | { readonly kind: 'closed'; readonly reasons: readonly RecordGateReason[] }

export const RECORD_GATE_NOT_DECLARED: RecordGate = { kind: 'closed', reasons: ['not-declared'] }

/**
 * Only an explicit server "open" opens the gate. An empty reason list is treated as
 * "not declared" — a closed gate with no reason would otherwise render as open-looking
 * silence.
 */
export function recordGateFrom(open: boolean, reasons: readonly RecordGateReason[]): RecordGate {
  if (open) return { kind: 'open' }
  return reasons.length === 0 ? RECORD_GATE_NOT_DECLARED : { kind: 'closed', reasons }
}
