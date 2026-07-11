// src/frontend/src/pages/EventLogs.tsx
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import {
  Alert, Button, Card, DatePicker, Input, Modal, Select, Space, Table, Tag, Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { EventLogEntryDto } from '../api/types'
import PageHeader from '../components/PageHeader'
import { formatDateTime } from '../utils/format'
import { findPreset } from '../config/eventLogPresets'

const { RangePicker } = DatePicker
const { Text, Paragraph } = Typography

const MAX_RECORDS_OPTIONS = [200, 500, 1000, 5000]

const LEVEL_OPTIONS = [
  { label: 'Критическая', value: 'Critical' },
  { label: 'Ошибка', value: 'Error' },
  { label: 'Предупреждение', value: 'Warning' },
  { label: 'Информация', value: 'Information' },
]

const LEVEL_COLORS: Record<string, string> = {
  Critical: 'red', Error: 'volcano', Warning: 'gold', Information: 'blue',
}

function quickRange(hours: number): [Dayjs, Dayjs] {
  return [dayjs().subtract(hours, 'hour'), dayjs()]
}

export default function EventLogs() {
  const { presetKey } = useParams<{ presetKey: string }>()
  const isCustom = presetKey === 'custom'
  const preset = findPreset(presetKey)

  const [logName, setLogName] = useState(preset?.logName ?? '')
  const [logNames, setLogNames] = useState<string[]>([])
  const [range, setRange] = useState<[Dayjs, Dayjs]>(quickRange(24))
  const [maxRecords, setMaxRecords] = useState(200)
  const [levels, setLevels] = useState<string[]>([])
  const [eventIds, setEventIds] = useState(preset?.eventIds?.join(',') ?? '')
  const [keyword, setKeyword] = useState('')
  const [user, setUser] = useState('')
  const [detail, setDetail] = useState<EventLogEntryDto | null>(null)

  // Сброс фильтров при смене пресета/маршрута
  useEffect(() => {
    setLogName(preset?.logName ?? '')
    setEventIds(preset?.eventIds?.join(',') ?? '')
    setRange(quickRange(24))
    setMaxRecords(200)
    setLevels([])
    setKeyword('')
    setUser('')
  }, [presetKey]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (isCustom) api.eventLogs.logNames().then(setLogNames).catch(() => setLogNames([]))
  }, [isCustom])

  const { data, loading, error, refresh } = useApi(() => api.eventLogs.query({
    logName,
    start: range[0].toISOString(),
    end: range[1].toISOString(),
    maxRecords,
    eventIds: eventIds || undefined,
    levels: levels.length ? levels.join(',') : undefined,
    keyword: keyword || undefined,
    user: user || undefined,
  }))

  // Автозапрос при смене журнала (пресет применился или выбран в кастомном режиме)
  useEffect(() => {
    if (logName) refresh()
  }, [logName]) // eslint-disable-line react-hooks/exhaustive-deps

  const columns: ColumnsType<EventLogEntryDto> = [
    { title: 'Время', dataIndex: 'timeCreated', render: (d: string) => formatDateTime(d), width: 180 },
    {
      title: 'Уровень', dataIndex: 'levelDisplayName', width: 130,
      render: (l: string | undefined, r) => (
        <Tag color={LEVEL_COLORS[r.level ?? ''] ?? 'default'}>{l ?? r.level ?? '—'}</Tag>
      ),
    },
    { title: 'Источник', dataIndex: 'providerName', width: 220, ellipsis: true },
    { title: 'ID события', dataIndex: 'eventId', width: 100 },
    {
      title: 'Пользователь', dataIndex: 'user', width: 160,
      render: (u?: string) => u ?? <Text type="secondary">—</Text>,
    },
    {
      title: 'Сообщение', dataIndex: 'message', ellipsis: true,
      render: (m?: string) => m ?? <Text type="secondary">—</Text>,
    },
  ]

  return (
    <>
      <PageHeader
        title={preset?.label ?? 'Произвольный журнал'}
        subtitle={data ? `${data.entries.length} записей` : undefined}
        onRefresh={refresh}
        loading={loading}
      />
      <Card variant="borderless" className="sp-glass" style={{ marginBottom: 16 }}>
        <Space wrap size="middle">
          {isCustom && (
            <Select
              showSearch
              placeholder="Выберите журнал"
              style={{ width: 280 }}
              value={logName || undefined}
              onChange={setLogName}
              options={logNames.map((n) => ({ label: n, value: n }))}
            />
          )}
          <Space.Compact>
            <Button onClick={() => setRange(quickRange(24))}>24ч</Button>
            <Button onClick={() => setRange(quickRange(24 * 7))}>7д</Button>
            <Button onClick={() => setRange(quickRange(24 * 30))}>30д</Button>
          </Space.Compact>
          <RangePicker showTime value={range} onChange={(v) => v && setRange(v as [Dayjs, Dayjs])} />
          <Select
            style={{ width: 130 }}
            value={maxRecords}
            onChange={setMaxRecords}
            options={MAX_RECORDS_OPTIONS.map((n) => ({ label: `${n} записей`, value: n }))}
          />
          <Select
            mode="multiple"
            allowClear
            placeholder="Уровень"
            style={{ minWidth: 200 }}
            value={levels}
            onChange={setLevels}
            options={LEVEL_OPTIONS}
          />
          <Input
            style={{ width: 170 }}
            placeholder="Event ID (4624,4625)"
            value={eventIds}
            onChange={(e) => setEventIds(e.target.value)}
          />
          <Input.Search
            style={{ width: 200 }}
            placeholder="Поиск по тексту"
            value={keyword}
            onChange={(e) => setKeyword(e.target.value)}
            onSearch={() => refresh()}
          />
          <Input.Search
            style={{ width: 200 }}
            placeholder="Пользователь (часть имени)"
            value={user}
            onChange={(e) => setUser(e.target.value)}
            onSearch={() => refresh()}
          />
          <Button type="primary" onClick={() => refresh()} loading={loading}>Применить</Button>
        </Space>
      </Card>

      {error && <Alert type="error" message={error} style={{ marginBottom: 16 }} />}
      {data?.truncated && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={`Показаны последние ${data.entries.length} записей за выбранный период. Сузьте фильтры или увеличьте лимит, чтобы увидеть больше.`}
        />
      )}

      <Card variant="borderless" className="sp-glass">
        <Table
          rowKey="id"
          size="small"
          columns={columns}
          dataSource={data?.entries ?? []}
          loading={loading}
          pagination={{ pageSize: 25, showSizeChanger: true }}
          onRow={(record) => ({ onClick: () => setDetail(record) })}
        />
      </Card>

      <Modal
        open={!!detail}
        onCancel={() => setDetail(null)}
        footer={null}
        width={640}
        title={detail ? `${detail.providerName ?? ''} · ID ${detail.eventId}` : ''}
      >
        {detail && (
          <>
            <Paragraph><Text strong>Время:</Text> {formatDateTime(detail.timeCreated)}</Paragraph>
            <Paragraph><Text strong>Журнал:</Text> {detail.logName}</Paragraph>
            <Paragraph><Text strong>Уровень:</Text> {detail.levelDisplayName ?? detail.level ?? '—'}</Paragraph>
            <Paragraph><Text strong>Пользователь:</Text> {detail.user ?? '—'}</Paragraph>
            <Paragraph><Text strong>Машина:</Text> {detail.machineName ?? '—'}</Paragraph>
            <Paragraph style={{ whiteSpace: 'pre-wrap' }}>{detail.message ?? '—'}</Paragraph>
          </>
        )}
      </Modal>
    </>
  )
}
