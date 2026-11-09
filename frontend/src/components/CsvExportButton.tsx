import { useState } from 'react'
import { Download } from 'lucide-react'

export function CsvExportButton({
  filename,
  columns,
  rows,
  disabled,
}: {
  filename: string
  columns: { key: string; label: string }[]
  rows: Record<string, unknown>[]
  disabled?: boolean
}) {
  const [error, setError] = useState('')
  function download() {
    setError('')
    let url: string | undefined
    try {
      const lines = [
        columns.map((column) => csvCell(column.label)).join(','),
        ...rows.map((row) => columns.map((column) => csvCell(row[column.key])).join(',')),
      ]
      url = URL.createObjectURL(new Blob(['\uFEFF', lines.join('\r\n'), '\r\n'], { type: 'text/csv;charset=utf-8' }))
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = `${filename.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').trim() || 'view'}.csv`
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'CSV export failed.')
    } finally {
      if (url) {
        const downloadUrl = url
        window.setTimeout(() => URL.revokeObjectURL(downloadUrl), 1000)
      }
    }
  }
  return (
    <>
      <button
        type="button"
        className="secondary"
        disabled={disabled || !columns.length}
        title="Export displayed rows and visible columns as CSV"
        onClick={download}
      >
        <Download aria-hidden="true" />
        Export CSV
      </button>
      {error && (
        <span className="error" role="alert">
          {error}
        </span>
      )}
    </>
  )
}

function csvCell(value: unknown) {
  if (value === null || value === undefined) {
    return ''
  }
  let text = typeof value === 'object' ? JSON.stringify(value) : String(value)
  if (typeof value === 'string' && /^[\s]*[=+@-]|^[\t\r\n]/.test(text)) {
    text = `'${text}`
  }
  return /[,"\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}
