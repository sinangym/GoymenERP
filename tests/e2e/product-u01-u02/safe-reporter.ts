/** Explicit field allowlist. Never serializes Playwright config, environment, workers, attachments or arbitrary errors. */
import { mkdirSync, writeFileSync } from 'node:fs'
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
    writeFileSync(path.join(runDir, 'tests.json'), JSON.stringify({
      task_id: 'U-01-U-02', lane: 'T', runId, role, coldStart, claimedAtUtc, ownerRecord: 'run-owner.json',
      startedAtUtc: this.startedAtUtc, finishedAtUtc: new Date().toISOString(), status: result.status, durationMs: result.duration,
      passed: this.tests.filter((t) => t.status === 'passed').length,
      failed: this.tests.filter((t) => t.status !== 'passed').length, globalErrors: this.globalErrors, tests: this.tests,
      note: 'Controlled T responses in a real Chromium against the unchanged frontend; no live API, SQL, login or capability publication. Not ERP acceptance.',
    }, null, 2) + '\n')
  }
}
