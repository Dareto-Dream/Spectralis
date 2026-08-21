import { VISUALIZER_SHOTS } from '../data/site.jsx'

export function VisualizerGallery() {
  return (
    <div className="viz-gallery">
      {VISUALIZER_SHOTS.map(({ src, title }) => (
        <figure key={src} className="viz-gallery__cell">
          <img src={src} alt={title} loading="lazy" />
          <figcaption>{title}</figcaption>
        </figure>
      ))}
    </div>
  )
}
