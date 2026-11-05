import { expect, type Locator, type Page } from '@playwright/test'

// Call after the relevant page/dialog/state has rendered, not before its data loads.
export async function expectButtonIcons(scope: Page | Locator) {
  const buttons = scope.locator('button:visible, a.button:visible')
  expect(await buttons.count(), 'The icon audit must inspect at least one button').toBeGreaterThan(0)
  for (const button of await buttons.all()) {
    const label = (await button.getAttribute('aria-label')) || (await button.innerText()).trim() || 'Unnamed button'
    await expect(button, `${label}: accessible name`).toHaveAccessibleName(/\S/)
    await expect(button.locator('svg:visible').first(), `${label}: visible icon`).toBeVisible()
    if (!(await button.innerText()).trim()) {
      await expect(button, `${label}: icon-only label`).toHaveAttribute('aria-label', /\S/)
      await expect(button, `${label}: icon-only tooltip`).toHaveAttribute('title', /\S/)
    }
  }
}
