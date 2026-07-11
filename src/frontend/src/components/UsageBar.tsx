import { usageColor } from '../utils/format'

/** Тонкая цветная полоса заполнения (как в проводнике Windows). */
export default function UsageBar({ percent, height = 8 }: { percent: number; height?: number }) {
  const color = usageColor(percent)
  return (
    <div style={{ width: '100%', height, borderRadius: height, background: 'rgba(255,255,255,0.08)', overflow: 'hidden' }}>
      <div
        style={{
          width: `${Math.min(100, Math.max(0, percent))}%`,
          height: '100%',
          borderRadius: height,
          background: `linear-gradient(90deg, ${color}cc, ${color})`,
          transition: 'width 0.4s ease',
        }}
      />
    </div>
  )
}
