import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api'
import type { Field, ImportJob, ImportPreview } from '../types'

export function ImportPanel({ tenantId, entityId, fields }: { tenantId: string; entityId: string; fields: Field[] }) {
  const client = useQueryClient(); const [job, setJob] = useState<ImportJob>(); const [preview, setPreview] = useState<ImportPreview>()
  const [mapping, setMapping] = useState<Record<string, string>>({})
  const upload = useMutation({ mutationFn: (file: File) => api.uploadImport(tenantId, entityId, file), onSuccess: result => {
    setJob(result); setMapping(Object.fromEntries(result.columns.map(column => [column, fields.find(field => field.name.toLowerCase() === column.toLowerCase())?.id ?? ''])))
  } })
  const validate = useMutation({ mutationFn: () => api.previewImport(tenantId, job!.id,
    Object.entries(mapping).filter(([, target]) => target).map(([sourceColumn, targetFieldId]) => ({ sourceColumn, targetFieldId }))), onSuccess: setPreview })
  const commit = useMutation({ mutationFn: () => api.commitImport(tenantId, job!.id), onSuccess: () => {
    void client.invalidateQueries({ queryKey: ['records', tenantId, entityId] }); setJob(undefined); setPreview(undefined)
  } })
  return <div className="import-panel">{!job ? <input type="file" accept=".csv,.xlsx" onChange={event => {
    const file = event.target.files?.[0]; if (file) upload.mutate(file)
  }} /> : <><p><strong>{job.fileName}</strong></p>{job.columns.map(column => <label className="mapping" key={column}><span>{column}</span>
    <select value={mapping[column] ?? ''} onChange={event => setMapping(current => ({ ...current, [column]: event.target.value }))}>
      <option value="">Do not import</option>{fields.map(field => <option key={field.id} value={field.id}>{field.displayName}</option>)}</select></label>)}
    <button onClick={() => validate.mutate()} disabled={validate.isPending}>Validate preview</button></>}
    {preview && <div className="preview"><p>{preview.validRows} valid · {preview.invalidRows} invalid</p>
      {preview.errors.slice(0, 5).map((error, index) => <p className="error" key={index}>Row {error.row}: {error.message}</p>)}
      <button disabled={preview.validRows === 0 || commit.isPending} onClick={() => commit.mutate()}>Import {preview.validRows} rows</button></div>}
    {(upload.error || validate.error || commit.error) && <p className="error">{upload.error?.message ?? validate.error?.message ?? commit.error?.message}</p>}</div>
}
