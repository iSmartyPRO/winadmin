import type { ReactNode } from 'react'
import { Button, Space, Typography } from 'antd'
import { ReloadOutlined } from '@ant-design/icons'

const { Title, Text } = Typography

export default function PageHeader({
  title, subtitle, onRefresh, loading, extra,
}: {
  title: string
  subtitle?: string
  onRefresh?: () => void
  loading?: boolean
  extra?: ReactNode
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', marginBottom: 20, gap: 16, flexWrap: 'wrap' }}>
      <div>
        <Title level={3} style={{ margin: 0 }}>{title}</Title>
        {subtitle && <Text type="secondary">{subtitle}</Text>}
      </div>
      <Space>
        {extra}
        {onRefresh && (
          <Button icon={<ReloadOutlined />} onClick={onRefresh} loading={loading}>
            Обновить
          </Button>
        )}
      </Space>
    </div>
  )
}
