/**
 * Gate regressions for scan-evidence.mjs (C007-P2-003). Node's built-in runner,
 * temp directories only, no network, no evidence tree touched.
 */
import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { after, test } from 'node:test'
import { PATTERNS, parseArgs, runScan } from './scan-evidence.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const SCRIPT = path.join(here, 'scan-evidence.mjs')
const tmp = mkdtempSync(path.join(tmpdir(), 'scan-evidence-test-'))
after(() => rmSync(tmp, { recursive: true, force: true }))

const scratch = (name) => {
  const dir = path.join(tmp, name)
  mkdirSync(dir, { recursive: true })
  return dir
}
const report = (file) => JSON.parse(readFileSync(file, 'utf8'))
const SYNTHETIC_VALUE = `${'s' + 'k-'}${'x'.repeat(24)}` // assembled so this test file never carries the marker literally

test('no root: insufficient/erroneous, fresh report, exit code 2', () => {
  const out = path.join(scratch('noroot'), 'scan.json')
  writeFileSync(out, JSON.stringify({ outcome: 'clean', clean: true, stale: true }))
  const r = runScan({ output: out, roots: [] })
  assert.equal(r.outcome, 'error')
  assert.equal(r.clean, false)
  assert.equal(r.exitCode, 2)
  const written = report(out)
  assert.equal(written.clean, false)
  assert.equal(written.stale, undefined, 'the stale clean report must be replaced')
  assert.ok(written.errors.some((e) => e.kind === 'roots'))
})

test('missing root: error report written, exit code 2, filesScanned 0', () => {
  const out = path.join(scratch('missing'), 'scan.json')
  const r = runScan({ output: out, roots: [path.join(tmp, 'does-not-exist')] })
  assert.equal(r.outcome, 'error')
  assert.equal(r.filesScanned, 0)
  assert.equal(r.exitCode, 2)
  assert.ok(report(out).errors.some((e) => e.kind === 'root-missing'))
})

test('root with only excluded files: insufficient coverage, not clean', () => {
  const dir = scratch('images')
  writeFileSync(path.join(dir, 'a.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47]))
  writeFileSync(path.join(dir, 'b.woff2'), Buffer.from([0, 1, 2]))
  const out = path.join(scratch('images-out'), 'scan.json')
  const r = runScan({ output: out, roots: [dir] })
  assert.equal(r.outcome, 'insufficient_coverage')
  assert.equal(r.clean, false)
  assert.equal(r.exitCode, 2)
  assert.equal(r.perRoot[0].skippedByExtension, 2)
  assert.ok(r.coverageProblems.some((c) => c.includes('contributed no scanned text file')))
})

test('empty root: insufficient coverage', () => {
  const dir = scratch('empty')
  const out = path.join(scratch('empty-out'), 'scan.json')
  const r = runScan({ output: out, roots: [dir] })
  assert.equal(r.outcome, 'insufficient_coverage')
  assert.equal(r.exitCode, 2)
})

test('clean fixture: outcome clean, exit 0, coverage counted per root', () => {
  const dir = scratch('clean')
  writeFileSync(path.join(dir, 'run.log'), '# cmd: something\nEXIT=0\n')
  mkdirSync(path.join(dir, 'sub'))
  writeFileSync(path.join(dir, 'sub', 'measure.json'), JSON.stringify({ value: 1, note: 'nothing sensitive here' }))
  const out = path.join(scratch('clean-out'), 'scan.json')
  const r = runScan({ output: out, roots: [dir], require: ['run.log', 'sub/measure.json'] })
  assert.equal(r.outcome, 'clean')
  assert.equal(r.clean, true)
  assert.equal(r.exitCode, 0)
  assert.equal(r.filesScanned, 2)
  assert.equal(r.perRoot[0].requiredMissing.length, 0)
  assert.equal(report(out).outcome, 'clean')
})

test('required evidence missing in a root: insufficient coverage even when nothing matched', () => {
  const dir = scratch('required')
  writeFileSync(path.join(dir, 'run.log'), 'EXIT=0\n')
  const out = path.join(scratch('required-out'), 'scan.json')
  const r = runScan({ output: out, roots: [dir], require: ['run.log', 'results.json'] })
  assert.equal(r.outcome, 'insufficient_coverage')
  assert.deepEqual(r.perRoot[0].requiredMissing, ['results.json'])
  assert.equal(r.exitCode, 2)
})

test('synthetic markers are reported by name and count only; the value never appears in the report', () => {
  const dir = scratch('markers')
  const envDump = { config: { webServer: { env: { [`${'USER'}${'PROFILE'}`]: 'x', [`${'Program'}${'Data'}`]: 'y' } } } }
  writeFileSync(path.join(dir, 'results.json'), JSON.stringify(envDump))
  writeFileSync(path.join(dir, 'notes.txt'), `token ${SYNTHETIC_VALUE} end\n`)
  const out = path.join(scratch('markers-out'), 'scan.json')
  const r = runScan({ output: out, roots: [dir] })
  assert.equal(r.outcome, 'findings')
  assert.equal(r.exitCode, 1)
  const names = r.findings.map((f) => f.pattern)
  assert.ok(names.includes('env-dump: user profile key'))
  assert.ok(names.includes('env-dump: programdata key'))
  assert.ok(names.includes('value: sk- key'))
  const text = readFileSync(out, 'utf8')
  assert.equal(text.includes(SYNTHETIC_VALUE), false, 'matched value must not be written')
  assert.equal(text.includes('x'.repeat(24)), false)
})

test('broken zip: error outcome with a fresh report, exit code 2, matched nothing is not clean', () => {
  const dir = scratch('zip')
  writeFileSync(path.join(dir, 'trace.zip'), Buffer.from('this is not a zip archive'))
  writeFileSync(path.join(dir, 'ok.txt'), 'plain\n')
  const out = path.join(scratch('zip-out'), 'scan.json')
  const r = runScan({ output: out, roots: [dir] })
  assert.equal(r.outcome, 'error')
  assert.equal(r.exitCode, 2)
  assert.ok(r.errors.some((e) => e.kind === 'archive'))
  assert.equal(r.clean, false)
})

test('the scanner does not scan its own output and reports its pattern names as names only', () => {
  const dir = scratch('self')
  writeFileSync(path.join(dir, 'a.txt'), 'nothing\n')
  const out = path.join(dir, 'scan.json')
  const first = runScan({ output: out, roots: [dir] })
  assert.equal(first.outcome, 'clean')
  const second = runScan({ output: out, roots: [dir] })
  assert.equal(second.outcome, 'clean', 'a previous report in the root must not produce findings')
  assert.equal(second.filesScanned, 1)
  assert.equal(PATTERNS.length, second.patterns.length)
})

test('CLI: missing root exits 2 and writes a structured failed report; --require is parsed', () => {
  const out = path.join(scratch('cli'), 'scan.json')
  const args = parseArgs([out, '--require', 'run.log,report.json', 'rootA', 'rootB'])
  assert.deepEqual(args.roots, ['rootA', 'rootB'])
  assert.deepEqual(args.require, ['run.log', 'report.json'])
  let code = 0
  try {
    execFileSync(process.execPath, [SCRIPT, out, path.join(tmp, 'nope')], { stdio: 'pipe' })
  } catch (error) {
    code = error.status
  }
  assert.equal(code, 2)
  assert.equal(report(out).outcome, 'error')
})

test('CLI: clean fixture exits 0', () => {
  const dir = scratch('cli-clean')
  writeFileSync(path.join(dir, 'run.log'), 'EXIT=0\n')
  const out = path.join(scratch('cli-clean-out'), 'scan.json')
  const stdout = execFileSync(process.execPath, [SCRIPT, out, dir], { encoding: 'utf8' })
  assert.match(stdout, /outcome=clean/)
  assert.equal(report(out).exitCode, 0)
})
