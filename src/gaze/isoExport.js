// AIA EAI Hin Nr Claude Sonnet 4.6 v1.0
// Module-level handler so the DOM HUD can trigger a render inside the Canvas.
let _handler = null

export function registerIsoHandler(fn) { _handler = fn }
export function unregisterIsoHandler()  { _handler = null }
export function triggerIsoSnapshot()    { if (_handler) _handler() }
