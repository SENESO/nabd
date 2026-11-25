async function request(path, options) {
  const res = await fetch(path, options)
  if (!res.ok) {
    let message = `Request failed (${res.status})`
    try {
      const body = await res.json()
      if (body?.error) message = body.error
    } catch { /* keep the generic message */ }
    throw new Error(message)
  }
  return res.status === 204 ? null : res.json()
}

export const api = {
  monitors: () => request('/api/monitors'),
  addMonitor: (form) =>
    request('/api/monitors', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(form),
    }),
  removeMonitor: (id) => request(`/api/monitors/${id}`, { method: 'DELETE' }),
}

export function ago(iso) {
  if (!iso) return 'never'
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  if (s < 5) return 'just now'
  if (s < 60) return `${Math.floor(s)}s ago`
  if (s < 3600) return `${Math.floor(s / 60)}m ago`
  return `${Math.floor(s / 3600)}h ago`
}
