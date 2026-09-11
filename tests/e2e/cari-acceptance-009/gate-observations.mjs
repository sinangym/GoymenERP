/**
 * Read-only observation helpers for the acceptance gates (F-DESKTOP-011, F-DESKTOP-012).
 *
 * Everything here computes or observes; nothing writes, nothing talks HTTP:
 *  - SHA-256 of bytes and files;
 *  - the real Git HEAD and the exact-byte diff of a worktree (the C-004 method:
 *    `git diff --binary --full-index HEAD` followed by `git diff --no-index /dev/null <untracked>`
 *    for every untracked, non-ignored path in C-locale (UTF-8 byte) order, no separators);
 *  - strict loopback validators for the API origin and the SQL endpoint;
 *  - the OS listener owning a port and that process's creation time, executable and
 *    command line (Windows: netstat + CIM), so a manifest's PID/start/listener/build
 *    claims can be checked against the machine instead of being copied.
 *
 * Git results are never guessed (C008-P2-001): a spawn failure, a signal, output cut
 * at the buffer limit, an exit status the command does not document, anything on
 * stderr, a NUL list without its final terminator or an empty/invalid patch for a file
 * Git listed all throw GitObservationError, so the caller reports not_verified instead
 * of hashing an empty chunk. File names come from `-z` output and are used exactly as
 * listed — no trimming, no unescaping — and reach Git as argv entries, never a shell.
 */
import { execFileSync, spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { readFileSync, statSync } from 'node:fs'
import path from 'node:path'

export function sha256Hex(data) {
  return createHash('sha256').update(data).digest('hex')
}

export function sha256File(filePath) {
  return sha256Hex(readFileSync(filePath))
}

/** Thrown when Git could not be run or reported anything other than its documented result. */
export class GitObservationError extends Error {
  constructor(message, details = {}) {
    super(message)
    this.name = 'GitObservationError'
    this.args = details.args ?? null
    this.status = details.status ?? null
    this.signal = details.signal ?? null
    this.code = details.code ?? null
    this.stderr = details.stderr ?? ''
  }
}

const MAX_BUFFER = 1024 * 1024 * 256
const STDERR_EXCERPT = 200
const DIFF_HEADER = Buffer.from('diff --git ')

/**
 * Run Git as an argv array (no shell) and return its stdout bytes only for a documented
 * outcome. `okStatuses` lists the exit statuses the caller documents as normal for that
 * command; everything else is an operational failure and throws:
 *  - `error` set — spawn failure (ENOENT, EACCES, …) or ENOBUFS, i.e. output cut at maxBuffer;
 *  - terminated by a signal;
 *  - an exit status outside `okStatuses`;
 *  - anything on stderr (these commands are silent when they succeed).
 * These observation-only commands disable safecrlf for this invocation: Git may
 * normalize CRLF patch output without warning. Attributes/conversion and persistent
 * config stay unchanged; every other stderr/error/signal/exit check remains strict.
 * `spawn` is injectable for isolated tests; the default is the real spawnSync.
 */
export function runGit(dir, args, { okStatuses = [0], spawn = spawnSync } = {}) {
  const fullArgs = ['-c', 'core.safecrlf=false', '-C', dir, ...args]
  const result = spawn('git', fullArgs, { encoding: 'buffer', maxBuffer: MAX_BUFFER, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true })
  const stderr = result.stderr ? result.stderr.toString('utf8').trim().slice(0, STDERR_EXCERPT) : ''
  const details = { args: fullArgs, status: result.status ?? null, signal: result.signal ?? null, code: result.error?.code ?? null, stderr }
  const what = `git ${args[0]}`
  if (result.error) {
    const truncated = result.error.code === 'ENOBUFS' ? ' (output truncated)' : ''
    throw new GitObservationError(`${what} could not run: ${result.error.code ?? result.error.message}${truncated}`, details)
  }
  if (result.signal) throw new GitObservationError(`${what} was terminated by ${result.signal}`, details)
  if (!okStatuses.includes(result.status)) throw new GitObservationError(`${what} exited ${result.status}${stderr ? `: ${stderr}` : ''}`, details)
  if (stderr.length > 0) throw new GitObservationError(`${what} reported: ${stderr}`, details)
  return { stdout: result.stdout ?? Buffer.alloc(0), status: result.status }
}

/**
 * Split NUL-terminated Git output (`-z`) into exact names. Every record ends with NUL;
 * nothing is trimmed or unescaped — a name that looks quoted or escaped IS the name.
 * A missing final terminator means the output was cut short; an empty record cannot be
 * a path. Both throw instead of yielding a shorter list.
 */
export function parseNulList(buffer) {
  const text = Buffer.isBuffer(buffer) ? buffer.toString('utf8') : String(buffer ?? '')
  if (text.length === 0) return []
  const parts = text.split('\0')
  if (parts.pop() !== '') throw new GitObservationError('NUL-terminated list is truncated: the last record has no terminator')
  if (parts.some((part) => part.length === 0)) throw new GitObservationError('NUL-terminated list contains an empty record')
  return parts
}

/** Byte order of the UTF-8 encoding — what `LC_ALL=C sort` produces for the pin method. */
export function compareBytes(a, b) {
  return Buffer.compare(Buffer.from(a, 'utf8'), Buffer.from(b, 'utf8'))
}

export function gitHead(dir, { spawn } = {}) {
  const head = runGit(dir, ['rev-parse', 'HEAD'], { spawn }).stdout.toString('utf8').replace(/\r?\n$/, '')
  if (!/^[0-9a-f]{40}$/.test(head)) throw new GitObservationError(`git rev-parse HEAD returned an unexpected value: ${JSON.stringify(head.slice(0, 60))}`)
  return head
}

const NO_INDEX_PATCH = ['diff', '--no-index', '--no-ext-diff', '--no-textconv', '--binary', '--full-index', '--no-color', '--', '/dev/null']

/**
 * The patch of one untracked file. Documented result of `git diff --no-index -- /dev/null <file>`:
 * exit 1 with the "new file" patch on stdout (a zero-byte file still yields its header).
 * Exit 0 is Git's documented "no differences" result and is accepted only for a
 * zero-byte file with empty output. A file Git listed but cannot read (Git exits 1 with
 * an empty patch and an error on stderr), any other exit status, a signal, a spawn or
 * truncation failure, or output that is not a patch throws.
 */
function untrackedPatch(dir, file, { spawn } = {}) {
  let size
  try {
    size = statSync(path.join(dir, file)).size
  } catch (error) {
    throw new GitObservationError(`untracked file ${JSON.stringify(file)} listed by Git cannot be read: ${error.code ?? error.message}`, { code: error.code ?? null })
  }
  const { stdout, status } = runGit(dir, [...NO_INDEX_PATCH, file], { okStatuses: [0, 1], spawn })
  if (status === 0) {
    if (size === 0 && stdout.length === 0) return stdout
    throw new GitObservationError(`git diff --no-index reported no difference for ${JSON.stringify(file)} (${size} bytes on disk, ${stdout.length} bytes of output)`, { status })
  }
  if (stdout.length < DIFF_HEADER.length || !stdout.subarray(0, DIFF_HEADER.length).equals(DIFF_HEADER)) {
    throw new GitObservationError(`git diff --no-index produced no valid patch for ${JSON.stringify(file)} (${stdout.length} bytes of output)`, { status })
  }
  return stdout
}

/**
 * Exact-byte diff of a worktree against HEAD, including untracked non-ignored files.
 * `excludePrefixes` removes paths (POSIX-style, relative to the worktree) from the
 * computation — used to pin the 36 application files independently of the harness.
 * Besides the pin fields the result lists the exact untracked names and the patch bytes
 * each one contributed, so a caller can see that no listed file was skipped.
 */
export function gitDiffSha256(dir, { excludePrefixes = [], spawn } = {}) {
  const excluded = (p) => excludePrefixes.some((prefix) => p.startsWith(prefix))
  const tracked = runGit(dir, ['diff', '--no-ext-diff', '--no-textconv', '--binary', '--full-index', '--no-color', 'HEAD', '--'], { spawn }).stdout
  const listed = parseNulList(runGit(dir, ['ls-files', '--others', '--exclude-standard', '-z'], { spawn }).stdout)
  const untracked = listed.filter((p) => !excluded(p)).sort(compareBytes)
  const hash = createHash('sha256')
  let bytes = 0
  // Tracked modifications of excluded paths cannot be filtered out of a single
  // `git diff` invocation without changing its bytes; callers pin trees where the
  // excluded prefix holds untracked files only (reported below).
  hash.update(tracked)
  bytes += tracked.length
  const contributions = []
  for (const file of untracked) {
    const patch = untrackedPatch(dir, file, { spawn })
    hash.update(patch)
    bytes += patch.length
    contributions.push({ path: file, bytes: patch.length })
  }
  const trackedNames = parseNulList(runGit(dir, ['diff', '--name-only', '-z', 'HEAD', '--'], { spawn }).stdout)
  return {
    sha256: hash.digest('hex'),
    bytes,
    trackedModified: trackedNames.length,
    trackedModifiedInExcluded: trackedNames.filter(excluded).length,
    untracked: untracked.length,
    untrackedPaths: untracked,
    contributions,
  }
}

/** `http://127.0.0.1:<port>` and nothing else: no other scheme, host, user, path, query or hash. */
export function validateLoopbackHttpOrigin(text, { requiredPort } = {}) {
  if (typeof text !== 'string' || text.length === 0) return { ok: false, reason: 'missing' }
  let url
  try {
    url = new URL(text)
  } catch {
    return { ok: false, reason: 'not a URL' }
  }
  if (url.protocol !== 'http:') return { ok: false, reason: `scheme ${url.protocol} is not http:` }
  if (url.hostname !== '127.0.0.1') return { ok: false, reason: `host ${url.hostname} is not 127.0.0.1` }
  if (url.username || url.password) return { ok: false, reason: 'credentials in URL' }
  if (url.pathname !== '/' && url.pathname !== '') return { ok: false, reason: `path ${url.pathname} not allowed` }
  if (url.search || url.hash) return { ok: false, reason: 'query or fragment not allowed' }
  if (text.endsWith('/') && text !== `${url.origin}/`) return { ok: false, reason: 'unexpected trailing content' }
  const port = Number(url.port)
  if (!Number.isInteger(port) || port < 1 || port > 65535) return { ok: false, reason: 'explicit port required' }
  if (requiredPort !== undefined && port !== Number(requiredPort)) return { ok: false, reason: `port ${port} is not the contract port ${requiredPort}` }
  return { ok: true, origin: url.origin, port }
}

/** `127.0.0.1,<port>` exactly (SQL Server host,port form). */
export function validateSqlLoopbackEndpoint(text, { requiredPort } = {}) {
  if (typeof text !== 'string') return { ok: false, reason: 'missing' }
  const match = /^127\.0\.0\.1,(\d{1,5})$/.exec(text)
  if (!match) return { ok: false, reason: 'not of the form 127.0.0.1,<port>' }
  const port = Number(match[1])
  if (port < 1 || port > 65535) return { ok: false, reason: 'port out of range' }
  if (requiredPort !== undefined && port !== Number(requiredPort)) return { ok: false, reason: `port ${port} is not the contract port ${requiredPort}` }
  return { ok: true, host: '127.0.0.1', port }
}

/** Parse `netstat -ano -p tcp` output into LISTENING sockets. */
export function parseNetstatListeners(text) {
  const listeners = []
  for (const raw of String(text).split(/\r?\n/)) {
    const line = raw.trim()
    if (!/^TCP\s/i.test(line) || !/LISTENING/i.test(line)) continue
    const parts = line.split(/\s+/)
    const local = parts[1] ?? ''
    const pid = Number(parts[parts.length - 1])
    const idx = local.lastIndexOf(':')
    if (idx < 0 || !Number.isInteger(pid)) continue
    listeners.push({ localAddress: local.slice(0, idx), port: Number(local.slice(idx + 1)), pid })
  }
  return listeners
}

/** The process owning a loopback listener on `port`, or null. */
export function observeListener(port, { netstatText } = {}) {
  const text = netstatText ?? execFileSync('netstat', ['-ano', '-p', 'tcp'], { encoding: 'utf8' })
  const hits = parseNetstatListeners(text).filter((l) => l.port === Number(port) && (l.localAddress === '127.0.0.1' || l.localAddress === '0.0.0.0'))
  if (hits.length === 0) return null
  return { pid: hits[0].pid, localAddress: hits[0].localAddress, listeners: hits.length }
}

/** Creation time (UTC ISO), name, executable and command line of a live process, or null. */
export function observeProcess(pid, { powershellJson } = {}) {
  let json = powershellJson
  if (json === undefined) {
    const script =
      `$p = Get-CimInstance Win32_Process -Filter "ProcessId=${Number(pid)}"; ` +
      `if ($p) { @{ pid = $p.ProcessId; name = $p.Name; createdUtc = $p.CreationDate.ToUniversalTime().ToString('o'); ` +
      `executablePath = $p.ExecutablePath; commandLine = $p.CommandLine } | ConvertTo-Json -Compress } else { 'null' }`
    json = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8' })
  }
  const parsed = JSON.parse(String(json).trim() || 'null')
  if (!parsed) return null
  return {
    pid: Number(parsed.pid),
    name: String(parsed.name ?? ''),
    createdUtc: String(parsed.createdUtc ?? ''),
    executablePath: parsed.executablePath ?? null,
    commandLine: parsed.commandLine ?? null,
  }
}

/** The first `*.dll` path on a command line (quoted or bare), or null. */
export function extractDllPath(commandLine) {
  if (typeof commandLine !== 'string') return null
  const quoted = /"([^"]+\.dll)"/i.exec(commandLine)
  if (quoted) return quoted[1]
  const bare = /(\S+\.dll)(?=\s|$)/i.exec(commandLine)
  return bare ? bare[1] : null
}

export function isoDiffSeconds(a, b) {
  const ta = Date.parse(a)
  const tb = Date.parse(b)
  if (Number.isNaN(ta) || Number.isNaN(tb)) return Number.POSITIVE_INFINITY
  return Math.abs(ta - tb) / 1000
}

export function resolveUnder(base, relative) {
  return path.isAbsolute(relative) ? relative : path.join(base, relative)
}

/**
 * The exact-byte diff between two COMMITS, restricted to pathspecs.
 *
 * This is not `gitDiffSha256`: that one hashes a dirty worktree against its own HEAD
 * and can exclude the harness. The C006 source binding's `FrontendDiffSha256` is a
 * committed range — base..head over `src/frontend/goymen-erp-web` and `tests/e2e`,
 * harness INCLUDED — so the two hashes answer different questions and neither may
 * stand in for the other. Worktree cleanliness stays a separate observation.
 *
 * The flag order is the publisher's, because the flags change the bytes.
 */
const COMMITTED_DIFF_FLAGS = ['diff', '--binary', '--full-index', '--no-color', '--no-ext-diff', '--no-textconv']

export function gitCommittedDiffSha256(dir, { base, head, paths = [], spawn } = {}) {
  for (const [label, value] of [['base', base], ['head', head]]) {
    if (typeof value !== 'string' || !/^[0-9a-f]{40}$/.test(value)) {
      throw new GitObservationError(`committed diff ${label} is not a full 40-hex commit id: ${JSON.stringify(String(value ?? '').slice(0, 60))}`)
    }
  }
  if (paths.length === 0) throw new GitObservationError('committed diff needs at least one pathspec; an unrestricted diff is a different hash')
  const { stdout } = runGit(dir, [...COMMITTED_DIFF_FLAGS, base, head, '--', ...paths], { spawn })
  return { sha256: sha256Hex(stdout), bytes: stdout.length }
}

/** A tree OID (`HEAD:src/backend`) — a Git object id, never a SHA-256 of anything. */
export function gitTreeOid(dir, spec, { spawn } = {}) {
  const oid = runGit(dir, ['rev-parse', spec], { spawn }).stdout.toString('utf8').replace(/\r?\n$/, '')
  if (!/^[0-9a-f]{40}$/.test(oid)) throw new GitObservationError(`git rev-parse ${spec} returned an unexpected value: ${JSON.stringify(oid.slice(0, 60))}`)
  return oid
}

/**
 * The committed migration INVENTORY hash: `git ls-tree -r HEAD -- <pathspec>`, keeping
 * only the lines whose path contains `/Migrations/`, in Git's own order and with their
 * complete mode/type/OID/path text, joined by LF with one final LF.
 *
 * This is a source inventory, not a live database schema hash, and must never be
 * described as one — no DDL is read and no server is contacted.
 */
export function gitMigrationSourcesSha256(dir, { pathspec = 'src/backend', spawn } = {}) {
  const stdout = runGit(dir, ['ls-tree', '-r', 'HEAD', '--', pathspec], { spawn }).stdout.toString('utf8')
  if (stdout.length === 0) throw new GitObservationError(`git ls-tree listed nothing under ${pathspec}`)
  const lines = stdout.split('\n')
  if (lines.pop() !== '') throw new GitObservationError('git ls-tree output is truncated: the last record has no line terminator')
  const migrations = lines.filter((line) => line.includes('/Migrations/'))
  return { sha256: sha256Hex(Buffer.from(migrations.map((line) => `${line}\n`).join(''), 'utf8')), lines: migrations.length }
}
