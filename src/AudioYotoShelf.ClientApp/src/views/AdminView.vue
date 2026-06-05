<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { adminApi } from '@/services/api'
import { useToast } from '@/composables/useToast'
import type { AdminOverview, AdminUser, UsagePoint } from '@/types'

const toast = useToast()
const overview = ref<AdminOverview | null>(null)
const users = ref<AdminUser[]>([])
const usage = ref<UsagePoint[]>([])
const isLoading = ref(true)

const maxUsage = computed(() =>
  Math.max(1, ...usage.value.map((p) => Math.max(p.logins, p.transfers))),
)

onMounted(async () => {
  try {
    const [o, u, g] = await Promise.all([
      adminApi.getOverview(),
      adminApi.getUsers(),
      adminApi.getUsage(14),
    ])
    overview.value = o.data
    users.value = u.data
    usage.value = g.data
  } catch {
    toast.error('Failed to load admin analytics')
  } finally {
    isLoading.value = false
  }
})

function fmtDate(iso: string | null): string {
  return iso ? new Date(iso).toLocaleDateString() : '—'
}

function barHeight(value: number): string {
  return `${Math.round((value / maxUsage.value) * 100)}%`
}

function dayLabel(date: string): string {
  return new Date(`${date}T00:00:00`).toLocaleDateString(undefined, {
    month: 'numeric',
    day: 'numeric',
  })
}
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
    <h1 class="text-2xl font-bold text-gray-900 mb-6">Admin · Usage</h1>

    <div v-if="isLoading" class="text-gray-500">Loading…</div>

    <template v-else-if="overview">
      <div class="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="text-3xl font-bold text-gray-900">{{ overview.totalUsers }}</div>
          <div class="text-sm text-gray-500">Total users</div>
        </div>
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="text-3xl font-bold text-gray-900">{{ overview.activeUsers7d }}</div>
          <div class="text-sm text-gray-500">Active users (7d)</div>
        </div>
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="text-3xl font-bold text-gray-900">{{ overview.logins7d }}</div>
          <div class="text-sm text-gray-500">Logins / sessions (7d)</div>
        </div>
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="text-3xl font-bold text-gray-900">{{ overview.totalTransfers }}</div>
          <div class="text-sm text-gray-500">Transfers</div>
        </div>
      </div>

      <div class="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8 text-sm">
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="font-medium text-gray-900">
            {{ overview.absConnectedUsers }} ABS · {{ overview.yotoConnectedUsers }} Yoto
          </div>
          <div class="text-gray-500">Connected</div>
        </div>
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="font-medium text-gray-900">{{ overview.totalLogins }}</div>
          <div class="text-gray-500">Total logins</div>
        </div>
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="font-medium text-gray-900">{{ overview.transferSuccessRate }}%</div>
          <div class="text-gray-500">Transfer success</div>
        </div>
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="font-medium text-gray-900">{{ overview.adminUsers }}</div>
          <div class="text-gray-500">Admins</div>
        </div>
      </div>

      <div class="bg-white rounded-lg shadow-sm p-4 mb-8">
        <h2 class="text-sm font-medium text-gray-700 mb-4">Last 14 days</h2>
        <div class="flex items-end gap-1 h-40">
          <div
            v-for="p in usage"
            :key="p.date"
            class="flex-1 flex flex-col items-center justify-end h-full"
          >
            <div
              class="w-full flex items-end justify-center gap-0.5 h-full"
              :title="`${p.date}: ${p.logins} logins, ${p.transfers} transfers`"
            >
              <div class="w-1/2 bg-yoto-blue rounded-t" :style="{ height: barHeight(p.logins) }" />
              <div
                class="w-1/2 bg-green-400 rounded-t"
                :style="{ height: barHeight(p.transfers) }"
              />
            </div>
            <div class="text-[10px] text-gray-400 mt-1">{{ dayLabel(p.date) }}</div>
          </div>
        </div>
        <div class="flex gap-4 mt-3 text-xs text-gray-500">
          <span class="flex items-center gap-1">
            <span class="w-3 h-3 bg-yoto-blue rounded-sm inline-block" /> Logins
          </span>
          <span class="flex items-center gap-1">
            <span class="w-3 h-3 bg-green-400 rounded-sm inline-block" /> Transfers
          </span>
        </div>
      </div>

      <div class="bg-white rounded-lg shadow-sm overflow-hidden">
        <table class="min-w-full divide-y divide-gray-200 text-sm">
          <thead class="bg-gray-50">
            <tr>
              <th class="px-4 py-2 text-left font-medium text-gray-500">User</th>
              <th class="px-4 py-2 text-left font-medium text-gray-500">Connections</th>
              <th class="px-4 py-2 text-right font-medium text-gray-500">Logins</th>
              <th class="px-4 py-2 text-right font-medium text-gray-500">Transfers</th>
              <th class="px-4 py-2 text-left font-medium text-gray-500">Last login</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-100">
            <tr v-for="u in users" :key="u.id">
              <td class="px-4 py-2">
                <span class="font-medium text-gray-900">{{ u.username }}</span>
                <span
                  v-if="u.isAdmin"
                  class="ml-2 text-xs bg-blue-100 text-blue-700 px-1.5 py-0.5 rounded"
                >
                  admin
                </span>
              </td>
              <td class="px-4 py-2 text-gray-600">
                <span :class="u.absConnected ? 'text-green-600' : 'text-gray-300'">ABS</span>
                ·
                <span :class="u.yotoConnected ? 'text-green-600' : 'text-gray-300'">Yoto</span>
              </td>
              <td class="px-4 py-2 text-right text-gray-700">{{ u.loginCount }}</td>
              <td class="px-4 py-2 text-right text-gray-700">{{ u.transferCount }}</td>
              <td class="px-4 py-2 text-gray-600">{{ fmtDate(u.lastLoginAt) }}</td>
            </tr>
            <tr v-if="users.length === 0">
              <td colspan="5" class="px-4 py-6 text-center text-gray-400">No users yet</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>
