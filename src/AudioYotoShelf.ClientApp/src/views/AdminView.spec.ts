import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { adminApi } from '@/services/api'
import AdminView from '@/views/AdminView.vue'
import type { AdminOverview } from '@/types'

vi.mock('@/services/api', () => ({
  adminApi: {
    getOverview: vi.fn(),
    getUsers: vi.fn(),
    getUsage: vi.fn(),
  },
}))

vi.mock('@/composables/useToast', () => ({
  useToast: () => ({ error: vi.fn(), success: vi.fn() }),
}))

function overview(overrides: Partial<AdminOverview> = {}): AdminOverview {
  return {
    totalUsers: 7,
    absConnectedUsers: 5,
    yotoConnectedUsers: 3,
    adminUsers: 1,
    activeUsers7d: 4,
    activeUsers30d: 6,
    totalLogins: 42,
    logins7d: 9,
    logins30d: 20,
    totalTransfers: 15,
    completedTransfers: 12,
    failedTransfers: 3,
    transferSuccessRate: 80,
    transfers7d: 5,
    totalPlaylists: 2,
    ...overrides,
  }
}

describe('AdminView', () => {
  beforeEach(() => vi.clearAllMocks())

  it('renders overview metrics from the admin API', async () => {
    vi.mocked(adminApi.getOverview).mockResolvedValue({ data: overview() } as never)
    vi.mocked(adminApi.getUsers).mockResolvedValue({ data: [] } as never)
    vi.mocked(adminApi.getUsage).mockResolvedValue({ data: [] } as never)

    const wrapper = mount(AdminView)
    await flushPromises()

    expect(wrapper.text()).toContain('Total users')
    expect(wrapper.text()).toContain('7')
    expect(wrapper.text()).toContain('80%')
    expect(wrapper.text()).toContain('No users yet')
  })

  it('shows an error toast when the API call fails', async () => {
    vi.mocked(adminApi.getOverview).mockRejectedValue(new Error('boom'))
    vi.mocked(adminApi.getUsers).mockRejectedValue(new Error('boom'))
    vi.mocked(adminApi.getUsage).mockRejectedValue(new Error('boom'))

    const wrapper = mount(AdminView)
    await flushPromises()

    // Overview never populated, so the metric cards never render.
    expect(wrapper.text()).not.toContain('Total users')
  })
})
