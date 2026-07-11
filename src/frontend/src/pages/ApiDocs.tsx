import { Card, Col, Row, Table, Tag, Typography, Button, Space } from 'antd'
import { ApiOutlined, FileTextOutlined, LinkOutlined } from '@ant-design/icons'
import PageHeader from '../components/PageHeader'

const { Paragraph, Text, Title } = Typography

const endpoints = [
  { method: 'GET', path: '/api/v1/system', scope: 'system.read', desc: 'Техническая информация о машине' },
  { method: 'GET', path: '/api/v1/system/metrics', scope: 'system.read', desc: 'Live-метрики CPU/RAM/сеть' },
  { method: 'GET', path: '/api/v1/disks', scope: 'disks.read', desc: 'Диски, разделы, заполнение' },
  { method: 'GET', path: '/api/v1/services', scope: 'services.read', desc: 'Список служб' },
  { method: 'POST', path: '/api/v1/services/{name}/{start|stop|restart}', scope: 'services.manage', desc: 'Управление службой' },
  { method: 'GET', path: '/api/v1/processes', scope: 'processes.read', desc: 'Список процессов' },
  { method: 'DELETE', path: '/api/v1/processes/{pid}', scope: 'processes.manage', desc: 'Завершить процесс' },
  { method: 'GET', path: '/api/v1/printers', scope: 'printers.read', desc: 'Список принтеров' },
  { method: 'POST', path: '/api/v1/printers/{name}/{pause|resume|purge}', scope: 'printers.manage', desc: 'Управление принтером' },
  { method: 'POST', path: '/api/v1/power/reboot', scope: 'power.manage', desc: 'Перезагрузка' },
  { method: 'POST', path: '/api/v1/power/shutdown', scope: 'power.manage', desc: 'Выключение' },
  { method: 'POST', path: '/api/v1/power/cancel', scope: 'power.manage', desc: 'Отмена действия питания' },
  { method: 'GET', path: '/api/v1/apikeys', scope: 'admin', desc: 'Список ключей' },
  { method: 'GET', path: '/api/v1/audit', scope: 'admin', desc: 'Журнал аудита' },
]

const methodColor: Record<string, string> = { GET: 'green', POST: 'blue', DELETE: 'red' }

const curlExample = `curl -H "X-API-Key: sp_ВАШ_КЛЮЧ" \\
  https://host/api/v1/system`

const psExample = `Invoke-RestMethod -Uri "https://host/api/v1/services/Spooler/restart" \`
  -Method Post -Headers @{ "X-API-Key" = "sp_ВАШ_КЛЮЧ" }`

export default function ApiDocs() {
  return (
    <>
      <PageHeader
        title="API-документация"
        subtitle="Интеграция внешних систем с WinAdmin"
        extra={
          <Space>
            <Button icon={<ApiOutlined />} href="/swagger" target="_blank">Swagger UI</Button>
            <Button icon={<FileTextOutlined />} href="/swagger/v1/swagger.json" target="_blank">OpenAPI JSON</Button>
          </Space>
        }
      />

      <Card variant="borderless" className="sp-glass" style={{ marginBottom: 16 }}>
        <Title level={5}>Аутентификация</Title>
        <Paragraph>
          Все запросы (кроме <Text code>/health</Text>) требуют заголовок{' '}
          <Text code>X-API-Key</Text> с действующим ключом. Права ключа задаются через scopes.
          Ключ со scope <Tag color="gold">admin</Tag> проходит любые проверки.
        </Paragraph>
        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Text type="secondary">curl</Text>
            <pre style={preStyle}>{curlExample}</pre>
          </Col>
          <Col xs={24} md={12}>
            <Text type="secondary">PowerShell</Text>
            <pre style={preStyle}>{psExample}</pre>
          </Col>
        </Row>
        <Paragraph type="secondary" style={{ marginTop: 8 }}>
          <LinkOutlined /> Полные примеры (C#, Python) — в <Text code>docs/04-integration-guides.md</Text>.
        </Paragraph>
      </Card>

      <Card variant="borderless" className="sp-glass" title="Эндпоинты">
        <Table
          rowKey="path"
          size="small"
          pagination={false}
          dataSource={endpoints}
          columns={[
            { title: 'Метод', dataIndex: 'method', width: 90, render: (m: string) => <Tag color={methodColor[m]}>{m}</Tag> },
            { title: 'Путь', dataIndex: 'path', render: (p: string) => <Text code>{p}</Text> },
            { title: 'Scope', dataIndex: 'scope', width: 160, render: (s: string) => <Tag color={s === 'admin' ? 'gold' : 'geekblue'}>{s}</Tag> },
            { title: 'Описание', dataIndex: 'desc' },
          ]}
        />
      </Card>
    </>
  )
}

const preStyle: React.CSSProperties = {
  background: '#0b0f16',
  border: '1px solid #222a36',
  borderRadius: 8,
  padding: 12,
  fontSize: 12,
  overflow: 'auto',
  marginTop: 4,
}
