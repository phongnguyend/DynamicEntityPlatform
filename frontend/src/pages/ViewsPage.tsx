import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Braces, FolderOpen, Pencil, Plus, Save, Trash2, X } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import { EntityTabs } from '../components/EntityTabs'
import { Modal } from '../components/Modal'
import type { SavedView } from '../types'
import { useEntityContext } from './useEntityContext'

export function ViewsPage({ tenantId }: { tenantId: string }) {
  const { entityId = '' } = useParams()
  const context = useEntityContext(tenantId, entityId)
  const client = useQueryClient()
  const [renaming, setRenaming] = useState<SavedView>()
  const [viewingJson, setViewingJson] = useState<SavedView>()
  const views = useQuery({ queryKey: ['views', tenantId, entityId], queryFn: () => api.views(tenantId, entityId) })
  const remove = useMutation({
    mutationFn: (viewId: string) => api.deleteView(tenantId, viewId),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['views', tenantId, entityId] }),
  })

  if (context.isLoading) return <p>Loading…</p>
  if (context.error || views.error) return <p className="error">{(context.error ?? views.error)?.message}</p>

  return <section className="page">
    <header><div><p className="eyebrow">Saved views</p><h2>{context.entity?.displayName}</h2></div></header>
    <EntityTabs entityId={entityId} />
    <div className="panel">
      <div className="panel-header"><h3>Views</h3><Link className="button" to={`/entities/${entityId}/views/new`}><Plus />New view</Link></div>
      <div className="panel-body">
      {views.data?.length ? <ul className="definition-list">{views.data.map(view => <li key={view.id}>
        <div><strong>{view.name}</strong><small>{describeView(view)}</small></div>
        <div className="actions">
          <Link className="link" to={`/entities/${entityId}/views/${view.id}`}><FolderOpen />Open</Link>
          <button type="button" className="link" onClick={() => setRenaming(view)}><Pencil />Rename</button>
          <button type="button" className="link" onClick={() => setViewingJson(view)}><Braces />View JSON</button>
          <button type="button" className="link danger" disabled={remove.isPending}
            onClick={() => { if (confirm(`Delete the saved view "${view.name}"?`)) remove.mutate(view.id) }}><Trash2 />Delete</button>
        </div>
      </li>)}</ul> : <p className="empty">No saved views yet.</p>}
      {remove.error && <p className="error">{remove.error.message}</p>}
      </div>
    </div>
    {renaming && <RenameViewModal tenantId={tenantId} entityId={entityId} view={renaming} onClose={() => setRenaming(undefined)} />}
    {viewingJson && <ViewJsonModal view={viewingJson} onClose={() => setViewingJson(undefined)} />}
  </section>
}

function RenameViewModal({ tenantId, entityId, view, onClose }: {
  tenantId: string
  entityId: string
  view: SavedView
  onClose: () => void
}) {
  const client = useQueryClient()
  const [name, setName] = useState(view.name)
  const save = useMutation({
    mutationFn: () => api.updateView(tenantId, view.id, entityId, name, view.definition),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['views', tenantId, entityId] })
      onClose()
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    if (name.trim() && name.trim() !== view.name) save.mutate()
  }

  return <Modal title={`Rename ${view.name}`} onClose={onClose}>
    <form className="view-editor" onSubmit={submit}>
      <label className="field">Name<input autoFocus required maxLength={200} value={name} onChange={event => setName(event.target.value)} /></label>
      {save.error && <p className="error">{save.error.message}</p>}
      <div className="modal-footer"><button disabled={!name.trim() || name.trim() === view.name || save.isPending}><Save />{save.isPending ? 'Saving…' : 'Save name'}</button>
        <button type="button" className="secondary" disabled={save.isPending} onClick={onClose}><X />Cancel</button></div>
    </form>
  </Modal>
}

function ViewJsonModal({ view, onClose }: { view: SavedView; onClose: () => void }) {
  return <Modal title={`${view.name} JSON`} onClose={onClose}>
    <div className="json-editor">
      <label className="field">Definition<textarea readOnly value={JSON.stringify(view.definition, null, 2)} spellCheck={false} /></label>
      <div className="modal-footer"><button type="button" className="secondary" onClick={onClose}><X />Close</button></div>
    </div>
  </Modal>
}

function describeView(view: SavedView) {
  const pageSize = typeof view.definition.pageSize === 'number' ? `${view.definition.pageSize} rows` : 'Default page size'
  return view.definition.filter ? `Filtered · ${pageSize}` : pageSize
}
