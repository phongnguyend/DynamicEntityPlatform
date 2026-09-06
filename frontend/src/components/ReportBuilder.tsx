import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Braces, Plus, RefreshCw, Save, Trash2 } from 'lucide-react'
import { api } from '../api'
import type { AggregateFunction, AnalyticsDimension, AnalyticsMeasure, AnalyticsQuery, Field, Report, VisualizationType } from '../types'
import { ReportViewer } from './ReportViewer'
import { AnalyticsFilterEditor } from './AnalyticsFilterEditor'
import { JsonEditorModal, requireJsonObject } from './JsonEditorModal'

export function ReportBuilder({ tenantId, entityId, fields, report, onSaved }: { tenantId: string; entityId: string; fields: Field[]; report?: Report; onSaved: (report: Report) => void }) {
  const [name, setName] = useState(report?.name ?? '')
  const [description, setDescription] = useState(report?.description ?? '')
  const [dimensions, setDimensions] = useState<AnalyticsDimension[]>(report?.query.dimensions ?? [])
  const [measures, setMeasures] = useState<AnalyticsMeasure[]>(report?.query.measures ?? [{ aggregate: 'Count', alias: 'count' }])
  const [filter, setFilter] = useState<unknown>(report?.query.filter)
  const [sortAlias, setSortAlias] = useState(report?.query.sort[0]?.alias ?? '')
  const [sortDirection, setSortDirection] = useState<'Asc' | 'Desc'>(report?.query.sort[0]?.direction ?? 'Asc')
  const [visualization, setVisualization] = useState<VisualizationType>(report?.visualization ?? 'Table')
  const [limit, setLimit] = useState(report?.query.limit ?? 100)
  const [jsonOpen, setJsonOpen] = useState(false)
  const [previewStale, setPreviewStale] = useState(false)
  const previewAbort = useRef<AbortController>(undefined)
  const query = useMemo<AnalyticsQuery>(() => ({ filter, dimensions, measures, sort: sortAlias ? [{ alias: sortAlias, direction: sortDirection }] : [], limit }), [filter, dimensions, measures, sortAlias, sortDirection, limit])
  const preview = useMutation({ mutationFn: ({ nextQuery, signal }: { nextQuery: AnalyticsQuery; signal: AbortSignal }) =>
    api.previewAnalytics(tenantId, entityId, nextQuery, signal), onSuccess: () => setPreviewStale(false) })
  const save = useMutation({ mutationFn: () => api.saveReport(tenantId, entityId, { name, description, query, visualization }, report?.id), onSuccess: onSaved })
  function refreshPreview(nextQuery: AnalyticsQuery) {
    previewAbort.current?.abort()
    const controller = new AbortController()
    previewAbort.current = controller
    preview.mutate({ nextQuery, signal: controller.signal })
  }
  function changeFilter(nextFilter?: unknown) {
    previewAbort.current?.abort()
    setFilter(nextFilter)
    setPreviewStale(true)
  }
  useEffect(() => {
    const handle = window.setTimeout(() => refreshPreview(query), 600)
    return () => { window.clearTimeout(handle); previewAbort.current?.abort() }
  }, [dimensions, measures, sortAlias, sortDirection, limit]) // eslint-disable-line react-hooks/exhaustive-deps
  const numeric = fields.filter(field => field.dataType === 'Integer' || field.dataType === 'Decimal')
  function addDimension() { const field = fields[0]; if (field && dimensions.length < 2) setDimensions([...dimensions, { fieldId: field.id, dateBucket: 'None', alias: `dimension_${dimensions.length + 1}` }]) }
  function addMeasure() { setMeasures([...measures, { aggregate: 'Count', alias: `measure_${measures.length + 1}` }]) }
  function reorder<T>(items: T[], from: number, to: number) { const next = [...items]; const [item] = next.splice(from, 1); next.splice(to, 0, item); return next }
  async function saveJson(value: unknown) {
    const input = requireJsonObject(value, { name: 'string', query: 'object', visualization: 'string' })
    const saved = await api.saveReport(tenantId, entityId, input as unknown as Omit<Report, 'id' | 'entityId' | 'createdAt' | 'updatedAt'>, report?.id)
    onSaved(saved)
  }
  return <div className="report-builder"><div className="builder-controls">
    <label className="field">Name<input value={name} onChange={event => setName(event.target.value)} /></label>
    <label className="field">Description<textarea value={description} onChange={event => setDescription(event.target.value)} /></label>
    <div className="builder-section"><div className="panel-title"><h3>Dimensions</h3><button className="secondary" disabled={dimensions.length >= 2 || !fields.length} onClick={addDimension}><Plus />Add</button></div>
      {dimensions.map((dimension, index) => <div className="builder-row" draggable key={index} onDragStart={event => event.dataTransfer.setData('dimension-index',String(index))} onDragOver={event => event.preventDefault()} onDrop={event => setDimensions(reorder(dimensions,Number(event.dataTransfer.getData('dimension-index')),index))}><select value={dimension.fieldId} onChange={event => setDimensions(dimensions.map((item, i) => i === index ? { ...item, fieldId: event.target.value } : item))}>{fields.map(field => <option value={field.id} key={field.id}>{field.displayName}</option>)}</select>
        <select value={dimension.dateBucket} onChange={event => setDimensions(dimensions.map((item, i) => i === index ? { ...item, dateBucket: event.target.value as AnalyticsDimension['dateBucket'] } : item))}><option>None</option><option>Day</option><option>Week</option><option>Month</option><option>Quarter</option><option>Year</option></select>
        <button className="link danger icon-only" aria-label="Remove dimension" title="Remove" onClick={() => setDimensions(dimensions.filter((_, i) => i !== index))}><Trash2 /></button></div>)}</div>
    <div className="builder-section"><div className="panel-title"><h3>Measures</h3><button className="secondary" onClick={addMeasure}><Plus />Add</button></div>
      {measures.map((measure, index) => <div className="builder-row" draggable key={index} onDragStart={event => event.dataTransfer.setData('measure-index',String(index))} onDragOver={event => event.preventDefault()} onDrop={event => setMeasures(reorder(measures,Number(event.dataTransfer.getData('measure-index')),index))}><select value={measure.aggregate} onChange={event => { const aggregate = event.target.value as AggregateFunction; setMeasures(measures.map((item, i) => i === index ? { ...item, aggregate, fieldId: aggregate === 'Count' ? undefined : item.fieldId ?? numeric[0]?.id } : item)) }}><option>Count</option><option>CountDistinct</option><option>Sum</option><option>Average</option><option>Min</option><option>Max</option></select>
        <select disabled={measure.aggregate === 'Count'} value={measure.fieldId ?? ''} onChange={event => setMeasures(measures.map((item, i) => i === index ? { ...item, fieldId: event.target.value || undefined } : item))}><option value="">No field</option>{fields.map(field => <option value={field.id} key={field.id}>{field.displayName}</option>)}</select>
        <button className="link danger icon-only" aria-label="Remove measure" title="Remove" disabled={measures.length === 1} onClick={() => setMeasures(measures.filter((_, i) => i !== index))}><Trash2 /></button></div>)}</div>
    <AnalyticsFilterEditor fields={fields} value={filter} onChange={changeFilter} />
    <div className="builder-section"><h3>Sorting</h3><div className="builder-row"><select value={sortAlias} onChange={event => setSortAlias(event.target.value)}><option value="">Default order</option>{[...dimensions,...measures].map(item => <option key={item.alias}>{item.alias}</option>)}</select><select value={sortDirection} onChange={event => setSortDirection(event.target.value as 'Asc'|'Desc')}><option>Asc</option><option>Desc</option></select></div></div>
    <label className="field">Visualization<select value={visualization} onChange={event => setVisualization(event.target.value as VisualizationType)}><option>Table</option><option>Number</option><option>Bar</option><option>Line</option><option>Donut</option></select></label>
    <label className="field">Row limit<input type="number" min="1" max="1000" value={limit} onChange={event => setLimit(Number(event.target.value))} /></label>
    <div className="actions"><button disabled={!name.trim() || save.isPending} onClick={() => save.mutate()}><Save />{save.isPending ? 'Saving…' : 'Save report'}</button><button className="secondary" onClick={() => setJsonOpen(true)}><Braces />Edit JSON</button><button className="secondary" onClick={() => refreshPreview(query)}><RefreshCw />Refresh preview</button></div>
    {(visiblePreviewError(preview.error) || save.error) && <p className="error">{(visiblePreviewError(preview.error) ?? save.error)?.message}</p>}
  </div><div className="panel preview-panel"><h3>Preview</h3>{previewStale && <p className="warning">Filters changed. Refresh the preview to apply them.</p>}{preview.isPending && <p className="empty">Calculating…</p>}{preview.data && <ReportViewer result={preview.data} visualization={visualization} />}</div>
    {jsonOpen && <JsonEditorModal title={`${report ? 'Edit' : 'Create'} report as JSON`} value={{ name, description, query, visualization, visualizationConfiguration: report?.visualizationConfiguration }} onClose={() => setJsonOpen(false)} onSave={saveJson} />}
  </div>
}

function visiblePreviewError(error: Error | null) {
  return error?.name === 'AbortError' ? null : error
}
