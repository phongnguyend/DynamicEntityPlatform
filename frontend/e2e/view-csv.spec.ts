import type { Download } from '@playwright/test'
import { test, expect } from './fixtures'

async function readCsv(download: Download) {
  const stream = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of stream!) {
    chunks.push(Buffer.from(chunk))
  }
  return Buffer.concat(chunks)
    .toString('utf8')
    .replace(/^\uFEFF/, '')
}

test('global view CSV uses parameterized results and saved visible column order', async ({ page, api }) => {
  api.collections['/api/views'].push({
    id: 'csv',
    name: 'Totals',
    definition: {
      sql: { enabled: true, text: 'SELECT @name AS name', parameters: [{ parameter: 'name', type: 'Text' }] },
      rowLimit: 25,
      columnOrder: ['count', 'name'],
      hiddenColumns: ['name'],
    },
  })
  await page.goto('/views/csv')
  const exportButton = page.getByRole('button', { name: 'Export CSV', exact: true })
  await expect(exportButton).toBeDisabled()
  await page.getByRole('button', { name: 'Run view', exact: true }).click()
  await page.locator('.modal').getByLabel('name', { exact: true }).fill('Alice')
  await page.locator('.modal').getByRole('button', { name: 'Run view', exact: true }).click()
  await expect(exportButton).toBeEnabled()
  const downloaded = page.waitForEvent('download')
  await exportButton.click()
  const download = await downloaded
  expect(download.suggestedFilename()).toBe('Totals.csv')
  expect(await readCsv(download)).toBe('Count\r\n42\r\n')
  expect(api.requests.find((request) => request.path === '/api/views/csv/run')?.body.parameters).toEqual({
    name: 'Alice',
  })
})

test('entity view CSV respects visible columns, filtered rows and CSV escaping', async ({ page, api }) => {
  const field = api.collections['/api/entities/customer/fields'][0]
  api.collections['/api/entities/customer/fields'].push({
    ...field,
    id: 'hidden',
    name: 'Hidden',
    displayName: 'Hidden',
    storageKey: 'Hidden',
  })
  api.collections['/api/entities/customer/views'][0].definition = {
    pageSize: 25,
    columnSettings: { order: ['hidden', field.id], hidden: ['hidden'] },
  }
  await page.route('**/api/entities/customer/query', (route) =>
    route.fulfill({
      json: {
        items: [
          {
            id: 'special',
            data: { [field.storageKey]: 'Alice, "A"\n第二行', Hidden: 'secret' },
            createdAt: '2026-01-01T00:00:00Z',
            updatedAt: '2026-01-02T00:00:00Z',
          },
          {
            id: 'formula',
            data: { [field.storageKey]: '=1+1' },
            createdAt: '2026-01-01T00:00:00Z',
            updatedAt: '2026-01-02T00:00:00Z',
          },
        ],
      },
    }),
  )
  await page.goto('/entities/customer/views/view-customer')
  const exportButton = page.getByRole('button', { name: 'Export CSV', exact: true })
  await expect(exportButton).toBeEnabled()
  const downloaded = page.waitForEvent('download')
  await exportButton.click()
  const csv = await readCsv(await downloaded)
  expect(csv).toBe(
    `${field.displayName},Created,Updated\r\n"Alice, ""A""\n第二行",2026-01-01T00:00:00Z,2026-01-02T00:00:00Z\r\n'=1+1,2026-01-01T00:00:00Z,2026-01-02T00:00:00Z\r\n`,
  )
  expect(csv).not.toContain('secret')
})

test('global view CSV allows header-only results and disables export after editing SQL', async ({ page }) => {
  await page.route('**/api/views/preview', (route) =>
    route.fulfill({
      json: {
        columns: [{ key: 'name', label: 'Name', dataType: 'Text', role: 'Dimension' }],
        rows: [],
        truncated: false,
        generatedAt: '2026-01-01T00:00:00Z',
      },
    }),
  )
  await page.goto('/views/new')
  await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill('SELECT 1 AS name')
  await page.getByRole('button', { name: 'Preview results', exact: true }).click()
  const exportButton = page.getByRole('button', { name: 'Export CSV', exact: true })
  await expect(exportButton).toBeEnabled()
  const downloaded = page.waitForEvent('download')
  await exportButton.click()
  expect(await readCsv(await downloaded)).toBe('Name\r\n')
  await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill('SELECT 2 AS name')
  await expect(exportButton).toBeDisabled()
})
