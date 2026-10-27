/**
 * EK-46 / BULGU-42 — the static half of the shared list and form standard (./listFormStandard.ts).
 *
 * It reads TypeScript/TSX source as text and never runs it; it imports nothing and touches no file. The Vitest guard
 * (./listFormAudit.test.ts) walks src/, passes every production file through `auditSource` and compares the counts
 * with the reviewed baseline (./listFormAuditBaseline.ts). The baseline only shrinks: a new finding fails, and a fixed
 * one fails until the baseline is lowered in the same change.
 *
 * Text level by design, with no parser dependency: comments are removed first; strings are kept so attribute values
 * can be read; an apostrophe inside JSX text (a Turkish suffix) does not start a string.
 */

export type ListFormRule =
  | 'table-name'
  | 'th-scope'
  | 'number-input'
  | 'nested-main'
  | 'button-type'
  | 'response-ok'
  | 'local-storage'

export const LIST_FORM_RULES: readonly ListFormRule[] = [
  'table-name',
  'th-scope',
  'number-input',
  'nested-main',
  'button-type',
  'response-ok',
  'local-storage',
]

export interface ListFormFinding {
  readonly file: string
  readonly rule: ListFormRule
  readonly line: number
}

export interface OpeningTag {
  readonly start: number
  readonly end: number
  readonly text: string
  readonly attributes: ReadonlySet<string>
  readonly hasSpread: boolean
  readonly selfClosing: boolean
}

export type ListFormCounts = Readonly<Record<ListFormRule, Readonly<Record<string, number>>>>

/** The one file that owns the page landmark (`#main-content`). */
export const MAIN_LANDMARK_OWNER = 'src/shell/AppShell.tsx'

/** Test-only helpers whose headers say so; they never ship in a screen. */
const TEST_SUPPORT_FILES: ReadonlySet<string> = new Set([
  'src/cari/commandPageHarness.ts',
  'src/cari/commandPageRender.tsx',
  'src/finance/contractExamples.ts',
  'src/finance/customerInstrumentFixtures.ts',
  'src/shell/fixtures.ts',
])

const NUMBER_TYPE = /\btype\s*=\s*(?:"number"|'number'|\{\s*(?:"number"|'number'|`number`)\s*\})/
const RESPONSE_OK = /\b(?:response|res)\s*\.\s*ok\b/g
const WEB_STORAGE = /\blocalStorage\b/g
const STRING_MAY_START_AFTER = new Set(['', '=', '(', ',', ':', '[', '!', '&', '|', '?', '{', '}', ';', '+', '-', '*', '%', '<', '>', '~', '^'])

export function isAuditedFile(file: string): boolean {
  if (!/^src\/.+\.tsx?$/.test(file)) return false
  if (file.endsWith('.d.ts') || /\.test\.tsx?$/.test(file)) return false
  if (file.startsWith('src/test/') || file.endsWith('/testHarness.tsx')) return false
  return !TEST_SUPPORT_FILES.has(file)
}

function blank(text: string): string {
  return text.replace(/[^\n]/g, ' ')
}

/** End (exclusive) of a one-line quoted string starting at `start`, or -1 when it does not close on its line. */
function quotedEnd(source: string, start: number, quote: string): number {
  for (let index = start + 1; index < source.length; index += 1) {
    const character = source[index]
    if (character === '\\') {
      index += 1
      continue
    }
    if (character === '\n') return -1
    if (character === quote) return index + 1
  }
  return -1
}

/** End (exclusive) of a template literal starting at `start`, counting `${ … }` nesting. */
function templateEnd(source: string, start: number): number {
  let depth = 0
  for (let index = start + 1; index < source.length; index += 1) {
    const character = source[index]
    if (character === '\\') {
      index += 1
      continue
    }
    if (character === '$' && source[index + 1] === '{') {
      depth += 1
      index += 1
      continue
    }
    if (character === '}' && depth > 0) {
      depth -= 1
      continue
    }
    if (character === '`' && depth === 0) return index + 1
  }
  return source.length
}

/**
 * Replaces every comment with spaces (line breaks kept, so offsets and line numbers stay true). A `//` right after a
 * colon is a URL, not a comment. A quote opens a string only where code can start one, so `Şirket'in` in JSX text is
 * left alone.
 */
export function stripComments(source: string): string {
  let result = ''
  let previous = ''
  let index = 0
  while (index < source.length) {
    const character = source[index]
    const next = source[index + 1]
    if (character === '/' && next === '*') {
      const close = source.indexOf('*/', index + 2)
      const stop = close < 0 ? source.length : close + 2
      result += blank(source.slice(index, stop))
      index = stop
      continue
    }
    if (character === '/' && next === '/' && source[index - 1] !== ':') {
      const close = source.indexOf('\n', index)
      const stop = close < 0 ? source.length : close
      result += blank(source.slice(index, stop))
      index = stop
      continue
    }
    if (character === '`') {
      const stop = templateEnd(source, index)
      result += source.slice(index, stop)
      previous = '`'
      index = stop
      continue
    }
    if ((character === '"' || character === "'") && STRING_MAY_START_AFTER.has(previous)) {
      const stop = quotedEnd(source, index, character)
      if (stop > 0) {
        result += source.slice(index, stop)
        previous = character
        index = stop
        continue
      }
    }
    result += character
    if (!/\s/.test(character)) previous = character
    index += 1
  }
  return result
}

/** End (exclusive) of the `{ … }` group opening at `open`, with quotes inside it skipped. */
function braceEnd(text: string, open: number): number {
  let depth = 0
  let quote: string | null = null
  for (let index = open; index < text.length; index += 1) {
    const character = text[index]
    if (quote !== null) {
      if (character === '\\') index += 1
      else if (character === quote) quote = null
      continue
    }
    if (character === '"' || character === "'" || character === '`') quote = character
    else if (character === '{') depth += 1
    else if (character === '}') {
      depth -= 1
      if (depth === 0) return index + 1
    }
  }
  return text.length
}

/** End (exclusive) of the opening tag whose attributes start at `from`, or -1 when it is not a JSX tag. */
function tagEnd(code: string, from: number): number {
  let index = from
  while (index < code.length) {
    const character = code[index]
    if (character === '{') {
      index = braceEnd(code, index)
      continue
    }
    if (character === '"' || character === "'") {
      const close = code.indexOf(character, index + 1)
      if (close < 0) return -1
      index = close + 1
      continue
    }
    if (character === '>') return index + 1
    if (character === '<' || character === ';') return -1
    index += 1
  }
  return -1
}

function attributesOf(inner: string): { readonly attributes: ReadonlySet<string>; readonly hasSpread: boolean } {
  const attributes = new Set<string>()
  let hasSpread = false
  let index = 0
  while (index < inner.length) {
    const character = inner[index]
    if (character === '{') {
      const close = braceEnd(inner, index)
      if (/^\{\s*\.\.\./.test(inner.slice(index, close))) hasSpread = true
      index = close
      continue
    }
    const name = /^[A-Za-z_][\w:.-]*/.exec(inner.slice(index))
    if (name === null) {
      index += 1
      continue
    }
    attributes.add(name[0])
    index += name[0].length
    while (index < inner.length && /\s/.test(inner[index])) index += 1
    if (inner[index] !== '=') continue
    index += 1
    while (index < inner.length && /\s/.test(inner[index])) index += 1
    const value = inner[index]
    if (value === '"' || value === "'") {
      const close = inner.indexOf(value, index + 1)
      index = close < 0 ? inner.length : close + 1
    } else if (value === '{') {
      index = braceEnd(inner, index)
    }
  }
  return { attributes, hasSpread }
}

/** Every opening (or self-closing) JSX tag of an intrinsic element in comment-free code. */
export function openingTags(code: string, element: string): readonly OpeningTag[] {
  const tags: OpeningTag[] = []
  for (const match of code.matchAll(new RegExp(`<${element}(?=[\\s/>])`, 'g'))) {
    const start = match.index ?? 0
    const end = tagEnd(code, start + match[0].length)
    if (end < 0) continue
    const text = code.slice(start, end)
    const selfClosing = text.endsWith('/>')
    const inner = code.slice(start + match[0].length, selfClosing ? end - 2 : end - 1)
    const { attributes, hasSpread } = attributesOf(inner)
    tags.push({ start, end, text, attributes, hasSpread, selfClosing })
  }
  return tags
}

function lineOf(code: string, index: number): number {
  let line = 1
  for (let at = 0; at < index; at += 1) if (code[at] === '\n') line += 1
  return line
}

function hasCaption(code: string, table: OpeningTag): boolean {
  if (table.selfClosing) return false
  const close = code.indexOf('</table>', table.end)
  return openingTags(code.slice(table.end, close < 0 ? code.length : close), 'caption').length > 0
}

export function auditSource(file: string, source: string): readonly ListFormFinding[] {
  const code = stripComments(source)
  const findings: ListFormFinding[] = []
  const add = (rule: ListFormRule, index: number) => findings.push({ file, rule, line: lineOf(code, index) })

  for (const table of openingTags(code, 'table')) {
    const named = table.attributes.has('aria-label') || table.attributes.has('aria-labelledby') || hasCaption(code, table)
    if (!named) add('table-name', table.start)
  }
  for (const header of openingTags(code, 'th')) if (!header.attributes.has('scope')) add('th-scope', header.start)
  for (const input of openingTags(code, 'input')) if (NUMBER_TYPE.test(input.text)) add('number-input', input.start)
  if (file !== MAIN_LANDMARK_OWNER) for (const main of openingTags(code, 'main')) add('nested-main', main.start)
  for (const button of openingTags(code, 'button')) if (!button.attributes.has('type')) add('button-type', button.start)
  for (const match of code.matchAll(RESPONSE_OK)) add('response-ok', match.index ?? 0)
  for (const match of code.matchAll(WEB_STORAGE)) add('local-storage', match.index ?? 0)
  return findings
}

/** Findings per rule and file, every rule present (an empty object when it has none). */
export function countFindings(findings: readonly ListFormFinding[]): ListFormCounts {
  const counts = Object.fromEntries(LIST_FORM_RULES.map((rule) => [rule, {} as Record<string, number>])) as Record<
    ListFormRule,
    Record<string, number>
  >
  for (const finding of findings) {
    const perFile = counts[finding.rule]
    perFile[finding.file] = (perFile[finding.file] ?? 0) + 1
  }
  return counts
}
