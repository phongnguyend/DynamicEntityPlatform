import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Code2, RefreshCw, X } from 'lucide-react'
import { api } from '../api'
import { HighlightedCode } from './HighlightedCode'
import { Modal } from './Modal'

export function EntitySqlSchemaButton({
  tenantId,
  entityId,
  name,
  className,
}: {
  tenantId: string
  entityId: string
  name: string
  className?: string
}) {
  const [open, setOpen] = useState(false)
  const schema = useQuery({
    queryKey: ['entity-sql-schema', tenantId, entityId],
    queryFn: () => api.entitySqlSchema(tenantId, entityId),
    enabled: open,
    staleTime: 0,
    retry: false,
  })

  return (
    <>
      <button type="button" className={className} onClick={() => setOpen(true)}>
        <Code2 aria-hidden="true" />
        View SQL schema
      </button>
      {open && (
        <Modal title={`${name} SQL schema`} wide onClose={() => setOpen(false)}>
          <p className="field-note">
            Physical SQL Server table definition, including computed columns, defaults, constraints, and indexes.
            Dynamic field values are stored in the Data JSON column; indexed fields may also have computed columns.
          </p>
          {schema.isFetching ? (
            <p role="status">Loading SQL schema…</p>
          ) : schema.error ? (
            <p className="error" role="alert">
              {schema.error.message}
            </p>
          ) : schema.data ? (
            <HighlightedCode label={schema.data.tableName} language="sql" value={schema.data.sql} />
          ) : null}
          <div className="modal-footer">
            <button
              type="button"
              className="secondary"
              disabled={schema.isFetching}
              onClick={() => void schema.refetch()}
            >
              <RefreshCw aria-hidden="true" />
              {schema.error ? 'Retry' : 'Refresh'}
            </button>
            <button type="button" className="secondary" onClick={() => setOpen(false)}>
              <X aria-hidden="true" />
              Close
            </button>
          </div>
        </Modal>
      )}
    </>
  )
}
