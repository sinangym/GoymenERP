/**
 * Request-generation fence for CompanyContext transitions.
 *
 * Design source: docs/delivery/CRITICAL_ANALYSIS/shell-companycontext-fence/claude.md §A.2
 * (CODEX-ORCHESTRATOR review: the fence advances at the START of a transition, not
 *  after a successful commit; committed-context revision is a separate concept.)
 *
 * INV-FENCE (ERP-000K_SHELL_CONTRACT §13.4): every outbound request captures the
 * fence value at start. On settle, if the captured value != the current value the
 * result is discarded — it is never written to state or cache, and a mutation
 * produces no UI effect. This holds even when transport cancellation (AbortController)
 * is missed and even when the transition later fails.
 *
 * This is a package-independent primitive: it sits above any fetch/mutation layer.
 */

export type FenceGeneration = number

export interface RequestFence {
  /** Current generation. A response whose captured generation differs is stale. */
  current(): FenceGeneration
  /** Advance the fence. Called at the START of switch / logout / capability revoke. */
  bump(): FenceGeneration
  /** Capture the current generation for an in-flight request. */
  capture(): FenceGeneration
  /** True when a response captured at `captured` must be rejected. */
  isStale(captured: FenceGeneration): boolean
}

export function createRequestFence(initial: FenceGeneration = 0): RequestFence {
  let generation = initial
  return {
    current: () => generation,
    bump: () => {
      generation += 1
      return generation
    },
    capture: () => generation,
    isStale: (captured) => captured !== generation,
  }
}
