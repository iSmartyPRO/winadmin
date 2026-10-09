import { useEffect, useState } from 'react'
import { App, Button, Card, Empty, Space, Table, Tag, Typography } from 'antd'
import { ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import type { CheckResult, CheckStatus, EnvironmentReport } from '../api/types'
import PageHeader from '../components/PageHeader'

const statusTag: Record<CheckStatus, { color: string; text: string }> = {
  Ok: { color: 'green', text: 'ок' },
  Warning: { color: 'gold', text: 'внимание' },
  Failed: { color: 'red', text: 'ошибка' },
  Skipped: { color: 'default', text: 'пропущено' },
}

const moduleTitle = (id: string) => ({ platform: 'Платформа (AD)', 'ad-users': 'Пользователи AD', 'ad-folders': 'Папки' } as Record<string, string>)[id] ?? id

export default function Environment() {
  const { message } = App.useApp()
  const [reports, setReports] = useState<EnvironmentReport[]>([])
  const [running, setRunning] = useState<'quick' | 'full'>()

  useEffect(() => { api.environment.latest().then(setReports).catch(() => {}) }, [])

  const run = async (depth: 'quick' | 'full') => {
    setRunning(depth)
    try { setReports(await api.environment.run(depth)) }
    catch { message.error('Проверка не выполнена') }
    finally { setRunning(undefined) }
  }

  return (
    <>
      <PageHeader title="Проверка окружения" subtitle="Готовность домена, учётки записи и модулей"
        extra={
          <Space>
            <Button icon={<ReloadOutlined />} loading={running === 'quick'} onClick={() => run('quick')}>Быстрая проверка</Button>
            <Button icon={<SearchOutlined />} loading={running === 'full'} onClick={() => run('full')}>Полная (с NTFS)</Button>
          </Space>
        } />
      {reports.length === 0 ? <Empty description="Проверка ещё не запускалась" /> : reports.map((r) => (
        <Card key={r.moduleId} className="sp-glass" style={{ marginBottom: 16 }}
          title={<Space>{moduleTitle(r.moduleId)}<Tag color={statusTag[r.overall].color}>{statusTag[r.overall].text}</Tag></Space>}
          extra={<Typography.Text type="secondary">{new Date(r.at).toLocaleString('ru-RU')}</Typography.Text>}>
          <Table<CheckResult> rowKey="code" size="small" pagination={false} dataSource={r.results}
            columns={[
              { title: 'Проверка', dataIndex: 'title', width: 200 },
              { title: 'Статус', dataIndex: 'status', width: 110, render: (s: CheckStatus) => <Tag color={statusTag[s].color}>{statusTag[s].text}</Tag> },
              { title: 'Результат', dataIndex: 'message' },
              { title: 'Что сделать', dataIndex: 'fix', render: (f: string | null) => f ?? '' },
            ]} />
        </Card>
      ))}
    </>
  )
}
