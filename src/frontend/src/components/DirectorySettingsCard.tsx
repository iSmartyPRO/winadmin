import { useEffect, useState } from 'react'
import { App, Button, Card, Form, Input, List, Space, Switch, Typography } from 'antd'
import { CheckCircleTwoTone, CloseCircleTwoTone } from '@ant-design/icons'
import { api } from '../api/client'
import type { DirectorySettings, DirectoryTestStep } from '../api/types'

export default function DirectorySettingsCard() {
  const { message } = App.useApp()
  const [form] = Form.useForm<DirectorySettings>()
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [steps, setSteps] = useState<DirectoryTestStep[]>()

  useEffect(() => { api.directory.get().then((s) => form.setFieldsValue(s)).catch(() => message.error('Не удалось загрузить настройки домена')) }, [form, message])

  const save = async (values: DirectorySettings) => {
    setSaving(true)
    try {
      form.setFieldsValue(await api.directory.save(values))
      message.success('Настройки домена сохранены')
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить')
    } finally {
      setSaving(false)
    }
  }

  const test = async () => {
    setTesting(true)
    try { setSteps(await api.directory.test()) } catch { message.error('Проверка не выполнена') } finally { setTesting(false) }
  }

  return (
    <Card title="Подключение к домену" className="sp-glass">
      <Typography.Paragraph type="secondary">
        Каталог читается учёткой компьютера; пароль не хранится. Вход доменной учёткой по паролю — только по HTTPS или с этого компьютера.
      </Typography.Paragraph>
      <Form form={form} layout="vertical" onFinish={save}>
        <Form.Item name="enabled" label="Вход учётками домена" valuePropName="checked"><Switch /></Form.Item>
        <Form.Item name="domain" label="Домен" rules={[{ validator: async (_, v) => { if (form.getFieldValue('enabled') && !v?.trim()) throw new Error('Укажите домен') } }]}>
          <Input placeholder="pcs-msk.com" />
        </Form.Item>
        <Form.Item name="server" label="Контроллер домена (пусто — найти через DNS)"><Input placeholder="dc.pcs-msk.com" /></Form.Item>
        <Form.Item name="baseDn" label="Корень поиска (пусто — весь домен)"><Input placeholder="DC=pcs-msk,DC=com" /></Form.Item>
        <Form.Item name="useLdaps" label="LDAPS (636) вместо LDAP с подписью (389)" valuePropName="checked"><Switch /></Form.Item>
        <Space>
          <Button type="primary" htmlType="submit" loading={saving}>Сохранить</Button>
          <Button onClick={test} loading={testing}>Проверить</Button>
        </Space>
      </Form>
      {steps && (
        <List style={{ marginTop: 16 }} size="small" dataSource={steps} renderItem={(s) => (
          <List.Item>
            <Space>
              {s.ok ? <CheckCircleTwoTone twoToneColor="#52c41a" /> : <CloseCircleTwoTone twoToneColor="#ff4d4f" />}
              <b>{s.name}</b> <span>{s.message}</span>
            </Space>
          </List.Item>
        )} />
      )}
    </Card>
  )
}
