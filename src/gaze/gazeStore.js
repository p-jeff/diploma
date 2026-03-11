// AIA EAI Hin Nr Claude Opus 4.6 v1.0
// Module-level mutable store — zero React overhead during tracking.
const gazeStore = {
  registeredIds: new Set(),
  dwellMs: {},          // id -> accumulated ms
  positionSamples: [],  // { x, z, timestamp }

  register(id) {
    this.registeredIds.add(id)
    if (!(id in this.dwellMs)) this.dwellMs[id] = 0
  },

  addDwell(id, deltaMs) {
    if (id in this.dwellMs) this.dwellMs[id] += deltaMs
  },

  addPosition(x, z, timestamp) {
    this.positionSamples.push({ x, z, timestamp })
  },

  getMax() {
    const vals = Object.values(this.dwellMs)
    return vals.length > 0 ? Math.max(1, ...vals) : 1
  },

  getNormalized(id) {
    return (this.dwellMs[id] ?? 0) / this.getMax()
  },

  reset() {
    this.positionSamples = []
    for (const id of this.registeredIds) {
      this.dwellMs[id] = 0
    }
  },

  exportJSON() {
    return {
      dwellMs: { ...this.dwellMs },
      positionSamples: [...this.positionSamples],
    }
  },
}

export default gazeStore
