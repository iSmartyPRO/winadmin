import { useState } from 'react'
import { Badge, Button, Card, Col, Empty, Popconfirm, Row, Skeleton, Space, Tag, Typography, App } from 'antd'
import {
  PrinterOutlined, PauseOutlined, CaretRightOutlined, DeleteOutlined, StarFilled,
} from '@ant-design/icons'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { PrinterInfo } from '../api/types'
import PageHeader from '../components/PageHeader'

const { Text } = Typography

const statusBadge: Record<string, 'success' | 'processing' | 'warning' | 'error' | 'default'> = {
  Idle: 'success', Printing: 'processing', Paused: 'warning', Offline: 'error', Error: 'error',
}

export default function Printers() {
  const { data, loading, refresh } = useApi(api.printers)
  const { message } = App.useApp()
  const [busy, setBusy] = useState<string>()

  const act = async (p: PrinterInfo, action: 'pause' | 'resume' | 'purge') => {
    setBusy(p.name)
    try {
      const r = await api.controlPrinter(p.name, action)
      r.success ? message.success(r.message) : message.error(r.message)
      await refresh(true)
    } catch (e) {
      message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Ошибка действия')
    } finally {
      setBusy(undefined)
    }
  }

  return (
    <>
      <PageHeader title="Принтеры" subtitle={data ? `${data.length} принтеров` : undefined} onRefresh={refresh} loading={loading} />
      {loading && !data ? (
        <Skeleton active paragraph={{ rows: 4 }} />
      ) : !data?.length ? (
        <Empty description="Принтеры не найдены" />
      ) : (
        <Row gutter={[16, 16]}>
          {data.map((p) => (
            <Col xs={24} sm={12} lg={8} key={p.name}>
              <Card variant="borderless" className="sp-glass" styles={{ body: { padding: 18 } }}>
                <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
                  <span style={{ fontSize: 26, color: '#7aa0ff' }}><PrinterOutlined /></span>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      <Text strong ellipsis style={{ maxWidth: 180 }}>{p.name}</Text>
                      {p.isDefault && <StarFilled style={{ color: '#f59e0b' }} title="По умолчанию" />}
                    </div>
                    <Text type="secondary" style={{ fontSize: 12 }} ellipsis>{p.driverName}</Text>
                    <div style={{ marginTop: 8 }}>
                      <Badge status={statusBadge[p.status] ?? 'default'} text={p.status} />
                      {p.queuedJobs > 0 && <Tag color="blue" style={{ marginLeft: 8 }}>{p.queuedJobs} в очереди</Tag>}
                      {p.workOffline && <Tag color="error" style={{ marginLeft: 8 }}>Не в сети</Tag>}
                    </div>
                  </div>
                </div>
                <Space style={{ marginTop: 16 }} size={6}>
                  <Button size="small" icon={<PauseOutlined />} loading={busy === p.name} onClick={() => act(p, 'pause')}>Пауза</Button>
                  <Button size="small" icon={<CaretRightOutlined />} loading={busy === p.name} onClick={() => act(p, 'resume')}>Возобновить</Button>
                  <Popconfirm title="Очистить очередь печати?" onConfirm={() => act(p, 'purge')} okText="Очистить" okButtonProps={{ danger: true }} cancelText="Отмена">
                    <Button size="small" danger icon={<DeleteOutlined />} loading={busy === p.name}>Очередь</Button>
                  </Popconfirm>
                </Space>
              </Card>
            </Col>
          ))}
        </Row>
      )}
    </>
  )
}
