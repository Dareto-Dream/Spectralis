// Minimal markdown -> JSX renderer for the legal docs (docs/legal/*.md).
// Supports headings (##, ###), bullet lists (-), paragraphs, **bold**, `code`,
// [text](url) links, and bare https:// URLs. Good enough for well-formed,
// hand-written docs — not a general-purpose markdown parser.

const INLINE_RE = /(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\([^)]+\)|https?:\/\/\S+)/g

function renderInline(text) {
  const parts = text.split(INLINE_RE)
  return parts.filter(Boolean).map((part, i) => {
    if (part.startsWith('**') && part.endsWith('**')) {
      return <strong key={i}>{part.slice(2, -2)}</strong>
    }
    if (part.startsWith('`') && part.endsWith('`')) {
      return <code key={i}>{part.slice(1, -1)}</code>
    }
    const linkMatch = /^\[([^\]]+)\]\(([^)]+)\)$/.exec(part)
    if (linkMatch) {
      return (
        <a key={i} href={linkMatch[2]} target="_blank" rel="noreferrer">
          {linkMatch[1]}
        </a>
      )
    }
    if (/^https?:\/\//.test(part)) {
      return (
        <a key={i} href={part} target="_blank" rel="noreferrer">
          {part}
        </a>
      )
    }
    return part
  })
}

function parseBlocks(lines) {
  const blocks = []
  let paraBuf = []
  const flushPara = () => {
    if (paraBuf.length) {
      blocks.push({ type: 'p', text: paraBuf.join(' ') })
      paraBuf = []
    }
  }
  let i = 0
  while (i < lines.length) {
    const trimmed = lines[i].trim()
    if (trimmed === '' || trimmed === '---') {
      flushPara()
      i++
      continue
    }
    if (trimmed.startsWith('### ')) {
      flushPara()
      blocks.push({ type: 'h3', text: trimmed.slice(4) })
      i++
      continue
    }
    if (trimmed.startsWith('## ')) {
      flushPara()
      blocks.push({ type: 'h2', text: trimmed.slice(3) })
      i++
      continue
    }
    if (trimmed.startsWith('- ')) {
      flushPara()
      const items = []
      while (i < lines.length && lines[i].trim().startsWith('- ')) {
        items.push(lines[i].trim().slice(2))
        i++
      }
      blocks.push({ type: 'ul', items })
      continue
    }
    paraBuf.push(trimmed)
    i++
  }
  flushPara()
  return blocks
}

function parseLegalDoc(markdown) {
  const lines = markdown.split('\n')
  let i = 0
  let title = ''
  let effectiveDate = ''
  if (lines[i]?.startsWith('# ')) {
    title = lines[i].slice(2).trim()
    i++
  }
  while (i < lines.length && lines[i].trim() === '') i++
  if (lines[i]?.trim().startsWith('Effective date:')) {
    effectiveDate = lines[i].trim()
    i++
  }
  return { title, effectiveDate, blocks: parseBlocks(lines.slice(i)) }
}

export function LegalDoc({ markdown }) {
  const { title, effectiveDate, blocks } = parseLegalDoc(markdown)
  return (
    <>
      <div className="page-head">
        <span className="section__label">Legal</span>
        <h1 className="page-head__title">{title}</h1>
        {effectiveDate && <p className="legal-doc__effective">{effectiveDate}</p>}
      </div>
      <div className="section legal-doc">
        {blocks.map((block, i) => {
          if (block.type === 'h2') return <h2 key={i} className="legal-doc__h2">{renderInline(block.text)}</h2>
          if (block.type === 'h3') return <h3 key={i} className="legal-doc__h3">{renderInline(block.text)}</h3>
          if (block.type === 'ul') {
            return (
              <ul key={i} className="legal-doc__ul">
                {block.items.map((item, j) => <li key={j}>{renderInline(item)}</li>)}
              </ul>
            )
          }
          return <p key={i} className="legal-doc__p">{renderInline(block.text)}</p>
        })}
      </div>
    </>
  )
}
