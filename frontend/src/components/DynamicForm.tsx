import { useMemo, useState, type FormEvent } from 'react'
import type { DynamicRecord, Field } from '../types'
import { Save, X } from 'lucide-react'
import { DynamicField } from './DynamicField'

interface Props {
  fields: Field[]; record?: DynamicRecord; busy?: boolean
  onSubmit: (data: Record<string, unknown>) => Promise<void> | void; onCancel?: () => void
}

export function DynamicForm({ fields, record, busy, onSubmit, onCancel }: Props) {
  const initial = useMemo(() => Object.fromEntries(fields.map(field =>
    [field.name, record?.data[field.storageKey] ?? field.defaultValue ?? (field.dataType === 'Boolean' ? false : '')])), [fields, record])
  const [values, setValues] = useState<Record<string, unknown>>(initial)

  async function submit(event: FormEvent) {
    event.preventDefault()
    await onSubmit(values)
    if (!record) setValues(initial)
  }

  return <form className="dynamic-form panel-body" onSubmit={submit}>
    {fields.map(field => <DynamicField key={field.id} field={field} value={values[field.name]}
      onChange={value => setValues(current => ({ ...current, [field.name]: value }))} />)}
    <div className="panel-footer"><button disabled={busy} type="submit"><Save />{busy ? 'Saving…' : 'Save'}</button>
      {onCancel && <button type="button" className="secondary" onClick={onCancel}><X />Cancel</button>}</div>
  </form>
}
