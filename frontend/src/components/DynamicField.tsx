import type { Field } from '../types'

interface Props { field: Field; value: unknown; onChange: (value: unknown) => void }

export function DynamicField({ field, value, onChange }: Props) {
  const common = { id: field.id, required: field.isRequired, name: field.name }
  const choices = field.configuration?.values ?? []
  let control: React.ReactNode

  switch (field.dataType) {
    case 'LongText':
      control = <textarea {...common} value={String(value ?? '')} onChange={event => onChange(event.target.value)} />
      break
    case 'Integer':
    case 'Decimal':
      control = <input {...common} type="number" step={field.dataType === 'Integer' ? 1 : 'any'}
        value={value == null ? '' : String(value)} onChange={event => onChange(event.target.value === '' ? null : Number(event.target.value))} />
      break
    case 'Boolean':
      control = <input {...common} type="checkbox" checked={Boolean(value)} onChange={event => onChange(event.target.checked)} />
      break
    case 'Date':
      control = <input {...common} type="date" value={String(value ?? '')} onChange={event => onChange(event.target.value)} />
      break
    case 'DateTime':
      control = <input {...common} type="datetime-local" value={String(value ?? '')} onChange={event => onChange(event.target.value)} />
      break
    case 'Email':
      control = <input {...common} type="email" value={String(value ?? '')} onChange={event => onChange(event.target.value)} />
      break
    case 'Url':
      control = <input {...common} type="url" value={String(value ?? '')} onChange={event => onChange(event.target.value)} />
      break
    case 'Choice':
      control = <select {...common} value={String(value ?? '')} onChange={event => onChange(event.target.value)}>
        <option value="">Select…</option>{choices.map(choice => <option key={choice}>{choice}</option>)}
      </select>
      break
    case 'MultiChoice': {
      const selected = Array.isArray(value) ? value.map(String) : []
      control = <select {...common} multiple value={selected}
        onChange={event => onChange(Array.from(event.target.selectedOptions, option => option.value))}>
        {choices.map(choice => <option key={choice}>{choice}</option>)}
      </select>
      break
    }
    case 'Lookup':
      control = <input {...common} type="text" placeholder="Record ID" value={String(value ?? '')} onChange={event => onChange(event.target.value)} />
      break
    default:
      control = <input {...common} type="text" value={String(value ?? '')} onChange={event => onChange(event.target.value)} />
  }

  return <label className="field"><span>{field.displayName}{field.isRequired && ' *'}</span>{control}</label>
}
