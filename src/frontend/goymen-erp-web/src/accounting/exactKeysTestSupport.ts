/** Test support only: builds the "one extra key" and "one required key removed" variants of a valid synthetic payload. */
type Path = readonly (string | number)[]
type Json = Record<string, unknown>
const at = (root: unknown, path: Path): Json => {
  const target = path.reduce<unknown>((value, key) => (value as Record<string | number, unknown>)[key], root)
  if (typeof target !== 'object' || target === null || Array.isArray(target)) throw new Error(`no object at /${path.join('/')}`)
  return target as Json
}
/** For each [path, requiredKey] level: the payload with an unknown key added there, and with `requiredKey` removed there. */
export function keyProbes(valid: unknown, levels: readonly (readonly [Path, string])[]): readonly (readonly [string, unknown])[] {
  return levels.flatMap(([path, key]) => {
    const extra = structuredClone(valid); at(extra, path).sentetikExtraKey = null
    const missing = structuredClone(valid); const target = at(missing, path)
    if (!Object.hasOwn(target, key)) throw new Error(`fixture has no ${key} at /${path.join('/')}`)
    delete target[key]
    return [[`extra key at /${path.join('/')}`, extra], [`missing ${key} at /${path.join('/')}`, missing]] as const
  })
}
