import { useEffect, useState } from 'react'
import { api, ago } from './api.js'

export default function App() {
  const [monitors, setMonitors] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    let alive = true
    const tick = () =>
      api.monitors()
        .then((m) => alive && setMonitors(m))
        .catch((e) => alive && setError(e.message))
    tick()
    const timer = setInterval(tick, 5000)
    return () => { alive = false; clearInterval(timer) }
  }, [])

  return (
    <div className="shell">
      <header className="topbar">
        <h1>
          نبض <span className="latin">Nabd</span>
        </h1>
        <p className="tagline">Your sites' pulse. Every monitor probed on schedule, results as they land.</p>
      </header>

      <AddMonitorForm onAdded={() => api.monitors().then(setMonitors).catch(() => {})} />
      {error && <p className="error">{error}</p>}

      {!monitors ? (
        <p className="tagline">Listening…</p>
      ) : monitors.length === 0 ? (
        <p className="tagline">No monitors yet. Add your first endpoint above.</p>
      ) : (
        <main className="grid">
          {monitors.map((m) => (
            <MonitorCard
              key={m.id}
              monitor={m}
              onRemove={() => setMonitors((ms) => ms.filter((x) => x.id !== m.id))}
            />
          ))}
        </main>
      )}
    </div>
  )
}

function AddMonitorForm({ onAdded }) {
  const [form, setForm] = useState({ name: '', url: '', intervalSeconds: 30 })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  async function submit(e) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.addMonitor({ ...form, intervalSeconds: Number(form.intervalSeconds) })
      setForm({ name: '', url: '', intervalSeconds: 30 })
      // The list refreshes immediately; nothing optimistic to fake here.
      onAdded && onAdded()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="addform" onSubmit={submit}>
      <input required placeholder="Name (Storefront)" value={form.name}
        onChange={(e) => setForm({ ...form, name: e.target.value })} />
      <input required type="url" placeholder="https://example.com/health" className="url" value={form.url}
        onChange={(e) => setForm({ ...form, url: e.target.value })} />
      <select value={form.intervalSeconds} onChange={(e) => setForm({ ...form, intervalSeconds: e.target.value })}>
        <option value={5}>every 5s</option>
        <option value={30}>every 30s</option>
        <option value={60}>every minute</option>
        <option value={300}>every 5 minutes</option>
      </select>
      <button className="primary" disabled={busy}>{busy ? 'Adding…' : 'Watch it'}</button>
      {error && <span className="error">{error}</span>}
    </form>
  )
}

function Sparkline({ points, up }) {
  if (!points || points.length < 2) return <div className="spark empty">gathering…</div>
  const w = 220, h = 44, pad = 4
  const max = Math.max(...points, 1)
  const step = (w - 2 * pad) / (points.length - 1)
  const path = points
    .map((p, i) => `${i === 0 ? 'M' : 'L'}${(pad + i * step).toFixed(1)},${(h - pad - (p / max) * (h - 2 * pad)).toFixed(1)}`)
    .join(' ')
  return (
    <svg className="spark" viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none">
      <path d={path} fill="none" stroke={up ? '#43d97b' : '#ff7a6b'} strokeWidth="2" />
    </svg>
  )
}

function MonitorCard({ monitor: m, onRemove }) {
  const up = m.status === 'up'
  return (
    <article className={`card ${m.status}`}>
      <header>
        <span className={`dot ${m.status}`} />
        <h2>{m.name}</h2>
        <button className="link danger" title="Stop watching" onClick={onRemove}>✕</button>
      </header>
      <p className="url">{m.url}</p>
      <Sparkline points={m.sparkline} up={up} />
      <footer>
        <span className="stat">
          <strong>{m.uptime24h === null ? '—' : `${m.uptime24h}%`}</strong> uptime 24h
        </span>
        <span className="stat">
          <strong>{m.lastLatencyMs === null ? '—' : `${m.lastLatencyMs} ms`}</strong> last
        </span>
        <span className="stat">
          checked <strong>{ago(m.lastCheckedAt)}</strong>
        </span>
      </footer>
    </article>
  )
}
