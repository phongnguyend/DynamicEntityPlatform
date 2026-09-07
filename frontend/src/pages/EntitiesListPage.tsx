import { useState, type DragEvent, type FormEvent, type KeyboardEvent, type LiHTMLAttributes, type ReactNode } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Building2, FolderOpen, GripVertical, ListTree, Pencil, Pin, PinOff, Plus, Save, X } from 'lucide-react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { EntityIcon, EntityIconPicker } from '../components/EntityIcon'
import { Modal } from '../components/Modal'
import { pinnedEntities } from '../pinnedEntities'
import type { Entity } from '../types'

export function EntitiesListPage({ tenantId, entities, onCreateEntity }: {
  tenantId: string; entities: Entity[]; onCreateEntity: () => void
}) {
  const client = useQueryClient()
  const [editingIcon, setEditingIcon] = useState<Entity>()
  // Holds the order being saved so a drag or a pin toggle shows immediately instead of waiting
  // for the round trip; the response replaces it.
  const [pendingOrder, setPendingOrder] = useState<string[]>()
  const [draggingId, setDraggingId] = useState<string>()
  const [dropTargetId, setDropTargetId] = useState<string>()
  const savePins = useMutation({
    mutationFn: (entityIds: string[]) => api.setEntityPins(tenantId, entityIds),
    onSuccess: updated => client.setQueryData(['entities', tenantId], updated),
    onSettled: () => setPendingOrder(undefined),
  })

  const pinnedIds = pendingOrder ?? pinnedEntities(entities).map(entity => entity.id)
  const pinned = pinnedIds.map(id => entities.find(entity => entity.id === id)).filter((entity): entity is Entity => Boolean(entity))

  function savePinnedOrder(entityIds: string[]) {
    setPendingOrder(entityIds)
    savePins.mutate(entityIds)
  }
  function togglePin(entity: Entity) {
    savePinnedOrder(pinnedIds.includes(entity.id)
      ? pinnedIds.filter(id => id !== entity.id)
      : [...pinnedIds, entity.id])
  }
  function move(entityId: string, to: number) {
    const from = pinnedIds.indexOf(entityId)
    if (from < 0 || to < 0 || to >= pinnedIds.length || to === from) return
    const next = pinnedIds.filter(id => id !== entityId)
    next.splice(to, 0, entityId)
    savePinnedOrder(next)
  }
  function drop(event: DragEvent, targetId: string) {
    event.preventDefault()
    setDropTargetId(undefined)
    const dragged = draggingId ?? event.dataTransfer.getData('text/plain')
    setDraggingId(undefined)
    if (dragged) move(dragged, pinnedIds.indexOf(targetId))
  }
  function moveByKey(event: KeyboardEvent, entityId: string) {
    const delta = event.key === 'ArrowUp' ? -1 : event.key === 'ArrowDown' ? 1 : 0
    if (!delta) return
    event.preventDefault()
    move(entityId, pinnedIds.indexOf(entityId) + delta)
  }

  return <section className="page"><header><div><p className="eyebrow">Workspace overview</p><h2>Entities</h2></div></header>
    <div className="panel"><div className="panel-header"><h3>Pinned</h3><div className="actions"><span>{pinned.length} pinned</span></div></div>
      <div className="panel-body">{pinned.length ? <ul className="definition-list pinned-list">{pinned.map(entity =>
        <EntityRow key={entity.id} entity={entity}
          className={[draggingId === entity.id && 'dragging', dropTargetId === entity.id && 'drop-target'].filter(Boolean).join(' ')}
          draggable onDragStart={event => { setDraggingId(entity.id); event.dataTransfer.setData('text/plain', entity.id); event.dataTransfer.effectAllowed = 'move' }}
          onDragEnd={() => { setDraggingId(undefined); setDropTargetId(undefined) }}
          onDragOver={event => { event.preventDefault(); event.dataTransfer.dropEffect = 'move'; setDropTargetId(entity.id) }}
          onDragLeave={() => setDropTargetId(current => current === entity.id ? undefined : current)}
          onDrop={event => drop(event, entity.id)}
          handle={<button type="button" className="drag-handle" title="Drag to reorder, or use the arrow keys"
            aria-label={`Reorder ${entity.displayName}`} onKeyDown={event => moveByKey(event, entity.id)}><GripVertical /></button>}>
          <EntityLinks entity={entity} onEditIcon={setEditingIcon} />
          <button type="button" className="link" title={`Unpin ${entity.displayName}`}
            onClick={() => togglePin(entity)}><PinOff />Unpin</button>
        </EntityRow>)}</ul> :
        <p className="empty">No pinned entities yet. Pin one to keep it in the sidebar.</p>}
      {savePins.error && <p className="error">{savePins.error.message}</p>}</div>
    </div>
    <div className="panel"><div className="panel-header"><h3>All entities</h3><div className="actions"><span>{entities.length} {entities.length === 1 ? 'entity' : 'entities'}</span><button type="button" onClick={onCreateEntity}><Plus />New entity</button></div></div>
      <div className="panel-body">{entities.length ? <ul className="definition-list">{entities.map(entity =>
        <EntityRow key={entity.id} entity={entity}>
          <EntityLinks entity={entity} onEditIcon={setEditingIcon} />
          {pinnedIds.includes(entity.id)
            ? <button type="button" className="link" title={`Unpin ${entity.displayName}`} onClick={() => togglePin(entity)}><PinOff />Unpin</button>
            : <button type="button" className="link" title={`Pin ${entity.displayName} to the sidebar`} onClick={() => togglePin(entity)}><Pin />Pin</button>}
        </EntityRow>)}</ul> : <div className="list-empty"><p className="empty">No entities yet.</p>
        <button type="button" onClick={onCreateEntity}><Building2 />Create your first entity</button></div>}</div>
    </div>
    {editingIcon && <EntityIconModal tenantId={tenantId} entity={editingIcon} onClose={() => setEditingIcon(undefined)} />}
  </section>
}

function EntityRow({ entity, children, handle, ...row }: {
  entity: Entity; children: ReactNode; handle?: ReactNode
} & LiHTMLAttributes<HTMLLIElement>) {
  return <li {...row}>
    <div className="entity-title">{handle}<span className="entity-icon"><EntityIcon icon={entity.icon} /></span>
      <div><strong>{entity.displayName}</strong><small>{describeEntity(entity)}</small></div></div>
    <div className="actions">{children}</div>
  </li>
}

function EntityLinks({ entity, onEditIcon }: { entity: Entity; onEditIcon: (entity: Entity) => void }) {
  return <>
    <Link className="link" to={`/entities/${entity.id}/records`}><FolderOpen />Open</Link>
    <Link className="link" to={`/entities/${entity.id}/fields`}><ListTree />Fields</Link>
    <button type="button" className="link" title={`Change the icon for ${entity.displayName}`}
      onClick={() => onEditIcon(entity)}><Pencil />Icon</button>
  </>
}

function EntityIconModal({ tenantId, entity, onClose }: { tenantId: string; entity: Entity; onClose: () => void }) {
  const client = useQueryClient()
  const [icon, setIcon] = useState(entity.icon)
  const isDirty = (icon ?? '') !== (entity.icon ?? '')
  const save = useMutation({
    // An empty icon clears the stored one, taking the entity back to the default.
    mutationFn: () => api.updateEntity(tenantId, entity.id, { icon: icon ?? '' }),
    onSuccess: () => { void client.invalidateQueries({ queryKey: ['entities', tenantId] }); onClose() },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    if (isDirty) save.mutate()
  }

  return <Modal title={`Icon for ${entity.displayName}`} onClose={onClose}>
    <form className="entity-icon-editor" onSubmit={submit}>
      <p className="field-note">The icon shows on this list and beside the entity in the sidebar.</p>
      <EntityIconPicker icon={icon} onChange={setIcon} />
      {save.error && <p className="error">{save.error.message}</p>}
      <div className="modal-footer"><button disabled={!isDirty || save.isPending}><Save />{save.isPending ? 'Saving…' : 'Save icon'}</button>
        <button type="button" className="secondary" disabled={save.isPending} onClick={onClose}><X />Cancel</button></div>
    </form>
  </Modal>
}

function describeEntity(entity: Entity) {
  const facts = [entity.name, `Schema v${entity.schemaVersion}`, `Updated ${new Date(entity.updatedAt).toLocaleDateString()}`]
  if (entity.status !== 'Active') facts.unshift(entity.status)
  return [entity.description, facts.join(' · ')].filter(Boolean).join(' — ')
}
