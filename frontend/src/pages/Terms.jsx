import { LegalDoc } from '../lib/markdown.jsx'
import terms from '../../../docs/legal/terms-of-service.md?raw'

export default function Terms() {
  return <LegalDoc markdown={terms} />
}
