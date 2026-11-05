import { useEffect, useId, useImperativeHandle, useMemo, useState, type Ref } from 'react'
import { useCodeHistory, type CodeEditorHandle } from './useCodeHistory'
import { Check, Copy } from 'lucide-react'
import './HighlightedCode.css'

const sqlKeywords = new Set(
  'SELECT FROM WHERE AND OR NOT NULL IS IN BETWEEN LIKE ESCAPE EXISTS AS DISTINCT TOP ORDER BY ASC DESC GROUP HAVING OFFSET ROWS FETCH NEXT ONLY JOIN LEFT RIGHT INNER OUTER ON UNION ALL CASE WHEN THEN ELSE END CAST CONVERT TRY_CONVERT COUNT COUNT_BIG SUM AVG MIN MAX JSON_VALUE JSON_QUERY OPENJSON DATEADD DATEDIFF DATEFROMPARTS YEAR MONTH DAY BIGINT INT DECIMAL BIT DATE DATETIME2 UNIQUEIDENTIFIER'.split(
    ' ',
  ),
)

export function HighlightedCode({
  label,
  value,
  language,
  onChange,
  disabled,
  editorRef,
}: {
  label: string
  value: string
  language: 'sql' | 'json'
  onChange?: (value: string) => void
  disabled?: boolean
  editorRef?: Ref<CodeEditorHandle>
}) {
  const history = useCodeHistory(value, onChange, disabled)
  useImperativeHandle(editorRef, () => ({ insertText: history.insertText }))
  const id = useId()
  const [feedback, setFeedback] = useState<{ value: string; success: boolean }>()
  const [copying, setCopying] = useState(false)
  const currentFeedback = feedback?.value === value ? feedback : undefined
  useEffect(() => {
    if (!feedback) {
      return
    }
    const timeout = window.setTimeout(() => setFeedback(undefined), 2500)
    return () => window.clearTimeout(timeout)
  }, [feedback])
  async function copy() {
    setCopying(true)
    try {
      await navigator.clipboard.writeText(value)
      setFeedback({ value, success: true })
    } catch {
      setFeedback({ value, success: false })
    } finally {
      setCopying(false)
    }
  }
  const content = useMemo(() => {
    // Tokenize original text, then let React escape every token (including user values).
    const pattern =
      language === 'sql'
        ? /(--[^\r\n]*|\/\*[\s\S]*?\*\/)|((?:N)?'(?:''|[^'])*')|(\[(?:\]\]|[^\]])*\]|"(?:""|[^"])*")|(@@?[\w]+)|(\b\d+(?:\.\d+)?(?:e[+-]?\d+)?\b)|([A-Za-z_][A-Za-z_0-9]*)/gi
        : /("(?:\\.|[^"\\])*"\s*:)|("(?:\\.|[^"\\])*")|\b(true|false|null)\b|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g
    const parts = []
    let offset = 0
    for (const match of value.matchAll(pattern)) {
      const index = match.index!
      parts.push(value.slice(offset, index))
      const kind =
        language === 'sql'
          ? match[1]
            ? 'comment'
            : match[2]
              ? 'string'
              : match[3]
                ? 'identifier'
                : match[4]
                  ? 'parameter'
                  : match[5]
                    ? 'number'
                    : sqlKeywords.has(match[0].toUpperCase())
                      ? 'keyword'
                      : ''
          : match[1]
            ? 'property'
            : match[2]
              ? 'string'
              : match[3]
                ? 'keyword'
                : 'number'
      parts.push(
        <span key={index} className={kind ? `syntax-${kind}` : undefined}>
          {match[0]}
        </span>,
      )
      offset = index + match[0].length
    }
    parts.push(value.slice(offset))
    return parts
  }, [value, language])

  return (
    <div className="highlighted-code">
      <div className="highlighted-code-label">
        <span id={id}>{label}</span>
        <div className="highlighted-code-actions">
          <span className="highlighted-code-language" aria-hidden="true">
            {language.toUpperCase()}
          </span>
          <button
            type="button"
            className="link"
            onClick={copy}
            disabled={copying}
            aria-label={`Copy ${label}`}
            title={`Copy ${label}`}
          >
            {currentFeedback?.success ? <Check /> : <Copy />}
            {currentFeedback?.success ? 'Copied' : 'Copy'}
          </button>
        </div>
      </div>
      <span className={currentFeedback?.success === false ? 'highlighted-code-copy-error' : 'sr-only'} role="status">
        {currentFeedback
          ? currentFeedback.success
            ? `${label} copied to clipboard.`
            : 'Could not copy. Select the text and copy it manually.'
          : ''}
      </span>
      {onChange ? (
        <div className="highlighted-code-editor">
          <pre aria-hidden="true">
            <code>
              {content}
              {'\n'}
            </code>
          </pre>
          <textarea
            ref={history.input}
            aria-labelledby={id}
            value={value}
            onChange={(event) =>
              history.commit({
                value: event.target.value,
                start: event.target.selectionStart,
                end: event.target.selectionEnd,
              })
            }
            onSelect={history.rememberSelection}
            onKeyDown={(event) => {
              if (!(event.ctrlKey || event.metaKey) || event.altKey || event.nativeEvent.isComposing) {
                return
              }
              const key = event.key.toLowerCase()
              if (key === 'z' || key === 'y') {
                event.preventDefault()
                history.move(key === 'y' || event.shiftKey ? 1 : -1)
              }
            }}
            disabled={disabled}
            spellCheck={false}
            autoCapitalize="off"
            autoCorrect="off"
          />
        </div>
      ) : (
        <pre tabIndex={0} aria-labelledby={id}>
          <code>{content}</code>
        </pre>
      )}
    </div>
  )
}
