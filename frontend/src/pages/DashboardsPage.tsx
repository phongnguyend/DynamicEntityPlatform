import { RuntimeParametersBadge } from '../components/RuntimeParametersBadge'
import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowLeft,
  BarChart3,
  Braces,
  GripVertical,
  Maximize2,
  Pencil,
  Plus,
  RefreshCw,
  SlidersHorizontal,
  Trash2,
  X,
} from 'lucide-react'
import { Responsive, useContainerWidth, verticalCompactor, type ResponsiveLayouts } from 'react-grid-layout'
import { Link, useNavigate, useParams } from 'react-router-dom'
import 'react-grid-layout/css/styles.css'
import 'react-resizable/css/styles.css'
import { api } from '../api'
import { DashboardEditor } from '../components/DashboardEditor'
import { DashboardParametersModal } from '../components/DashboardParametersModal'
import { Modal } from '../components/Modal'
import { HighlightedCode } from '../components/HighlightedCode'
import { ReportViewer } from '../components/ReportViewer'
import { RuntimeParametersForm, metricParameterNames } from '../components/RuntimeParametersForm'
import type {
  Dashboard,
  DashboardDefinition,
  DashboardGridBreakpoint as GridBreakpoint,
  DashboardItem,
  Entity,
  Metric,
  Report,
} from '../types'

const breakpoints: Record<GridBreakpoint, number> = { lg: 1200, md: 900, sm: 640, xs: 420, xxs: 0 }
const columns: Record<GridBreakpoint, number> = { lg: 12, md: 10, sm: 6, xs: 4, xxs: 2 }
const gridBreakpoints = Object.keys(breakpoints) as GridBreakpoint[]
const itemKey = (item: DashboardItem) => `${item.kind}:${item.entityId}:${item.id}`

function fitLayouts(
  items: DashboardItem[],
  layouts: ResponsiveLayouts<GridBreakpoint>,
): ResponsiveLayouts<GridBreakpoint> {
  const keys = new Set(items.map(itemKey))
  const next: ResponsiveLayouts<GridBreakpoint> = {}
  for (const breakpoint of gridBreakpoints) {
    const cols = columns[breakpoint]
    const current = (layouts[breakpoint] ?? [])
      .filter((item) => keys.has(item.i))
      .map((item) => {
        const w = Math.max(1, Math.min(item.w, cols))
        return { ...item, x: Math.max(0, Math.min(item.x, cols - w)), w }
      })
    const present = new Set(current.map((item) => item.i))
    for (const item of items) {
      const key = itemKey(item)
      if (present.has(key)) {
        continue
      }
      const reportWidth = Math.min(cols, breakpoint === 'lg' ? 6 : breakpoint === 'md' ? 5 : cols)
      const metricWidth = Math.min(
        cols,
        breakpoint === 'lg' ? 3 : breakpoint === 'md' ? 5 : breakpoint === 'sm' ? 3 : cols,
      )
      current.push({
        i: key,
        x: 0,
        y: current.reduce((bottom, entry) => Math.max(bottom, entry.y + entry.h), 0),
        w: item.kind === 'report' ? reportWidth : metricWidth,
        h: item.kind === 'report' ? 9 : 4,
        minW: Math.min(cols, item.kind === 'report' ? 3 : 2),
        minH: item.kind === 'report' ? 5 : 3,
      })
    }
    next[breakpoint] = current
  }
  return next
}

export function DashboardsPage({ tenantId, entities }: { tenantId: string; entities: Entity[] }) {
  const { dashboardId = '' } = useParams()
  const navigate = useNavigate()
  const client = useQueryClient()
  const dashboardQuery = useQuery({
    queryKey: ['dashboard', tenantId, dashboardId],
    queryFn: () => api.dashboard(tenantId, dashboardId),
  })
  const [draft, setDraft] = useState<{ dashboardId: string; definition: DashboardDefinition }>()
  const [libraryOpen, setLibraryOpen] = useState(false)
  const [renaming, setRenaming] = useState(false)
  const [jsonOpen, setJsonOpen] = useState(false)
  const [expandedKey, setExpandedKey] = useState<string>()
  const [configureKey, setConfigureKey] = useState<string>()
  const [parametersOpen, setParametersOpen] = useState(false)
  const dashboard = dashboardQuery.data
  const definition =
    draft?.dashboardId === dashboardId ? draft.definition : (dashboard?.definition ?? { items: [], layouts: {} })
  const { width, containerRef, mounted, measureWidth } = useContainerWidth({
    initialWidth: 1000,
    measureBeforeMount: true,
  })
  const reportQueries = useQueries({
    queries: entities.map((entity) => ({
      queryKey: ['reports', tenantId, entity.id],
      queryFn: () => api.reports(tenantId, entity.id),
    })),
  })
  const metricQueries = useQueries({
    queries: entities.map((entity) => ({
      queryKey: ['metrics', tenantId, entity.id],
      queryFn: () => api.metrics(tenantId, entity.id),
    })),
  })
  const reports = useMemo(() => reportQueries.flatMap((query) => query.data ?? []), [reportQueries])
  const metrics = useMemo(() => metricQueries.flatMap((query) => query.data ?? []), [metricQueries])
  const layouts = useMemo(
    () => fitLayouts(definition.items, definition.layouts),
    [definition.items, definition.layouts],
  )
  const available = useMemo(
    () =>
      new Set([
        ...reports.map((report) => itemKey({ kind: 'report', entityId: report.entityId, id: report.id })),
        ...metrics.map((metric) => itemKey({ kind: 'metric', entityId: metric.entityId, id: metric.id })),
      ]),
    [reports, metrics],
  )
  const loading = reportQueries.some((query) => query.isLoading) || metricQueries.some((query) => query.isLoading)
  const error = reportQueries.find((query) => query.error)?.error ?? metricQueries.find((query) => query.error)?.error

  useEffect(() => {
    if (dashboard) {
      measureWidth()
    }
  }, [dashboard, measureWidth])

  const save = useMutation({
    mutationFn: (next: DashboardDefinition) => api.updateDashboard(tenantId, dashboardId, dashboard!.name, next),
    scope: { id: `dashboard-${dashboardId}` },
    onSuccess: (saved) => {
      client.setQueryData<Dashboard>(['dashboard', tenantId, dashboardId], saved)
      void client.invalidateQueries({ queryKey: ['dashboards', tenantId] })
    },
  })
  const deleteDashboard = useMutation({
    mutationFn: () => api.deleteDashboard(tenantId, dashboardId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['dashboards', tenantId] })
      navigate('/dashboards')
    },
  })

  function updateDashboard(update: (current: DashboardDefinition) => DashboardDefinition) {
    if (!dashboard) {
      return
    }
    const next = update(definition)
    setDraft({ dashboardId, definition: next })
    save.mutate(next)
  }

  function add(item: DashboardItem) {
    updateDashboard((current) => {
      if (current.items.some((existing) => itemKey(existing) === itemKey(item))) {
        return current
      }
      const items = [...current.items, item]
      return { ...current, items, layouts: fitLayouts(items, current.layouts) }
    })
  }
  function remove(key: string) {
    updateDashboard((current) => {
      const items = current.items.filter((item) => itemKey(item) !== key)
      return { ...current, items, layouts: fitLayouts(items, current.layouts) }
    })
  }
  function saveLayouts(next: ResponsiveLayouts<GridBreakpoint>) {
    updateDashboard((current) => ({ ...current, layouts: fitLayouts(current.items, next) }))
  }
  function removeDashboard() {
    if (dashboard && confirm(`Delete the dashboard "${dashboard.name}"?`)) {
      deleteDashboard.mutate()
    }
  }

  const entityName = (entityId: string) =>
    entities.find((entity) => entity.id === entityId)?.displayName ?? 'Unknown entity'
  const expandedItem = definition.items.find((item) => itemKey(item) === expandedKey)
  const configureItem = definition.items.find((item) => itemKey(item) === configureKey)
  const configureSource =
    configureItem?.kind === 'report'
      ? reports.find((report) => report.id === configureItem.id && report.entityId === configureItem.entityId)
      : metrics.find((metric) => metric.id === configureItem?.id && metric.entityId === configureItem?.entityId)
  const expandedReport =
    expandedItem?.kind === 'report'
      ? reports.find((report) => report.id === expandedItem.id && report.entityId === expandedItem.entityId)
      : undefined
  const expandedMetric =
    expandedItem?.kind === 'metric'
      ? metrics.find((metric) => metric.id === expandedItem.id && metric.entityId === expandedItem.entityId)
      : undefined
  if (dashboardQuery.isLoading) {
    return <p>Loading dashboard…</p>
  }
  if (dashboardQuery.error) {
    return (
      <section className="page">
        <p className="error">{dashboardQuery.error.message}</p>
        <Link className="button" to="/dashboards">
          <ArrowLeft />
          Back to dashboards
        </Link>
      </section>
    )
  }
  if (!dashboard) {
    return (
      <section className="page">
        <p className="error">Dashboard not found.</p>
        <Link className="button" to="/dashboards">
          <ArrowLeft />
          Back to dashboards
        </Link>
      </section>
    )
  }

  return (
    <section className="page dashboard-page">
      <header>
        <div>
          <p className="eyebrow">Dashboard</p>
          <h2>{dashboard.name}</h2>
          <p className="dashboard-intro">
            Drag cards by their handles and resize them from the lower-right corner. Cards automatically pack upward.
          </p>
        </div>
        <div className="page-actions">
          <Link className="button secondary" to="/dashboards">
            <ArrowLeft />
            All dashboards
          </Link>
          <button type="button" className="secondary" onClick={() => setJsonOpen(true)}>
            <Braces />
            View JSON
          </button>
          <button type="button" className="secondary" onClick={() => setParametersOpen(true)}>
            <SlidersHorizontal />
            Parameters ({definition.parameters?.length ?? 0})
          </button>
          <button type="button" className="secondary" onClick={() => setRenaming(true)}>
            <Pencil />
            Edit
          </button>
          <button type="button" className="secondary dashboard-delete" onClick={removeDashboard}>
            <Trash2 />
            Delete
          </button>
          <button type="button" onClick={() => setLibraryOpen(true)}>
            <Plus />
            Add cards
          </button>
        </div>
      </header>
      {(error || save.error || deleteDashboard.error) && (
        <p className="error">{(error ?? save.error ?? deleteDashboard.error)?.message}</p>
      )}
      <div ref={containerRef} className="dashboard-grid-container" aria-label="Dashboard cards">
        {!definition.items.length && (
          <div className="dashboard-empty">
            <BarChart3 />
            <h3>Your dashboard is empty</h3>
            <p>Add cards from the library, then position and resize them however you like.</p>
          </div>
        )}
        {mounted && definition.items.length > 0 && (
          <Responsive<GridBreakpoint>
            width={width}
            layouts={layouts}
            breakpoints={breakpoints}
            cols={columns}
            rowHeight={36}
            margin={[16, 16]}
            containerPadding={[0, 0]}
            compactor={verticalCompactor}
            autoSize
            dragConfig={{ enabled: true, handle: '.drag-handle' }}
            resizeConfig={{ enabled: true, handles: ['se'] }}
            onLayoutChange={(_, next) => saveLayouts(next)}
          >
            {definition.items.map((item) => {
              const key = itemKey(item)
              const report = reports.find((value) => value.id === item.id && value.entityId === item.entityId)
              const metric = metrics.find((value) => value.id === item.id && value.entityId === item.entityId)
              return (
                <article className="dashboard-card" key={key}>
                  <div className="dashboard-card-header">
                    <span className="drag-handle" title="Drag to move card">
                      <GripVertical />
                    </span>
                    <div>
                      <small>
                        {entityName(item.entityId)} · {item.kind}
                      </small>
                      <h3>{report?.name ?? metric?.name ?? 'Unavailable card'}</h3>
                      <RuntimeParametersBadge filter={report?.query.filter ?? metric?.filter} />
                    </div>
                    <div className="dashboard-card-actions">
                      {metricParameterNames(report?.query.filter ?? metric?.filter).length > 0 && (
                        <button
                          type="button"
                          className="link icon-only"
                          title="Configure runtime parameters"
                          aria-label="Configure runtime parameters"
                          onClick={() => setConfigureKey(key)}
                        >
                          <SlidersHorizontal />
                        </button>
                      )}
                      <button
                        className="link icon-only"
                        title="Open larger view"
                        aria-label="Open card in larger view"
                        onClick={() => setExpandedKey(key)}
                      >
                        <Maximize2 />
                      </button>
                      <button
                        className="link danger icon-only"
                        title="Remove from dashboard"
                        aria-label="Remove from dashboard"
                        onClick={() => remove(key)}
                      >
                        <Trash2 />
                      </button>
                    </div>
                  </div>
                  {!available.has(key) && !loading ? (
                    <p className="empty">This saved item no longer exists.</p>
                  ) : report ? (
                    <ReportCard
                      tenantId={tenantId}
                      report={report}
                      parameters={item.parameters}
                      onConfigure={() => setConfigureKey(key)}
                    />
                  ) : metric ? (
                    <MetricCard
                      tenantId={tenantId}
                      metric={metric}
                      parameters={item.parameters}
                      onConfigure={() => setConfigureKey(key)}
                    />
                  ) : (
                    <p className="empty">Loading card…</p>
                  )}
                </article>
              )
            })}
          </Responsive>
        )}
      </div>
      {libraryOpen && (
        <Modal title="Add dashboard cards" onClose={() => setLibraryOpen(false)}>
          <div className="dashboard-library">
            <div className="dashboard-library-summary">
              <span>{reports.length + metrics.length} available</span>
              <span>{definition.items.length} added</span>
            </div>
            {loading && <p className="empty">Loading cards…</p>}
            {!loading && !reports.length && !metrics.length && (
              <p className="empty">Create a report or metric in an entity to add it here.</p>
            )}
            {entities.map((entity) => {
              const entityReports = reports.filter((report) => report.entityId === entity.id)
              const entityMetrics = metrics.filter((metric) => metric.entityId === entity.id)
              if (!entityReports.length && !entityMetrics.length) {
                return null
              }
              return (
                <section className="dashboard-library-group" key={entity.id}>
                  <h4>{entity.displayName}</h4>
                  {[
                    ...entityMetrics.map((metric) => ({ kind: 'metric' as const, value: metric })),
                    ...entityReports.map((report) => ({ kind: 'report' as const, value: report })),
                  ].map((entry) => {
                    const card = { kind: entry.kind, entityId: entity.id, id: entry.value.id }
                    const added = definition.items.some((item) => itemKey(item) === itemKey(card))
                    return (
                      <button
                        type="button"
                        className="dashboard-library-item"
                        disabled={added}
                        key={itemKey(card)}
                        onClick={() => add(card)}
                      >
                        <span>
                          <small>{entry.kind}</small>
                          <strong>{entry.value.name}</strong>
                        </span>
                        <span className="dashboard-library-action">
                          {added ? (
                            'Added'
                          ) : (
                            <>
                              <Plus aria-hidden="true" />
                              Add
                            </>
                          )}
                        </span>
                      </button>
                    )
                  })}
                </section>
              )
            })}
          </div>
        </Modal>
      )}
      {jsonOpen && (
        <Modal title={`${dashboard.name} JSON`} onClose={() => setJsonOpen(false)}>
          <div className="json-editor">
            <HighlightedCode label="Definition" language="json" value={JSON.stringify(definition, null, 2)} />
            <div className="modal-footer">
              <button type="button" className="secondary" onClick={() => setJsonOpen(false)}>
                <X />
                Close
              </button>
            </div>
          </div>
        </Modal>
      )}
      {expandedItem && (
        <Modal
          title={expandedReport?.name ?? expandedMetric?.name ?? 'Dashboard card'}
          onClose={() => setExpandedKey(undefined)}
        >
          <div className="dashboard-card-expanded">
            <p className="dashboard-expanded-context">
              {entityName(expandedItem.entityId)} · {expandedItem.kind}
            </p>
            {expandedReport ? (
              <ReportCard
                tenantId={tenantId}
                report={expandedReport}
                parameters={expandedItem.parameters}
                onConfigure={() => setConfigureKey(expandedKey)}
              />
            ) : expandedMetric ? (
              <MetricCard
                tenantId={tenantId}
                metric={expandedMetric}
                parameters={expandedItem.parameters}
                onConfigure={() => setConfigureKey(expandedKey)}
              />
            ) : (
              <p className="empty">This saved item is no longer available.</p>
            )}
          </div>
        </Modal>
      )}
      {configureItem && configureSource && (
        <CardParametersModal
          key={configureKey}
          tenantId={tenantId}
          item={configureItem}
          source={configureSource}
          onClose={() => setConfigureKey(undefined)}
          onSave={async (parameters) => {
            const next = {
              ...definition,
              items: definition.items.map((item) => (itemKey(item) === configureKey ? { ...item, parameters } : item)),
            }
            await save.mutateAsync(next)
            setDraft({ dashboardId, definition: next })
            setConfigureKey(undefined)
          }}
        />
      )}
      {parametersOpen && (
        <DashboardParametersModal
          parameters={definition.parameters ?? []}
          onClose={() => setParametersOpen(false)}
          onSave={async (parameters) => {
            const next = { ...definition, parameters }
            await save.mutateAsync(next)
            setDraft({ dashboardId, definition: next })
            setParametersOpen(false)
          }}
        />
      )}
      {renaming && (
        <DashboardEditor
          tenantId={tenantId}
          dashboard={{ ...dashboard, definition }}
          onClose={() => setRenaming(false)}
          onSaved={(saved) => {
            setRenaming(false)
            client.setQueryData<Dashboard>(['dashboard', tenantId, dashboardId], saved)
            void client.invalidateQueries({ queryKey: ['dashboards', tenantId] })
          }}
        />
      )}
    </section>
  )
}

function CardParametersModal({
  tenantId,
  item,
  source,
  onClose,
  onSave,
}: {
  tenantId: string
  item: DashboardItem
  source: Report | Metric
  onClose: () => void
  onSave: (parameters: Record<string, unknown>) => Promise<void>
}) {
  const filter = 'query' in source ? source.query.filter : source.filter
  const saveParameters = useMutation({
    mutationFn: async (parameters: Record<string, unknown>) => {
      if (item.kind === 'report') {
        await api.runReport(tenantId, item.entityId, item.id, parameters)
      } else {
        await api.evaluateMetric(tenantId, item.entityId, item.id, parameters)
      }
      await onSave(parameters)
    },
  })
  return (
    <Modal
      title={`Configure parameters: ${source.name}`}
      onClose={() => {
        if (!saveParameters.isPending) {
          onClose()
        }
      }}
    >
      <p className="field-note">Values are saved for this dashboard card and reused when it loads or refreshes.</p>
      <RuntimeParametersForm
        names={metricParameterNames(filter)}
        initialValues={item.parameters}
        busy={saveParameters.isPending}
        submitLabel="Save parameters"
        onCancel={onClose}
        onEvaluate={(parameters) => saveParameters.mutate(parameters)}
      />
      {saveParameters.error && (
        <p className="error" role="alert">
          {saveParameters.error.message}
        </p>
      )}
    </Modal>
  )
}

type CardParameterProps = {
  parameters?: Record<string, unknown>
  onConfigure: () => void
}

function hasCardParameters(filter: unknown, parameters: Record<string, unknown> | undefined) {
  return metricParameterNames(filter, true).every((name) =>
    Object.prototype.hasOwnProperty.call(parameters ?? {}, name),
  )
}

function ReportCard({
  tenantId,
  report,
  parameters,
  onConfigure,
}: { tenantId: string; report: Report } & CardParameterProps) {
  const configured = hasCardParameters(report.query.filter, parameters)
  const result = useQuery({
    queryKey: ['dashboard-report', tenantId, report.entityId, report.id, report.updatedAt, parameters],
    queryFn: () => api.runReport(tenantId, report.entityId, report.id, parameters),
    enabled: configured,
  })
  return (
    <div className="dashboard-card-body">
      {result.data ? (
        <ReportViewer result={result.data} visualization={report.visualization} />
      ) : (
        <p className="empty">
          {result.isFetching
            ? 'Running report…'
            : configured
              ? 'No report result yet.'
              : 'Configure runtime parameters to run this report.'}
        </p>
      )}
      {result.error && <p className="error">{result.error.message}</p>}
      <button
        className="link dashboard-refresh"
        disabled={result.isFetching}
        onClick={() => {
          if (configured) {
            void result.refetch()
          } else {
            onConfigure()
          }
        }}
      >
        {configured ? <RefreshCw /> : <SlidersHorizontal />}
        {configured ? 'Refresh' : 'Configure parameters'}
      </button>
    </div>
  )
}

function MetricCard({
  tenantId,
  metric,
  parameters,
  onConfigure,
}: { tenantId: string; metric: Metric } & CardParameterProps) {
  const configured = hasCardParameters(metric.filter, parameters)
  const result = useQuery({
    queryKey: ['dashboard-metric', tenantId, metric.entityId, metric.id, metric.updatedAt, parameters],
    queryFn: () => api.evaluateMetric(tenantId, metric.entityId, metric.id, parameters),
    enabled: configured,
  })
  return (
    <div className="dashboard-card-body metric-dashboard-card">
      <strong>{result.data ? formatMetric(result.data.value, metric) : '—'}</strong>
      <span>
        {result.data
          ? `Updated ${new Date(result.data.evaluatedAt).toLocaleTimeString()}`
          : configured
            ? `${metric.aggregate} metric`
            : 'Configure runtime parameters to run this metric.'}
      </span>
      {result.error && <p className="error">{result.error.message}</p>}
      <button
        className="link dashboard-refresh"
        disabled={result.isFetching}
        onClick={() => {
          if (configured) {
            void result.refetch()
          } else {
            onConfigure()
          }
        }}
      >
        {configured ? <RefreshCw /> : <SlidersHorizontal />}
        {configured ? 'Refresh' : 'Configure parameters'}
      </button>
    </div>
  )
}
function formatMetric(value: unknown, metric: Metric) {
  const number = Number(value)
  const format = metric.format
  if (!Number.isFinite(number)) {
    return String(value ?? '—')
  }
  if (format?.style === 'duration') {
    const seconds = Math.max(0, Math.round(number))
    return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m ${seconds % 60}s`
  }
  if (format?.style === 'percentage') {
    return new Intl.NumberFormat(undefined, {
      style: 'percent',
      maximumFractionDigits: format.decimalPlaces ?? 2,
    }).format(number)
  }
  if (format?.style === 'currency') {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: format.currencyCode ?? 'USD',
      maximumFractionDigits: format.decimalPlaces ?? 2,
    }).format(number)
  }
  return new Intl.NumberFormat(undefined, {
    maximumFractionDigits: format?.style === 'integer' ? 0 : (format?.decimalPlaces ?? 2),
  }).format(number)
}
