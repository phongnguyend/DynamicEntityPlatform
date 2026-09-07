import { useState, type DragEvent, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, GripVertical, Plus, Trash2, X } from 'lucide-react'
import { api } from '../api'
import type { Field } from '../types'
import { Modal } from './Modal'

interface Props {
  tenantId: string
  entityId: string
  fields: Field[]
}

const UNINDEXABLE_TYPES = new Set(['LongText', 'MultiChoice'])

export function IndexManager({ tenantId, entityId, fields }: Props) {
  const [creating, setCreating] = useState(false)
  const client = useQueryClient()
  const indexes = useQuery({
    queryKey: ['indexes', tenantId, entityId],
    queryFn: () => api.indexes(tenantId, entityId),
  })
  const deleteIndex = useMutation({
    mutationFn: (indexId: string) => api.deleteIndex(tenantId, entityId, indexId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['indexes', tenantId, entityId] })
      void client.invalidateQueries({ queryKey: ['fields', tenantId, entityId] })
    },
  })
  const fieldsById = new Map(fields.map(field => [field.id, field]))

  function confirmRemoval(indexId: string, indexName: string) {
    if (confirm(`Remove index ${indexName}? Queries relying on it may become slower.`)) {
      deleteIndex.mutate(indexId)
    }
  }

  return <div className="index-manager">
    <div className="panel-header">
      <h3>Indexes</h3>
      <button type="button" onClick={() => setCreating(true)}><Plus />Create index</button>
    </div>
    <div className="panel-body">
    <p className="field-note">Composite SQL indexes can speed up filtering and sorting across multiple columns.</p>
    {indexes.isLoading && <p>Loading…</p>}
    {indexes.error && <p className="error">{indexes.error.message}</p>}
    {indexes.data && indexes.data.length === 0 && <p className="empty">No indexes have been created.</p>}
    {indexes.data && indexes.data.length > 0 &&
      <ul className="index-list">{indexes.data.map(index => <li key={index.id}>
        <div>
          <strong>{index.indexName}</strong>
          <small>{index.columns.slice().sort((a, b) => a.sortOrder - b.sortOrder)
            .map(column => `${fieldsById.get(column.fieldId)?.displayName ?? column.fieldId} ${column.isDescending ? 'DESC' : 'ASC'}`)
            .join(', ')}</small>
        </div>
        <div className="field-actions">
          <span className="index-status">{index.status}</span>
          <button type="button" className="link danger" disabled={deleteIndex.isPending}
            onClick={() => confirmRemoval(index.id, index.indexName)}>
            <Trash2 />{deleteIndex.isPending && deleteIndex.variables === index.id ? 'Removing…' : 'Remove'}
          </button>
        </div>
      </li>)}</ul>}
    </div>
    {creating && <Modal title="Create index" onClose={() => setCreating(false)}>
      <IndexCreator tenantId={tenantId} entityId={entityId} fields={fields} onClose={() => setCreating(false)} />
    </Modal>}
  </div>
}

function IndexCreator({ tenantId, entityId, fields, onClose }: {
  tenantId: string
  entityId: string
  fields: Field[]
  onClose: () => void
}) {
  const client = useQueryClient()
  const indexableFields = fields.filter(field => !UNINDEXABLE_TYPES.has(field.dataType))
  const [selection, setSelection] = useState<Array<{ fieldId: string; descending: boolean }>>([])
  const [draggedId, setDraggedId] = useState<string>()
  const create = useMutation({
    mutationFn: () => api.createIndex(tenantId, entityId, selection),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['indexes', tenantId, entityId] })
      void client.invalidateQueries({ queryKey: ['fields', tenantId, entityId] })
      onClose()
    },
  })

  function toggleField(fieldId: string, checked: boolean) {
    setSelection(current => checked
      ? [...current, { fieldId, descending: false }]
      : current.filter(entry => entry.fieldId !== fieldId))
  }

  function setDescending(fieldId: string, descending: boolean) {
    setSelection(current => current.map(entry => entry.fieldId === fieldId ? { ...entry, descending } : entry))
  }

  function move(fromIndex: number, toIndex: number) {
    setSelection(current => {
      if (fromIndex === toIndex || fromIndex < 0 || toIndex < 0 ||
          fromIndex >= current.length || toIndex >= current.length) return current
      const next = current.slice()
      const [entry] = next.splice(fromIndex, 1)
      next.splice(toIndex, 0, entry)
      return next
    })
  }

  function dragOver(event: DragEvent<HTMLLIElement>, targetId: string) {
    event.preventDefault()
    if (!draggedId || draggedId === targetId) return
    const fromIndex = selection.findIndex(entry => entry.fieldId === draggedId)
    const toIndex = selection.findIndex(entry => entry.fieldId === targetId)
    move(fromIndex, toIndex)
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    if (selection.length === 0) return
    create.mutate()
  }

  return <form className="index-creator" onSubmit={submit}>
    <p className="field-note">Select one or more columns, then drag them into index order and choose a direction.</p>
    <ul className="field-list">{indexableFields.map(field => {
      const selected = selection.some(entry => entry.fieldId === field.id)
      return <li key={field.id}>
        <label className="check">
          <input type="checkbox" checked={selected}
            onChange={event => toggleField(field.id, event.target.checked)} />
          {field.displayName}<small> {field.name} · {field.dataType}</small>
        </label>
      </li>
    })}</ul>
    {selection.length > 0 && <div className="index-column-order">
      <h4>Index column order</h4>
      <ol className="field-order-list">{selection.map((entry, index) => {
        const field = fields.find(candidate => candidate.id === entry.fieldId)
        const label = field?.displayName ?? entry.fieldId
        return <li key={entry.fieldId} draggable
          className={draggedId === entry.fieldId ? 'dragging' : ''}
          onDragStart={event => {
            setDraggedId(entry.fieldId)
            event.dataTransfer.effectAllowed = 'move'
            event.dataTransfer.setData('text/plain', entry.fieldId)
          }}
          onDragOver={event => dragOver(event, entry.fieldId)}
          onDragEnd={() => setDraggedId(undefined)}>
          <span className="drag-handle" aria-hidden="true"><GripVertical /></span>
          <div><strong>{label}</strong>
            {field && <small>{field.name} &middot; {field.dataType}</small>}</div>
          <div className="index-column-controls">
            <select aria-label={`Direction for ${label}`}
              value={entry.descending ? 'DESC' : 'ASC'}
              onChange={event => setDescending(entry.fieldId, event.target.value === 'DESC')}>
              <option value="ASC">Ascending</option>
              <option value="DESC">Descending</option>
            </select>
            <div className="order-actions">
              <button type="button" className="link" aria-label={`Move ${label} up`}
                disabled={index === 0} onClick={() => move(index, index - 1)}><ArrowUp /></button>
              <button type="button" className="link" aria-label={`Move ${label} down`}
                disabled={index === selection.length - 1} onClick={() => move(index, index + 1)}><ArrowDown /></button>
            </div>
          </div>
        </li>
      })}</ol>
    </div>}
    {create.error && <p className="error">{create.error.message}</p>}
    <div className="modal-footer">
      <button disabled={selection.length === 0 || create.isPending}><Plus />
        {create.isPending ? 'Creating…' : 'Create index'}
      </button>
      <button type="button" className="secondary" onClick={onClose}><X />Cancel</button>
    </div>
  </form>
}
