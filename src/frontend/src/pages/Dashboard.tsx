import { useEffect, useRef, useState } from 'react'
import { Card, Col, Row, Skeleton, Tag, Typography, Empty } from 'antd'
import {
  WindowsOutlined, ThunderboltOutlined, DatabaseOutlined, ClockCircleOutlined,
  HddOutlined, DeploymentUnitOutlined,
} from '@ant-design/icons'
import {
  Area, AreaChart, ResponsiveContainer, Tooltip as RTooltip, XAxis, YAxis,
} from 'recharts'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { SystemMetrics } from '../api/types'
import PageHeader from '../components/PageHeader'
import StatCard from '../components/StatCard'
import MetricGauge from '../components/MetricGauge'
import UsageBar from '../components/UsageBar'
import { formatBytes, formatUptime } from '../utils/format'

const { Text } = Typography

export default function Dashboard() {
  const { data: info, loading, refresh } = useApi(api.system, 0)
  const { data: metrics } = useApi<SystemMetrics>(api.metrics, 3000)
  const { data: disks } = useApi(api.disks, 0)
  const [history, setHistory] = useState<{ t: string; cpu: number; mem: number }[]>([])
  const counter = useRef(0)

  useEffect(() => {
    if (!metrics) return
    setHistory((h) => [
      ...h.slice(-29),
      { t: String(counter.current++), cpu: metrics.cpuUsagePercent, mem: metrics.memoryUsagePercent },
    ])
  }, [metrics])

  return (
    <>
      <PageHeader
        title="Дашборд"
        subtitle={info ? `${info.hostname}${info.domain ? ' · ' + info.domain : ''}` : 'Загрузка…'}
        onRefresh={refresh}
        loading={loading}
      />

      {loading && !info ? (
        <Skeleton active paragraph={{ rows: 6 }} />
      ) : (
        <>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12} xl={6}>
              <StatCard icon={<WindowsOutlined />} label="Операционная система"
                value={info?.osName ?? '—'} hint={`${info?.osArchitecture ?? ''} · ${info?.osVersion ?? ''}`} />
            </Col>
            <Col xs={24} sm={12} xl={6}>
              <StatCard icon={<ThunderboltOutlined />} accent="#22c55e" label="Процессор"
                value={`${info?.cpuLogicalCores ?? 0} потоков`} hint={info?.cpuName} />
            </Col>
            <Col xs={24} sm={12} xl={6}>
              <StatCard icon={<DatabaseOutlined />} accent="#a855f7" label="Память"
                value={formatBytes(info?.totalMemoryBytes ?? 0)} hint={`занято ${metrics?.memoryUsagePercent ?? 0}%`} />
            </Col>
            <Col xs={24} sm={12} xl={6}>
              <StatCard icon={<ClockCircleOutlined />} accent="#f59e0b" label="Время работы"
                value={formatUptime(info?.uptime)} hint={info?.manufacturer && info?.model ? `${info.manufacturer} ${info.model}` : undefined} />
            </Col>
          </Row>

          <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
            <Col xs={24} sm={8}>
              <Card variant="borderless" className="sp-glass">
                <MetricGauge value={metrics?.cpuUsagePercent ?? 0} label="CPU" />
              </Card>
            </Col>
            <Col xs={24} sm={8}>
              <Card variant="borderless" className="sp-glass">
                <MetricGauge value={metrics?.memoryUsagePercent ?? 0} label="Память"
                  sublabel={metrics ? `${formatBytes(metrics.memoryUsedBytes)} / ${formatBytes(metrics.memoryTotalBytes)}` : undefined} />
              </Card>
            </Col>
            <Col xs={24} sm={8}>
              <Card variant="borderless" className="sp-glass" title={<span><DeploymentUnitOutlined /> Сеть</span>}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 12, paddingTop: 8 }}>
                  <div>
                    <Text type="secondary">↓ Приём</Text>
                    <div style={{ fontSize: 22, fontWeight: 700, color: '#22c55e' }}>
                      {formatBytes(metrics?.networkBytesReceivedPerSec ?? 0)}/s
                    </div>
                  </div>
                  <div>
                    <Text type="secondary">↑ Передача</Text>
                    <div style={{ fontSize: 22, fontWeight: 700, color: '#4f7cff' }}>
                      {formatBytes(metrics?.networkBytesSentPerSec ?? 0)}/s
                    </div>
                  </div>
                </div>
              </Card>
            </Col>
          </Row>

          <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
            <Col xs={24} xl={15}>
              <Card variant="borderless" className="sp-glass" title="Нагрузка в реальном времени" styles={{ body: { height: 260 } }}>
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={history} margin={{ top: 8, right: 8, left: -20, bottom: 0 }}>
                    <defs>
                      <linearGradient id="gCpu" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="0%" stopColor="#4f7cff" stopOpacity={0.5} />
                        <stop offset="100%" stopColor="#4f7cff" stopOpacity={0} />
                      </linearGradient>
                      <linearGradient id="gMem" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="0%" stopColor="#a855f7" stopOpacity={0.4} />
                        <stop offset="100%" stopColor="#a855f7" stopOpacity={0} />
                      </linearGradient>
                    </defs>
                    <XAxis dataKey="t" hide />
                    <YAxis domain={[0, 100]} tick={{ fill: '#6b7280', fontSize: 11 }} />
                    <RTooltip contentStyle={{ background: '#11161e', border: '1px solid #2a3242', borderRadius: 8 }}
                      formatter={(v, n) => [`${v}%`, n === 'cpu' ? 'CPU' : 'Память']} labelFormatter={() => ''} />
                    <Area type="monotone" dataKey="cpu" stroke="#4f7cff" fill="url(#gCpu)" strokeWidth={2} isAnimationActive={false} />
                    <Area type="monotone" dataKey="mem" stroke="#a855f7" fill="url(#gMem)" strokeWidth={2} isAnimationActive={false} />
                  </AreaChart>
                </ResponsiveContainer>
              </Card>
            </Col>
            <Col xs={24} xl={9}>
              <Card variant="borderless" className="sp-glass" title={<span><HddOutlined /> Диски</span>}>
                {!disks?.length && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Нет данных" />}
                {disks?.flatMap((d) => d.volumes).map((v) => (
                  <div key={v.drive} style={{ marginBottom: 14 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
                      <Text strong>{v.drive} {v.label && <Text type="secondary">{v.label}</Text>}</Text>
                      <Text type="secondary">{formatBytes(v.freeBytes)} свободно</Text>
                    </div>
                    <UsageBar percent={v.usedPercent} />
                  </div>
                ))}
                {info?.networkAdapters?.some((a) => a.isUp) && (
                  <div style={{ marginTop: 16 }}>
                    <Text type="secondary">Активные адаптеры</Text>
                    <div style={{ marginTop: 6, display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                      {info.networkAdapters.filter((a) => a.isUp && a.ipAddresses.length).slice(0, 4).map((a) => (
                        <Tag key={a.name} color="blue">{a.ipAddresses[0]}</Tag>
                      ))}
                    </div>
                  </div>
                )}
              </Card>
            </Col>
          </Row>
        </>
      )}
    </>
  )
}
