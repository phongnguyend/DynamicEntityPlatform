import { useMemo, useState, type DragEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ArrowRight, Check, CheckCircle2, FileSpreadsheet, Loader2, RotateCcw, TriangleAlert, Upload, UploadCloud, Wand2 } from 'lucide-react'
import { api } from '../api'
import type { Field, ImportJob, ImportPreview } from '../types'

const acceptedExtensions = ['.csv', '.xlsx']
const steps = ['Upload file', 'Map columns', 'Review & import']
const listedErrors = 12

export function ImportPanel({ tenantId, entityId, fields, onClose }: { tenantId: string; entityId: string; fields: Field[]; onClose?: () => void }) {
  const client = useQueryClient()
  const [job, setJob] = useState<ImportJob>(); const [preview, setPreview] = useState<ImportPreview>()
  const [mapping, setMapping] = useState<Record<string, string>>({})
  const [imported, setImported] = useState<number>()
  const [dragging, setDragging] = useState(false); const [fileError, setFileError] = useState('')

  const upload = useMutation({ mutationFn: (file: File) => api.uploadImport(tenantId, entityId, file), onSuccess: result => {
    setJob(result); setMapping(autoMatch(result.columns, fields))
  } })
  const validate = useMutation({ mutationFn: () => api.previewImport(tenantId, job!.id,
    Object.entries(mapping).filter(([, target]) => target).map(([sourceColumn, targetFieldId]) => ({ sourceColumn, targetFieldId }))), onSuccess: setPreview })
  const commit = useMutation({ mutationFn: () => api.commitImport(tenantId, job!.id), onSuccess: result => {
    void client.invalidateQueries({ queryKey: ['records', tenantId, entityId] })
    setImported(result.importedRows); setJob(undefined); setPreview(undefined)
  } })

  // The step drives both the progress markers and which panel body renders; step 3 is the post-commit summary.
  const step = imported !== undefined ? 3 : preview ? 2 : job ? 1 : 0
  const mappedFieldIds = useMemo(() => new Set(Object.values(mapping).filter(Boolean)), [mapping])
  const missingRequired = fields.filter(field => field.isRequired && !mappedFieldIds.has(field.id))
  const error = fileError || upload.error?.message || validate.error?.message || commit.error?.message

  function reset() {
    setJob(undefined); setPreview(undefined); setMapping({}); setImported(undefined); setFileError('')
    upload.reset(); validate.reset(); commit.reset()
  }

  function pick(file: File | undefined) {
    if (!file) return
    if (!acceptedExtensions.some(extension => file.name.toLowerCase().endsWith(extension))) {
      setFileError(`'${file.name}' is not a CSV or Excel file.`); return
    }
    setFileError(''); upload.mutate(file)
  }

  function drop(event: DragEvent<HTMLLabelElement>) {
    event.preventDefault(); setDragging(false); pick(event.dataTransfer.files?.[0])
  }

  return <div className="import-panel">
    <ol className="import-steps">{steps.map((label, index) => <li key={label} className={index === step ? 'active' : index < step ? 'done' : undefined}>
      <span className="import-step-mark">{index < step ? <Check /> : index + 1}</span>{label}</li>)}</ol>

    {step === 0 && (upload.isPending
      ? <div className="dropzone busy"><Loader2 className="spinner" /><strong>Uploading {upload.variables?.name}…</strong>
        <small>Reading the header row and staging rows for validation.</small></div>
      : <label className={`dropzone${dragging ? ' dragging' : ''}`} onDrop={drop}
        onDragOver={event => { event.preventDefault(); setDragging(true) }}
        onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false) }}>
        {/* The value is cleared so re-picking the same file after a rejection still fires a change. */}
        <input type="file" accept={acceptedExtensions.join(',')} onChange={event => { pick(event.target.files?.[0]); event.target.value = '' }} />
        <UploadCloud /><strong>Drop a file here, or click to browse</strong>
        <small>CSV or Excel (.xlsx). The first row is read as column headers.</small></label>)}

    {step === 1 && job && <>
      <div className="import-file"><FileSpreadsheet />
        <div><strong>{job.fileName}</strong><small>{job.columns.length} column{job.columns.length === 1 ? '' : 's'} detected</small></div>
        <button type="button" className="secondary" onClick={reset}><RotateCcw />Change file</button></div>
      <div className="mapping-toolbar"><span>{mappedFieldIds.size} of {job.columns.length} columns mapped</span>
        <button type="button" className="link" onClick={() => setMapping(autoMatch(job.columns, fields))}><Wand2 />Auto-match</button></div>
      <div className="mapping-list">
        <div className="mapping mapping-labels"><span>Column in file</span><span /><span>Target field</span></div>
        {job.columns.map(column => <label className="mapping" key={column}>
          <span className="mapping-source" title={column}>{column}</span><ArrowRight className="mapping-arrow" />
          <select value={mapping[column] ?? ''} onChange={event => setMapping(current => ({ ...current, [column]: event.target.value }))}>
            <option value="">Skip this column</option>
            {/* A field may only be the target of one column, so options taken by another column are locked. */}
            {fields.map(field => <option key={field.id} value={field.id} disabled={mapping[column] !== field.id && mappedFieldIds.has(field.id)}>
              {field.displayName}{field.isRequired ? ' *' : ''}</option>)}
          </select></label>)}
      </div>
      {missingRequired.length > 0 && <p className="warning import-warning"><TriangleAlert />
        Required field{missingRequired.length === 1 ? '' : 's'} {missingRequired.map(field => field.displayName).join(', ')} {missingRequired.length === 1 ? 'is' : 'are'} not mapped, so every row will fail validation.</p>}
      <div className="modal-footer"><span className="footer-note">Unmapped columns are ignored.</span>
        <button disabled={mappedFieldIds.size === 0 || validate.isPending} onClick={() => validate.mutate()}>
          {validate.isPending ? <><Loader2 className="spinner" />Validating…</> : <><CheckCircle2 />Validate preview</>}</button></div>
    </>}

    {step === 2 && preview && <>
      <div className="import-stats">
        <div className="import-stat"><strong>{preview.totalRows}</strong><span>Rows in file</span></div>
        <div className="import-stat ok"><strong>{preview.validRows}</strong><span>Ready to import</span></div>
        <div className="import-stat bad"><strong>{preview.invalidRows}</strong><span>Will be skipped</span></div>
      </div>
      {preview.errors.length > 0 && <div className="import-errors"><h4>Validation errors</h4>
        <ul>{preview.errors.slice(0, listedErrors).map((rowError, index) => <li key={index}>
          <span className="import-error-row">Row {rowError.row}</span><span>{rowError.message}</span></li>)}</ul>
        {preview.errors.length > listedErrors && <p className="empty">+{preview.errors.length - listedErrors} more error{preview.errors.length - listedErrors === 1 ? '' : 's'}</p>}</div>}
      {preview.validRows === 0 && <p className="warning import-warning"><TriangleAlert />
        No rows passed validation. Fix the file or change the column mapping, then validate again.</p>}
      <div className="modal-footer">
        <button type="button" className="secondary" onClick={() => { setPreview(undefined); validate.reset() }}>Back to mapping</button>
        <button disabled={preview.validRows === 0 || commit.isPending} onClick={() => commit.mutate()}>
          {commit.isPending ? <><Loader2 className="spinner" />Importing…</> : <><Upload />Import {preview.validRows} row{preview.validRows === 1 ? '' : 's'}</>}</button></div>
    </>}

    {step === 3 && <>
      <div className="import-done"><CheckCircle2 /><strong>{imported} record{imported === 1 ? '' : 's'} imported</strong>
        <small>The record list has been refreshed.</small></div>
      <div className="modal-footer"><button type="button" className="secondary" onClick={reset}><RotateCcw />Import another file</button>
        {onClose && <button type="button" onClick={onClose}><Check />Done</button>}</div>
    </>}

    {error && <p className="error">{error}</p>}
  </div>
}

/** Pairs each column with the first field whose name or label matches it, leaving already-taken fields alone. */
function autoMatch(columns: string[], fields: Field[]) {
  const taken = new Set<string>()
  return Object.fromEntries(columns.map(column => {
    const match = fields.find(field => !taken.has(field.id) &&
      (field.name.toLowerCase() === column.toLowerCase() || field.displayName.toLowerCase() === column.toLowerCase()))
    if (match) taken.add(match.id)
    return [column, match?.id ?? '']
  }))
}
