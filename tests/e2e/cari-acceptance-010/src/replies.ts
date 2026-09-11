export type HttpReply = { status: number; body: unknown; headers?: Record<string, string>; fixture?: string }
export type Reply = HttpReply | { abort: true }
export const json = (body: unknown, status = 200, headers?: Record<string, string>, fixture?: string): HttpReply => ({ status, body, headers, fixture })
export const problem = (status: number, code: string): Reply => ({
  status, body: { type: 'about:blank', title: 'Kontrollü T yanıtı', status, code },
})
export function held() {
  let release = () => {}
  const promise = new Promise<void>((done) => { release = done })
  return { promise, release: () => release() }
}
