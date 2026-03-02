import { ref, computed } from 'vue'

/**
 * Phase 2: Selection state composable.
 * SRP: Manages only selection state — no transfer logic, no API calls.
 * DI: Views depend on this abstraction, not concrete UI state.
 */
export function useSelection() {
  const selectedIds = ref<Set<string>>(new Set())
  const isSelectionMode = ref(false)

  const selectionCount = computed(() => selectedIds.value.size)
  const hasSelection = computed(() => selectedIds.value.size > 0)

  function toggleSelect(id: string) {
    const next = new Set(selectedIds.value)
    if (next.has(id)) {
      next.delete(id)
    } else {
      next.add(id)
    }
    selectedIds.value = next

    // Auto-exit selection mode when nothing selected
    if (next.size === 0) {
      isSelectionMode.value = false
    }
  }

  function isSelected(id: string): boolean {
    return selectedIds.value.has(id)
  }

  function selectAll(ids: string[]) {
    selectedIds.value = new Set(ids)
    if (ids.length > 0) isSelectionMode.value = true
  }

  function clearSelection() {
    selectedIds.value = new Set()
    isSelectionMode.value = false
  }

  function enterSelectionMode() {
    isSelectionMode.value = true
  }

  function getSelectedArray(): string[] {
    return Array.from(selectedIds.value)
  }

  return {
    selectedIds,
    isSelectionMode,
    selectionCount,
    hasSelection,
    toggleSelect,
    isSelected,
    selectAll,
    clearSelection,
    enterSelectionMode,
    getSelectedArray,
  }
}
