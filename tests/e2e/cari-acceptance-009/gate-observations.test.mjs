/**
 * Regressions for the read-only observation helpers (F-DESKTOP-011, F-DESKTOP-012).
 * Pure functions, throw-away Git repositories under the temp directory, and in-process
 * spawn doubles for the Git failure modes that cannot be produced reliably on disk.
 * Nothing here opens a socket or touches the acceptance worktree.
 */
import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { appendFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, unlinkSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, test } from 'node:test'
import {
  GitObservationError,
  compareBytes,
  extractDllPath,
  gitCommittedDiffSha256,
  gitDiffSha256,
  gitHead,
  gitMigrationSourcesSha256,
  gitTreeOid,
  isoDiffSeconds,
  observeListener,
  observeProcess,
  parseNetstatListeners,
  parseNulList,
  runGit,
  sha256File,
  sha256Hex,
  validateLoopbackHttpOrigin,
  validateSqlLoopbackEndpoint,
} from './gate-observations.mjs'

const tmp = mkdtempSync(path.join(tmpdir(), 'gate-observations-test-'))
after(() => rmSync(tmp, { recursive: true, force: true }))

const NO_INDEX = ['diff', '--no-index', '--no-ext-diff', '--no-textconv', '--binary', '--full-index', '--no-color', '--', '/dev/null']

/** A fresh repository with one committed file. `core.quotepath=true` (Git's default) so non-ASCII names are quoted in non-`-z` output. */
let repoCounter = 0
function makeRepo() {
  repoCounter += 1
  const repo = path.join(tmp, `repo-${repoCounter}`)
  mkdirSync(repo)
  const git = (...args) => execFileSync('git', ['-C', repo, ...args], { encoding: 'utf8' })
  git('init', '-q')
  git('config', 'user.email', 't@example.test')
  git('config', 'user.name', 'gate test')
  git('config', 'core.autocrlf', 'false')
  git('config', 'core.quotepath', 'true')
  writeFileSync(path.join(repo, 'tracked.txt'), 'one\n')
  git('add', 'tracked.txt')
  git('commit', '-q', '-m', 'base')
  return { repo, git }
}

/**
 * Independent strict recomputation of the C-004 method: every Git result is asserted
 * (documented status, no signal, nothing on stderr) instead of being caught, and the
 * names come from `-z`. This is the reference the helper must agree with.
 */
function strictGit(repo, args, okStatuses = [0]) {
  const r = spawnSync('git', ['-C', repo, ...args], { encoding: 'buffer', maxBuffer: 64 * 1024 * 1024 })
  assert.equal(r.error, undefined, `spawn error for git ${args[0]}: ${r.error?.code}`)
  assert.equal(r.signal, null)
  assert.ok(okStatuses.includes(r.status), `git ${args.join(' ')} exited ${r.status}: ${r.stderr}`)
  assert.equal(r.stderr.toString('utf8'), '', `git ${args[0]} wrote to stderr`)
  return r.stdout
}
function strictExpected(repo, excludePrefixes = []) {
  const excluded = (p) => excludePrefixes.some((prefix) => p.startsWith(prefix))
  let expected = strictGit(repo, ['diff', '--no-ext-diff', '--no-textconv', '--binary', '--full-index', '--no-color', 'HEAD', '--'])
  const names = strictGit(repo, ['ls-files', '--others', '--exclude-standard', '-z']).toString('utf8').split('\0')
  assert.equal(names.pop(), '', 'NUL list must end with a terminator')
  const files = names.filter((n) => !excluded(n)).sort((a, b) => Buffer.compare(Buffer.from(a, 'utf8'), Buffer.from(b, 'utf8')))
  const chunks = {}
  for (const file of files) {
    const chunk = strictGit(repo, [...NO_INDEX, file], [1])
    assert.ok(chunk.length > 0 && chunk.subarray(0, 11).toString() === 'diff --git ', `no patch for ${file}`)
    chunks[file] = chunk
    expected = Buffer.concat([expected, chunk])
  }
  return { sha256: sha256Hex(expected), bytes: expected.length, files, chunks }
}

/** A spawn double: calls whose argv contains `match` get `fake`; everything else runs the real Git. */
function fakeSpawn(match, fake) {
  return (cmd, args, opts) => (args.includes(match) ? { status: null, signal: null, stdout: Buffer.alloc(0), stderr: Buffer.alloc(0), ...fake } : spawnSync(cmd, args, opts))
}
const gitError = (pattern) => (error) => error instanceof GitObservationError && pattern.test(error.message)

test('sha256 of known bytes', () => {
  assert.equal(sha256Hex('abc'), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')
  const file = path.join(tmp, 'abc.txt')
  writeFileSync(file, 'abc')
  assert.equal(sha256File(file), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')
})

test('strict loopback API origin: accepts only http://127.0.0.1:<port>', () => {
  assert.equal(validateLoopbackHttpOrigin('http://127.0.0.1:15080', { requiredPort: 15080 }).ok, true)
  assert.equal(validateLoopbackHttpOrigin('http://127.0.0.1:15080/', { requiredPort: 15080 }).ok, true)
  for (const bad of ['http://localhost:15080', 'https://127.0.0.1:15080', 'http://127.0.0.1', 'http://127.0.0.1:15080/x', 'http://127.0.0.1:15080?q=1', 'http://u:p@127.0.0.1:15080', 'http://127.0.0.2:15080', 'ftp://127.0.0.1:15080', '', undefined, 'not a url']) {
    assert.equal(validateLoopbackHttpOrigin(bad, { requiredPort: 15080 }).ok, false, String(bad))
  }
  assert.equal(validateLoopbackHttpOrigin('http://127.0.0.1:15081', { requiredPort: 15080 }).ok, false)
})

test('strict SQL loopback endpoint: 127.0.0.1,<port> only', () => {
  assert.deepEqual(validateSqlLoopbackEndpoint('127.0.0.1,15433', { requiredPort: 15433 }), { ok: true, host: '127.0.0.1', port: 15433 })
  for (const bad of ['127.0.0.1:15433', 'localhost,15433', '127.0.0.1,0', '127.0.0.1,99999', '10.0.0.1,15433', ' 127.0.0.1,15433', undefined]) {
    assert.equal(validateSqlLoopbackEndpoint(bad, { requiredPort: 15433 }).ok, false, String(bad))
  }
  assert.equal(validateSqlLoopbackEndpoint('127.0.0.1,15434', { requiredPort: 15433 }).ok, false)
})

test('netstat parsing and listener lookup use a supplied text, never the network', () => {
  const text = [
    'Active Connections',
    '  Proto  Local Address          Foreign Address        State           PID',
    '  TCP    127.0.0.1:15080        0.0.0.0:0              LISTENING       34876',
    '  TCP    127.0.0.1:15080        127.0.0.1:50225        TIME_WAIT       0',
    '  TCP    0.0.0.0:135            0.0.0.0:0              LISTENING       1234',
    '  TCP    [::]:15080             [::]:0                 LISTENING       777',
  ].join('\r\n')
  const listeners = parseNetstatListeners(text)
  assert.deepEqual(listeners.find((l) => l.pid === 34876), { localAddress: '127.0.0.1', port: 15080, pid: 34876 })
  assert.equal(observeListener(15080, { netstatText: text }).pid, 34876)
  assert.equal(observeListener(15081, { netstatText: text }), null)
})

test('process observation parses the PowerShell JSON it is given', () => {
  const json = JSON.stringify({ pid: 34876, name: 'dotnet.exe', createdUtc: '2026-09-05T23:33:07.4882460Z', executablePath: 'C:\\dotnet.exe', commandLine: '"C:\\dotnet.exe" C:\\x\\Served.dll serve' })
  const proc = observeProcess(34876, { powershellJson: json })
  assert.equal(proc.pid, 34876)
  assert.equal(proc.createdUtc, '2026-09-05T23:33:07.4882460Z')
  assert.equal(observeProcess(1, { powershellJson: 'null' }), null)
  assert.equal(extractDllPath('"C:\\Program Files\\dotnet\\dotnet.exe" C:\\x\\Served.dll serve'), 'C:\\x\\Served.dll')
  assert.equal(extractDllPath('"C:\\d.exe" "C:\\with space\\Served.dll" serve'), 'C:\\with space\\Served.dll')
  assert.equal(extractDllPath('node app.js'), null)
  assert.equal(extractDllPath(null), null)
})

test('iso timestamp skew in seconds; invalid input is infinite', () => {
  assert.equal(isoDiffSeconds('2026-09-05T23:33:07.4882460Z', '2026-09-05T23:33:07.4882461Z') < 0.001, true)
  assert.equal(isoDiffSeconds('2026-09-05T23:33:07Z', '2026-09-05T23:33:10Z'), 3)
  assert.equal(isoDiffSeconds('garbage', '2026-09-05T23:33:10Z'), Number.POSITIVE_INFINITY)
})

test('git diff hash follows the C-004 method, excludes untracked harness paths, and matches a strict independent recomputation', () => {
  const { repo } = makeRepo()
  writeFileSync(path.join(repo, 'tracked.txt'), 'two\n')
  writeFileSync(path.join(repo, 'new.txt'), 'new\n')
  mkdirSync(path.join(repo, 'harness'))
  writeFileSync(path.join(repo, 'harness', 'tool.mjs'), 'export const x = 1\n')

  assert.equal(gitHead(repo), strictGit(repo, ['rev-parse', 'HEAD']).toString('utf8').trim())
  const full = gitDiffSha256(repo)
  const appOnly = gitDiffSha256(repo, { excludePrefixes: ['harness/'] })
  assert.equal(full.untracked, 2)
  assert.deepEqual(full.untrackedPaths, ['harness/tool.mjs', 'new.txt'])
  assert.equal(appOnly.untracked, 1)
  assert.deepEqual(appOnly.untrackedPaths, ['new.txt'])
  assert.notEqual(full.sha256, appOnly.sha256)
  assert.equal(appOnly.trackedModified, 1)
  assert.equal(appOnly.trackedModifiedInExcluded, 0)

  const expectedFull = strictExpected(repo)
  const expectedApp = strictExpected(repo, ['harness/'])
  assert.equal(full.sha256, expectedFull.sha256)
  assert.equal(full.bytes, expectedFull.bytes)
  assert.equal(appOnly.sha256, expectedApp.sha256)
  assert.equal(appOnly.bytes, expectedApp.bytes)
  assert.deepEqual(appOnly.contributions, [{ path: 'new.txt', bytes: expectedApp.chunks['new.txt'].length }])
})

test('C008-P2-001: NUL lists are split exactly — no trimming, no unescaping; truncation and empty records throw', () => {
  const list = parseNulList(Buffer.from('a.txt\0 lead.txt\0trail.txt \0boş luk/dosya ş.md\0"\\303\\247.txt"\0new\nline.txt\0', 'utf8'))
  assert.deepEqual(list, ['a.txt', ' lead.txt', 'trail.txt ', 'boş luk/dosya ş.md', '"\\303\\247.txt"', 'new\nline.txt'])
  assert.deepEqual(parseNulList(Buffer.alloc(0)), [])
  assert.throws(() => parseNulList(Buffer.from('a.txt\0b.txt')), gitError(/truncated/))
  assert.throws(() => parseNulList(Buffer.from('a.txt\0\0b.txt\0')), gitError(/empty record/))
  assert.deepEqual(['ç.txt', 'z.txt', 'Z.txt', 'a b.txt'].sort(compareBytes), ['Z.txt', 'a b.txt', 'z.txt', 'ç.txt'])
})

test('F013-P2-001: command-local safecrlf suppresses CRLF warnings without changing patch bytes, hashes, files or persistent config', () => {
  const { repo } = makeRepo()
  // Attributes belong only to this disposable fixture, not the acceptance worktree.
  // info/attributes reproduces its text=auto eol=lf rule without editing .gitattributes.
  const attributes = path.join(repo, '.git', 'info', 'attributes')
  writeFileSync(attributes, '* text=auto eol=lf\n')
  const originals = {
    'tracked.txt': Buffer.from('two\r\n'),
    'compose-fixture.json': Buffer.from('{\r\n  "fixture": "compose"\r\n}\r\n'),
    'run-fixture.json': Buffer.from('{\r\n  "fixture": "run"\r\n}\r\n'),
  }
  for (const [name, bytes] of Object.entries(originals)) writeFileSync(path.join(repo, name), bytes)
  const configBefore = readFileSync(path.join(repo, '.git', 'config'))
  const attributesBefore = readFileSync(attributes)
  const settings = ['-c', 'core.autocrlf=true', '-c', 'core.safecrlf=warn']
  const diffArgs = ['diff', '--no-ext-diff', '--no-textconv', '--binary', '--full-index', '--no-color', 'HEAD', '--']
  const files = ['compose-fixture.json', 'run-fixture.json']
  const expectedChunks = []
  const silentChunks = []
  for (const [args, status] of [[diffArgs, 0], ...files.map((name) => [[...NO_INDEX, name], 1])]) {
    const plain = spawnSync('git', [...settings, '-C', repo, ...args], { encoding: 'buffer' })
    const quiet = spawnSync('git', [...settings, '-c', 'core.safecrlf=false', '-C', repo, ...args], { encoding: 'buffer' })
    for (const result of [plain, quiet]) {
      assert.equal(result.error, undefined)
      assert.equal(result.signal, null)
      assert.equal(result.status, status)
      assert.ok(result.stdout.subarray(0, 11).equals(Buffer.from('diff --git ')))
    }
    assert.match(plain.stderr.toString('utf8'), /CRLF will be replaced by LF/, 'the fixture must reproduce the original warning')
    assert.equal(quiet.stderr.length, 0)
    assert.deepEqual(quiet.stdout, plain.stdout, 'only the diagnostic changes, never patch bytes')
    assert.equal(plain.stdout.includes(13), false, 'normalization still produces LF patch bytes')
    expectedChunks.push(plain.stdout)
    silentChunks.push(quiet.stdout)
  }
  const expected = Buffer.concat(expectedChunks)
  assert.equal(sha256Hex(expected), sha256Hex(Buffer.concat(silentChunks)))
  const calls = []
  const spawn = (cmd, args, opts) => {
    calls.push(args)
    return spawnSync(cmd, [...settings, ...args], opts)
  }
  const actual = gitDiffSha256(repo, { spawn })
  assert.equal(actual.sha256, sha256Hex(expected), 'reference uses the original warning-producing Git stdout')
  assert.equal(actual.bytes, expected.length)
  assert.equal(actual.trackedModified, 1)
  assert.deepEqual(actual.untrackedPaths, files)
  assert.deepEqual(actual.contributions, files.map((name, i) => ({ path: name, bytes: expectedChunks[i + 1].length })))
  assert.equal(gitHead(repo, { spawn }), strictGit(repo, ['rev-parse', 'HEAD']).toString('utf8').trim())
  assert.ok(calls.every((args) => args[0] === '-c' && args[1] === 'core.safecrlf=false'))
  assert.ok(calls.every((args) => ['rev-parse', 'diff', 'ls-files'].includes(args[4])), 'only observation commands')
  for (const [name, bytes] of Object.entries(originals)) assert.deepEqual(readFileSync(path.join(repo, name)), bytes)
  assert.deepEqual(readFileSync(path.join(repo, '.git', 'config')), configBefore)
  assert.deepEqual(readFileSync(attributes), attributesBefore)
})

test('C008-P2-001: Git receives an argv array without a shell, and listed names reach it untouched', () => {
  const { repo } = makeRepo()
  const name = 'çağrı ödeme.txt'
  writeFileSync(path.join(repo, name), 'a\n')
  const calls = []
  const spawn = (cmd, args, opts) => {
    calls.push({ cmd, args, opts })
    return spawnSync(cmd, args, opts)
  }
  gitDiffSha256(repo, { spawn })
  const fileCall = calls.find((c) => c.args.includes('--no-index'))
  assert.equal(fileCall.cmd, 'git')
  assert.equal(fileCall.args.at(-1), name)
  assert.ok(calls.every((c) => c.cmd === 'git' && Array.isArray(c.args) && c.opts.shell === undefined))
  assert.ok(calls.some((c) => c.args.includes('ls-files') && c.args.includes('-z')))
  assert.ok(calls.some((c) => c.args.includes('--name-only') && c.args.includes('-z')))
  const head = runGit(repo, ['rev-parse', 'HEAD'], { spawn })
  assert.equal(head.status, 0)
  assert.match(head.stdout.toString('utf8'), /^[0-9a-f]{40}\r?\n$/)
})

test('C008-P2-001 (real files): Turkish and space-containing untracked names are listed raw, hashed, and their bytes change the hash', () => {
  const { repo, git } = makeRepo()
  const turkish = 'çağrı ödeme.txt'
  const nested = 'boşluk lu/dosya ş.md'
  writeFileSync(path.join(repo, 'ascii.txt'), 'plain\n')
  writeFileSync(path.join(repo, turkish), 'ilk satır\n')
  mkdirSync(path.join(repo, 'boşluk lu'))
  writeFileSync(path.join(repo, nested), 'iç\n')
  // Why `-z`: the plain listing quotes and octal-escapes these names; that text is not a path.
  const plainListing = git('ls-files', '--others', '--exclude-standard')
  assert.match(plainListing, /"[^"\n]*\\3[0-7]{2}/, 'plain listing quotes/escapes non-ASCII names')
  assert.equal(plainListing.includes(turkish), false)

  const first = gitDiffSha256(repo)
  assert.equal(first.untracked, 3)
  assert.deepEqual(first.untrackedPaths, ['ascii.txt', nested, turkish])
  assert.ok(first.contributions.every((c) => c.bytes > 0), 'every listed file contributes patch bytes')
  const expected = strictExpected(repo)
  assert.deepEqual(expected.files, first.untrackedPaths)
  assert.equal(first.sha256, expected.sha256)
  assert.equal(first.bytes, expected.bytes)
  assert.ok(expected.chunks[turkish].includes(Buffer.from('+ilk satır\n', 'utf8')), 'the Turkish file content is inside the hashed bytes')
  assert.ok(expected.chunks[nested].includes(Buffer.from('+iç\n', 'utf8')))

  appendFileSync(path.join(repo, turkish), 'ikinci\n')
  const second = gitDiffSha256(repo)
  assert.notEqual(second.sha256, first.sha256)
  assert.ok(second.bytes > first.bytes)
  assert.equal(second.untracked, 3)
  assert.equal(second.sha256, strictExpected(repo).sha256)
  const before = first.contributions.find((c) => c.path === turkish).bytes
  const afterChange = second.contributions.find((c) => c.path === turkish).bytes
  assert.ok(afterChange > before)
  assert.equal(second.bytes - first.bytes, afterChange - before, 'only that file changed')

  writeFileSync(path.join(repo, 'boş.txt'), '') // zero-byte file: Git still emits its "new file" record with exit 1
  const third = gitDiffSha256(repo)
  assert.equal(third.untracked, 4)
  assert.ok(third.contributions.find((c) => c.path === 'boş.txt').bytes > 0)
  assert.equal(third.sha256, strictExpected(repo).sha256)
  // Names containing '"', a newline or a trailing space cannot be created on Windows; they are
  // covered by the NUL-parsing test above and are not claimed as real-file coverage here.
})

test('C008-P2-001 (real Git): a listed file that disappears before its patch throws instead of hashing an empty chunk', () => {
  const { repo } = makeRepo()
  writeFileSync(path.join(repo, 'keep.txt'), 'keep\n')
  writeFileSync(path.join(repo, 'vanish.txt'), 'gone\n')
  const spawn = (cmd, args, opts) => {
    const result = spawnSync(cmd, args, opts)
    if (args.includes('ls-files')) unlinkSync(path.join(repo, 'vanish.txt'))
    return result
  }
  assert.throws(() => gitDiffSha256(repo, { spawn }), gitError(/"vanish\.txt" listed by Git cannot be read/))
})

test('C008-P2-001 (real Git): a directory that is not a repository throws for HEAD and for the diff', () => {
  const notARepo = path.join(tmp, 'not-a-repo')
  mkdirSync(notARepo, { recursive: true })
  // rev-parse exits 128 ("fatal: not a git repository"); `git diff HEAD` exits 129 with a warning — both are undocumented for the helper.
  assert.throws(() => gitHead(notARepo), gitError(/exited 128: fatal: not a git repository/i))
  assert.throws(() => gitDiffSha256(notARepo), gitError(/exited 12[89]: .*not a git repository/i))
})

test('C008-P2-001 (controlled doubles): undocumented Git outcomes for a file patch throw; the documented exit 1 + patch is accepted', () => {
  const { repo } = makeRepo()
  writeFileSync(path.join(repo, 'new.txt'), 'new\n')
  const patch = strictGit(repo, [...NO_INDEX, 'new.txt'], [1])
  const cases = [
    ['unexpected exit 128', { status: 128, stderr: Buffer.from('fatal: bad revision') }, /exited 128: fatal: bad revision/],
    ['unexpected exit 129 (bad option)', { status: 129, stderr: Buffer.from('error: unknown option') }, /exited 129/],
    ['spawn failure ENOENT', { error: Object.assign(new Error('spawn git ENOENT'), { code: 'ENOENT' }) }, /could not run: ENOENT/],
    ['spawn failure EACCES', { error: Object.assign(new Error('spawn git EACCES'), { code: 'EACCES' }) }, /could not run: EACCES/],
    ['output cut at maxBuffer (ENOBUFS)', { error: Object.assign(new Error('spawnSync git ENOBUFS'), { code: 'ENOBUFS' }), signal: 'SIGTERM', stdout: patch.subarray(0, 40) }, /ENOBUFS \(output truncated\)/],
    ['killed by a signal', { signal: 'SIGKILL' }, /terminated by SIGKILL/],
    ['exit 1, empty stdout, read error on stderr (what Git does for a missing file)', { status: 1, stderr: Buffer.from("error: Could not access 'new.txt'") }, /Could not access/],
    ['exit 1 with a patch but noise on stderr', { status: 1, stdout: patch, stderr: Buffer.from('warning: something') }, /reported: warning: something/],
    ['exit 1 with garbage instead of a patch', { status: 1, stdout: Buffer.from('garbage') }, /no valid patch/],
    ['exit 1 with empty stdout and silent stderr', { status: 1 }, /no valid patch/],
    ['exit 0 ("no difference") for a file that has bytes', { status: 0 }, /no difference/],
  ]
  for (const [label, fake, pattern] of cases) {
    assert.throws(() => gitDiffSha256(repo, { spawn: fakeSpawn('--no-index', fake) }), gitError(pattern), label)
  }
  const real = gitDiffSha256(repo)
  const accepted = gitDiffSha256(repo, { spawn: fakeSpawn('--no-index', { status: 1, stdout: patch }) })
  assert.equal(accepted.sha256, real.sha256)
  assert.equal(accepted.bytes, real.bytes)
  assert.deepEqual(accepted.contributions, [{ path: 'new.txt', bytes: patch.length }])
})

test('C008-P2-001 (controlled doubles): failures of rev-parse, the tracked diff and the file listing propagate', () => {
  const { repo } = makeRepo()
  writeFileSync(path.join(repo, 'new.txt'), 'new\n')
  assert.throws(() => gitHead(repo, { spawn: fakeSpawn('rev-parse', { status: 128, stderr: Buffer.from('fatal: not a git repository') }) }), gitError(/exited 128: fatal: not a git repository/))
  assert.throws(() => gitHead(repo, { spawn: fakeSpawn('rev-parse', { status: 0, stdout: Buffer.from('HEAD\n') }) }), gitError(/unexpected value/))
  assert.throws(() => gitHead(repo, { spawn: fakeSpawn('rev-parse', { error: Object.assign(new Error('spawn git ENOENT'), { code: 'ENOENT' }) }) }), gitError(/could not run: ENOENT/))
  assert.throws(() => gitDiffSha256(repo, { spawn: fakeSpawn('HEAD', { status: 0, stderr: Buffer.from('warning: unexpected') }) }), gitError(/reported: warning: unexpected/))
  assert.throws(() => gitDiffSha256(repo, { spawn: fakeSpawn('HEAD', { status: 128, stderr: Buffer.from('fatal: bad object HEAD') }) }), gitError(/exited 128/))
  assert.throws(() => gitDiffSha256(repo, { spawn: fakeSpawn('ls-files', { status: 0, stdout: Buffer.from('new.txt') }) }), gitError(/truncated/))
  assert.throws(() => gitDiffSha256(repo, { spawn: fakeSpawn('ls-files', { error: Object.assign(new Error('spawn git EACCES'), { code: 'EACCES' }) }) }), gitError(/could not run: EACCES/))
  assert.throws(() => gitDiffSha256(repo, { spawn: fakeSpawn('ls-files', { status: 0, stdout: Buffer.from('new.txt\0\0') }) }), gitError(/empty record/))
  const errorDetails = (() => {
    try {
      gitDiffSha256(repo, { spawn: fakeSpawn('HEAD', { status: 128, stderr: Buffer.from('fatal: x') }) })
      return null
    } catch (error) {
      return error
    }
  })()
  assert.equal(errorDetails.name, 'GitObservationError')
  assert.equal(errorDetails.status, 128)
  assert.deepEqual(errorDetails.args.slice(0, 5), ['-c', 'core.safecrlf=false', '-C', repo, 'diff'])
})

// ---- C006 source-schema recipes (e35ad38) -------------------------------------------------
// Three observations the old gate had no equivalent of: a COMMITTED range diff, a tree
// OID, and the committed migration inventory. Each is measured against a real throw-away
// repository, because each recipe's value depends on details (flag order, pathspecs,
// line terminators) that a hand-written expectation would not catch.

test('gitCommittedDiffSha256 hashes the committed range over the given pathspecs, harness included', () => {
  const { repo, git } = makeRepo()
  mkdirSync(path.join(repo, 'src/frontend/goymen-erp-web'), { recursive: true })
  mkdirSync(path.join(repo, 'tests/e2e'), { recursive: true })
  mkdirSync(path.join(repo, 'src/backend'), { recursive: true })
  writeFileSync(path.join(repo, 'src/frontend/goymen-erp-web/app.ts'), 'export const a = 1\n')
  writeFileSync(path.join(repo, 'tests/e2e/harness.ts'), 'export const h = 1\n')
  writeFileSync(path.join(repo, 'src/backend/Api.cs'), 'class Api {}\n')
  git('add', '-A')
  git('commit', '-q', '-m', 'base')
  const base = gitHead(repo)
  writeFileSync(path.join(repo, 'src/frontend/goymen-erp-web/app.ts'), 'export const a = 2\n')
  writeFileSync(path.join(repo, 'tests/e2e/harness.ts'), 'export const h = 2\n')
  writeFileSync(path.join(repo, 'src/backend/Api.cs'), 'class Api { int x; }\n')
  git('add', '-A')
  git('commit', '-q', '-m', 'head')
  const head = gitHead(repo)

  const paths = ['src/frontend/goymen-erp-web', 'tests/e2e']
  const result = gitCommittedDiffSha256(repo, { base, head, paths })
  const expected = strictGit(repo, ['diff', '--binary', '--full-index', '--no-color', '--no-ext-diff', '--no-textconv', base, head, '--', ...paths])
  assert.equal(result.sha256, sha256Hex(expected))
  assert.equal(result.bytes, expected.length)

  // The harness is INSIDE this range: dropping tests/e2e changes the hash, so the old
  // harness-excluded worktree diff can never stand in for it.
  assert.notEqual(gitCommittedDiffSha256(repo, { base, head, paths: ['src/frontend/goymen-erp-web'] }).sha256, result.sha256)
  // The backend change is outside it: the pathspecs are part of the recipe.
  assert.equal(String(expected).includes('src/backend/Api.cs'), false)
  // A dirty worktree does not move a committed range.
  writeFileSync(path.join(repo, 'src/frontend/goymen-erp-web/app.ts'), 'export const a = 99\n')
  assert.equal(gitCommittedDiffSha256(repo, { base, head, paths }).sha256, result.sha256)
})

test('gitCommittedDiffSha256 refuses an incomplete recipe instead of hashing something else', () => {
  const { repo } = makeRepo()
  const head = gitHead(repo)
  assert.throws(() => gitCommittedDiffSha256(repo, { base: 'HEAD~1', head, paths: ['src'] }), gitError(/base is not a full 40-hex commit id/))
  assert.throws(() => gitCommittedDiffSha256(repo, { base: head, head: 'main', paths: ['src'] }), gitError(/head is not a full 40-hex commit id/))
  // An unrestricted diff is a different hash, so an empty pathspec list is an error, not
  // a convenience.
  assert.throws(() => gitCommittedDiffSha256(repo, { base: head, head, paths: [] }), gitError(/at least one pathspec/))
  assert.throws(() => gitCommittedDiffSha256(repo, { base: head, head, paths: ['src'], spawn: fakeSpawn('diff', { status: 128, stderr: Buffer.from('fatal: bad object') }) }), gitError(/exited 128/))
})

test('gitTreeOid returns the tree object id of a path, which is not a content hash', () => {
  const { repo, git } = makeRepo()
  mkdirSync(path.join(repo, 'src/backend'), { recursive: true })
  writeFileSync(path.join(repo, 'src/backend/Api.cs'), 'class Api {}\n')
  git('add', '-A')
  git('commit', '-q', '-m', 'backend')
  const oid = gitTreeOid(repo, 'HEAD:src/backend')
  assert.match(oid, /^[0-9a-f]{40}$/)
  assert.equal(oid, String(strictGit(repo, ['rev-parse', 'HEAD:src/backend'])).trim())
  // A change under the path moves the tree; a change outside it does not.
  writeFileSync(path.join(repo, 'src/backend/Api.cs'), 'class Api { int x; }\n')
  git('add', '-A')
  git('commit', '-q', '-m', 'changed')
  assert.notEqual(gitTreeOid(repo, 'HEAD:src/backend'), oid)
  assert.throws(() => gitTreeOid(repo, 'HEAD:does/not/exist'), gitError(/exited 128/))
})

test('gitMigrationSourcesSha256 hashes the committed migration inventory, in Git order, LF-terminated', () => {
  const { repo, git } = makeRepo()
  mkdirSync(path.join(repo, 'src/backend/Data/Migrations'), { recursive: true })
  mkdirSync(path.join(repo, 'src/backend/Api'), { recursive: true })
  writeFileSync(path.join(repo, 'src/backend/Data/Migrations/0001_Init.cs'), 'init\n')
  writeFileSync(path.join(repo, 'src/backend/Data/Migrations/0002_Party.cs'), 'party\n')
  writeFileSync(path.join(repo, 'src/backend/Api/Controller.cs'), 'controller\n')
  git('add', '-A')
  git('commit', '-q', '-m', 'migrations')

  const listing = String(strictGit(repo, ['ls-tree', '-r', 'HEAD', '--', 'src/backend']))
  const migrationLines = listing.split('\n').filter((line) => line.includes('/Migrations/'))
  const expected = sha256Hex(Buffer.from(migrationLines.map((line) => `${line}\n`).join(''), 'utf8'))
  const result = gitMigrationSourcesSha256(repo, {})
  assert.equal(result.sha256, expected)
  assert.equal(result.lines, 2)
  // It is a SOURCE inventory: the non-migration file is excluded, and editing it changes
  // nothing here. No database is read and none could be.
  writeFileSync(path.join(repo, 'src/backend/Api/Controller.cs'), 'controller v2\n')
  git('add', '-A')
  git('commit', '-q', '-m', 'api only')
  assert.equal(gitMigrationSourcesSha256(repo, {}).sha256, expected)
  // A new migration does change it.
  writeFileSync(path.join(repo, 'src/backend/Data/Migrations/0003_Roles.cs'), 'roles\n')
  git('add', '-A')
  git('commit', '-q', '-m', 'third migration')
  const grown = gitMigrationSourcesSha256(repo, {})
  assert.equal(grown.lines, 3)
  assert.notEqual(grown.sha256, expected)
  assert.throws(() => gitMigrationSourcesSha256(repo, { spawn: fakeSpawn('ls-tree', { status: 0, stdout: Buffer.from('100644 blob abc\tsrc/backend/Data/Migrations/x.cs') }) }), gitError(/truncated/))
  assert.throws(() => gitMigrationSourcesSha256(repo, { pathspec: 'does/not/exist' }), gitError(/listed nothing/))
})
