import type { ReactNode } from 'react'
import { Card, Typography } from 'antd'

const { Text } = Typography

export default function StatCard({
  icon, label, value, hint, accent = '#4f7cff',
}: {
  icon: ReactNode
  label: string
  value: ReactNode
  hint?: ReactNode
  accent?: string
}) {
  return (
    <Card variant="borderless" className="sp-glass" styles={{ body: { padding: 18 } }}>
      <div style={{ display: 'flex', gap: 14, alignItems: 'center' }}>
        <div
          style={{
            width: 46, height: 46, borderRadius: 12, display: 'grid', placeItems: 'center',
            background: `${accent}22`, color: accent, fontSize: 22, flexShrink: 0,
          }}
        >
          {icon}
        </div>
        <div style={{ minWidth: 0 }}>
          <Text type="secondary" style={{ fontSize: 13 }}>{label}</Text>
          <div style={{ fontSize: 20, fontWeight: 600, lineHeight: 1.2, marginTop: 2 }}>{value}</div>
          {hint && <Text type="secondary" style={{ fontSize: 12 }}>{hint}</Text>}
        </div>
      </div>
    </Card>
  )
}
