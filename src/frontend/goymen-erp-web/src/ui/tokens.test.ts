// GOY-DS-001 T0: the token file may only declare --goy-ds-* custom properties on :root,
// and index.css may only add an import of it. These are global-cascade guards: a reset,
// an element rule, a legacy alias or a font asset here would restyle every screen.
import { describe, expect, it } from 'vitest'

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
// Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
const read = (file: string) => fs.readFileSync(path.resolve(nodeProcess.cwd(), file), 'utf8')
const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '')

const tokensCss = stripComments(read('src/ui/tokens.css'))
const primitivesCss = stripComments(read('src/ui/primitives.module.css'))
const indexCss = stripComments(read('src/index.css'))

const declarations = [...tokensCss.matchAll(/([-\w]+)\s*:\s*([^;]+);/g)].map((m) => ({ name: m[1], value: m[2].trim() }))
const tokenNames = new Set(declarations.map((d) => d.name))

describe('GOY-DS-001 tokens.css', () => {
  it('has exactly one rule, on :root, and no at-rules', () => {
    const selectors = [...tokensCss.matchAll(/([^{}]+)\{/g)].map((m) => m[1].trim())
    expect(selectors).toEqual([':root'])
    expect(tokensCss).not.toMatch(/@(import|font-face|media|layer|supports)/)
  })

  it('declares only --goy-ds-* custom properties, each once', () => {
    expect(declarations.length).toBeGreaterThan(20)
    for (const d of declarations) expect(d.name).toMatch(/^--goy-ds-[a-z0-9-]+$/)
    expect(tokenNames.size).toBe(declarations.length)
  })

  it('aliases no legacy names and loads no asset', () => {
    expect(tokensCss).not.toMatch(/--shell-|--color-/)
    expect(tokensCss).not.toMatch(/url\(/i)
  })

  it('leads with IBM Plex and keeps the current system chain as the fallback (FONT-01)', () => {
    const sans = declarations.find((d) => d.name === '--goy-ds-font-sans')?.value
    const mono = declarations.find((d) => d.name === '--goy-ds-font-mono')?.value
    expect(sans).toBe("'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif")
    expect(mono).toBe("'IBM Plex Mono', ui-monospace, SFMono-Regular, Consolas, 'Liberation Mono', monospace")
  })
})

describe('GOY-DS-001 primitives.module.css', () => {
  it('reads colours only through defined --goy-ds-* tokens', () => {
    expect(primitivesCss).not.toMatch(/#[0-9a-f]{3,8}\b/i)
    expect(primitivesCss).not.toMatch(/rgba?\(/i)
    const used = [...primitivesCss.matchAll(/var\((--[-\w]+)/g)].map((m) => m[1])
    expect(used.length).toBeGreaterThan(0)
    for (const name of used) expect(tokenNames.has(name), `${name} is not defined in tokens.css`).toBe(true)
  })

  it('styles only its own classes — no element, universal or :root selector', () => {
    const selectors = [...primitivesCss.matchAll(/([^{}]+)\{/g)].flatMap((m) => m[1].split(',').map((s) => s.trim()))
    for (const s of selectors) expect(s, s).toMatch(/^\.[a-z]/i)
  })
})

describe('index.css', () => {
  it('only gains the namespaced token and local font imports; its existing rules are unchanged', () => {
    const lines = indexCss.split('\n').map((l) => l.trim()).filter(Boolean)
    expect(lines).toEqual(["@import './ui/tokens.css';", "@import './ui/fonts.css';", 'body { margin: 0; }', '#root { min-height: 100vh; }'])
  })
})
