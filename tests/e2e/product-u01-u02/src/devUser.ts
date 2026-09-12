/**
 * The bundle's development user for the L lane — compiled in from the JOINED binding,
 * never read from the environment.
 *
 * Why this exists as its own function: the first live run (`urun-l-da7ad01-01`) failed
 * at warm-up because the Vite profile's `envPrefix` (correct for the T lane) hid the
 * ambient `VITE_DEV_USER` from `import.meta.env`, the app fell back to its default
 * development user, the server published no Product capability for it and `/urun`
 * rendered the neutral block. The `define` map below is what the profile feeds Vite;
 * the regression file proves it carries exactly the bound actor and nothing else, and
 * that an empty or missing actor is refused rather than defaulted.
 *
 * Only erasable TypeScript syntax is used so Node can run the regression file directly.
 */
export const DEV_USER_DEFINE_KEY = 'import.meta.env.VITE_DEV_USER'

export class DevUserError extends Error {
  readonly code: string
  constructor(code: string, message: string) {
    super('U-01/U-02 development user refused [' + code + ']: ' + message)
    this.code = code
  }
}

/** The Vite `define` entries for the L lane, or an empty map for a lane without a binding. */
export function devUserDefine(binding: { actor?: unknown } | null): Record<string, string> {
  if (binding === null) return {}
  const actor = binding.actor
  if (typeof actor !== 'string' || actor.trim().length === 0) throw new DevUserError('ACTOR', 'the bound actor is missing or empty; the bundle must not fall back to a default development user')
  if (/[\r\n"\\]/.test(actor)) throw new DevUserError('ACTOR', 'the bound actor contains characters that cannot be compiled into a header literal')
  return { [DEV_USER_DEFINE_KEY]: JSON.stringify(actor) }
}
