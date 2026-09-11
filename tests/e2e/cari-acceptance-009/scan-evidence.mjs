#!/usr/bin/env node
/**
 * Forbidden secret/token scan over evidence trees (F-DESKTOP-010, hardened in F-DESKTOP-011).
 *
 * Reports ONLY pattern names, file paths and match counts — never the matched text.
 * Text files are scanned directly; `.zip` archives (Playwright traces) are expanded
 * to a temporary directory and each entry is scanned; images and fonts are skipped.
 *
 * A scan is CLEAN only when it actually covered something: at least one root was
 * given, every root exists and is readable, every root contributed at least one
 * scanned text file, every `--require`d relative path exists and was scanned in
 * every root, no archive failed to expand, and nothing matched. Anything else is
 * `insufficient_coverage` or `error` (exit 2), never a clean pass — and a fresh
 * report is written for every invocation so a stale clean report cannot stand in
 * for the current call.
 *
 * The credential-variable names and value markers this scanner looks for are
 * assembled from fragments so that neither this source file nor a diff of it can
 * itself trip a scanner (the evidence tree carries the harness source in final-diff.bin).
 *
 * usage: node scan-evidence.mjs <outputJson> [--require rel1,rel2] <dir> [<dir> ...]
 * exit:  0 clean · 1 findings · 2 insufficient coverage or error
 */
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const J = (...parts) => parts.join('')
const U = (...parts) => parts.join('_')
const key = (...parts) => new RegExp(`"${U(...parts)}"\\s*:`)
const word = (...parts) => new RegExp(`\\b${U(...parts)}\\b`)

export const PATTERNS = [
  // process-environment dumps (JSON keys that only exist when an env object was serialized)
  { name: 'env-dump: user profile key', re: key(J('USER', 'PROFILE')) },
  { name: 'env-dump: appdata key', re: key(J('APP', 'DATA')) },
  { name: 'env-dump: local appdata key', re: key(J('LOCAL', 'APP', 'DATA')) },
  { name: 'env-dump: comspec key', re: key(J('Com', 'Spec')) },
  { name: 'env-dump: systemroot key', re: key(J('System', 'Root')) },
  { name: 'env-dump: programdata key', re: key(J('Program', 'Data')) },
  // well-known credential variable names (assembled, never literal)
  { name: 'var: anthropic api key', re: word('ANTHROPIC', 'API', 'KEY') },
  { name: 'var: claude token', re: new RegExp(`CLAUDE_[A-Z_]*${'TOK' + 'EN'}`) },
  { name: 'var: openai api key', re: word('OPENAI', 'API', 'KEY') },
  { name: 'var: github or gh token', re: new RegExp(`\\b(${U('GITHUB', 'TOKEN')}|${U('GH', 'TOKEN')})\\b`) },
  { name: 'var: npm token', re: word('NPM', 'TOKEN') },
  { name: 'var: aws secret access key', re: word('AWS', 'SECRET', 'ACCESS', 'KEY') },
  { name: 'var: azure key or secret', re: new RegExp(`AZURE_[A-Z_]*(${'K' + 'EY'}|${'SEC' + 'RET'})`) },
  { name: 'var: sa password', re: new RegExp(U('SA', J('PASS', 'WORD'))) },
  { name: 'var: connection strings', re: new RegExp(J('Connection', 'Strings', '__')) },
  // secret-shaped values (assembled)
  { name: 'value: sk- key', re: new RegExp(`\\b${'s' + 'k-'}[A-Za-z0-9_-]{16,}`) },
  { name: 'value: gh*_ token', re: new RegExp(`\\b${'g' + 'h'}[pousr]_[A-Za-z0-9]{20,}`) },
  { name: 'value: github pat', re: new RegExp(`${J('github', '_pat_')}[A-Za-z0-9_]{20,}`) },
  { name: 'value: slack xox token', re: new RegExp(`\\b${'x' + 'ox'}[abprs]-[A-Za-z0-9-]{10,}`) },
  { name: 'value: aws access key id', re: new RegExp(`\\b${'AK' + 'IA'}[0-9A-Z]{16}\\b`) },
  { name: 'value: google api key', re: new RegExp(`\\b${'AI' + 'za'}[0-9A-Za-z_-]{35}`) },
  { name: 'value: jwt', re: new RegExp(`\\b${'ey' + 'J'}[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}`) },
  { name: 'value: private key block', re: new RegExp(J('-----BEGIN ', '[A-Z ]*PRIVATE ', 'KEY-----')) },
  { name: 'value: bearer token', re: new RegExp(`\\b${'Bea' + 'rer'}\\s+[A-Za-z0-9._~+/=-]{20,}`) },
  { name: 'value: password assignment', re: new RegExp(`\\b(${'pass' + 'word'}|${'pass' + 'wd'}|${'p' + 'wd'})\\s*=\\s*[^\\s;"']{4,}`, 'i') },
  { name: 'header: authorization', re: new RegExp(`"${'author' + 'ization'}"\\s*:`, 'i') },
  { name: 'header: cookie', re: new RegExp(`"(${'coo' + 'kie'}|${'set-coo' + 'kie'})"\\s*:`, 'i') },
]
export const SKIP_EXT = new Set(['.png', '.jpg', '.jpeg', '.gif', '.webp', '.ico', '.woff', '.woff2', '.ttf'])
export const COVERAGE_NOTE =
  'Text is decoded as latin1 byte-per-character, so any single-byte or UTF-8 content is matched byte-wise; images and fonts ' +
  `(${[...SKIP_EXT].join(' ')}) are not scanned; only .zip archives are expanded; the scanner never scans its own output file.`

/** Expand a zip: Info-ZIP `unzip` (Git for Windows) first, then Windows' bsdtar. */
function extractZip(file, into) {
  const attempts = [
    ['unzip', ['-o', '-q', file, '-d', into]],
    [path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'tar.exe'), ['-xf', file, '-C', into]],
  ]
  let lastError = null
  for (const [command, args] of attempts) {
    try {
      execFileSync(command, args, { stdio: 'ignore' })
      return
    } catch (error) {
      lastError = error
    }
  }
  throw lastError ?? new Error('no zip extractor available')
}

function writeReport(output, report) {
  mkdirSync(path.dirname(path.resolve(output)), { recursive: true })
  writeFileSync(output, `${JSON.stringify(report, null, 2)}\n`, 'utf8')
}

/**
 * Run a scan. Returns the report (also written to `output`). Never throws for
 * expected failure modes; those become `outcome: 'error'` reports.
 */
export function runScan({ output, roots = [], require = [] }) {
  const startedAtUtc = new Date().toISOString()
  const outputAbs = path.resolve(output)
  const base = {
    tool: 'scan-evidence.mjs',
    startedAtUtc,
    roots: roots.map((r) => path.resolve(r)),
    required: [...require],
    patterns: PATTERNS.map((p) => p.name),
    coverageNote: COVERAGE_NOTE,
    note: 'Counts and pattern names only; matched text is never recorded or printed.',
  }
  // A fresh, explicitly incomplete report replaces whatever the previous call left.
  writeReport(outputAbs, { ...base, outcome: 'incomplete', clean: false, exitCode: 2 })

  const findings = []
  const errors = []
  const perRoot = []
  let filesScanned = 0
  let archivesExpanded = 0
  let bytesScanned = 0

  const scanText = (text, label) => {
    for (const p of PATTERNS) {
      const flags = p.re.flags.includes('g') ? p.re.flags : `${p.re.flags}g`
      const count = (text.match(new RegExp(p.re.source, flags)) ?? []).length
      if (count > 0) findings.push({ pattern: p.name, file: label, count })
    }
  }

  const scanFile = (file, label, rootStats) => {
    if (path.resolve(file) === outputAbs) return
    const ext = path.extname(file).toLowerCase()
    if (SKIP_EXT.has(ext)) {
      rootStats.skipped += 1
      return
    }
    if (ext === '.zip') {
      const tmp = mkdtempSync(path.join(tmpdir(), 'f011-scan-'))
      try {
        extractZip(file, tmp)
        archivesExpanded += 1
        rootStats.archivesExpanded += 1
        walk(tmp, tmp, `${label}::`, rootStats)
      } catch (error) {
        errors.push({ kind: 'archive', file: label, note: String(error?.message ?? error).slice(0, 120) })
      } finally {
        rmSync(tmp, { recursive: true, force: true })
      }
      return
    }
    let buffer
    try {
      buffer = readFileSync(file)
    } catch (error) {
      errors.push({ kind: 'read', file: label, note: String(error?.code ?? error).slice(0, 60) })
      return
    }
    filesScanned += 1
    rootStats.filesScanned += 1
    bytesScanned += buffer.length
    rootStats.scannedRelative.add(label.split('::')[0].slice(rootStats.prefix.length))
    scanText(buffer.toString('latin1'), label)
  }

  const walk = (dir, baseDir, prefix, rootStats) => {
    let entries
    try {
      entries = readdirSync(dir)
    } catch (error) {
      errors.push({ kind: 'readdir', file: `${prefix}${path.relative(baseDir, dir).replace(/\\/g, '/')}`, note: String(error?.code ?? error).slice(0, 60) })
      return
    }
    for (const entry of entries) {
      const full = path.join(dir, entry)
      let st
      try {
        st = statSync(full)
      } catch (error) {
        errors.push({ kind: 'stat', file: `${prefix}${entry}`, note: String(error?.code ?? error).slice(0, 60) })
        continue
      }
      if (st.isDirectory()) {
        walk(full, baseDir, prefix, rootStats)
        continue
      }
      scanFile(full, `${prefix}${path.relative(baseDir, full).replace(/\\/g, '/')}`, rootStats)
    }
  }

  if (roots.length === 0) errors.push({ kind: 'roots', file: null, note: 'no root given' })
  for (const root of roots) {
    const abs = path.resolve(root)
    const prefix = `${path.basename(abs)}/`
    const stats = { root: abs, exists: false, isDirectory: false, prefix, filesScanned: 0, archivesExpanded: 0, skipped: 0, scannedRelative: new Set(), requiredMissing: [] }
    perRoot.push(stats)
    if (!existsSync(abs)) {
      errors.push({ kind: 'root-missing', file: abs, note: 'root does not exist' })
      continue
    }
    let st
    try {
      st = statSync(abs)
    } catch (error) {
      errors.push({ kind: 'root-unreadable', file: abs, note: String(error?.code ?? error).slice(0, 60) })
      continue
    }
    stats.exists = true
    if (!st.isDirectory()) {
      errors.push({ kind: 'root-not-directory', file: abs, note: 'root is not a directory' })
      continue
    }
    stats.isDirectory = true
    walk(abs, abs, prefix, stats)
    for (const rel of require) {
      const normalized = rel.replace(/\\/g, '/')
      if (!stats.scannedRelative.has(normalized)) stats.requiredMissing.push(normalized)
    }
  }

  const coverageProblems = []
  if (roots.length === 0) coverageProblems.push('no root given')
  for (const r of perRoot) {
    if (r.exists && r.isDirectory && r.filesScanned === 0) coverageProblems.push(`root ${r.root} contributed no scanned text file`)
    for (const rel of r.requiredMissing) coverageProblems.push(`root ${r.root} missing required evidence ${rel}`)
  }
  if (filesScanned === 0 && coverageProblems.length === 0) coverageProblems.push('zero files scanned')

  let outcome
  if (errors.length > 0) outcome = 'error'
  else if (coverageProblems.length > 0) outcome = 'insufficient_coverage'
  else if (findings.length > 0) outcome = 'findings'
  else outcome = 'clean'
  const exitCode = outcome === 'clean' ? 0 : outcome === 'findings' ? 1 : 2

  const report = {
    ...base,
    finishedAtUtc: new Date().toISOString(),
    outcome,
    clean: outcome === 'clean',
    exitCode,
    filesScanned,
    archivesExpanded,
    bytesScanned,
    perRoot: perRoot.map((r) => ({ root: r.root, exists: r.exists, isDirectory: r.isDirectory, filesScanned: r.filesScanned, archivesExpanded: r.archivesExpanded, skippedByExtension: r.skipped, requiredMissing: r.requiredMissing })),
    coverageProblems,
    errors,
    findingCount: findings.length,
    zeroFindings: findings.length === 0,
    findings,
  }
  writeReport(outputAbs, report)
  return report
}

export function parseArgs(argv) {
  const [output, ...rest] = argv
  const roots = []
  const require = []
  for (let i = 0; i < rest.length; i += 1) {
    if (rest[i] === '--require') {
      const value = rest[i + 1] ?? ''
      require.push(...value.split(',').map((s) => s.trim()).filter(Boolean))
      i += 1
      continue
    }
    roots.push(rest[i])
  }
  return { output, roots, require }
}

function main() {
  const { output, roots, require } = parseArgs(process.argv.slice(2))
  if (!output) {
    console.error('usage: node scan-evidence.mjs <outputJson> [--require rel1,rel2] <dir> [<dir> ...]')
    process.exit(2)
  }
  const report = runScan({ output, roots, require })
  console.log(`outcome=${report.outcome} files=${report.filesScanned} archives=${report.archivesExpanded} findings=${report.findingCount} errors=${report.errors.length} coverageProblems=${report.coverageProblems.length}`)
  for (const f of report.findings) console.log(`  finding ${f.pattern}  ${f.file}  x${f.count}`)
  for (const e of report.errors) console.log(`  error ${e.kind}  ${e.file ?? ''}  ${e.note}`)
  for (const c of report.coverageProblems) console.log(`  coverage ${c}`)
  process.exit(report.exitCode)
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main()
