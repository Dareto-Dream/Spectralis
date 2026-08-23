import { LegalDoc } from '../lib/markdown.jsx'
// Synced from docs/legal/terms-of-service.md — kept local since the Railway
// frontend service's build root is scoped to frontend/, not the repo root.
import terms from '../legal/terms-of-service.md?raw'

export default function Terms() {
  return <LegalDoc markdown={terms} />
}
