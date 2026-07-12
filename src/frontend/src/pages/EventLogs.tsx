// src/frontend/src/pages/EventLogs.tsx
import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import {
  Alert, Button, Card, DatePicker, Input, Modal, Select, Space, Switch, Table, Tag, Tooltip, Typography,
} from 'antd'
import { InfoCircleOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { EventLogEntryDto } from '../api/types'
import PageHeader from '../components/PageHeader'
import { formatDateTime } from '../utils/format'
import { findPreset } from '../config/eventLogPresets'
import { groupAuthEvents, isAuthEventGroup } from '../utils/authEventGrouping'
import type { AuthEventRow } from '../utils/authEventGrouping'

const { RangePicker } = DatePicker
const { Text, Paragraph } = Typography

const MAX_RECORDS_OPTIONS = [200, 500, 1000, 5000, 10000, 50000]

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
  // Мontируем EventLogsView заново при каждой смене пресета: свежий useState
  // уже инициализируется значениями нового пресета, без гонок между эффектами
  // сброса и повторного запроса.
  return <EventLogsView key={presetKey} presetKey={presetKey} />
}

function EventLogsView({ presetKey }: { presetKey?: string }) {
  const isCustom = presetKey === 'custom'
  const isAuthPreset = presetKey === 'auth'
  const preset = findPreset(presetKey)

  const [logName, setLogName] = useState(preset?.logName ?? '')
  const [logNames, setLogNames] = useState<string[]>([])
  const [range, setRange] = useState<[Dayjs, Dayjs]>(quickRange(24))
  const [maxRecords, setMaxRecords] = useState(200)
  const [levels, setLevels] = useState<string[]>([])
  const [eventIds, setEventIds] = useState(preset?.eventIds?.join(',') ?? '')
  const [keyword, setKeyword] = useState('')
  const [user, setUser] = useState('')
  const [excludeSystem, setExcludeSystem] = useState(true)
  const [hideNoise, setHideNoise] = useState(true)
  const [detailed, setDetailed] = useState(false)
  const [detail, setDetail] = useState<EventLogEntryDto | null>(null)

  useEffect(() => {
    if (isCustom) api.eventLogs.logNames().then(setLogNames).catch(() => setLogNames([]))
  }, [isCustom])

  const { data, loading, error, refresh } = useApi(() => {
    if (!logName) {
      return Promise.resolve({ entries: [], truncated: false, scannedCount: 0 })
    }
    return api.eventLogs.query({
      logName,
      start: range[0].toISOString(),
      end: range[1].toISOString(),
      maxRecords,
      eventIds: eventIds || undefined,
      levels: levels.length ? levels.join(',') : undefined,
      keyword: keyword || undefined,
      user: user || undefined,
      excludeSystemAccounts: isAuthPreset ? excludeSystem : undefined,
      // Скрываем фоновый шум (сетевые 3, служебные 5), кроме поиска по конкретному пользователю.
      excludeLogonTypes: isAuthPreset && hideNoise && !user.trim() ? '3,5' : undefined,
    })
  })

  // Автозапрос при выборе журнала в кастомном режиме (смена пресета уже
  // обрабатывается ремонтом всего компонента по ключу presetKey, поэтому
  // здесь остаётся только один сценарий: пользователь меняет logName через
  // Select, оставаясь на том же смонтированном инстансе).
  const isFirstRender = useRef(true)
  useEffect(() => {
    if (isFirstRender.current) {
      isFirstRender.current = false
      return
    }
    if (isCustom && logName) refresh()
  }, [logName]) // eslint-disable-line react-hooks/exhaustive-deps

  const isFirstRenderExcludeSystem = useRef(true)
  useEffect(() => {
    if (isFirstRenderExcludeSystem.current) {
      isFirstRenderExcludeSystem.current = false
      return
    }
    if (isAuthPreset) refresh()
  }, [excludeSystem]) // eslint-disable-line react-hooks/exhaustive-deps

  const isFirstRenderHideNoise = useRef(true)
  useEffect(() => {
    if (isFirstRenderHideNoise.current) {
      isFirstRenderHideNoise.current = false
      return
    }
    if (isAuthPreset) refresh()
  }, [hideNoise]) // eslint-disable-line react-hooks/exhaustive-deps

  const tableData: AuthEventRow[] = isAuthPreset
    ? groupAuthEvents(data?.entries ?? [])
    : (data?.entries ?? [])

  const timeCol: ColumnsType<AuthEventRow>[number] = {
    title: 'Время', dataIndex: 'timeCreated', width: 180,
    render: (d: string) => formatDateTime(d),
  }
  const levelCol: ColumnsType<AuthEventRow>[number] = {
    title: isAuthPreset ? 'Событие' : 'Уровень', dataIndex: 'levelDisplayName', width: 150,
    render: (l: string | undefined, r: AuthEventRow) => {
      if (isAuthEventGroup(r)) return <Tag color={r.summary.color}>{r.summary.label}</Tag>
      return <Tag color={LEVEL_COLORS[r.level ?? ''] ?? 'default'}>{l ?? r.level ?? '—'}</Tag>
    },
  }
  const sourceCol: ColumnsType<AuthEventRow>[number] = {
    title: 'Источник', dataIndex: 'providerName', width: 220, ellipsis: true,
    render: (v: string | undefined, r: AuthEventRow) => {
      if (isAuthEventGroup(r)) return <Text type="secondary">—</Text>
      return v ?? <Text type="secondary">—</Text>
    },
  }
  const eventIdCol: ColumnsType<AuthEventRow>[number] = {
    title: 'ID события', dataIndex: 'eventId', width: 100,
    render: (v: number | undefined, r: AuthEventRow) => (isAuthEventGroup(r) ? `${r.entries.length} событий` : v),
  }
  const userCol: ColumnsType<AuthEventRow>[number] = {
    title: 'Пользователь', dataIndex: 'user', width: 160,
    render: (u?: string) => u ?? <Text type="secondary">—</Text>,
  }
  const ipCol: ColumnsType<AuthEventRow>[number] = {
    title: 'IP-адрес', dataIndex: 'ipAddress', width: 150,
    render: (ip?: string) => (ip ? <Text>{ip}</Text> : <Text type="secondary">—</Text>),
  }
  const messageCol: ColumnsType<AuthEventRow>[number] = {
    title: 'Сообщение', dataIndex: 'message', ellipsis: true,
    render: (m: string | undefined, r: AuthEventRow) => {
      if (isAuthEventGroup(r)) return <Text type="secondary">—</Text>
      return m ?? <Text type="secondary">—</Text>
    },
  }
  const detailsCol: ColumnsType<AuthEventRow>[number] = {
    title: 'Детали', key: 'details', width: 80, align: 'center',
    render: (_: unknown, r: AuthEventRow) => {
      if (isAuthEventGroup(r)) return <Text type="secondary">—</Text>
      return (
        <Tooltip title="Подробно о событии">
          <Button
            type="text"
            size="small"
            icon={<InfoCircleOutlined />}
            onClick={(e) => { e.stopPropagation(); setDetail(r) }}
          />
        </Tooltip>
      )
    },
  }

  const columns: ColumnsType<AuthEventRow> = isAuthPreset
    ? (detailed
        ? [timeCol, levelCol, sourceCol, eventIdCol, userCol, ipCol, messageCol, detailsCol]
        : [timeCol, levelCol, userCol, ipCol, detailsCol])
    : [timeCol, levelCol, sourceCol, eventIdCol, userCol, messageCol, detailsCol]

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
            placeholder="Точное имя учётной записи"
            value={user}
            onChange={(e) => setUser(e.target.value)}
            onSearch={() => refresh()}
          />
          {isAuthPreset && (
            <Switch
              checked={excludeSystem}
              onChange={setExcludeSystem}
              checkedChildren="Только пользователи"
              unCheckedChildren="Все записи"
            />
          )}
          {isAuthPreset && (
            <Switch
              checked={hideNoise}
              onChange={setHideNoise}
              checkedChildren="Без сетевых/служебных"
              unCheckedChildren="Все типы входа"
              disabled={!!user.trim()}
            />
          )}
          {isAuthPreset && (
            <Switch
              checked={detailed}
              onChange={setDetailed}
              checkedChildren="Подробно"
              unCheckedChildren="Кратко"
            />
          )}
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
          rowKey={(record) => (isAuthEventGroup(record) ? record.key : record.id)}
          size="small"
          columns={columns}
          dataSource={tableData}
          loading={loading}
          pagination={{ pageSize: 25, showSizeChanger: true }}
          onRow={(record) => ({
            onClick: () => {
              if (!isAuthEventGroup(record)) setDetail(record)
            },
          })}
          expandable={{
            expandRowByClick: true,
            rowExpandable: (record) => isAuthEventGroup(record),
            expandedRowRender: (record) => {
              if (!isAuthEventGroup(record)) return null
              return (
                <Table
                  size="small"
                  showHeader={false}
                  pagination={false}
                  rowKey="id"
                  dataSource={record.entries}
                  onRow={(entry) => ({ onClick: () => setDetail(entry) })}
                  columns={[
                    { dataIndex: 'timeCreated', width: 180, render: (d: string) => formatDateTime(d) },
                    {
                      dataIndex: 'levelDisplayName', width: 130,
                      render: (l: string | undefined, r: EventLogEntryDto) => (
                        <Tag color={LEVEL_COLORS[r.level ?? ''] ?? 'default'}>{l ?? r.level ?? '—'}</Tag>
                      ),
                    },
                    { dataIndex: 'eventId', width: 100 },
                    {
                      dataIndex: 'user', width: 160,
                      render: (u?: string) => u ?? <Text type="secondary">—</Text>,
                    },
                    {
                      dataIndex: 'ipAddress', width: 150,
                      render: (ip?: string) => (ip ? <Text>{ip}</Text> : <Text type="secondary">—</Text>),
                    },
                    {
                      dataIndex: 'message', ellipsis: true,
                      render: (m?: string) => m ?? <Text type="secondary">—</Text>,
                    },
                  ]}
                />
              )
            },
          }}
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
            <Paragraph><Text strong>IP-адрес:</Text> {detail.ipAddress ?? '—'}</Paragraph>
            <Paragraph><Text strong>Машина:</Text> {detail.machineName ?? '—'}</Paragraph>
            <Paragraph style={{ whiteSpace: 'pre-wrap' }}>{detail.message ?? '—'}</Paragraph>
          </>
        )}
      </Modal>
    </>
  )
}
