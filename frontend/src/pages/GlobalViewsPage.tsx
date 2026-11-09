import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Braces, FolderOpen, Pencil, Plus, Trash2 } from 'lucide-react'
import { api } from '../api'
import type { GlobalView } from '../types'
import { Modal } from '../components/Modal'
import { HighlightedCode } from '../components/HighlightedCode'
import { SqlPreviewButton } from '../components/SqlPreviewButton'
import { RuntimeParametersBadge } from '../components/RuntimeParametersBadge'
import { runtimeParameterSource } from '../components/RuntimeParametersForm'
import { ViewColorDot, getViewColor } from '../components/ViewColorPicker'

export function GlobalViewsPage({ tenantId }: { tenantId: string }) {
  const client = useQueryClient()
  const views = useQuery({ queryKey: ['global-views', tenantId], queryFn: () => api.globalViews(tenantId) })
  const [jsonView, setJsonView] = useState<GlobalView>()
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteGlobalView(tenantId, id),
    onSuccess: () => client.invalidateQueries({ queryKey: ['global-views', tenantId] }),
  })
  return (
    <section className="page">
      <header>
        <div>
          <p className="eyebrow">Saved SQL views</p>
          <h2>Global views</h2>
          <p>Query across entities and explore read-only results.</p>
        </div>
      </header>
      <div className="panel">
        <div className="panel-header">
          <h3>Views</h3>
          <Link className="button" to="/views/new">
            <Plus aria-hidden="true" />
            New view
          </Link>
        </div>
        <div className="panel-body">
          {views.isLoading && <p role="status">Loading views…</p>}
          {(views.error || remove.error) && (
            <p className="error" role="alert">
              {views.error?.message ?? remove.error?.message}
            </p>
          )}
          {views.data?.length === 0 && <p className="empty">No global views yet. Create a SQL view to get started.</p>}
          <ul className="definition-list">
            {views.data?.map((view) => (
              <li key={view.id}>
                <div>
                  <strong className="view-title">
                    <ViewColorDot color={getViewColor({ color: view.definition.color })} />
                    {view.name}
                  </strong>
                  <small>{view.description || `Read-only · Up to ${view.definition.rowLimit} rows`}</small>
                  <RuntimeParametersBadge filter={runtimeParameterSource(view.definition)} />
                </div>
                <div className="actions">
                  <Link className="link" to={`/views/${view.id}`}>
                    <FolderOpen aria-hidden="true" />
                    Open
                  </Link>
                  <Link className="link" to={`/views/${view.id}/edit`}>
                    <Pencil aria-hidden="true" />
                    Edit
                  </Link>
                  <button type="button" className="link" onClick={() => setJsonView(view)}>
                    <Braces aria-hidden="true" />
                    View JSON
                  </button>
                  <SqlPreviewButton
                    name={view.name}
                    filter={runtimeParameterSource(view.definition)}
                    className="link"
                    load={(parameters) => api.globalViewSql(tenantId, view.definition, parameters)}
                  />
                  <button
                    type="button"
                    className="link danger"
                    disabled={remove.isPending}
                    onClick={() => {
                      if (confirm(`Delete the global view "${view.name}"?`)) {
                        remove.mutate(view.id)
                      }
                    }}
                  >
                    <Trash2 aria-hidden="true" />
                    Delete
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </div>
      </div>
      {jsonView && (
        <Modal title={`${jsonView.name} JSON`} onClose={() => setJsonView(undefined)}>
          <HighlightedCode label="Definition" language="json" value={JSON.stringify(jsonView.definition, null, 2)} />
        </Modal>
      )}
    </section>
  )
}
