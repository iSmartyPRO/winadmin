import { useState } from 'react'
import { Button, Card, Form, Input, Typography, App } from 'antd'
import { KeyOutlined } from '@ant-design/icons'
import { api, setStoredKey } from '../api/client'

const { Title, Paragraph } = Typography

export default function KeyGate({ onAuthed }: { onAuthed: () => void }) {
  const { message } = App.useApp()
  const [loading, setLoading] = useState(false)

  const submit = async ({ key }: { key: string }) => {
    setLoading(true)
    setStoredKey(key.trim())
    try {
      // /me отвечает любому действительному ключу, независимо от его прав.
      await api.me()
      message.success('Доступ подтверждён')
      onAuthed()
    } catch {
      message.error('Ключ недействителен')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', padding: 24 }}>
      <Card className="sp-glass sp-fade-in" style={{ width: 420, maxWidth: '100%' }} variant="borderless">
        <div style={{ textAlign: 'center', marginBottom: 12 }}>
          <img src="/favicon.svg" alt="" width={42} height={42} />
          <Title level={3} style={{ marginTop: 12, marginBottom: 0 }}>WinAdmin</Title>
          <Paragraph type="secondary" style={{ marginTop: 6 }}>
            Управление и мониторинг Windows-машины
          </Paragraph>
        </div>
        <Form layout="vertical" onFinish={submit}>
          <Form.Item
            name="key"
            label="API-ключ"
            rules={[{ required: true, message: 'Введите API-ключ' }]}
          >
            <Input.Password prefix={<KeyOutlined />} placeholder="sp_..." size="large" autoFocus />
          </Form.Item>
          <Button type="primary" htmlType="submit" block size="large" loading={loading}>
            Войти
          </Button>
        </Form>
        <Paragraph type="secondary" style={{ fontSize: 12, marginTop: 16, marginBottom: 0 }}>
          Стартовый admin-ключ создаётся при первом запуске и сохраняется в
          <code> bootstrap-key.txt</code> рядом с приложением.
        </Paragraph>
      </Card>
    </div>
  )
}
