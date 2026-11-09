// public/js/core/state.js
// Shared mutable client state. ES module bindings are read-only for importers,
// so everything that is reassigned lives on this one object.
export const state = {
  currentCharId: null,
  currentTab: 'profile',
  /** World calendar config (loaded from the server). */
  worldCal: { calMonths: 10, calDays: 36, calHours: 26, calName: 'Vigilia Insectianis' },
  /** Client-side cache of { id => profile }. */
  npcMapCache: new Map(),
  /** Full relationship data for the current character. */
  relData: null,
  /** True while load-all is in progress (UI locked). */
  isLoading: false,
  /** EventId -> name lookup from the server event registry. */
  EVENT_ID_NAMES: {},
  /** Tick scrubber data: { ticks:[...], count, totalTicks, truncated } and the shown index. */
  tickData: null,
  tickIndex: 0,
};
