/**
 * What a Finance source screen may SAY about one source — derived from the decoded
 * #258 shapes, so the wrong sentence cannot be built.
 *
 *  - spec §1: a draft "para kaynağı, banka bakiyesi veya gerçekleşmiş tahsilat diye
 *    gösterilmez" → a draft view has NO unallocated member, whatever the body carried;
 *  - spec §3 / #258: allocation `Unavailable` is shown as unavailable, never as 0;
 *  - spec §4: `Redacted` carries no digits (the decoder already refused money on it).
 */

import type { AmountView, SourceDetail, SourceSummary } from './contract'
import { formatDecimalTextTr } from './decimalText'
import { AMOUNT_REDACTED, AMOUNT_UNAVAILABLE, AMOUNT_UNREADABLE } from './financeText'

export type AmountShown =
  | { readonly kind: 'visible'; readonly amount: string }
  | { readonly kind: 'redacted' }
  | { readonly kind: 'unavailable' }

export function amountShown(view: AmountView): AmountShown {
  switch (view.disclosure) {
    case 'Visible':
      return { kind: 'visible', amount: view.money.amount }
    case 'Redacted':
      return { kind: 'redacted' }
    case 'Unavailable':
      return { kind: 'unavailable' }
  }
}

export type SourceStatusView =
  | { readonly stage: 'draft'; readonly amount: AmountShown }
  | { readonly stage: 'recorded' | 'reversed'; readonly amount: AmountShown; readonly unallocated: AmountShown }

export function stageKey(summary: SourceSummary): SourceStatusView['stage'] {
  switch (summary.stage) {
    case 'Draft':
      return 'draft'
    case 'Recorded':
      return 'recorded'
    case 'Reversed':
      return 'reversed'
  }
}

export function statusViewOf(detail: SourceDetail): SourceStatusView {
  const stage = stageKey(detail.source)
  const amount = amountShown(detail.source.amount)
  if (stage === 'draft') return { stage, amount }
  const unallocated: AmountShown =
    detail.unallocated.knowledge === 'Unavailable' ? { kind: 'unavailable' } : amountShown(detail.unallocated.amount)
  return { stage, amount, unallocated }
}

/** The only way an amount becomes text on a Finance screen. */
export function amountText(view: AmountShown): string {
  switch (view.kind) {
    case 'visible':
      return formatDecimalTextTr(view.amount) ?? AMOUNT_UNREADABLE
    case 'redacted':
      return AMOUNT_REDACTED
    case 'unavailable':
      return AMOUNT_UNAVAILABLE
  }
}
