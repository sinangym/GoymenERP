import { exactKeys } from './exactKeys'

export interface InventoryCostStaleClosure {
  readonly eventId: string; readonly preparedVersion: number; readonly preparedBy: string; readonly closedAtUtc: string
  readonly reasonCode: 'inventory_cost_preparation_stale'; readonly reasonText: 'Bayatladı'
  readonly correlationId: string; readonly causationId: string | null
}
export interface InventoryCostRenewal {
  readonly previousProposalId: string; readonly previousClosedVersion: number; readonly closureEventId: string
}
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const guid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const version = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0

/** YOL-D40 evidence accompanies an already company-fenced owner proposal; it grants no command authority. */
export function validInventoryCostStaleEvidence(proposal: Record<string, unknown>): boolean {
  const closure = proposal.staleClosure
  if (proposal.status === 'Stale') {
    const preparation = proposal.preparation
    if (!record(closure) || !exactKeys(closure, ['eventId', 'preparedVersion', 'preparedBy', 'closedAtUtc', 'reasonCode', 'reasonText', 'correlationId', 'causationId'])
      || !record(preparation) || !guid(closure.eventId) || !guid(closure.preparedBy) || !guid(preparation.preparedBy)
      || closure.preparedBy.toLowerCase() !== preparation.preparedBy.toLowerCase()
      || !version(closure.preparedVersion) || closure.preparedVersion < 2 || proposal.version !== closure.preparedVersion + 1
      || closure.reasonCode !== 'inventory_cost_preparation_stale' || closure.reasonText !== 'Bayatladı'
      || typeof closure.closedAtUtc !== 'string' || !/^\d{4}-\d\d-\d\dT.*(?:Z|\+00:00)$/.test(closure.closedAtUtc)
      || !Number.isFinite(Date.parse(closure.closedAtUtc))
      || typeof closure.correlationId !== 'string' || closure.correlationId.trim().length === 0 || closure.correlationId.length > 128
      || closure.causationId !== null && (typeof closure.causationId !== 'string' || closure.causationId.length > 128)
      || proposal.cancellationId !== null || proposal.reversalProposal !== null) return false
  } else if (closure !== undefined) return false // Additive evidence is omitted outside the immutable stale version.
  const renewal = proposal.renewedFrom
  return renewal === undefined || record(renewal)
    && exactKeys(renewal, ['previousProposalId', 'previousClosedVersion', 'closureEventId'])
    && guid(renewal.previousProposalId) && typeof proposal.proposalId === 'string'
    && renewal.previousProposalId.toLowerCase() !== proposal.proposalId.toLowerCase()
    && guid(renewal.closureEventId) && version(renewal.previousClosedVersion) && renewal.previousClosedVersion >= 3
    && version(proposal.version) && renewal.previousClosedVersion < proposal.version
}
