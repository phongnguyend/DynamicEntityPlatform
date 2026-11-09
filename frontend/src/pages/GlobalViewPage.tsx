import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Braces, Columns3, Pencil, Play, Save, X } from 'lucide-react'
import { api } from '../api'
import type { AnalyticsColumn, GlobalView, GlobalViewSpecification } from '../types'
import { SqlDefinitionEditor } from '../components/SqlDefinitionEditor'
import { SqlPreviewButton } from '../components/SqlPreviewButton'
import { Modal } from '../components/Modal'
import { ColumnSettingsModal } from '../components/ColumnSettingsModal'
import { CsvExportButton } from '../components/CsvExportButton'
import { HighlightedCode } from '../components/HighlightedCode'
import { JsonEditorModal, requireJsonObject } from '../components/JsonEditorModal'
import {
  RuntimeParametersForm,
  metricParameterNames,
  runtimeParameterSource,
} from '../components/RuntimeParametersForm'
import { ViewColorPicker } from '../components/ViewColorPicker'
import './GlobalViewPage.css'

const emptyDefinition: GlobalViewSpecification = { sql: { enabled: true, text: '', parameters: [] }, rowLimit: 100 }

export function GlobalViewPage({ tenantId, editing = false }: { tenantId: string; editing?: boolean }) {
  const { viewId } = useParams()
  const view = useQuery({
    queryKey: ['global-view', tenantId, viewId],
    queryFn: () => api.globalView(tenantId, viewId!),
    enabled: Boolean(viewId),
  })
  if (view.isLoading) {
    return <p role="status">Loading view…</p>
  }
  if (view.error) {
    return (
      <p className="error" role="alert">
        {view.error.message}
      </p>
    )
  }
  return (
    <GlobalViewContent
      key={`${tenantId}:${viewId ?? 'new'}:${editing}`}
      tenantId={tenantId}
      view={view.data}
      editing={editing}
    />
  )
}

function GlobalViewContent({ tenantId, view, editing }: { tenantId: string; view?: GlobalView; editing: boolean }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const [name, setName] = useState(view?.name ?? '')
  const [description, setDescription] = useState(view?.description ?? '')
  const [definition, setDefinition] = useState(view?.definition ?? emptyDefinition)
  const [jsonOpen, setJsonOpen] = useState(false)
  const activeDefinition = editing ? definition : view!.definition
  const save = useMutation({
    mutationFn: (next: GlobalViewSpecification) =>
      api.saveGlobalView(
        tenantId,
        { name: editing ? name : view!.name, description: editing ? description : view?.description, definition: next },
        view?.id,
      ),
    onSuccess: (saved) => {
      client.setQueryData(['global-view', tenantId, saved.id], saved)
      void client.invalidateQueries({ queryKey: ['global-views', tenantId] })
      if (editing) {
        navigate(`/views/${saved.id}`)
      }
    },
  })
  const previewSql = () => api.globalViewSql(tenantId, activeDefinition)
  return (
    <section className="page global-view-page">
      <header>
        <div>
          <p className="eyebrow">{editing ? 'SQL view editor' : 'Global view · Read-only'}</p>
          <h2>{editing ? (view ? `Edit ${view.name}` : 'New global view') : view!.name}</h2>
          {!editing && view?.description && <p>{view.description}</p>}
        </div>
        <div className="page-actions">
          <Link className="button secondary" to="/views">
            <ArrowLeft aria-hidden="true" />
            Views
          </Link>
          {!editing && (
            <Link className="button" to={`/views/${view!.id}/edit`}>
              <Pencil aria-hidden="true" />
              Edit view
            </Link>
          )}
        </div>
      </header>
      <div className="panel">
        {editing && (
          <div className="panel-body designer">
            <label className="field">
              Name
              <input required maxLength={200} value={name} onChange={(event) => setName(event.target.value)} />
            </label>
            <label className="field">
              Description
              <input value={description} onChange={(event) => setDescription(event.target.value)} />
            </label>
            <SqlDefinitionEditor
              tenantId={tenantId}
              entityId=""
              value={definition.sql}
              onChange={(sql) => setDefinition((current) => ({ ...current, sql }))}
              previewSql={previewSql}
              generateSql={previewSql}
            />
            <label className="field">
              Row limit
              <input
                type="number"
                min={1}
                max={1000}
                value={definition.rowLimit}
                onChange={(event) => setDefinition((current) => ({ ...current, rowLimit: Number(event.target.value) }))}
              />
            </label>
            <ViewColorPicker
              color={definition.color}
              onChange={(color) => setDefinition((current) => ({ ...current, color }))}
            />
          </div>
        )}
        <div className="panel-footer">
          <button type="button" className="secondary" onClick={() => setJsonOpen(true)}>
            <Braces aria-hidden="true" />
            {editing ? 'Edit JSON' : 'View JSON'}
          </button>
          <SqlPreviewButton
            name={name}
            disabled={!activeDefinition.sql.text.trim()}
            filter={runtimeParameterSource(activeDefinition)}
            load={(parameters) => api.globalViewSql(tenantId, activeDefinition, parameters)}
          />
          {editing && (
            <>
              <Link className="button secondary" to={view ? `/views/${view.id}` : '/views'}>
                <X aria-hidden="true" />
                Cancel
              </Link>
              <button
                type="button"
                disabled={
                  !name.trim() ||
                  !definition.sql.text.trim() ||
                  definition.rowLimit < 1 ||
                  definition.rowLimit > 1000 ||
                  save.isPending
                }
                onClick={() => save.mutate(definition)}
              >
                <Save aria-hidden="true" />
                {save.isPending ? 'Saving…' : 'Save view'}
              </button>
            </>
          )}
        </div>
      </div>
      {save.error && (
        <p role="alert" className="error">
          {save.error.message}
        </p>
      )}
      <GlobalViewResults
        name={editing ? name : view!.name}
        tenantId={tenantId}
        definition={activeDefinition}
        viewId={editing ? undefined : view!.id}
        onSaveColumns={
          editing
            ? async (next) => setDefinition(next)
            : async (next) => {
                await save.mutateAsync(next)
              }
        }
      />
      {jsonOpen &&
        (editing ? (
          <JsonEditorModal
            title="Edit global view JSON"
            value={definition}
            onClose={() => setJsonOpen(false)}
            onSave={async (value) => {
              const input = requireJsonObject(value, { sql: 'object', rowLimit: 'number' })
              const next = input as unknown as GlobalViewSpecification
              await api.globalViewSql(tenantId, next)
              setDefinition(next)
            }}
          />
        ) : (
          <Modal title={`${view!.name} JSON`} onClose={() => setJsonOpen(false)}>
            <HighlightedCode label="Definition" language="json" value={JSON.stringify(activeDefinition, null, 2)} />
          </Modal>
        ))}
    </section>
  )
}

function GlobalViewResults({
  name,
  tenantId,
  definition,
  viewId,
  onSaveColumns,
}: {
  name: string
  tenantId: string
  definition: GlobalViewSpecification
  viewId?: string
  onSaveColumns: (definition: GlobalViewSpecification) => Promise<void>
}) {
  const source = runtimeParameterSource(definition)
  const names = metricParameterNames(source)
  const requiredNames = metricParameterNames(source, true)
  const [parametersOpen, setParametersOpen] = useState(false)
  const [columnsOpen, setColumnsOpen] = useState(false)
  const [run, setRun] = useState<
    { definition: GlobalViewSpecification; parameters: Record<string, unknown>; version: number } | undefined
  >(() => (viewId && !requiredNames.length ? { definition, parameters: {}, version: 0 } : undefined))
  const results = useQuery({
    queryKey: ['global-view-results', tenantId, viewId, run],
    queryFn: () =>
      viewId
        ? api.runGlobalView(tenantId, viewId, run!.parameters)
        : api.previewGlobalView(tenantId, run!.definition, run!.parameters),
    enabled: Boolean(run),
    retry: false,
  })
  function execute(parameters: Record<string, unknown>) {
    setRun((current) => ({ definition, parameters, version: (current?.version ?? 0) + 1 }))
    setParametersOpen(false)
  }
  const columns = results.data?.columns ?? []
  const byKey = new Map(columns.map((column) => [column.key, column]))
  const orderedKeys = [...new Set([...(definition.columnOrder ?? []), ...columns.map((column) => column.key)])].filter(
    (key) => byKey.has(key),
  )
  const visible = orderedKeys.filter((key) => !definition.hiddenColumns?.includes(key))
  return (
    <div className="panel">
      <div className="panel-header">
        <h3>Results</h3>
        <div className="page-actions">
          <CsvExportButton
            filename={name || 'View results'}
            columns={visible.map((key) => byKey.get(key)!)}
            rows={results.data?.rows ?? []}
            disabled={
              !results.data ||
              results.isFetching ||
              Boolean(results.error) ||
              JSON.stringify(run?.definition.sql) !== JSON.stringify(definition.sql) ||
              run?.definition.rowLimit !== definition.rowLimit
            }
          />
          <button type="button" className="secondary" disabled={!columns.length} onClick={() => setColumnsOpen(true)}>
            <Columns3 aria-hidden="true" />
            Column settings
          </button>
          <button
            type="button"
            disabled={!definition.sql.text.trim() || results.isFetching}
            onClick={() => (names.length ? setParametersOpen(true) : execute({}))}
          >
            <Play aria-hidden="true" />
            {viewId ? 'Run view' : 'Preview results'}
          </button>
        </div>
      </div>
      <div className="panel-body">
        {!run && (
          <p className="empty">
            {requiredNames.length
              ? 'Run this view to provide its runtime parameter values.'
              : 'Preview the SQL query to see its results.'}
          </p>
        )}
        {results.isFetching && <p role="status">Running query…</p>}
        {results.error && (
          <p role="alert" className="error">
            {results.error.message}
          </p>
        )}
        {results.data && (
          <>
            <p className="field-note">
              Read-only results · {results.data.rows.length} rows
              {results.data.truncated
                ? ` · Row limit reached (${run?.definition.rowLimit}). More rows are available.`
                : ''}
            </p>
            <p className="field-note">
              CSV exports these displayed rows and visible columns, within the configured row limit.
            </p>
            {run && run.definition.sql.text !== definition.sql.text && (
              <p className="field-note">The SQL has changed. Preview again to update these results.</p>
            )}
            {!visible.length && columns.length > 0 && (
              <p className="empty">All result columns are hidden. Use Column settings to show them.</p>
            )}
            {visible.length > 0 && (
              <div className="grid-wrap">
                <table>
                  <thead>
                    <tr>
                      {visible.map((key) => (
                        <th key={key}>{byKey.get(key)!.label}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {results.data.rows.map((row, index) => (
                      <tr key={index}>
                        {visible.map((key) => (
                          <td key={key}>{formatValue(row[key])}</td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {!results.data.rows.length && <p className="empty">No rows match this query.</p>}
          </>
        )}
      </div>
      {parametersOpen && (
        <Modal title="View parameters" onClose={() => setParametersOpen(false)}>
          <RuntimeParametersForm
            names={names}
            busy={results.isFetching}
            optional={!requiredNames.length}
            initialValues={run?.parameters}
            submitLabel="Run view"
            onEvaluate={execute}
            onCancel={() => setParametersOpen(false)}
          />
        </Modal>
      )}
      {columnsOpen && (
        <GlobalViewColumns
          definition={definition}
          columns={orderedKeys.map((key) => byKey.get(key)!)}
          onClose={() => setColumnsOpen(false)}
          onSave={onSaveColumns}
        />
      )}
    </div>
  )
}

function GlobalViewColumns({
  definition,
  columns,
  onClose,
  onSave,
}: {
  definition: GlobalViewSpecification
  columns: AnalyticsColumn[]
  onClose: () => void
  onSave: (definition: GlobalViewSpecification) => Promise<void>
}) {
  return (
    <ColumnSettingsModal
      columns={columns.map((column) => ({
        id: column.key,
        name: column.key,
        displayName: column.label,
        dataType: column.dataType,
      }))}
      settings={{ order: columns.map((column) => column.key), hidden: definition.hiddenColumns ?? [] }}
      description="Drag columns into display order and choose which columns are visible. Apply settings to update this view."
      onClose={onClose}
      onApply={async (settings) => {
        await onSave({ ...definition, columnOrder: settings.order, hiddenColumns: settings.hidden })
        onClose()
      }}
    />
  )
}

function formatValue(value: unknown) {
  if (value === null || value === undefined) {
    return '—'
  }
  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}
