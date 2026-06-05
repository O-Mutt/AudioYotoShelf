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
      props: (route) => ({
        seriesId: route.params.seriesId as string,
        libraryId: typeof route.query.libraryId === 'string' ? route.query.libraryId : undefined,
      }),
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
    {
      path: '/admin',
      name: 'admin',
      component: () => import('@/views/AdminView.vue'),
      meta: { requiresAuth: true, requiresAdmin: true },
    },
  ],
})

router.beforeEach(async (to) => {
  const connectionStore = useConnectionStore()

  // Restore session from the http-only cookie on first navigation.
  if (!connectionStore.status) {
    await connectionStore.refreshStatus()
  }

  if (to.meta.requiresAuth && !connectionStore.isAbsConnected) {
    return { name: 'setup' }
  }

  // Admin-only routes: bounce non-admins back to the library.
  if (to.meta.requiresAdmin && !connectionStore.isAdmin) {
    return { name: 'library' }
  }
})

export default router
