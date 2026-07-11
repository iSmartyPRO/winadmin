import { Card, Tag, Typography } from 'antd'
import { HddOutlined, UsbOutlined, CloudServerOutlined } from '@ant-design/icons'
import type { DiskVolume } from '../api/types'
import { formatBytes } from '../utils/format'
import UsageBar from './UsageBar'

const { Text } = Typography

function volumeIcon(type: string) {
  if (type === 'Removable') return <UsbOutlined />
  if (type === 'Network') return <CloudServerOutlined />
  return <HddOutlined />
}

/** Карточка тома «как в проводнике»: буква, метка, полоса заполнения. */
export default function DiskCard({ volume }: { volume: DiskVolume }) {
  const title = volume.label ? `${volume.label} (${volume.drive})` : volume.drive
  return (
    <Card variant="borderless" className="sp-glass" styles={{ body: { padding: 16 } }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
        <span style={{ fontSize: 22, color: '#7aa0ff' }}>{volumeIcon(volume.driveType)}</span>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{title}</div>
          <Text type="secondary" style={{ fontSize: 12 }}>{volume.fileSystem} · {volume.driveType}</Text>
        </div>
        <Tag color={volume.usedPercent >= 90 ? 'error' : 'default'} style={{ marginInlineEnd: 0 }}>
          {volume.usedPercent}%
        </Tag>
      </div>
      <UsageBar percent={volume.usedPercent} />
      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 10 }}>
        <Text type="secondary" style={{ fontSize: 12 }}>
          Свободно <b style={{ color: '#e6edf3' }}>{formatBytes(volume.freeBytes)}</b>
        </Text>
        <Text type="secondary" style={{ fontSize: 12 }}>
          Всего {formatBytes(volume.totalBytes)}
        </Text>
      </div>
    </Card>
  )
}
