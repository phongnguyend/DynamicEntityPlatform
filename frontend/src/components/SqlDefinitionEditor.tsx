import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Check, Copy, Plus, Search, Table2, SlidersHorizontal, Code2, Wand2, CheckCircle2 } from 'lucide-react'
import { api } from '../api'
import type { SqlPreview, VirtualSqlDefinition } from '../types'
import { HighlightedCode } from './HighlightedCode'
import type { CodeEditorHandle } from './useCodeHistory'
import './SqlDefinitionEditor.css'

const emptySql: VirtualSqlDefinition = { enabled: false, text: '', parameters: [] }
const types = ['Text', 'Integer', 'Decimal', 'Boolean', 'Date', 'DateTime', 'Guid']

function CopySchemaName({ name, kind }: { name: string; kind: 'table' | 'column' }) {
  const [feedback, setFeedback] = useState('')
  useEffect(() => {
    if (!feedback) {
      return
    }
    const timeout = window.setTimeout(() => setFeedback(''), 2500)
    return () => window.clearTimeout(timeout)
  }, [feedback])
  async function copy() {
    try {
      await navigator.clipboard.writeText(name)
      setFeedback('Copied')
    } catch {
      setFeedback('Unable to copy. Please try again.')
    }
  }
  return (
    <>
      <button
        type="button"
        className="sql-schema-copy"
        aria-label={`Copy ${kind} name ${name}`}
        title={feedback || `Copy ${name}`}
        onClick={copy}
      >
        {feedback === 'Copied' ? <Check aria-hidden="true" /> : <Copy aria-hidden="true" />}
      </button>
      <span className="sr-only" role="status">
        {feedback}
      </span>
    </>
  )
}

// Skip comments, quoted strings and identifiers when suggesting parameter declarations.
export function sqlParameterNames(text: string) {
  const tokens =
    text.match(
      /--[^\r\n]*|\/\*[\s\S]*?\*\/|'(?:''|[^'])*'|\[(?:\]\]|[^\]])*\]|"(?:""|[^"])*"|@@?[A-Za-z][A-Za-z0-9_]*/g,
    ) ?? []
  const names = new Map<string, string>()
  for (const token of tokens) {
    if (token.startsWith('@') && !token.startsWith('@@') && !names.has(token.toLowerCase())) {
      names.set(token.toLowerCase(), token.slice(1))
    }
  }
  return [...names.values()]
}

export function SqlDefinitionEditor({
  tenantId,
  entityId,
  value,
  onChange,
  previewSql,
  generateSql,
  metric = false,
}: {
  tenantId: string
  entityId: string
  value?: VirtualSqlDefinition
  onChange: (value: VirtualSqlDefinition) => void
  previewSql: () => Promise<SqlPreview>
  generateSql: () => Promise<SqlPreview>
  metric?: boolean
}) {
  const sqlOnly = !entityId
  const definition = value ?? { ...emptySql, enabled: sqlOnly }
  const editor = useRef<CodeEditorHandle>(null)
  const [tableId, setTableId] = useState(entityId)
  const [search, setSearch] = useState('')
  const [entitySearch, setEntitySearch] = useState('')
  const entities = useQuery({ queryKey: ['entities', tenantId], queryFn: () => api.entities(tenantId) })
  const fields = useQuery({
    queryKey: ['fields', tenantId, tableId],
    queryFn: () => api.fields(tenantId, tableId),
    enabled: !!tableId,
  })
  const validation = useMutation({ mutationFn: previewSql })
  useEffect(() => {
    validation.reset()
  }, [value])
  function changeText(text: string) {
    validation.reset()
    onChange({
      ...definition,
      text,
      parameters: sqlParameterNames(text).map(
        (name) =>
          definition.parameters.find((parameter) => parameter.parameter === name) ?? { parameter: name, type: 'Text' },
      ),
    })
  }
  function insert(text: string) {
    editor.current?.insertText(text)
  }
  const generate = useMutation({
    mutationFn: generateSql,
    onSuccess: (preview) => {
      const parameters = preview.virtualParameters.map((parameter) => ({
        parameter: parameter.name.replace(/^@/, ''),
        type:
          typeof parameter.value === 'number'
            ? Number.isInteger(parameter.value)
              ? 'Integer'
              : 'Decimal'
            : typeof parameter.value === 'boolean'
              ? 'Boolean'
              : 'Text',
        ...(parameter.value == null ? {} : { defaultValue: parameter.value }),
      }))
      onChange({ ...definition, enabled: true, text: preview.virtualSql, parameters })
      validation.reset()
    },
  })
  const table = entities.data?.find((entity) => entity.id === tableId)
  const matchingEntities = (entities.data ?? []).filter((entity) =>
    entity.name.toLowerCase().includes(entitySearch.trim().toLowerCase()),
  )
  const schemaFields = [
    { name: 'Id', dataType: 'Guid' },
    { name: 'CreatedAt', dataType: 'DateTime' },
    { name: 'UpdatedAt', dataType: 'DateTime' },
    ...(fields.data ?? []),
  ]
  const matchingFields = schemaFields.filter((field) => field.name.toLowerCase().includes(search.trim().toLowerCase()))
  const quote = (name: string) => `[${name.replaceAll(']', ']]')}]`
  return (
    <div className="sql-definition-editor">
      {!sqlOnly && (
        <div className="sql-definition-mode" role="group" aria-label="Definition mode">
          <button
            type="button"
            className={!definition.enabled ? '' : 'secondary'}
            aria-pressed={!definition.enabled}
            onClick={() => onChange({ ...definition, enabled: false })}
          >
            <SlidersHorizontal aria-hidden="true" />
            Builder
          </button>
          <button
            type="button"
            className={definition.enabled ? '' : 'secondary'}
            aria-pressed={definition.enabled}
            onClick={() => onChange({ ...definition, enabled: true })}
          >
            <Code2 aria-hidden="true" />
            SQL
          </button>
        </div>
      )}
      {definition.enabled && (
        <>
          <p className="field-note">
            Use virtual entity and field names, and @name for parameters.{' '}
            {metric
              ? 'Return exactly one row and one column.'
              : 'Return named columns; numeric outputs are measures by default.'}{' '}
            Use SYSUTCDATETIME() for now or CAST(SYSUTCDATETIME() AS date) for today.
          </p>
          <HighlightedCode
            editorRef={editor}
            label="SQL query"
            language="sql"
            value={definition.text}
            onChange={changeText}
          />
          <details className="sql-schema-browser">
            <summary>
              Schema browser <span className="sql-schema-summary-note">Insert tables and fields</span>
            </summary>
            <div className="sql-schema-content">
              <section className="sql-schema-pane" aria-label="Entities">
                <div className="sql-schema-pane-heading">
                  <strong>
                    Entities <span>{matchingEntities.length}</span>
                  </strong>
                  <small>Select to browse columns</small>
                </div>
                <label className="sql-schema-search">
                  <Search aria-hidden="true" />
                  <input
                    aria-label="Search entities"
                    type="search"
                    placeholder="Search entities…"
                    value={entitySearch}
                    onChange={(event) => setEntitySearch(event.target.value)}
                  />
                </label>
                <div className="sql-schema-scroll">
                  <ul className="sql-schema-entities">
                    {matchingEntities.map((entity) => (
                      <li key={entity.id} className={entity.id === tableId ? 'is-selected' : ''}>
                        <button
                          type="button"
                          className="sql-schema-entity"
                          aria-pressed={entity.id === tableId}
                          onClick={() => {
                            setTableId(entity.id)
                            setSearch('')
                          }}
                        >
                          <Table2 aria-hidden="true" />
                          <span>{entity.name}</span>
                        </button>
                        <CopySchemaName name={entity.name} kind="table" />
                        <button
                          type="button"
                          className="sql-schema-insert"
                          aria-label={`Insert table ${entity.name}`}
                          title={`Insert ${quote(entity.name)}`}
                          onClick={() => insert(quote(entity.name))}
                        >
                          <Plus aria-hidden="true" />
                        </button>
                      </li>
                    ))}
                  </ul>
                  {entities.isLoading && (
                    <p className="sql-schema-empty" role="status">
                      Loading entities…
                    </p>
                  )}
                  {!entities.isLoading && !entities.error && !matchingEntities.length && (
                    <p className="sql-schema-empty" role="status">
                      No entities found.
                    </p>
                  )}
                  {entities.error && <p className="error">{entities.error.message}</p>}
                </div>
              </section>
              <section className="sql-schema-pane" aria-label="Columns">
                <div className="sql-schema-pane-heading">
                  <strong>
                    Columns <span>{table ? matchingFields.length : 0}</span>
                  </strong>
                  <small title={table?.name}>{table?.name ?? 'Select an entity'}</small>
                </div>
                <label className="sql-schema-search">
                  <Search aria-hidden="true" />
                  <input
                    aria-label="Search columns"
                    type="search"
                    placeholder="Search columns…"
                    value={search}
                    disabled={!table}
                    onChange={(event) => setSearch(event.target.value)}
                  />
                </label>
                <div className="sql-schema-scroll">
                  {table && (
                    <table className="sql-schema-columns" aria-label={`Columns in ${table.name}`}>
                      <thead>
                        <tr>
                          <th scope="col">Name</th>
                          <th scope="col">Type</th>
                          <th scope="col">
                            <span className="sr-only">Actions</span>
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {matchingFields.map((field) => (
                          <tr key={field.name}>
                            <td>{field.name}</td>
                            <td>
                              <span className="sql-schema-type">{field.dataType}</span>
                            </td>
                            <td>
                              <CopySchemaName key={`${tableId}:${field.name}`} name={field.name} kind="column" />
                              <button
                                type="button"
                                className="sql-schema-insert"
                                title={`Insert ${quote(field.name)}`}
                                aria-label={`Insert field ${field.name}`}
                                onClick={() => insert(quote(field.name))}
                              >
                                <Plus aria-hidden="true" />
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                  {!table && <p className="sql-schema-empty">Select an entity to browse its columns.</p>}
                  {fields.isLoading && (
                    <p className="sql-schema-empty" role="status">
                      Loading columns…
                    </p>
                  )}
                  {table && !matchingFields.length && !fields.isLoading && !fields.error && (
                    <p className="sql-schema-empty" role="status">
                      No columns found.
                    </p>
                  )}
                  {fields.error && <p className="error">{fields.error.message}</p>}
                </div>
              </section>
              <p className="sql-schema-hint">Use + to insert at the cursor. Ctrl+Z to undo.</p>
            </div>
          </details>
          {definition.parameters.length > 0 && (
            <fieldset>
              <legend>SQL parameters</legend>
              {definition.parameters.map((parameter, index) => (
                <div className="sql-parameter-row" key={parameter.parameter}>
                  <button
                    type="button"
                    className="link"
                    title="Insert parameter"
                    onClick={() => insert('@' + parameter.parameter)}
                  >
                    <Plus aria-hidden="true" />@{parameter.parameter}
                  </button>
                  <label className="field">
                    Type
                    <select
                      value={parameter.type}
                      onChange={(event) =>
                        onChange({
                          ...definition,
                          parameters: definition.parameters.map((item, current) =>
                            current === index ? { ...item, type: event.target.value } : item,
                          ),
                        })
                      }
                    >
                      {types.map((type) => (
                        <option key={type}>{type}</option>
                      ))}
                    </select>
                  </label>
                  <label className="field">
                    Default value
                    <input
                      placeholder="Required at runtime if omitted"
                      value={parameter.defaultValue == null ? '' : String(parameter.defaultValue)}
                      onChange={(event) =>
                        onChange({
                          ...definition,
                          parameters: definition.parameters.map((item, current) =>
                            current === index
                              ? {
                                  parameter: item.parameter,
                                  type: item.type,
                                  ...(event.target.value === '' ? {} : { defaultValue: event.target.value }),
                                }
                              : item,
                          ),
                        })
                      }
                    />
                  </label>
                </div>
              ))}
            </fieldset>
          )}
          {!metric && (
            <label className="field">
              Measure columns (optional, comma-separated)
              <input
                value={definition.measureColumns?.join(', ') ?? ''}
                placeholder="e.g. Total, Count"
                onChange={(event) =>
                  onChange({
                    ...definition,
                    measureColumns: event.target.value.trim()
                      ? event.target.value.split(',').map((name) => name.trim())
                      : undefined,
                  })
                }
              />
              <small>Other output columns become dimensions for charts.</small>
            </label>
          )}
          <div className="actions">
            {!sqlOnly && (
              <button
                type="button"
                className="secondary"
                disabled={generate.isPending}
                onClick={() => {
                  if (
                    !definition.text.trim() ||
                    confirm('Replace the SQL text with SQL generated from the Builder definition?')
                  ) {
                    generate.mutate()
                  }
                }}
              >
                <Wand2 aria-hidden="true" />
                Generate from Builder
              </button>
            )}
            <button
              type="button"
              className="secondary"
              disabled={!definition.text.trim() || validation.isPending}
              onClick={() => validation.mutate()}
            >
              <CheckCircle2 aria-hidden="true" />
              {validation.isPending ? 'Validating…' : 'Validate SQL'}
            </button>
          </div>
          {(validation.error || generate.error) && (
            <p className="error" role="alert">
              {(validation.error ?? generate.error)?.message}
            </p>
          )}
          {validation.data && !validation.isPending && <p role="status">SQL is valid.</p>}
        </>
      )}
    </div>
  )
}
