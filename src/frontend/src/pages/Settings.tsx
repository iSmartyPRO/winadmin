import { useCallback, useEffect, useState } from 'react'
import { App, Button, Card, Empty, Form, Input, List, Popconfirm, Space, Tag, Typography } from 'antd'
import { DeleteOutlined, PlusOutlined, UserDeleteOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import type { ExcludedUserDto } from '../api/types'
import PageHeader from '../components/PageHeader'
import NetworkSettingsCard from '../components/NetworkSettingsCard'
import DirectorySettingsCard from '../components/DirectorySettingsCard'
import AdStructureCard from '../components/AdStructureCard'
import { useAuth } from '../auth/AuthProvider'

const { Text, Paragraph } = Typography

export default function Settings() {
  const { message } = App.useApp()
  const [excluded, setExcluded] = useState<ExcludedUserDto[]>([])
  const [loading, setLoading] = useState(true)
  const [adding, setAdding] = useState(false)
  const [form] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setExcluded(await api.settings.excludedUsers())
    } catch {
      message.error('Не удалось загрузить список исключений')
    } finally {
      setLoading(false)
    }
  }, [message])

  const { can, moduleOn } = useAuth()
  const canExcluded = can('eventlogs.manage') && moduleOn('eventlogs')

  useEffect(() => {
    if (!canExcluded) {
      setLoading(false)
      return
    }
    load()
  }, [load, canExcluded])

  const handleAdd = async (values: { userName: string }) => {
    setAdding(true)
    try {
      await api.settings.addExcludedUser(values.userName)
      message.success('Учётная запись добавлена в исключения')
      form.resetFields()
      load()
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось добавить')
    } finally {
      setAdding(false)
    }
  }

  const handleRemove = async (item: ExcludedUserDto) => {
    try {
      await api.settings.removeExcludedUser(item.id)
      message.success('Удалено из исключений')
      load()
    } catch {
      message.error('Не удалось удалить')
    }
  }

  return (
    <div>
      <PageHeader
        title="Настройки"
        subtitle="Сетевой доступ к панели и учётные записи, скрываемые из журналов"
        onRefresh={canExcluded ? load : undefined}
        loading={loading}
      />

      {can('platform.network.manage') && <NetworkSettingsCard />}
      {can('platform.directory.manage') && <DirectorySettingsCard />}
      {can('platform.directory.manage') && <AdStructureCard />}

      {canExcluded && (
      <Card
        title={
          <Space>
            <UserDeleteOutlined />
            <span>Исключённые учётные записи</span>
          </Space>
        }
        style={{ maxWidth: 720 }}
      >
        <Paragraph type="secondary" style={{ marginTop: 0 }}>
          Эти учётные записи полностью скрываются из всех журналов Windows (например
          служебные или технические аккаунты вроде <Text code>superadmin</Text>, за которыми
          не нужно наблюдать). Фильтр применяется на сервере, сравнение регистронезависимое и
          учитывает как короткое имя, так и вариант <Text code>DOMAIN\имя</Text>.
        </Paragraph>

        <Form form={form} layout="inline" onFinish={handleAdd} style={{ marginBottom: 20 }}>
          <Form.Item
            name="userName"
            rules={[{ required: true, message: 'Укажите имя учётной записи' }]}
            style={{ flex: 1, marginInlineEnd: 8 }}
          >
            <Input placeholder="Имя учётной записи, например superadmin" allowClear />
          </Form.Item>
          <Form.Item style={{ marginInlineEnd: 0 }}>
            <Button type="primary" htmlType="submit" icon={<PlusOutlined />} loading={adding}>
              Добавить
            </Button>
          </Form.Item>
        </Form>

        {excluded.length === 0 ? (
          <Empty description="Список исключений пуст" image={Empty.PRESENTED_IMAGE_SIMPLE} />
        ) : (
          <List
            loading={loading}
            dataSource={excluded}
            renderItem={(item) => (
              <List.Item
                actions={[
                  <Popconfirm
                    key="del"
                    title="Убрать из исключений?"
                    description="Записи этой учётной записи снова начнут показываться в журналах."
                    okText="Убрать"
                    cancelText="Отмена"
                    onConfirm={() => handleRemove(item)}
                  >
                    <Button size="small" danger icon={<DeleteOutlined />} />
                  </Popconfirm>,
                ]}
              >
                <Space>
                  <Tag color="volcano">{item.userName}</Tag>
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    добавлено {new Date(item.createdAt).toLocaleDateString('ru')}
                  </Text>
                </Space>
              </List.Item>
            )}
          />
        )}
      </Card>
      )}
    </div>
  )
}
