/**
 * Exact wire key set of one decoded object: every schema-required key is present and no key lies outside
 * required ∪ optional (OpenAPI properties that are not in `required`). An extra or missing key fails closed.
 */
export const exactKeys = (value: Record<string, unknown>, required: readonly string[], optional: readonly string[] = []): boolean =>
  required.every(key => Object.hasOwn(value, key)) && Object.keys(value).every(key => required.includes(key) || optional.includes(key))
