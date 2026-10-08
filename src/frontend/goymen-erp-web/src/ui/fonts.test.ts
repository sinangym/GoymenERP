// GOY-DS-001 FONT-01: the local IBM Plex faces are exactly IBM's official files, declared with IBM's own
// weights and unicode ranges, loaded only from the app's assets, and covering Turkish in every shipped weight.
import { describe, expect, it } from 'vitest'

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as {
  readFileSync(path: string, encoding?: 'utf8'): string & Uint8Array
  readdirSync(path: string): string[]
}
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
const crypto = nodeProcess.getBuiltinModule('node:crypto') as {
  createHash(alg: string): { update(data: Uint8Array): { digest(enc: 'hex'): string } }
}
// Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
const at = (file: string) => path.resolve(nodeProcess.cwd(), file)
const fontsCss = fs.readFileSync(at('src/ui/fonts.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')
const sha256 = (file: string) => crypto.createHash('sha256').update(fs.readFileSync(at(file))).digest('hex')

// From the coordinator manifest FONT01_OFFICIAL_SOURCE_VERIFICATION/PROVENANCE_AND_CANDIDATE_SCOPE.json
// (@ibm/plex-sans@1.1.0, @ibm/plex-mono@2.5.0, package/fonts/split/woff2/*, unmodified).
const OFFICIAL: ReadonlyArray<readonly [string, string, number, string, number]> = [
  ['IBMPlexSans-Regular-Latin1', 'IBM Plex Sans', 400, 'b5ad7bd39f996144915f0ad9849a90183b27d8c28ad97ed98af5b1bebc51f6b1', 20984],
  ['IBMPlexSans-Regular-Latin2', 'IBM Plex Sans', 400, 'fcfa82e0d46ed01a8df38610b451860ca7e7518770c52fdd2a1d136247c40f28', 15212],
  ['IBMPlexSans-Medium-Latin1', 'IBM Plex Sans', 500, 'b5610af04d0d4b5a14a621d96d974b993e945a065db1a8861918f69ef9321934', 21960],
  ['IBMPlexSans-Medium-Latin2', 'IBM Plex Sans', 500, '46fbe4f398e7ff3cc7773a1156b1efef0f81e4d034f682369c221de587f443ca', 15372],
  ['IBMPlexSans-SemiBold-Latin1', 'IBM Plex Sans', 600, 'fff0ab3a88b0b4aa0b693e4f0201359a15183b08e3fa5696d1918d8f0ade8ad5', 22260],
  ['IBMPlexSans-SemiBold-Latin2', 'IBM Plex Sans', 600, '2bc85a8e53540c4c211f7af36e806bb045ba7da8946bb221d87144c12c83d577', 15624],
  ['IBMPlexSans-Bold-Latin1', 'IBM Plex Sans', 700, '914f1400f363e636b6f9cc7965aa807ff01e93586e1437617525cba0a62aa78d', 21256],
  ['IBMPlexSans-Bold-Latin2', 'IBM Plex Sans', 700, '56e3282a5068bfabb5c71cb19400695a707e710e03393a67d3bd059156b60933', 15376],
  ['IBMPlexMono-Regular-Latin1', 'IBM Plex Mono', 400, 'e8993d946649b9d01abb1ed06d574b19d8ea3e66b5c3948602db335c44c18e56', 17544],
  ['IBMPlexMono-Regular-Latin2', 'IBM Plex Mono', 400, '8c5e48e4c6ee60346744db62fd8bbf530c21e531e1a1498c3a410ed8f7d3986e', 13260],
  ['IBMPlexMono-Medium-Latin1', 'IBM Plex Mono', 500, '41201b658a328b9d00368215c2f1102770f80b15952ab82631e4006255e6365d', 17868],
  ['IBMPlexMono-Medium-Latin2', 'IBM Plex Mono', 500, '86208dbe8e00256a77bc3edd09aa6a584b0da393caa1fb200d44eac1d578512c', 13424],
]
const OFL_SHA256 = '91c25c350d3cac39da2736d74f7ba37ef648f5237a4e330a240615bc8d8c4360'

// Copied from IBM's own split CSS (IBMPlexSans-*.css / IBMPlexMono-*.css) for the same files.
const LATIN2 = 'U+0100-0101, U+0104-0130, U+0132-0151, U+0154-017F, U+018F, U+0192, U+01A0-01A1, U+01AF-01B0, U+01FA-01FF, U+0218-021B, U+0237, U+0259, U+1E80-1E85, U+1E9E, U+20A1, U+20A4, U+20A6, U+20A8-20AA, U+20AD-20AE, U+20B1-20B2, U+20B4-20B5, U+20B8-20BA, U+20BD, U+20BF'
const LATIN1_TAIL = 'U+0020-007E, U+00A0-00FF, U+0131, U+0152-0153, U+02C6, U+02DA, U+02DC, U+2013-2014, U+2018-201A, U+201C-201E, U+2020-2022, U+2026, U+2030, U+2039-203A, U+2044, U+20AC, U+2122, U+2212, U+FB01-FB02'
const EXPECTED_RANGE: Record<string, string> = {
  'IBM Plex Sans Latin1': `U+0000, U+000D, ${LATIN1_TAIL}`,
  'IBM Plex Mono Latin1': LATIN1_TAIL,
  'IBM Plex Sans Latin2': LATIN2,
  'IBM Plex Mono Latin2': LATIN2,
}

type Face = { family: string; weight: number; style: string; display: string; src: string; range: string }
const faces: Face[] = [...fontsCss.matchAll(/@font-face\s*\{([^}]*)\}/g)].map((m) => {
  const get = (prop: string) => new RegExp(`${prop}\\s*:\\s*([^;]+);`).exec(m[1])?.[1].trim() ?? ''
  return { family: get('font-family').replace(/'/g, ''), weight: Number(get('font-weight')), style: get('font-style'), display: get('font-display'), src: get('src'), range: get('unicode-range') }
})
const fileOf = (f: Face) => /url\('\.\.\/assets\/fonts\/([^']+)\.woff2'\) format\('woff2'\)$/.exec(f.src)?.[1]
const inRange = (range: string, cp: number) => range.split(',').some((part) => {
  const [a, b] = part.trim().replace('U+', '').split('-').map((h) => parseInt(h, 16)); return cp >= a && cp <= (b ?? a)
})

describe('FONT-01 assets', () => {
  it('ships exactly the 12 official woff2 files and the OFL, byte-identical to the manifest', () => {
    expect(fs.readdirSync(at('src/assets/fonts')).sort()).toEqual([...OFFICIAL.map(([n]) => `${n}.woff2`), 'OFL.txt'].sort())
    for (const [name, , , sha, bytes] of OFFICIAL) {
      expect(fs.readFileSync(at(`src/assets/fonts/${name}.woff2`)).length, name).toBe(bytes)
      expect(sha256(`src/assets/fonts/${name}.woff2`), name).toBe(sha)
    }
    expect(sha256('src/assets/fonts/OFL.txt')).toBe(OFL_SHA256)
    expect(fs.readFileSync(at('src/assets/fonts/OFL.txt'), 'utf8')).toContain('SIL Open Font License')
  })
})

describe('FONT-01 fonts.css', () => {
  it('declares one face per official file, with IBM weights, normal style and swap display', () => {
    expect(faces).toHaveLength(OFFICIAL.length)
    for (const [name, family, weight] of OFFICIAL) {
      const face = faces.find((f) => fileOf(f) === name)
      expect(face, name).toBeDefined()
      expect(face).toMatchObject({ family, weight, style: 'normal', display: 'swap' })
    }
  })

  it('uses IBM unicode ranges for each subset', () => {
    for (const f of faces) expect(f.range, fileOf(f)).toBe(EXPECTED_RANGE[`${f.family} ${fileOf(f)?.endsWith('Latin1') ? 'Latin1' : 'Latin2'}`])
  })

  it('covers İıŞşĞğÇçÖöÜü (and ₺) in every shipped family and weight', () => {
    const groups = new Map<string, string[]>()
    for (const f of faces) groups.set(`${f.family} ${f.weight}`, [...(groups.get(`${f.family} ${f.weight}`) ?? []), f.range])
    expect(groups.size).toBe(6)
    for (const [key, ranges] of groups) for (const ch of 'İıŞşĞğÇçÖöÜü₺') {
      expect(ranges.some((r) => inRange(r, ch.codePointAt(0)!)), `${key} ${ch}`).toBe(true)
    }
  })

  it('loads only from the app assets: no external URL, no local(), no other at-rule', () => {
    expect(fontsCss).not.toMatch(/https?:|\/\/fonts\.|local\(/i)
    expect(fontsCss).not.toMatch(/@(import|media|layer|supports)/)
    for (const f of faces) expect(fileOf(f), f.src).toBeDefined()
  })
})
