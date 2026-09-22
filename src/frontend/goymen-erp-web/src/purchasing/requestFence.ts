export interface RequestToken {
  readonly generation: number
  readonly signal: AbortSignal
}

export interface RequestFence {
  capture(): RequestToken
  invalidate(): void
  isCurrent(token: RequestToken): boolean
}

export function createRequestFence(): RequestFence {
  let generation = 0
  let active: AbortController | null = null
  return {
    capture() {
      active?.abort()
      active = new AbortController()
      return { generation, signal: active.signal }
    },
    invalidate() {
      generation += 1
      active?.abort()
      active = null
    },
    isCurrent(token) {
      return token.generation === generation && !token.signal.aborted
    },
  }
}
