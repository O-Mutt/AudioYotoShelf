import type { Page } from '@playwright/test'

interface MockOptions {
  isAdmin?: boolean
}

function adminOverview() {
  return {
    totalUsers: 3,
    absConnectedUsers: 3,
    yotoConnectedUsers: 1,
    adminUsers: 1,
    activeUsers7d: 2,
    activeUsers30d: 3,
    totalLogins: 12,
    logins7d: 4,
    logins30d: 9,
    totalTransfers: 5,
    completedTransfers: 4,
    failedTransfers: 1,
    transferSuccessRate: 80,
    transfers7d: 2,
    totalPlaylists: 1,
  }
}

/**
 * Intercepts every /api/** call so the real SPA can be driven through its journeys without a
 * backend. State (logged-in / not) is tracked across requests so login + logout behave realistically.
 */
export async function mockBackend(page: Page, opts: MockOptions = {}): Promise<void> {
  let connected = false

  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const method = route.request().method()

    const json = (status: number, body: unknown) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })

    if (path === '/api/auth/abs/connect' && method === 'POST') {
      connected = true
      return json(200, {
        userConnectionId: 'conn-1',
        username: 'alice',
        absConnected: true,
        yotoConnected: false,
        defaultLibraryId: 'lib-1',
        libraries: ['lib-1'],
      })
    }

    if (path === '/api/auth/status' && method === 'GET') {
      if (!connected) return json(401, { message: 'no session' })
      return json(200, {
        id: 'conn-1',
        username: 'alice',
        absConnected: true,
        audiobookshelfUrl: 'http://abs.local',
        yotoConnected: false,
        yotoTokenExpiresAt: null,
        defaultLibraryId: 'lib-1',
        defaultMinAge: 5,
        defaultMaxAge: 10,
        isAdmin: Boolean(opts.isAdmin),
      })
    }

    if (path === '/api/auth/logout' && method === 'POST') {
      connected = false
      return json(200, { loggedOut: true })
    }

    if (path === '/api/libraries' && method === 'GET') {
      return json(200, [{ id: 'lib-1', name: 'My Books', mediaType: 'book' }])
    }

    if (path.startsWith('/api/libraries/library/')) {
      return json(200, { results: [], total: 0, limit: 20, page: 0 })
    }

    if (path === '/api/admin/overview') return json(200, adminOverview())
    if (path === '/api/admin/users') return json(200, [])
    if (path === '/api/admin/usage') return json(200, [])

    if (path === '/api/transfers') return json(200, { results: [], total: 0 })

    return json(200, {})
  })
}
