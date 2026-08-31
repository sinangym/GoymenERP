/**
 * Request-generation fence for CompanyContext transitions.
 *
 * Design source: docs/delivery/CRITICAL_ANALYSIS/shell-companycontext-fence/claude.md §A.2
 *
 * REV.2 (Codex #133 review P1-3): there is exactly ONE authoritative generation
 * counter — `ShellState.requestFenceGeneration` in ./shellState.ts. This module has
 * NO independent mutable counter; it is a pure staleness check plus a read-only
 * "view" that a transport layer holds. A request captures the current generation at
 * start; on settle it is stale if the generation has advanced.
 *
 * INV-FENCE (ERP-000K_SHELL_CONTRACT §13.4): a stale result is never written to
 * state or cache and a mutation produces no UI effect — even when transport
 * cancellation (AbortController) is missed and even when the transition later fails.
 */

export type FenceGeneration = number

/** Pure check: a response captured at `captured` is stale once `current` has moved. */
export function isResponseStale(
  captured: FenceGeneration,
  current: FenceGeneration,
): boolean {
  return captured !== current
}

/**
 * Read-only fence view backed by the single authoritative generation source
 * (`() => shellStore.getState().requestFenceGeneration`). Held by the transport /
 * query layer. It cannot advance the generation — only the reducer does that.
 */
export interface FenceView {
  /** Capture the current generation for an in-flight request. */
  capture(): FenceGeneration
  /** True when a response captured at `captured` must be rejected. */
  isStale(captured: FenceGeneration): boolean
}

export function createFenceView(
  readGeneration: () => FenceGeneration,
): FenceView {
  return {
    capture: () => readGeneration(),
    isStale: (captured) => isResponseStale(captured, readGeneration()),
  }
}
