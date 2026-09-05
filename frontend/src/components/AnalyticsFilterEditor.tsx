import type { Field } from '../types'

type FilterLogic = 'And' | 'Or'
type FilterCondition = { fieldId: string; operator: string; value?: unknown }
type FilterNode = FilterCondition | FilterGroup
type FilterGroup = { logic: FilterLogic; conditions: FilterNode[] }

export function AnalyticsFilterEditor({ fields, value, onChange }: { fields: Field[]; value?: unknown; onChange: (value?: FilterGroup) => void }) {
  const filterableFields = fields.filter(field => field.isFilterable)
  const group = isGroup(value) ? value : undefined

  function startFilter() {
    const field = filterableFields[0]
    if (field) onChange({ logic: 'And', conditions: [defaultCondition(field)] })
  }

  return <div className="builder-section analytics-filters">
    <div className="panel-title"><h3>Filters</h3><div className="actions">
      {group && <button type="button" className="link danger" onClick={() => onChange(undefined)}>Clear all</button>}
      {!group && <button type="button" className="secondary" disabled={!filterableFields.length} onClick={startFilter}>Add filter</button>}
    </div></div>
    {!group && <p className="empty">No filters. All records are included.</p>}
    {group && <FilterGroupEditor fields={filterableFields} group={group} depth={0} onChange={onChange} />}
  </div>
}

function FilterGroupEditor({ fields, group, depth, onChange, onRemove }: {
  fields: Field[]
  group: FilterGroup
  depth: number
  onChange: (group: FilterGroup) => void
  onRemove?: () => void
}) {
  const full = group.conditions.length >= 50

  function addCondition() {
    const field = fields[0]
    if (field && !full) onChange({ ...group, conditions: [...group.conditions, defaultCondition(field)] })
  }

  function addGroup() {
    const field = fields[0]
    if (field && !full && depth < 10) onChange({ ...group, conditions: [...group.conditions, { logic: 'And', conditions: [defaultCondition(field)] }] })
  }

  function updateNode(index: number, node: FilterNode) {
    onChange({ ...group, conditions: group.conditions.map((current, currentIndex) => currentIndex === index ? node : current) })
  }

  function removeNode(index: number) {
    onChange({ ...group, conditions: group.conditions.filter((_, currentIndex) => currentIndex !== index) })
  }

  return <div className={`analytics-filter-group${depth ? ' nested' : ''}`}>
    <div className="filter-group-header">
      <label className="field filter-logic">Match
        <select aria-label={`Group level ${depth + 1} logic`} value={group.logic} onChange={event => onChange({ ...group, logic: event.target.value as FilterLogic })}>
          <option value="And">All items (AND)</option><option value="Or">Any item (OR)</option>
        </select>
      </label>
      <div className="actions">
        <button type="button" className="secondary" disabled={!fields.length || full} onClick={addCondition}>Add filter</button>
        <button type="button" className="secondary" disabled={!fields.length || full || depth >= 10} onClick={addGroup}>Add group</button>
        {onRemove && <button type="button" className="link danger" onClick={onRemove}>Remove group</button>}
      </div>
    </div>
    {group.conditions.length === 0 && <p className="empty">This group is empty.</p>}
    <div className="filter-group-children">{group.conditions.map((node, index) => isGroup(node)
      ? <FilterGroupEditor key={index} fields={fields} group={node} depth={depth + 1} onChange={next => updateNode(index, next)} onRemove={() => removeNode(index)} />
      : <FilterConditionEditor key={index} fields={fields} condition={node} label={`Filter ${index + 1}`} onChange={next => updateNode(index, next)} onRemove={() => removeNode(index)} />
    )}</div>
  </div>
}

function FilterConditionEditor({ fields, condition, label, onChange, onRemove }: {
  fields: Field[]
  condition: FilterCondition
  label: string
  onChange: (condition: FilterCondition) => void
  onRemove: () => void
}) {
  const field = fields.find(item => item.id === condition.fieldId)
  const operators = operatorsFor(field)
  const hasValue = condition.operator !== 'IsNull' && condition.operator !== 'IsNotNull'

  function changeField(fieldId: string) {
    const nextField = fields.find(item => item.id === fieldId)
    if (nextField) onChange(defaultCondition(nextField))
  }

  function changeOperator(operator: string) {
    onChange({ ...condition, operator, value: operator === 'IsNull' || operator === 'IsNotNull' ? undefined : condition.value ?? defaultValue(field) })
  }

  return <div className="analytics-filter-row">
    <select aria-label={`${label} field`} value={condition.fieldId} onChange={event => changeField(event.target.value)}>{fields.map(item => <option value={item.id} key={item.id}>{item.displayName}</option>)}</select>
    <select aria-label={`${label} operator`} value={condition.operator} onChange={event => changeOperator(event.target.value)}>{operators.map(operator => <option key={operator}>{operator}</option>)}</select>
    {hasValue ? <FilterValue field={field} value={condition.value} onChange={value => onChange({ ...condition, value })} /> : <span className="filter-no-value">No value</span>}
    <button type="button" className="link danger" onClick={onRemove}>Remove</button>
  </div>
}

function FilterValue({ field, value, onChange }: { field?: Field; value: unknown; onChange: (value: unknown) => void }) {
  if (field?.dataType === 'Boolean') return <select aria-label={`${field.displayName} value`} value={String(value ?? false)} onChange={event => onChange(event.target.value === 'true')}><option value="true">True</option><option value="false">False</option></select>
  const inputType = field?.dataType === 'Date' ? 'date' : field?.dataType === 'DateTime' ? 'datetime-local' : field?.dataType === 'Integer' || field?.dataType === 'Decimal' ? 'number' : 'text'
  return <input aria-label={`${field?.displayName ?? 'Filter'} value`} type={inputType} step={field?.dataType === 'Integer' ? '1' : field?.dataType === 'Decimal' ? 'any' : undefined} value={String(value ?? '')} onChange={event => onChange(field?.dataType === 'Integer' || field?.dataType === 'Decimal' ? Number(event.target.value) : event.target.value)} />
}

function defaultCondition(field: Field): FilterCondition { return { fieldId: field.id, operator: 'Equal', value: defaultValue(field) } }
function defaultValue(field?: Field) { return field?.dataType === 'Boolean' ? false : field?.dataType === 'Integer' || field?.dataType === 'Decimal' ? 0 : '' }
function operatorsFor(field?: Field) {
  if (field?.dataType === 'MultiChoice') return ['Contains', 'IsNull', 'IsNotNull']
  if (field?.dataType === 'Boolean' || field?.dataType === 'Lookup') return ['Equal', 'NotEqual', 'IsNull', 'IsNotNull']
  if (field?.dataType === 'Integer' || field?.dataType === 'Decimal' || field?.dataType === 'Date' || field?.dataType === 'DateTime') return ['Equal', 'NotEqual', 'GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual', 'IsNull', 'IsNotNull']
  return ['Equal', 'NotEqual', 'Contains', 'StartsWith', 'EndsWith', 'IsNull', 'IsNotNull']
}
function isCondition(value: unknown): value is FilterCondition {
  return typeof value === 'object' && value !== null && !Array.isArray(value) &&
    typeof (value as FilterCondition).fieldId === 'string' && typeof (value as FilterCondition).operator === 'string'
}
function isGroup(value: unknown): value is FilterGroup {
  return typeof value === 'object' && value !== null && !Array.isArray(value) &&
    ((value as FilterGroup).logic === 'And' || (value as FilterGroup).logic === 'Or') &&
    Array.isArray((value as FilterGroup).conditions) &&
    (value as FilterGroup).conditions.every(node => isCondition(node) || isGroup(node))
}
