import { LegalDoc } from '../lib/markdown.jsx'
import privacy from '../../../docs/legal/privacy-policy.md?raw'

export default function Privacy() {
  return <LegalDoc markdown={privacy} />
}
