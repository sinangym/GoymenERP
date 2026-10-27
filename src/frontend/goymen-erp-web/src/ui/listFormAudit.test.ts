// EK-46 / BULGU-42: the static list/form audit. The rule tests pin what each rule reads; the tree guard holds
// today's findings exactly at the reviewed baseline, so a new finding fails and a fixed one must lower the baseline.
import { describe, expect, it } from 'vitest'
import { LIST_FORM_RULES, auditSource, countFindings, isAuditedFile, openingTags, stripComments } from './listFormAudit'
import { LIST_FORM_AUDIT_BASELINE } from './listFormAuditBaseline'
import { LIST_FORM_STANDARD } from './listFormStandard'

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as {
  readFileSync(path: string, encoding: 'utf8'): string
  readdirSync(path: string, options: { withFileTypes: true }): readonly { readonly name: string; isDirectory(): boolean }[]
}
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
// Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
const root = nodeProcess.cwd()

function walk(relative: string): string[] {
  return fs.readdirSync(path.resolve(root, relative), { withFileTypes: true }).flatMap((entry) => {
    const child = `${relative}/${entry.name}`
    return entry.isDirectory() ? walk(child) : [child]
  })
}

const rules = (file: string, source: string) => auditSource(file, source).map((finding) => finding.rule)

describe('EK-46 list/form audit rules', () => {
  it('names a table by aria-label, aria-labelledby or a caption', () => {
    expect(rules('src/a/A.tsx', '<table className={s.t}><thead /></table>')).toEqual(['table-name'])
    expect(rules('src/a/A.tsx', '<table aria-label={TEXT.list}></table>')).toEqual([])
    expect(rules('src/a/A.tsx', '<table aria-labelledby="h"></table>')).toEqual([])
    expect(rules('src/a/A.tsx', '<table>\n  <caption>{TEXT.list}</caption>\n</table>')).toEqual([])
  })

  it('requires a scope on every header cell and leaves thead alone', () => {
    expect(rules('src/a/A.tsx', '<thead><tr><th>Kod</th><th scope="col">Ad</th></tr></thead>')).toEqual(['th-scope'])
  })

  it('refuses a number input for decimals', () => {
    expect(rules('src/a/A.tsx', '<input type="number" />')).toEqual(['number-input'])
    expect(rules('src/a/A.tsx', "<input type={'number'} />")).toEqual(['number-input'])
    expect(rules('src/a/A.tsx', '<input type="text" inputMode="decimal" />')).toEqual([])
  })

  it('allows the page landmark only in the shell', () => {
    expect(rules('src/a/Page.tsx', '<main className={s.page}>x</main>')).toEqual(['nested-main'])
    expect(rules('src/shell/AppShell.tsx', '<main id="main-content">x</main>')).toEqual([])
  })

  it('requires an explicit button type, behind a spread too', () => {
    expect(rules('src/a/A.tsx', '<button onClick={() => go(a > b)}>Git</button>')).toEqual(['button-type'])
    expect(rules('src/a/A.tsx', '<button {...native} />')).toEqual(['button-type'])
    expect(rules('src/a/A.tsx', '<button type="button" onClick={() => go(a > b)}>Git</button>')).toEqual([])
  })

  it('flags a generic success flag and web storage in code, never in comments', () => {
    expect(rules('src/a/a.ts', 'if (response.ok) return\nconst x = window.localStorage')).toEqual(['response-ok', 'local-storage'])
    expect(rules('src/a/a.ts', '// response.ok and localStorage are not used here\n/* <main> */')).toEqual([])
    expect(rules('src/a/a.ts', 'if (!built.ok) return')).toEqual([])
  })

  it('keeps URLs and JSX apostrophes from hiding markup', () => {
    const source = "const url = 'https://ornek.test/a'\nconst view = <p>Şirket'in kayıtları</p>\nconst t = <table></table>"
    expect(stripComments(source)).toBe(source)
    expect(auditSource('src/a/A.tsx', source)).toEqual([{ file: 'src/a/A.tsx', rule: 'table-name', line: 3 }])
  })

  it('reads attributes past nested braces and quoted angle brackets', () => {
    const [tag] = openingTags('<th title="a > b" style={{ width: x > 1 ? 2 : 3 }} scope="col">', 'th')
    expect([...tag.attributes]).toEqual(['title', 'style', 'scope'])
  })

  it('audits production sources only', () => {
    expect(isAuditedFile('src/ui/DataTable.tsx')).toBe(true)
    for (const file of ['src/ui/DataTable.test.tsx', 'src/test/setup.ts', 'src/sales/testHarness.tsx', 'src/vite-env.d.ts',
      'src/shell/fixtures.ts', 'src/cari/commandPageHarness.ts']) {
      expect(isAuditedFile(file), file).toBe(false)
    }
  })
})

describe('EK-46 standard', () => {
  it('ties every static rule to exactly one checklist item, in order', () => {
    const ruled = LIST_FORM_STANDARD.flatMap((item) => (item.rule === null ? [] : [item.rule]))
    expect([...ruled].sort()).toEqual([...LIST_FORM_RULES].sort())
    expect(LIST_FORM_STANDARD.map((item) => item.id)).toEqual(
      LIST_FORM_STANDARD.map((_, index) => `LF-${String(index + 1).padStart(2, '0')}`),
    )
    for (const item of LIST_FORM_STANDARD) expect(item.evidence.length, item.id).toBeGreaterThan(0)
  })
})

describe('EK-46 audit of src/', () => {
  const files = walk('src').filter(isAuditedFile).sort()
  const counts = countFindings(files.flatMap((file) => auditSource(file, fs.readFileSync(path.resolve(root, file), 'utf8'))))

  it('scans the whole tree, the new conflict diff included', () => {
    expect(files.length).toBeGreaterThan(300)
    expect(files).toContain('src/ui/DataTable.tsx')
    expect(files).toContain('src/ui/ConflictDiff.tsx')
    expect(files).toContain('src/organization/MasterRecordForm.tsx')
  })

  it('has exactly the reviewed baseline: no new finding, and a fixed one lowers the baseline', () => {
    for (const rule of LIST_FORM_RULES) expect(counts[rule], rule).toEqual(LIST_FORM_AUDIT_BASELINE[rule])
  })

  it('keeps the shared primitives free of findings', () => {
    for (const rule of LIST_FORM_RULES) {
      for (const file of Object.keys(counts[rule])) expect(file.startsWith('src/ui/'), `${rule} ${file}`).toBe(false)
    }
  })
})
