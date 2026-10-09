import { useEffect, useState } from 'react'
import { Button, Card, Divider, Form, Input, Typography, App } from 'antd'
import { LockOutlined, UserOutlined, WindowsOutlined } from '@ant-design/icons'
import { authApi } from '../api/authApi'
import { setStoredToken } from '../api/client'

const { Title, Paragraph } = Typography

export default function LoginForm({ onAuthed }: { onAuthed: () => void }) {
  const { message } = App.useApp()
  const [loading, setLoading] = useState(false)
  const [windowsAvailable, setWindowsAvailable] = useState(false)
  const [windowsLoading, setWindowsLoading] = useState(false)

  useEffect(() => { authApi.options().then((o) => setWindowsAvailable(o.directory)).catch(() => {}) }, [])

  const signInWindows = async () => {
    setWindowsLoading(true)
    try {
      const resp = await authApi.windows()
      setStoredToken(resp.accessToken)
      onAuthed()
    } catch (err: any) {
      message.error(err?.message ?? 'Вход Windows не выполнен')
    } finally {
      setWindowsLoading(false)
    }
  }

  const submit = async ({ login, password }: { login: string; password: string }) => {
    setLoading(true)
    try {
      const resp = await authApi.login(login, password)
      setStoredToken(resp.accessToken)
      message.success('Добро пожаловать!')
      onAuthed()
    } catch (err: any) {
      message.error(err?.message ?? 'Ошибка входа')
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
            name="login"
            label="Логин"
            rules={[{ required: true, message: 'Введите логин' }]}
          >
            <Input prefix={<UserOutlined />} placeholder="admin или ДОМЕН\\пользователь" size="large" autoFocus />
          </Form.Item>
          <Form.Item
            name="password"
            label="Пароль"
            rules={[{ required: true, message: 'Введите пароль' }]}
          >
            <Input.Password prefix={<LockOutlined />} placeholder="••••••••" size="large" />
          </Form.Item>
          <Button type="primary" htmlType="submit" block size="large" loading={loading}>
            Войти
          </Button>
          {windowsAvailable && (
            <>
              <Divider plain>или</Divider>
              <Button icon={<WindowsOutlined />} block size="large" loading={windowsLoading} onClick={signInWindows}>
                Войти как текущий пользователь Windows
              </Button>
            </>
          )}
        </Form>
      </Card>
    </div>
  )
}
