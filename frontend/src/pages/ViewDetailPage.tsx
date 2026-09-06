import { useState, type DragEvent, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Columns3, CopyPlus, GripVertical, Save, X } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'
import { DynamicGrid } from '../components/DynamicGrid'
import { EntityTabs } from '../components/EntityTabs'
import { FilterPanel } from '../components/FilterPanel'
import { Modal } from '../components/Modal'
import type { DynamicRecord, Field, SavedView } from '../types'
import { getAppliedFilterCondition, getAppliedFilterFieldId } from './RecordsListPage'
import { useEntityContext } from './useEntityContext'

export function ViewDetailPage({ tenantId }: { tenantId: string }) {
  const { entityId = '', viewId = '' } = useParams()
  const isNew = viewId === 'new'
  const navigate = useNavigate()
  const client = useQueryClient()
  const { entity, fields, isLoading, error } = useEntityContext(tenantId, entityId)
  const views = useQuery({ queryKey: ['views', tenantId, entityId], queryFn: () => api.views(tenantId, entityId), enabled: !isNew })
  const view = views.data?.find(item => item.id === viewId)
  const [draft, setDraft] = useState<{ viewId: string; definition: Record<string, unknown> }>()
  const [savedSnapshot, setSavedSnapshot] = useState<{ viewId: string; definition: Record<string, unknown> }>()
  const [filterFieldId, setFilterFieldId] = useState('')
  const [saveAsOpen, setSaveAsOpen] = useState(false)
  const [columnSettingsOpen, setColumnSettingsOpen] = useState(false)
  const persistedDefinition = savedSnapshot?.viewId === viewId ? savedSnapshot.definition : view?.definition ?? (isNew ? { pageSize: 100 } : null)
  const definition = draft?.viewId === viewId ? draft.definition : persistedDefinition
  const isDirty = Boolean(definition && persistedDefinition && !definitionsEqual(definition, persistedDefinition))
  const columnSettings = getColumnSettings(definition, fields)
  const fieldsById = new Map(fields.map(field => [field.id, field]))
  const visibleFields = columnSettings.order.map(fieldId => fieldsById.get(fieldId))
    .filter((field): field is Field => Boolean(field) && !columnSettings.hidden.includes(field!.id))
  const recordQuery = definition ? toRecordQuery(definition) : null
  const records = useQuery({
    queryKey: ['records', tenantId, entityId, 'view', viewId, recordQuery],
    queryFn: () => api.query(tenantId, entityId, recordQuery!),
    enabled: Boolean(recordQuery) && (isNew || Boolean(view)),
  })
  const save = useMutation({
    mutationFn: () => api.updateView(tenantId, viewId, entityId, view!.name, definition!),
    onSuccess: updated => {
      client.setQueryData<SavedView[]>(['views', tenantId, entityId], current => [
        ...(current ?? []).filter(item => item.id !== updated.id), updated,
      ])
      void client.invalidateQueries({ queryKey: ['views', tenantId, entityId] })
      setSavedSnapshot({ viewId, definition: updated.definition })
      setDraft({ viewId, definition: updated.definition })
    },
  })
  const removeRecord = useMutation({
    mutationFn: (record: DynamicRecord) => api.deleteRecord(tenantId, entityId, record),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['records', tenantId, entityId] }),
  })

  if (isLoading || !isNew && views.isLoading) return <p>Loading view…</p>
  if (error || views.error) return <p className="error">{(error ?? views.error)?.message}</p>
  if (!entity) return <p className="error">Entity not found.</p>
  if (!isNew && !view || !definition) return <p className="error">Saved view not found.</p>

  const appliedFilterFieldId = getAppliedFilterFieldId(definition)
  return <section className="page">
    <header><div><p className="eyebrow">{isNew ? 'New saved view' : 'Saved view'}</p><h2>{isNew ? 'Configure view' : view!.name}</h2><p>{entity.displayName}</p></div></header>
    <EntityTabs entityId={entityId} />
    <div className="view-detail-actions">
      <button type="button" className="secondary" onClick={() => setColumnSettingsOpen(true)}><Columns3 />Column Settings</button>
      <button type="button" disabled={!isNew && (!isDirty || save.isPending)} onClick={() => isNew ? setSaveAsOpen(true) : save.mutate()}><Save />{save.isPending ? 'Saving…' : isNew ? 'Save view' : 'Save'}</button>
      {!isNew && <button type="button" className="secondary" onClick={() => setSaveAsOpen(true)}><CopyPlus />Save as</button>}
    </div>
    {save.error && <p className="error">{save.error.message}</p>}
    {records.isLoading ? <p>Loading records…</p> : records.error ? <p className="error">{records.error.message}</p> : <>
      <DynamicGrid fields={visibleFields} maxColumns={visibleFields.length} records={records.data?.items ?? []}
        selectedFilterFieldId={filterFieldId} appliedFilterFieldId={appliedFilterFieldId}
        onSelectFilter={field => setFilterFieldId(current => current === field.id ? '' : field.id)}
        renderFilter={field => <FilterPanel key={field.id} tenantId={tenantId} entityId={entityId}
          field={field} condition={getAppliedFilterCondition(definition, field.id)}
          onApply={next => {
            setDraft({ viewId, definition: next ? { ...definition, ...next } : { ...definition, filter: undefined } })
            setFilterFieldId('')
          }} onClose={() => setFilterFieldId('')} />}
        onEdit={record => navigate(`/entities/${entityId}/records/${record.id}/edit`, { state: { record } })}
        onDelete={record => { if (confirm('Delete this record?')) removeRecord.mutate(record) }} />
      <p className="record-count">{records.data?.items.length ?? 0} record{records.data?.items.length === 1 ? '' : 's'}</p>
    </>}
    {saveAsOpen && <SaveViewAsModal tenantId={tenantId} entityId={entityId} definition={definition} creating={isNew}
      onClose={() => setSaveAsOpen(false)} onSaved={savedViewId => {
        setSaveAsOpen(false)
        navigate(`/entities/${entityId}/views/${savedViewId}`)
      }} />}
    {columnSettingsOpen && <ColumnSettingsModal fields={fields} settings={columnSettings}
      onClose={() => setColumnSettingsOpen(false)} onApply={settings => {
        setDraft({ viewId, definition: { ...definition, columnSettings: settings } })
        setColumnSettingsOpen(false)
      }} />}
  </section>
}

interface ViewColumnSettings {
  order: string[]
  hidden: string[]
}

function ColumnSettingsModal({ fields, settings, onClose, onApply }: {
  fields: Field[]
  settings: ViewColumnSettings
  onClose: () => void
  onApply: (settings: ViewColumnSettings) => void
}) {
  const fieldsById = new Map(fields.map(field => [field.id, field]))
  const [order, setOrder] = useState(settings.order)
  const [hidden, setHidden] = useState(() => new Set(settings.hidden))
  const [draggedId, setDraggedId] = useState<string>()

  function dragOver(event: DragEvent<HTMLLIElement>, targetId: string) {
    event.preventDefault()
    if (!draggedId || draggedId === targetId) return
    setOrder(current => {
      const from = current.indexOf(draggedId)
      const to = current.indexOf(targetId)
      if (from < 0 || to < 0) return current
      const next = current.slice()
      const [fieldId] = next.splice(from, 1)
      next.splice(to, 0, fieldId)
      return next
    })
  }

  function toggle(fieldId: string) {
    setHidden(current => {
      const next = new Set(current)
      if (next.has(fieldId)) next.delete(fieldId)
      else next.add(fieldId)
      return next
    })
  }

  return <Modal title="Column settings" onClose={onClose}>
    <div className="column-settings">
      <p className="field-note">Drag columns into display order and choose which columns are visible. Use Save to persist the changes.</p>
      <ol className="field-order-list">{order.map(fieldId => {
        const field = fieldsById.get(fieldId)
        if (!field) return null
        return <li key={fieldId} draggable className={draggedId === fieldId ? 'dragging' : ''}
          onDragStart={event => { setDraggedId(fieldId); event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', fieldId) }}
          onDragOver={event => dragOver(event, fieldId)} onDragEnd={() => setDraggedId(undefined)}>
          <span className="drag-handle" aria-hidden="true"><GripVertical /></span>
          <label className="check"><input type="checkbox" checked={!hidden.has(fieldId)} onChange={() => toggle(fieldId)} />
            <span><strong>{field.displayName}</strong><small>{field.name} · {field.dataType}</small></span></label>
          <span className={hidden.has(fieldId) ? 'column-hidden' : 'index-status'}>{hidden.has(fieldId) ? 'Hidden' : 'Visible'}</span>
        </li>
      })}</ol>
      <div className="actions"><button type="button" onClick={() => onApply({ order, hidden: [...hidden] })}><Columns3 />Apply settings</button>
        <button type="button" className="secondary" onClick={onClose}><X />Cancel</button></div>
    </div>
  </Modal>
}

function getColumnSettings(definition: Record<string, unknown> | null, fields: Field[]): ViewColumnSettings {
  const raw = definition?.columnSettings
  const settings = typeof raw === 'object' && raw !== null && !Array.isArray(raw)
    ? raw as { order?: unknown; hidden?: unknown }
    : undefined
  const validIds = new Set(fields.map(field => field.id))
  const configuredOrder = Array.isArray(settings?.order)
    ? settings.order.filter((fieldId): fieldId is string => typeof fieldId === 'string' && validIds.has(fieldId))
    : []
  const order = [...new Set([...configuredOrder, ...fields.map(field => field.id)])]
  const hidden = Array.isArray(settings?.hidden)
    ? [...new Set(settings.hidden.filter((fieldId): fieldId is string => typeof fieldId === 'string' && validIds.has(fieldId)))]
    : []
  return { order, hidden }
}

function toRecordQuery(definition: Record<string, unknown>) {
  const { columnSettings: _columnSettings, ...query } = definition
  return query
}

function definitionsEqual(left: Record<string, unknown>, right: Record<string, unknown>) {
  return JSON.stringify(left) === JSON.stringify(right)
}

function SaveViewAsModal({ tenantId, entityId, definition, creating, onClose, onSaved }: {
  tenantId: string
  entityId: string
  definition: Record<string, unknown>
  creating?: boolean
  onClose: () => void
  onSaved: (viewId: string) => void
}) {
  const client = useQueryClient()
  const [name, setName] = useState('')
  const save = useMutation({
    mutationFn: () => api.createView(tenantId, entityId, name, definition),
    onSuccess: view => {
      client.setQueryData<SavedView[]>(['views', tenantId, entityId], current => [
        ...(current ?? []).filter(item => item.id !== view.id), view,
      ])
      void client.invalidateQueries({ queryKey: ['views', tenantId, entityId] })
      onSaved(view.id)
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    if (name.trim()) save.mutate()
  }

  return <Modal title={creating ? 'Save new view' : 'Save view as'} onClose={onClose}>
    <form className="view-editor" onSubmit={submit}>
      <label className="field">Name<input required autoFocus maxLength={200} value={name} onChange={event => setName(event.target.value)} /></label>
      {save.error && <p className="error">{save.error.message}</p>}
      <div className="actions"><button disabled={!name.trim() || save.isPending}>{creating ? <Save /> : <CopyPlus />}{save.isPending ? 'Saving…' : creating ? 'Save view' : 'Save as new view'}</button>
        <button type="button" className="secondary" disabled={save.isPending} onClick={onClose}><X />Cancel</button></div>
    </form>
  </Modal>
}
