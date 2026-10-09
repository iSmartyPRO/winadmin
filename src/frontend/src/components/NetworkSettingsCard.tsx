import { useCallback, useEffect, useState } from 'react'
import { Alert, App, Button, Card, Form, InputNumber, Radio, Select, Space, Typography } from 'antd'
import { GlobalOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import type { NetworkMode, NetworkSettingsDto } from '../api/types'

const { Paragraph, Text } = Typography

interface FormValues {
  mode: NetworkMode
  port: number
  allow: string[]
}

export default function NetworkSettingsCard() {
  const { message, modal } = App.useApp()
  const [form] = Form.useForm<FormValues>()
  const [current, setCurrent] = useState<NetworkSettingsDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const mode = Form.useWatch('mode', form)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api.settings.network()
      setCurrent(data)
      form.setFieldsValue({ mode: data.mode, port: data.port, allow: data.allow })
    } catch {
      message.error('Не удалось загрузить сетевые настройки')
    } finally {
      setLoading(false)
    }
  }, [form, message])

  useEffect(() => {
    load()
  }, [load])

  const save = async (values: FormValues) => {
    setSaving(true)
    try {
      const { url } = await api.settings.updateNetwork({ ...values, allow: values.allow ?? [] })
      message.success(`Настройки сохранены. Переход на ${url}`)
      setTimeout(() => window.location.assign(url + window.location.pathname), 1500)
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить сетевые настройки')
      setSaving(false)
    }
  }

  const handleFinish = (values: FormValues) => {
    modal.confirm({
      title: 'Применить сетевые настройки?',
      content:
        'Панель переедет на новый адрес, войти потребуется заново. Если вы подключены не с этого компьютера, доступ может пропасть.',
      okText: 'Применить',
      cancelText: 'Отмена',
      onOk: () => save(values),
    })
  }

  return (
    <Card
      loading={loading}
      title={
        <Space>
          <GlobalOutlined />
          <span>Сеть</span>
        </Space>
      }
      style={{ maxWidth: 720, marginBottom: 16 }}
    >
      <Paragraph type="secondary" style={{ marginTop: 0 }}>
        Откуда доступна панель. Изменения применяются без перезапуска службы. Текущий адрес:{' '}
        <Text code>{current?.url}</Text>
      </Paragraph>

      <Form form={form} layout="vertical" onFinish={handleFinish} requiredMark={false}>
        <Form.Item name="mode" label="Доступ">
          <Radio.Group>
            <Radio.Button value="Local">Только этот компьютер</Radio.Button>
            <Radio.Button value="Network">Сеть</Radio.Button>
          </Radio.Group>
        </Form.Item>

        <Form.Item
          name="port"
          label="Порт"
          rules={[{ required: true, message: 'Укажите порт' }]}
        >
          <InputNumber min={1} max={65535} style={{ width: 160 }} />
        </Form.Item>

        {mode === 'Network' && (
          <>
            <Form.Item
              name="allow"
              label="Разрешённые адреса и подсети"
              extra="Например 10.77.77.0/24 или 192.168.88.5. Остальным брандмауэр закроет доступ."
              rules={[{ required: true, type: 'array', min: 1, message: 'Укажите хотя бы одну подсеть' }]}
            >
              <Select mode="tags" tokenSeparators={[',', ' ']} placeholder="10.0.0.0/24" open={false} />
            </Form.Item>
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 16 }}
              message="Трафик не шифруется (HTTP). Используйте режим «Сеть» только в доверенной сети."
            />
          </>
        )}

        <Form.Item style={{ marginBottom: 0 }}>
          <Button type="primary" htmlType="submit" loading={saving}>
            Сохранить
          </Button>
        </Form.Item>
      </Form>
    </Card>
  )
}
