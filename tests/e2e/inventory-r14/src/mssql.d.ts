/**
 * The workspace installs `mssql` without its types. The R14/R19 specs only read, so this declares the small
 * read surface they use: a pool, a request with typed inputs, and `query<T>` answering `recordset: T[]`.
 */
declare module 'mssql' {
  interface SqlRequest {
    input(name: string, type: unknown, value?: unknown): SqlRequest
    query<T = any>(command: string): Promise<{ readonly recordset: T[] }>
  }

  interface SqlConnectionPool {
    connect(): Promise<SqlConnectionPool>
    request(): SqlRequest
    close(): Promise<void>
  }

  const sql: {
    readonly ConnectionPool: new (config: string) => SqlConnectionPool
    readonly [type: string]: any
  }
  export default sql
}
