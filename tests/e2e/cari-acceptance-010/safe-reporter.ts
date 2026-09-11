/** Explicit field allowlist. Never serializes Playwright config, environment, workers, attachments or arbitrary errors. */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import type { FullResult, Reporter, TestCase, TestResult } from '@playwright/test/reporter'
import { runDir, runId, coldStart, role, claimedAtUtc } from './paths'
export default class SafeReporter implements Reporter {
  private readonly startedAtUtc = new Date().toISOString()
  private globalErrors = 0
  onError() { this.globalErrors += 1 }
  private readonly tests: { title: string; file: string; line: number; status: string; durationMs: number; retry: number; assertionsFailed: number }[] = []
  onTestEnd(test: TestCase, result: TestResult) {
    this.tests.push({
      title: test.title, file: path.basename(test.location.file), line: test.location.line,
      status: result.status, durationMs: result.duration, retry: result.retry, assertionsFailed: result.errors.length,
    })
  }
  onEnd(result: FullResult) {
    mkdirSync(runDir, { recursive: true })
    const warmUpFile = path.join(runDir, 'diagnostics', 'warm-up.json')
    let warmUp: { warmUpMs?: number; coldStart?: boolean } | null = null
    if (existsSync(warmUpFile)) {
      const parsed: unknown = JSON.parse(readFileSync(warmUpFile, 'utf8'))
      if (parsed && typeof parsed === 'object') warmUp = parsed as { warmUpMs?: number; coldStart?: boolean }
    }
    writeFileSync(path.join(runDir, 'tests.json'), JSON.stringify({
      task_id: 'C-DESKTOP-010', lane: 'T', runId, role, coldStart, claimedAtUtc, ownerRecord: 'run-owner.json', startedAtUtc: this.startedAtUtc,
      finishedAtUtc: new Date().toISOString(), status: result.status, durationMs: result.duration,
      warmUpMs: warmUp?.warmUpMs ?? null,
      passed: this.tests.filter((t) => t.status === 'passed').length,
      failed: this.tests.filter((t) => t.status !== 'passed').length, globalErrors: this.globalErrors, tests: this.tests,
    }, null, 2) + '\n')
  }
}
