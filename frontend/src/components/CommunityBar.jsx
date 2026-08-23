import { useState, useEffect } from 'react'

const COMMUNITY_URL = 'https://cdn.deltavdevs.com/spectralis/community.json'

const AVATAR_COLORS = ['#a882f2', '#764cd2', '#e78aa7', '#5ca3ff', '#62d4b0', '#fc8444']

function colorFor(name) {
  let hash = 0
  for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash)
  return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length]
}

function initialsFor(name) {
  return name.split(' ').map(w => w[0]).slice(0, 2).join('').toUpperCase()
}

function Person({ name, subtitle, avatar }) {
  return (
    <div className="community-card">
      {avatar ? (
        <img src={avatar} alt="" className="community-card__avatar" />
      ) : (
        <span className="community-card__avatar community-card__avatar--initials" style={{ background: colorFor(name) }}>
          {initialsFor(name)}
        </span>
      )}
      <div>
        <p className="community-card__name">{name}</p>
        {subtitle && <p className="community-card__subtitle">{subtitle}</p>}
      </div>
    </div>
  )
}

export function CommunityBar() {
  const [people, setPeople] = useState(null)

  useEffect(() => {
    let cancelled = false
    fetch(COMMUNITY_URL)
      .then(res => { if (!res.ok) throw new Error(res.status); return res.json() })
      .then(data => { if (!cancelled) setPeople(data) })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  if (!people || people.length === 0) return null

  // duplicate the list so the CSS marquee can loop seamlessly
  const track = [...people, ...people]

  return (
    <div className="community-bar" aria-label="People using Spectralis">
      <div className="community-bar__track">
        {track.map((person, i) => (
          <Person key={`${person.name}-${i}`} {...person} />
        ))}
      </div>
    </div>
  )
}
