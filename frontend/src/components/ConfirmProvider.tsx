import { createContext, useCallback, useContext, useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Check, X } from 'lucide-react'
import { Modal } from './Modal'

interface Confirmation {
  title: string
  message: string
  confirmLabel: string
}

type Confirm = (confirmation: Confirmation) => Promise<boolean>
const ConfirmContext = createContext<Confirm | null>(null)

export function useConfirm() {
  const confirm = useContext(ConfirmContext)
  if (!confirm) {
    throw new Error('useConfirm must be used within ConfirmProvider')
  }
  return confirm
}

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null)
  const pending = useRef<((accepted: boolean) => void) | null>(null)
  const trigger = useRef<HTMLElement | null>(null)
  const descriptionId = useId()

  const confirm = useCallback<Confirm>((options) => {
    if (pending.current) {
      return Promise.resolve(false)
    }
    trigger.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    return new Promise<boolean>((resolve) => {
      pending.current = resolve
      setConfirmation(options)
    })
  }, [])

  function finish(accepted: boolean) {
    const resolve = pending.current
    pending.current = null
    setConfirmation(null)
    resolve?.(accepted)
  }

  useEffect(() => {
    if (!confirmation) {
      return
    }
    const root = document.getElementById('root')
    const wasInert = root?.inert ?? false
    if (root) {
      root.inert = true
    }
    return () => {
      if (root) {
        root.inert = wasInert
      }
      trigger.current?.focus()
    }
  }, [confirmation])

  useEffect(() => () => pending.current?.(false), [])

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      {confirmation &&
        createPortal(
          <div
            onKeyDown={(event) => {
              if (event.key === 'Escape') {
                event.preventDefault()
                event.stopPropagation()
                finish(false)
              }
              if (event.key === 'Tab') {
                const buttons = event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')
                const first = buttons[0]
                const last = buttons[buttons.length - 1]
                if (event.shiftKey && document.activeElement === first) {
                  event.preventDefault()
                  last?.focus()
                } else if (!event.shiftKey && document.activeElement === last) {
                  event.preventDefault()
                  first?.focus()
                }
              }
            }}
          >
            <Modal
              title={confirmation.title}
              role="alertdialog"
              descriptionId={descriptionId}
              onClose={() => finish(false)}
            >
              <p id={descriptionId}>{confirmation.message}</p>
              <div className="modal-footer">
                <button type="button" className="secondary" autoFocus onClick={() => finish(false)}>
                  <X aria-hidden="true" />
                  Cancel
                </button>
                <button type="button" onClick={() => finish(true)}>
                  <Check aria-hidden="true" />
                  {confirmation.confirmLabel}
                </button>
              </div>
            </Modal>
          </div>,
          document.body,
        )}
    </ConfirmContext.Provider>
  )
}
