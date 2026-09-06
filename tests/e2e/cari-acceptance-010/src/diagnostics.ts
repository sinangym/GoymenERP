/**
 * Selected per-test diagnostics (F014-P2-001): timings of page open, console errors and
 * warnings, page errors, failed requests (method + path + reason) and navigations, written
 * per test into <run>/diagnostics/. Nothing else: no environment, cookies, storage,
 * headers, request or response bodies.
 */
import { existsSync, mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import type { Page, TestInfo } from '@playwright/test'
import { runDir, runId, coldStart, role, viteCacheDir } from '../paths'

export interface DiagnosticEvent { atMs: number; kind: string; detail: string }

const byPage = new WeakMap<Page, Diagnostics>()

export class Diagnostics {
  readonly startedAt = Date.now()
  readonly events: DiagnosticEvent[] = []
  consoleErrors = 0
  consoleWarnings = 0
  pageErrors = 0
  failedRequests = 0
  private constructor(readonly page: Page) {}
  static attach(page: Page): Diagnostics {
    const existing = byPage.get(page)
    if (existing) return existing
    const d = new Diagnostics(page)
    byPage.set(page, d)
    page.on('console', (message) => {
      const type = message.type()
      if (type !== 'error' && type !== 'warning') return
      if (type === 'error') d.consoleErrors += 1
      else d.consoleWarnings += 1
      d.push('console-' + type, message.text().slice(0, 300))
    })
    page.on('pageerror', (error) => {
      d.pageErrors += 1
      d.push('pageerror', (error.name + ': ' + error.message).slice(0, 300))
    })
    page.on('requestfailed', (request) => {
      d.failedRequests += 1
      d.push('requestfailed', request.method() + ' ' + new URL(request.url()).pathname + ' ' + (request.failure()?.errorText ?? ''))
    })
    page.on('framenavigated', (frame) => {
      if (frame === page.mainFrame()) d.push('navigated', new URL(frame.url()).pathname)
    })
    page.on('load', () => d.push('load'))
    return d
  }
  static for(page: Page): Diagnostics | undefined { return byPage.get(page) }
  mark(kind: string, detail = ''): void { this.push(kind, detail) }
  private push(kind: string, detail = ''): void { this.events.push({ atMs: Date.now() - this.startedAt, kind, detail }) }
  flush(info: TestInfo): string {
    const dir = path.join(runDir, 'diagnostics')
    mkdirSync(dir, { recursive: true })
    const slug = info.title.replace(/[^A-Za-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 90).toLowerCase()
    const file = path.join(dir, `${path.basename(info.file, '.ts')}--${slug}.json`)
    writeFileSync(file, JSON.stringify({
      // Identity fields come from the run's owner record (immutable for the whole run); the
      // cache state at test time is a separate, measured field.
      runId, role, coldStart, cacheMetadataPresentAtTest: existsSync(path.join(viteCacheDir, 'deps', '_metadata.json')),
      title: info.title, file: path.basename(info.file), line: info.line,
      status: info.status ?? 'unknown', expectedStatus: info.expectedStatus, durationMs: info.duration, retry: info.retry,
      errors: info.errors.map((error) => (error.message ?? '').slice(0, 600)),
      counts: { consoleErrors: this.consoleErrors, consoleWarnings: this.consoleWarnings, pageErrors: this.pageErrors, failedRequests: this.failedRequests },
      attachments: info.attachments.filter((a) => a.path).map((a) => ({ name: a.name, path: path.relative(runDir, a.path as string) })),
      events: this.events,
    }, null, 2) + '\n')
    return file
  }
}
