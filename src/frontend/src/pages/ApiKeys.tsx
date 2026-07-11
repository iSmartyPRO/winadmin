import { useState } from 'react'
import {
  Alert, Button, Card, DatePicker, Form, Input, Modal, Popconfirm, Select,
  Space, Table, Tag, Typography, App,
} from 'antd'
import { DeleteOutlined, PlusOutlined, CopyOutlined, KeyOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import type { Dayjs } from 'dayjs'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { ApiKeyDto } from '../api/types'
import PageHeader from '../components/PageHeader'
import { formatDateTime } from '../utils/format'

const { Text } = Typography

export default function ApiKeys() {
  const { data, loading, refresh } = useApi(api.apiKeys)
  const { data: scopes } = useApi(api.availableScopes)
  const { message } = App.useApp()
  const [form] = Form.useForm()
  const [open, setOpen] = useState(false)
  const [creating, setCreating] = useState(false)
  const [newSecret, setNewSecret] = useState<string>()

  const submit = async () => {
    const v = await form.validateFields() as { name: string; scopes: string[]; expiresAt?: Dayjs }
    setCreating(true)
    try {
      const created = await api.createKey(v.name, v.scopes, v.expiresAt?.toISOString())
      setNewSecret(created.plaintextKey)
      setOpen(false)
      form.resetFields()
      await refresh(true)
    } catch (e) {
      message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Не удалось создать ключ')
    } finally {
      setCreating(false)
    }
  }

  const revoke = async (id: string) => {
    try {
      await api.revokeKey(id)
      message.success('Ключ отозван')
      await refresh(true)
    } catch {
      message.error('Не удалось отозвать ключ')
    }
  }

  const copy = (text: string) => {
    navigator.clipboard.writeText(text)
    message.success('Скопировано в буфер обмена')
  }

  const columns: ColumnsType<ApiKeyDto> = [
    {
      title: 'Имя', dataIndex: 'name',
      render: (name: string, r) => (
        <Space>
          <KeyOutlined style={{ color: '#7aa0ff' }} />
          <span>{name}</span>
          {r.hint && <Text type="secondary" code>…{r.hint}</Text>}
        </Space>
      ),
    },
    {
      title: 'Scopes', dataIndex: 'scopes',
      render: (s: string[]) => (
        <Space size={[4, 4]} wrap>
          {s.map((x) => <Tag key={x} color={x === 'admin' ? 'gold' : 'blue'}>{x}</Tag>)}
        </Space>
      ),
    },
    { title: 'Создан', dataIndex: 'createdAt', render: (d) => formatDateTime(d), width: 170 },
    { title: 'Использован', dataIndex: 'lastUsedAt', render: (d) => formatDateTime(d), width: 170 },
    {
      title: 'Статус', dataIndex: 'isRevoked', width: 110,
      render: (revoked: boolean) => revoked ? <Tag color="error">Отозван</Tag> : <Tag color="success">Активен</Tag>,
    },
    {
      title: '', width: 60,
      render: (_, r) => !r.isRevoked && (
        <Popconfirm title="Отозвать ключ?" description="Системы с этим ключом потеряют доступ." onConfirm={() => revoke(r.id)} okText="Отозвать" okButtonProps={{ danger: true }} cancelText="Отмена">
          <Button type="text" danger icon={<DeleteOutlined />} />
        </Popconfirm>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title="API-ключи"
        subtitle="Доступ внешних систем к API"
        onRefresh={refresh}
        loading={loading}
        extra={<Button type="primary" icon={<PlusOutlined />} onClick={() => setOpen(true)}>Создать ключ</Button>}
      />

      <Card variant="borderless" className="sp-glass">
        <Table rowKey="id" columns={columns} dataSource={data ?? []} loading={loading} pagination={false} />
      </Card>

      <Modal title="Новый API-ключ" open={open} onOk={submit} confirmLoading={creating} onCancel={() => setOpen(false)} okText="Создать">
        <Form form={form} layout="vertical" initialValues={{ scopes: [] }}>
          <Form.Item name="name" label="Имя" rules={[{ required: true, message: 'Укажите имя' }]}>
            <Input placeholder="Например: monitoring-system" />
          </Form.Item>
          <Form.Item name="scopes" label="Права (scopes)" rules={[{ required: true, message: 'Выберите хотя бы один scope' }]}>
            <Select mode="multiple" placeholder="Выберите scopes"
              options={(scopes ?? []).map((s) => ({ label: s, value: s }))} />
          </Form.Item>
          <Form.Item name="expiresAt" label="Срок действия (опционально)">
            <DatePicker showTime style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="Ключ создан"
        open={Boolean(newSecret)}
        onCancel={() => setNewSecret(undefined)}
        footer={[<Button key="ok" type="primary" onClick={() => setNewSecret(undefined)}>Готово</Button>]}
      >
        <Alert type="warning" showIcon style={{ marginBottom: 12 }}
          message="Сохраните ключ сейчас — он показывается только один раз." />
        <Space.Compact style={{ display: 'flex', width: '100%' }}>
          <Input readOnly value={newSecret} />
          <Button icon={<CopyOutlined />} onClick={() => newSecret && copy(newSecret)}>Копировать</Button>
        </Space.Compact>
      </Modal>
    </>
  )
}
