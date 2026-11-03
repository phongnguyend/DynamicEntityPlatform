import { useState } from 'react'
import { createPortal } from 'react-dom'
import { useMutation } from '@tanstack/react-query'
import { Code } from 'lucide-react'
import { api } from '../api'
import type { Metric } from '../types'
import { Modal } from './Modal'
import { MetricParametersForm, metricParameterNames } from './MetricParametersForm'

export function MetricSqlButton({ tenantId, entityId, metric, disabled }: {
  tenantId: string; entityId: string; metric: Pick<Metric, 'name' | 'aggregate' | 'fieldId' | 'filter'>; disabled?: boolean
}) {
  const [open, setOpen] = useState(false)
  const names = metricParameterNames(metric.filter)
  const preview = useMutation({ mutationFn: (parameters?: Record<string, unknown>) =>
    api.metricSql(tenantId, entityId, metric, parameters) })
  return <>
    <button type="button" className="secondary" disabled={disabled} title="View SQL for the current metric form" onClick={() => {
      preview.reset()
      setOpen(true)
      preview.mutate(undefined)
    }}><Code />View SQL</button>
    {open && createPortal(<Modal wide title={`SQL · ${metric.name.trim() || 'New metric'}`} onClose={() => setOpen(false)}>
      <p className="field-note">Preview the generated query without executing it. Automatic dates and times resolve when this preview is generated.</p>
      {names.length > 0 && <details><summary>Parameter values (optional)</summary>
        <MetricParametersForm names={names} busy={preview.isPending} optional
          submitLabel="Update SQL preview" onEvaluate={parameters => preview.mutate(parameters)} />
      </details>}
      {preview.isPending && <p role="status">Generating SQL…</p>}
      {preview.error && <p className="error" role="alert">{preview.error.message}</p>}
      {preview.data && <>
        <label className="field">Generated SQL<textarea readOnly rows={8} value={preview.data.sql} spellCheck={false} style={{ fontFamily: 'monospace' }} /></label>
        <label className="field">Parameters<textarea readOnly rows={6} value={JSON.stringify(preview.data.parameters, null, 2)} spellCheck={false} style={{ fontFamily: 'monospace' }} /></label>
      </>}
      <div className="modal-footer"><button type="button" className="secondary" onClick={() => setOpen(false)}>Close</button></div>
    </Modal>, document.body)}
  </>
}
