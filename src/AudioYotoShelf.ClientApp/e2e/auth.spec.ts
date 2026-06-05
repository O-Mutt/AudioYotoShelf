import { expect, test } from '@playwright/test'
import { mockBackend } from './helpers'

test('unauthenticated visitors are redirected to setup', async ({ page }) => {
  await mockBackend(page)

  await page.goto('/library')

  await expect(page).toHaveURL(/\/setup/)
  await expect(page.getByRole('heading', { name: /Welcome to AudioYotoShelf/ })).toBeVisible()
})

test('a user can sign in with Audiobookshelf and reach the library', async ({ page }) => {
  await mockBackend(page)

  await page.goto('/')
  await expect(page).toHaveURL(/\/setup/)

  await page.locator('input[type="url"]').fill('http://localhost:13378')
  await page.locator('input[type="text"]').first().fill('alice')
  await page.locator('input[type="password"]').fill('password')
  await page.getByRole('button', { name: 'Connect', exact: true }).click()

  // After connecting, the setup screen shows the connected state and a continue button.
  await expect(page.getByText(/Connected as/)).toBeVisible()
  await page.getByRole('button', { name: /Continue/ }).click()

  await expect(page).toHaveURL(/\/library/)
  await expect(page.getByRole('link', { name: 'Library' })).toBeVisible()
})

test('logging out returns to the setup screen', async ({ page }) => {
  await mockBackend(page)

  await page.goto('/')
  await page.locator('input[type="url"]').fill('http://localhost:13378')
  await page.locator('input[type="text"]').first().fill('alice')
  await page.locator('input[type="password"]').fill('password')
  await page.getByRole('button', { name: 'Connect', exact: true }).click()
  await page.getByRole('button', { name: /Continue/ }).click()
  await expect(page).toHaveURL(/\/library/)

  await page.getByRole('button', { name: 'Logout' }).click()

  await expect(page).toHaveURL(/\/setup/)
})
