import { LegalDoc } from '../lib/markdown.jsx'
// Synced from docs/legal/privacy-policy.md — kept local since the Railway
// frontend service's build root is scoped to frontend/, not the repo root.
import privacy from '../legal/privacy-policy.md?raw'

export default function Privacy() {
  return <LegalDoc markdown={privacy} />
}
