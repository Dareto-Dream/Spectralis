// Shared prose building blocks for Learn article bodies (see data/articles.jsx).

export function DocTable({ headers, rows }) {
  return (
    <table className="learn-table">
      <thead>
        <tr>{headers.map((h, i) => <th key={i}>{h}</th>)}</tr>
      </thead>
      <tbody>
        {rows.map((row, i) => (
          <tr key={i}>{row.map((cell, j) => <td key={j}>{cell}</td>)}</tr>
        ))}
      </tbody>
    </table>
  )
}

export function CodeBlock({ children }) {
  return <pre className="learn-code">{children}</pre>
}
