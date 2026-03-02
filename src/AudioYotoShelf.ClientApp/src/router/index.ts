import { createRouter, createWebHistory } from 'vue-router'
import { useConnectionStore } from '@/stores/connectionStore'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      redirect: '/library',
    },
    {
      path: '/setup',
      name: 'setup',
      component: () => import('@/views/SetupView.vue'),
    },
    {
      path: '/library',
      name: 'library',
      component: () => import('@/views/LibraryView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/book/:itemId',
      name: 'book-detail',
      component: () => import('@/views/BookDetailView.vue'),
      meta: { requiresAuth: true },
      props: true,
    },
    {
      path: '/series/:seriesId',
      name: 'series-detail',
      component: () => import('@/views/SeriesDetailView.vue'),
      meta: { requiresAuth: true },
      props: true,
    },
    {
      path: '/transfers',
      name: 'transfers',
      component: () => import('@/views/TransfersView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/cards',
      name: 'cards',
      component: () => import('@/views/CardsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('@/views/SettingsView.vue'),
    },
  ],
})

router.beforeEach(async (to) => {
  const connectionStore = useConnectionStore()

  // Restore session on first navigation
  if (connectionStore.userConnectionId && !connectionStore.status) {
    await connectionStore.refreshStatus()
  }

  if (to.meta.requiresAuth && !connectionStore.isAbsConnected) {
    return { name: 'setup' }
  }
})

export default router
