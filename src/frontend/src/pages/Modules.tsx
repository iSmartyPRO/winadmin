import { useState } from 'react'
import { Alert, App, Button, Card, Col, Form, Input, InputNumber, Row, Select, Space, Switch, Tag, Tooltip, Typography } from 'antd'
import { useApi } from '../hooks/useApi'
import { useAuth } from '../auth/AuthProvider'
import { api } from '../api/client'
import type { ModuleDto, SettingsField } from '../api/types'
import PageHeader from '../components/PageHeader'

const { Paragraph, Text } = Typography

function SettingsForm({ module, onSaved }: { module: ModuleDto; onSaved: () => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm()
  const [saving, setSaving] = useState(false)

  const initial = Object.fromEntries(module.settingsSchema
    .filter((f) => f.kind !== 'secret')
    .map((f) => [f.name, module.settings[f.name]]))

  const save = async (values: Record<string, unknown>) => {
    setSaving(true)
    try {
      await api.modules.update(module.id, { settings: values })
      message.success('Настройки сохранены')
      form.resetFields(module.settingsSchema.filter((f) => f.kind === 'secret').map((f) => f.name))
      onSaved()
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить настройки')
    } finally {
      setSaving(false)
    }
  }

  const field = (f: SettingsField) => {
    switch (f.kind) {
      case 'boolean': return <Switch />
      case 'number': return <InputNumber style={{ width: 200 }} />
      case 'stringList': return <Select mode="tags" open={false} tokenSeparators={[',']} />
      case 'secret': {
        const isSet = (module.settings[f.name] as { isSet?: boolean } | undefined)?.isSet
        return <Input.Password placeholder={isSet ? 'задано — оставьте пустым, чтобы не менять' : 'не задано'} autoComplete="new-password" />
      }
      default: return <Input />
    }
  }

  return (
    <Form form={form} layout="vertical" initialValues={initial} onFinish={save}>
      {module.settingsSchema.map((f) => (
        <Form.Item key={f.name} name={f.name} label={f.title} valuePropName={f.kind === 'boolean' ? 'checked' : 'value'}>
          {field(f)}
        </Form.Item>
      ))}
      <Button htmlType="submit" type="primary" loading={saving}>Сохранить настройки</Button>
    </Form>
  )
}

export default function Modules() {
  const { data, loading, refresh } = useApi(api.modules.list)
  const { reload } = useAuth()
  const { message } = App.useApp()
  const [busy, setBusy] = useState<string>()

  const toggle = async (m: ModuleDto, enabled: boolean) => {
    setBusy(m.id)
    try {
      await api.modules.update(m.id, { enabled })
      message.success(enabled ? `Модуль «${m.title}» включён` : `Модуль «${m.title}» выключен`)
      await refresh(true)
      await reload() // меню и маршруты зависят от включённых модулей
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось изменить модуль')
    } finally {
      setBusy(undefined)
    }
  }

  return (
    <>
      <PageHeader title="Модули" subtitle="Включение, отключение и настройка разделов WinAdmin" onRefresh={refresh} loading={loading} />
      <Alert type="info" showIcon style={{ marginBottom: 16 }}
        message="Выключенный модуль скрыт из меню у всех пользователей, а его API отвечает 404. Права на него можно заранее выдать в ролях." />
      <Row gutter={[16, 16]}>
        {(data ?? []).map((m) => (
          <Col key={m.id} xs={24} lg={12}>
            <Card
              className="sp-glass"
              variant="borderless"
              title={<Space>{m.title}<Text type="secondary" code>{m.id}</Text></Space>}
              extra={
                <Tooltip title={m.available ? undefined : `Недоступен: ${m.unavailableReason}`}>
                  <Switch checked={m.enabled} disabled={!m.available} loading={busy === m.id} onChange={(v) => toggle(m, v)} />
                </Tooltip>
              }
            >
              {m.description && <Paragraph type="secondary" style={{ marginTop: 0 }}>{m.description}</Paragraph>}
              {!m.available && <Alert type="warning" showIcon style={{ marginBottom: 12 }} message={`Недоступен на этой машине: ${m.unavailableReason}`} />}
              <Space size={[4, 4]} wrap style={{ marginBottom: m.settingsSchema.length ? 16 : 0 }}>
                {m.permissions.map((p) => (
                  <Tooltip key={p.id} title={p.description}>
                    <Tag color={p.dangerous ? 'volcano' : 'blue'}>{p.title}</Tag>
                  </Tooltip>
                ))}
              </Space>
              {m.settingsSchema.length > 0 && <SettingsForm module={m} onSaved={() => refresh(true)} />}
            </Card>
          </Col>
        ))}
      </Row>
    </>
  )
}
