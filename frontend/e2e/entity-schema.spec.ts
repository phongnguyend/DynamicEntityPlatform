import { test, expect, entityPath } from './fixtures'
import { expectButtonIcons } from './button-icon-assertions'

for (const width of [1280, 390]) {
  test(`schema header keeps compact, aligned controls at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 })
    await page.goto(`${entityPath}/fields`)
    const header = page.locator('.fields-page > header')
    const badge = header.locator('.entity-schema-version')
    const button = header.getByRole('button', { name: 'View SQL schema' })
    await expect(badge).toBeVisible()
    await expect(button).toBeVisible()
    await expectButtonIcons(header)
    const badgeBox = (await badge.boundingBox())!
    const buttonBox = (await button.boundingBox())!
    const headerBox = (await header.boundingBox())!
    expect(badgeBox.height).toBeLessThanOrEqual(28)
    expect(buttonBox.height).toBeLessThanOrEqual(36)
    expect(Math.abs(badgeBox.y + badgeBox.height / 2 - buttonBox.y - buttonBox.height / 2)).toBeLessThan(1)
    expect(buttonBox.x).toBeGreaterThan(badgeBox.x + badgeBox.width)
    expect(buttonBox.x + buttonBox.width).toBeLessThanOrEqual(headerBox.x + headerBox.width + 1)
    await header.screenshot({ path: testInfo.outputPath('schema-header.png') })
  })
}

for (const path of ['/entities', `${entityPath}/fields`]) {
  test(`entity SQL schema: view, copy and refresh from ${path}`, async ({ page, api, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    await page.goto(path)
    expect(api.requests.filter((request) => request.path.endsWith('/sql-schema'))).toHaveLength(0)
    await page.getByRole('button', { name: 'View SQL schema', exact: true }).first().click()
    const modal = page.getByRole('dialog', { name: 'Customers SQL schema' })
    await expect(modal.locator('pre')).toContainText('CREATE TABLE [dbo].[E_customer]')
    await expect(modal.locator('.syntax-keyword').first()).toHaveText('CREATE')
    await expectButtonIcons(modal)
    await modal.getByRole('button', { name: /Copy/ }).click()
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toContain('NEWSEQUENTIALID()')
    await page.mouse.click(2, 2)
    await expect(modal).toBeVisible()
    await modal.getByRole('button', { name: 'Refresh', exact: true }).click()
    await expect.poll(() => api.requests.filter((request) => request.path.endsWith('/sql-schema')).length).toBe(2)
    await modal.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(modal).toHaveCount(0)
    await page.getByRole('button', { name: 'View SQL schema', exact: true }).first().click()
    await expect.poll(() => api.requests.filter((request) => request.path.endsWith('/sql-schema')).length).toBe(3)
    expect(
      api.requests
        .filter((request) => request.path.endsWith('/sql-schema'))
        .every((request) => request.method === 'GET'),
    ).toBe(true)
  })
}

test('entity SQL schema: loading, error and retry', async ({ page }) => {
  let release: (() => void) | undefined
  await page.route('**/api/entities/customer/sql-schema', async (route) => {
    await new Promise<void>((resolve) => {
      release = resolve
    })
    await route.fulfill({ status: 403, json: { title: 'Access denied.' } })
  })
  await page.goto(`${entityPath}/fields`)
  await page.getByRole('button', { name: 'View SQL schema' }).click()
  const modal = page.getByRole('dialog', { name: 'Customers SQL schema' })
  await expect(modal.getByRole('status')).toHaveText('Loading SQL schema…')
  await expect(modal.getByRole('button', { name: 'Refresh' })).toBeDisabled()
  await expectButtonIcons(modal)
  await expect.poll(() => !!release).toBe(true)
  release!()
  await expect(modal.getByRole('alert')).toHaveText('Access denied.')
  await expect(modal.locator('pre')).toHaveCount(0)
  await page.unroute('**/api/entities/customer/sql-schema')
  await modal.getByRole('button', { name: 'Retry' }).click()
  await expect(modal.locator('pre')).toContainText('CREATE TABLE')
})
