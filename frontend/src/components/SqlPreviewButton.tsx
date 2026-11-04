import { useState } from 'react'
import './SqlPreviewButton.css'
import { createPortal } from 'react-dom'
import { useMutation } from '@tanstack/react-query'
import { Code } from 'lucide-react'
import type { SqlPreview } from '../types'
import { Modal } from './Modal'
import { HighlightedCode } from './HighlightedCode'
import { MetricParametersForm, metricParameterNames } from './MetricParametersForm'

export function SqlPreviewButton({ name, filter, disabled, load, className = 'secondary', description = 'Preview the generated query without executing it. Automatic dates and times resolve when this preview is generated.' }: {
  name: string; filter: unknown; disabled?: boolean;
  className?: string; description?: string;
  load: (parameters?: Record<string, unknown>) => Promise<SqlPreview>
}) {
  const [open, setOpen] = useState(false)
  const [mode, setMode] = useState<'virtual' | 'database'>('virtual')
  const names = metricParameterNames(filter)
  const preview = useMutation({ mutationFn: load })
  return <>
    <button type="button" className={className} disabled={disabled} title={`View SQL for ${name.trim() || 'the current definition'}`} onClick={() => {
      preview.reset()
      setOpen(true)
      preview.mutate(undefined)
    }}><Code />View SQL</button>
    {open && createPortal(<Modal wide title={`SQL · ${name.trim() || 'New definition'}`} onClose={() => setOpen(false)}>
      <div className="sql-preview">
      <p className="field-note">{description}</p>
      {names.length > 0 && <details className="sql-preview-inputs"><summary>Parameter values (optional)</summary>
        <MetricParametersForm names={names} busy={preview.isPending} optional
          submitLabel="Update SQL preview" onEvaluate={parameters => preview.mutate(parameters)} />
      </details>}
      {preview.isPending && <p role="status">Generating SQL…</p>}
      {preview.error && <p className="error" role="alert">{preview.error.message}</p>}
      {preview.data && <>
        <div className="actions sql-preview-modes" role="group" aria-label="SQL representation">
          <button type="button" className={mode === 'virtual' ? '' : 'secondary'} aria-pressed={mode === 'virtual'} onClick={() => setMode('virtual')}>Virtual names</button>
          <button type="button" className={mode === 'database' ? '' : 'secondary'} aria-pressed={mode === 'database'} onClick={() => setMode('database')}>Database names</button>
        </div>
        <p className="field-note">{mode === 'virtual' ? 'Logical representation using entity and field names. These are virtual names, not database objects.' : 'Actual database query using physical tables, JSON paths and indexed columns.'}</p>
        <HighlightedCode label="Generated SQL" language="sql" value={mode === 'virtual' ? preview.data.virtualSql : preview.data.sql} />
        <HighlightedCode label="Parameters" language="json" value={JSON.stringify(mode === 'virtual' ? preview.data.virtualParameters : preview.data.parameters, null, 2)} />
      </>}
      <div className="modal-footer"><button type="button" className="secondary" onClick={() => setOpen(false)}>Close</button></div>
      </div>
    </Modal>, document.body)}
  </>
}
