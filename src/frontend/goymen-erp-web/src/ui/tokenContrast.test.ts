// EK-46 LF-07: WCAG 2.2 contrast of the GOY-DS-001 token pairs the primitives and the shell draw. Text needs 4.5:1
// (1.4.3) and the focus indicator 3:1 (1.4.11). Line and dot tokens under 3:1 are listed exactly: they may only be
// decorative (the StatusIndicator dot is aria-hidden; its text carries the meaning) or the edge of a button its text
// already identifies — never an input's only boundary.
import { describe, expect, it } from 'vitest'

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
const read = (file: string) => fs.readFileSync(path.resolve(nodeProcess.cwd(), file), 'utf8')

const TEXT_MINIMUM = 4.5
const NON_TEXT_MINIMUM = 3

const tokens = new Map(
  [...read('src/ui/tokens.css').replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/--goy-ds-color-([a-z0-9-]+)\s*:\s*(#[0-9a-f]{6})\s*;/gi)]
    .map((match) => [match[1], match[2]] as const),
)

function colour(name: string): string {
  const value = tokens.get(name)
  if (value === undefined) throw new Error(`--goy-ds-color-${name} is not a six-digit hex token`)
  return value
}

function luminance(hex: string): number {
  const [red, green, blue] = [1, 3, 5].map((offset) => {
    const channel = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * red + 0.7152 * green + 0.0722 * blue
}

function contrast(foreground: string, background: string): number {
  const [light, dark] = [luminance(colour(foreground)), luminance(colour(background))].sort((a, b) => b - a)
  return (light + 0.05) / (dark + 0.05)
}

const TEXT_PAIRS: Array<[string, string]> = [
  ['text', 'surface'],
  ['text', 'surface-muted'],
  ['text', 'surface-pressed'],
  ['text', 'danger-soft'],
  ['text-secondary', 'surface'],
  ['text-secondary', 'surface-muted'],
  ['text-secondary', 'surface-pressed'],
  ['text-secondary', 'danger-soft'],
  ['text-tertiary', 'surface'],
  ['text-tertiary', 'surface-muted'],
  ['accent-text', 'surface'],
  ['accent-text', 'accent-soft'],
  ['on-accent', 'accent'],
  ['on-accent', 'accent-hover'],
  ['on-accent', 'danger-fill'],
  ['on-accent', 'danger-fill-hover'],
  ['danger', 'surface'],
  ['danger', 'danger-soft'],
  ['success', 'surface'],
  ['warning', 'surface'],
]

const FOCUS_PAIRS: Array<[string, string]> = [
  ['accent', 'surface'],
  ['accent', 'surface-muted'],
  ['accent', 'danger-soft'],
]

const LINE_AND_DOT_TOKENS = ['border', 'border-control', 'border-hover', 'success-dot', 'warning-dot', 'danger-dot', 'info-dot', 'neutral-dot']

/** Measured on white: border 1.275, border-control 1.474, border-hover 1.954, warning-dot 2.487, neutral-dot 2.642. */
const KNOWN_BELOW_NON_TEXT_MINIMUM = ['border', 'border-control', 'border-hover', 'warning-dot', 'neutral-dot']

describe('EK-46 LF-07 token contrast', () => {
  it.each(TEXT_PAIRS)('%s on %s reaches 4.5:1', (foreground, background) => {
    expect(contrast(foreground, background)).toBeGreaterThanOrEqual(TEXT_MINIMUM)
  })

  it.each(FOCUS_PAIRS)('the %s focus outline on %s reaches 3:1', (foreground, background) => {
    expect(contrast(foreground, background)).toBeGreaterThanOrEqual(NON_TEXT_MINIMUM)
  })

  it('lists every line or dot token under 3:1 on white, so a new one is a reviewed decision', () => {
    const below = LINE_AND_DOT_TOKENS.filter((name) => contrast(name, 'surface') < NON_TEXT_MINIMUM)
    expect(below).toEqual(KNOWN_BELOW_NON_TEXT_MINIMUM)
  })

  it('keeps the status dot decorative, so its colour is never the only signal', () => {
    expect(read('src/ui/StatusIndicator.tsx')).toMatch(/className=\{styles\.dot\} aria-hidden="true"/)
  })
})
