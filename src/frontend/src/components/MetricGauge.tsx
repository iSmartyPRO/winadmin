import { RadialBar, RadialBarChart, PolarAngleAxis, ResponsiveContainer } from 'recharts'
import { Typography } from 'antd'
import { usageColor } from '../utils/format'

const { Text } = Typography

/** Радиальный гейдж загрузки (0–100%). */
export default function MetricGauge({
  value, label, sublabel,
}: {
  value: number
  label: string
  sublabel?: string
}) {
  const color = usageColor(value)
  const data = [{ name: label, value: Math.min(100, Math.max(0, value)) }]

  return (
    <div style={{ position: 'relative', width: '100%', height: 168 }}>
      <ResponsiveContainer>
        <RadialBarChart
          innerRadius="74%"
          outerRadius="100%"
          data={data}
          startAngle={220}
          endAngle={-40}
        >
          <PolarAngleAxis type="number" domain={[0, 100]} tick={false} />
          <RadialBar background={{ fill: 'rgba(255,255,255,0.06)' }} dataKey="value" cornerRadius={10} fill={color} />
        </RadialBarChart>
      </ResponsiveContainer>
      <div style={{ position: 'absolute', inset: 0, display: 'grid', placeItems: 'center', textAlign: 'center', pointerEvents: 'none' }}>
        <div>
          <div style={{ fontSize: 30, fontWeight: 700, color }}>{value.toFixed(0)}%</div>
          <Text type="secondary" style={{ fontSize: 13 }}>{label}</Text>
          {sublabel && <div><Text type="secondary" style={{ fontSize: 11 }}>{sublabel}</Text></div>}
        </div>
      </div>
    </div>
  )
}
