/** Test-only browser File fixture. JSDOM 27 implements FileReader but not Blob.arrayBuffer. */
export function readableFile(parts: BlobPart[], name: string, options?: FilePropertyBag): File {
  const file = new File(parts, name, options)
  if (typeof file.arrayBuffer !== 'function') {
    Object.defineProperty(file, 'arrayBuffer', {
      configurable: true,
      value: () => new Promise<ArrayBuffer>((resolve, reject) => {
        const reader = new FileReader()
        reader.onerror = () => reject(reader.error)
        reader.onabort = () => reject(new DOMException('File read aborted', 'AbortError'))
        reader.onload = () => resolve(reader.result as ArrayBuffer)
        reader.readAsArrayBuffer(file)
      }),
    })
  }
  return file
}
