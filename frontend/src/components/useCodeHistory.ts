import { useEffect, useRef } from 'react'

type Snapshot = { value: string; start: number; end: number }

export interface CodeEditorHandle {
  insertText: (text: string) => void
}

export function useCodeHistory(value: string, onChange?: (value: string) => void, disabled?: boolean) {
  const input = useRef<HTMLTextAreaElement>(null)
  const history = useRef<Snapshot[]>([{ value, start: 0, end: 0 }])
  const position = useRef(0)

  useEffect(() => {
    // Loading a different definition starts a new history. Changes made by this
    // editor already match the current snapshot and retain their history.
    if (history.current[position.current].value !== value) {
      history.current = [{ value, start: 0, end: 0 }]
      position.current = 0
    }
  }, [value])

  function rememberSelection() {
    const element = input.current
    const current = history.current[position.current]
    if (element && element.value === current.value) {
      current.start = element.selectionStart
      current.end = element.selectionEnd
    }
  }

  function commit(next: Snapshot) {
    if (!onChange || disabled || next.value === history.current[position.current].value) {
      return
    }
    history.current = [...history.current.slice(0, position.current + 1), next].slice(-200)
    position.current = history.current.length - 1
    onChange(next.value)
  }

  function restoreSelection(snapshot: Snapshot) {
    requestAnimationFrame(() => {
      input.current?.focus()
      input.current?.setSelectionRange(snapshot.start, snapshot.end)
    })
  }

  function move(direction: number) {
    const next = position.current + direction
    if (!onChange || disabled || next < 0 || next >= history.current.length) {
      return
    }
    rememberSelection()
    position.current = next
    const snapshot = history.current[next]
    onChange(snapshot.value)
    restoreSelection(snapshot)
  }

  useEffect(() => {
    const element = input.current
    function beforeInput(event: InputEvent) {
      if (event.inputType === 'historyUndo' || event.inputType === 'historyRedo') {
        event.preventDefault()
        move(event.inputType === 'historyUndo' ? -1 : 1)
      } else {
        rememberSelection()
      }
    }
    element?.addEventListener('beforeinput', beforeInput)
    return () => element?.removeEventListener('beforeinput', beforeInput)
  })

  function insertText(text: string) {
    if (!input.current || !onChange || disabled) {
      return
    }
    rememberSelection()
    const current = history.current[position.current]
    const next = {
      value: current.value.slice(0, current.start) + text + current.value.slice(current.end),
      start: current.start + text.length,
      end: current.start + text.length,
    }
    commit(next)
    restoreSelection(next)
  }

  return { input, commit, move, rememberSelection, insertText }
}
