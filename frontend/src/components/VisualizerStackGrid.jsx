import { VISUALIZER_SHOTS } from '../data/site.jsx'

const GRID_SHOTS = VISUALIZER_SHOTS.filter(({ title }) => title !== 'Album Cover')

const COLUMNS = 6
const COLUMN_OFFSET = [2, 1, 0, 0, 1, 2]

export function VisualizerStackGrid() {
  const columns = Array.from({ length: COLUMNS }, (_, ci) =>
    GRID_SHOTS.filter((_, i) => i % COLUMNS === ci)
  )

  return (
    <div className="viz-grid">
      {columns.map((col, ci) => (
        <div
          key={ci}
          className="viz-grid__stack"
          style={{ marginTop: COLUMN_OFFSET[ci] ? `${COLUMN_OFFSET[ci] * 45}px` : 0 }}
        >
          {col.map(({ src, title }) => (
            <div key={src} className="viz-grid__tile" style={{ backgroundImage: `url(${src})` }}>
              <span className="viz-grid__label">{title}</span>
            </div>
          ))}
        </div>
      ))}
    </div>
  )
}
