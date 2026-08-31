/**
 * TEMPORARY contract fixtures for SHELL-PILOT-01B parallel development.
 *
 * Issue #129 "Parallel-development rule": before SHELL-PILOT-01A (#128 / PR #132)
 * and the generated TypeScript client (Issue #122) land, Claude may build shell
 * structure against fixtures that exactly mirror the parent contract — but must
 * NOT hand-write a second permanent DTO source.
 *
 * >>> DELETION CONTRACT <<<
 * Every symbol in this file is removed when the generated client lands. The shell
 * store and components import context/capability *shapes* only from here; final
 * integration re-points them at `src/api/generated/**`. A drift check on the
 * generated client is the real contract boundary.
 *
 * These fixtures assert NO business truth: no real company names, VKN, KPIs,
 * balances, permissions or routes. `isPlaceholder: true` mirrors the current
 * server `CompanySummary` for OPEN-001 placeholder companies.
 */

import type { CompanyContext } from './shellState'

/** Mirrors the existing server `CompanySummary(id, code, legalName, isPlaceholder)`. */
export interface CompanyDirectoryEntryFixture {
  readonly id: string
  readonly code: string
  readonly legalName: string
  readonly isPlaceholder: boolean
}

/** Opaque coarse capability availability — never a permission/ABAC inventory. */
export interface CapabilitySnapshotFixture {
  /** Opaque marker; the shell only compares equality, never parses it. */
  readonly generation: string
  /** Coarse feature-mount availability keyed by stable feature id. */
  readonly features: Readonly<Record<string, boolean>>
}

export const companyDirectoryFixture: readonly CompanyDirectoryEntryFixture[] = [
  { id: 'fixture-company-a', code: 'GOY-A', legalName: 'TBD-GOY-01', isPlaceholder: true },
  { id: 'fixture-company-b', code: 'GOY-B', legalName: 'TBD-GOY-02', isPlaceholder: true },
]

export const capabilitySnapshotFixture: CapabilitySnapshotFixture = {
  generation: 'fixture-cap-gen-1',
  // SHELL-PILOT scope: no business feature is mounted yet. Everything false.
  features: {},
}

export function fixtureContext(
  companyId: string,
  generation: string | null = capabilitySnapshotFixture.generation,
): CompanyContext {
  return {
    userId: 'fixture-user',
    scopeMode: 'single',
    companyId,
    capabilityGeneration: generation,
  }
}
