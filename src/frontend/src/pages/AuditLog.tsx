import { Card, Table, Tag, Typography } from 'antd'
import { CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { AuditEntryDto } from '../api/types'
import PageHeader from '../components/PageHeader'
import { formatDateTime } from '../utils/format'

const { Text } = Typography

export default function AuditLog() {
  const { data, loading, refresh } = useApi(() => api.audit(500))

  const columns: ColumnsType<AuditEntryDto> = [
    { title: 'Время', dataIndex: 'timestamp', render: (d) => formatDateTime(d), width: 180, defaultSortOrder: 'descend', sorter: (a, b) => a.id - b.id },
    { title: 'Действие', dataIndex: 'action', render: (a: string) => <Tag color="geekblue">{a}</Tag>, width: 160 },
    { title: 'Объект', dataIndex: 'target', render: (t) => t ?? <Text type="secondary">—</Text> },
    {
      title: 'Результат', dataIndex: 'success', width: 120,
      render: (ok: boolean) => ok
        ? <Tag icon={<CheckCircleOutlined />} color="success">Успех</Tag>
        : <Tag icon={<CloseCircleOutlined />} color="error">Ошибка</Tag>,
      filters: [{ text: 'Успех', value: true }, { text: 'Ошибка', value: false }],
      onFilter: (v, r) => r.success === v,
    },
    { title: 'Ключ', dataIndex: 'actor', width: 160 },
    { title: 'IP', dataIndex: 'sourceIp', width: 130, render: (ip) => ip ?? '—' },
    { title: 'Детали', dataIndex: 'details', render: (d) => <Text type="secondary" style={{ fontSize: 12 }}>{d}</Text> },
  ]

  return (
    <>
      <PageHeader title="Журнал аудита" subtitle={data ? `${data.length} записей` : undefined} onRefresh={refresh} loading={loading} />
      <Card variant="borderless" className="sp-glass">
        <Table rowKey="id" size="small" columns={columns} dataSource={data ?? []} loading={loading}
          pagination={{ pageSize: 25, showSizeChanger: true }} />
      </Card>
    </>
  )
}
