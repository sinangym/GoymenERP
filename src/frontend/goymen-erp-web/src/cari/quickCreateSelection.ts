/** Select only an exact account returned by the caller's existing authorized owner read. */
export async function findQuickCreatedOffer<T>(
  signal: AbortSignal,
  read: (cursor?: string) => Promise<{ readonly items: readonly T[]; readonly nextCursor: string | null } | null>,
  matches: (item: T) => boolean,
): Promise<T | null> {
  const seen = new Set<string>()
  let cursor: string | undefined
  while (!signal.aborted) {
    const page = await read(cursor)
    if (signal.aborted || page === null) return null
    const item = page.items.find(matches)
    if (item !== undefined) return item
    if (page.nextCursor === null || seen.has(page.nextCursor)) return null
    seen.add(page.nextCursor)
    cursor = page.nextCursor
  }
  return null
}
