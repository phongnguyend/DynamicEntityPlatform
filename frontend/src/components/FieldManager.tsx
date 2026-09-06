import { useState, type DragEvent, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, GripVertical, Pencil, Save, X } from 'lucide-react'
import { api } from '../api'
import type { Field } from '../types'
import { Modal } from './Modal'

interface Props {
  tenantId: string
  entityId: string
  fields: Field[]
}

export function FieldManager({ tenantId, entityId, fields }: Props) {
  const [editing, setEditing] = useState<Field>()
  const [reordering, setReordering] = useState(false)

  return <div className="field-manager">
    <div className="field-manager-header">
      <h3>Manage fields</h3>
      <button type="button" className="secondary" disabled={fields.length < 2}
        onClick={() => setReordering(true)}><GripVertical />Change order</button>
    </div>
    <p className="field-note">Indexes can improve filtering and sorting. Manage them from the Indexes section.</p>
    {fields.length === 0 ? <p className="empty">No fields have been added.</p> :
      <ul className="field-list">{fields.map(field => <li key={field.id}>
        <div><strong>{field.displayName}</strong><small>{field.name} · {field.dataType}</small></div>
        <div className="field-actions">
          {field.indexColumnName && <span className="index-status">Indexed</span>}
          <button type="button" className="link" onClick={() => setEditing(field)}><Pencil />Edit</button>
        </div>
      </li>)}</ul>}
    {editing && <Modal title={`Edit ${editing.displayName}`} onClose={() => setEditing(undefined)}>
      <FieldEditor key={editing.id} tenantId={tenantId} entityId={entityId}
        field={editing} onClose={() => setEditing(undefined)} />
    </Modal>}
    {reordering && <Modal title="Change field order" onClose={() => setReordering(false)}>
      <FieldOrderEditor tenantId={tenantId} entityId={entityId} fields={fields}
        onClose={() => setReordering(false)} />
    </Modal>}
  </div>
}

function FieldOrderEditor({ tenantId, entityId, fields, onClose }: Props & { onClose: () => void }) {
  const client = useQueryClient()
  const [orderedFields, setOrderedFields] = useState(() => [...fields])
  const [draggedId, setDraggedId] = useState<string>()
  const save = useMutation({
    mutationFn: () => Promise.all(orderedFields.map((field, index) =>
      field.sortOrder === index
        ? Promise.resolve(field)
        : api.updateField(tenantId, entityId, field.id, { sortOrder: index }))),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['fields', tenantId, entityId] })
      onClose()
    },
  })

  function moveField(fromIndex: number, toIndex: number) {
    if (fromIndex === toIndex || toIndex < 0 || toIndex >= orderedFields.length) return
    setOrderedFields(current => {
      const next = [...current]
      const [field] = next.splice(fromIndex, 1)
      next.splice(toIndex, 0, field)
      return next
    })
  }

  function dragOver(event: DragEvent<HTMLLIElement>, targetId: string) {
    event.preventDefault()
    if (!draggedId || draggedId === targetId) return
    const fromIndex = orderedFields.findIndex(field => field.id === draggedId)
    const toIndex = orderedFields.findIndex(field => field.id === targetId)
    moveField(fromIndex, toIndex)
  }

  return <div className="field-order-editor">
    <p className="field-note">Drag fields into the order they should appear, then save.</p>
    <ol className="field-order-list">
      {orderedFields.map((field, index) => <li key={field.id} draggable
        className={draggedId === field.id ? 'dragging' : ''}
        onDragStart={event => {
          setDraggedId(field.id)
          event.dataTransfer.effectAllowed = 'move'
          event.dataTransfer.setData('text/plain', field.id)
        }}
        onDragOver={event => dragOver(event, field.id)}
        onDragEnd={() => setDraggedId(undefined)}>
        <span className="drag-handle" aria-hidden="true"><GripVertical /></span>
        <div><strong>{field.displayName}</strong><small>{field.name} &middot; {field.dataType}</small></div>
        <div className="order-actions">
          <button type="button" className="link" aria-label={`Move ${field.displayName} up`}
            disabled={index === 0} onClick={() => moveField(index, index - 1)}><ArrowUp /></button>
          <button type="button" className="link" aria-label={`Move ${field.displayName} down`}
            disabled={index === orderedFields.length - 1} onClick={() => moveField(index, index + 1)}><ArrowDown /></button>
        </div>
      </li>)}
    </ol>
    {save.error && <p className="error">{save.error.message}</p>}
    <div className="actions">
      <button type="button" disabled={save.isPending} onClick={() => save.mutate()}><Save />
        {save.isPending ? 'Saving…' : 'Save order'}
      </button>
      <button type="button" className="secondary" disabled={save.isPending} onClick={onClose}><X />Cancel</button>
    </div>
  </div>
}

function FieldEditor({ tenantId, entityId, field, onClose }: {
  tenantId: string
  entityId: string
  field: Field
  onClose: () => void
}) {
  const client = useQueryClient()
  const [name, setName] = useState(field.name)
  const [displayName, setDisplayName] = useState(field.displayName)
  const [required, setRequired] = useState(field.isRequired)
  const [unique, setUnique] = useState(field.isUnique)
  const [filterable, setFilterable] = useState(field.isFilterable)
  const [sortable, setSortable] = useState(field.isSortable)
  const [facetable, setFacetable] = useState(field.isFacetable)
  const [searchable, setSearchable] = useState(field.isSearchable)
  const [configuration, setConfiguration] = useState(
    field.configuration ? JSON.stringify(field.configuration, null, 2) : '')
  const [configurationError, setConfigurationError] = useState('')
  const save = useMutation({
    mutationFn: (parsedConfiguration: unknown) => api.updateField(tenantId, entityId, field.id, {
      name, displayName, configuration: parsedConfiguration,
      isRequired: required, isUnique: unique, isFilterable: filterable,
      isSortable: sortable, isFacetable: facetable, isSearchable: searchable,
    }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['fields', tenantId, entityId] })
      onClose()
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setConfigurationError('')
    try {
      const parsed = configuration.trim() ? JSON.parse(configuration) as unknown : undefined
      if (parsed !== undefined && (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed))) {
        setConfigurationError('Configuration must be a JSON object.')
        return
      }
      save.mutate(parsed)
    } catch {
      setConfigurationError('Configuration must be valid JSON.')
    }
  }

  return <form className="field-editor" onSubmit={submit}>
    <p className="field-note">Data type: {field.dataType} · The data type cannot be changed after creation.</p>
    <label className="field"><span>Field name</span><input required pattern="[A-Za-z][A-Za-z0-9_]*"
      value={name} onChange={event => setName(event.target.value)} /></label>
    <label className="field"><span>Display name</span><input required value={displayName}
      onChange={event => setDisplayName(event.target.value)} /></label>
    <fieldset className="field-options"><legend>Behavior</legend>
      <label className="check"><input type="checkbox" checked={required} onChange={event => setRequired(event.target.checked)} /> Required</label>
      <label className="check"><input type="checkbox" checked={unique} onChange={event => setUnique(event.target.checked)} /> Unique</label>
      <label className="check"><input type="checkbox" checked={filterable} onChange={event => setFilterable(event.target.checked)} /> Filterable</label>
      <label className="check"><input type="checkbox" checked={sortable} onChange={event => setSortable(event.target.checked)} /> Sortable</label>
      <label className="check"><input type="checkbox" checked={facetable} onChange={event => setFacetable(event.target.checked)} /> Facetable</label>
      <label className="check"><input type="checkbox" checked={searchable} onChange={event => setSearchable(event.target.checked)} /> Searchable</label>
    </fieldset>
    <label className="field"><span>Configuration (JSON object)</span><textarea value={configuration}
      placeholder="{}" onChange={event => setConfiguration(event.target.value)} /></label>
    {configurationError && <p className="error">{configurationError}</p>}
    {save.error && <p className="error">{save.error.message}</p>}
    <div className="actions"><button disabled={save.isPending}><Save />{save.isPending ? 'Saving…' : 'Save field'}</button>
      <button type="button" className="secondary" onClick={onClose}><X />Cancel</button></div>
  </form>
}
