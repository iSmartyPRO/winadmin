import { useEffect, useState, useCallback } from 'react'
import { Button, Modal, Form, Input, Select, Space, Tag, Typography, App, Popconfirm } from 'antd'
import { PlusOutlined, EditOutlined, DeleteOutlined, StopOutlined, PlayCircleOutlined } from '@ant-design/icons'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef } from 'ag-grid-community'
import { api } from '../api/client'
import type { UserDto } from '../api/types'

const { Title } = Typography

const ALL_SCOPES = [
  'system.read', 'disks.read',
  'services.read', 'services.manage',
  'processes.read', 'processes.manage',
  'printers.read', 'printers.manage',
  'power.manage', 'admin',
]

export default function Users() {
  const { message } = App.useApp()
  const [users, setUsers] = useState<UserDto[]>([])
  const [loading, setLoading] = useState(true)

  const [createOpen, setCreateOpen] = useState(false)
  const [scopesOpen, setScopesOpen] = useState(false)
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [selected, setSelected] = useState<UserDto | null>(null)

  const [createForm] = Form.useForm()
  const [scopesForm] = Form.useForm()
  const [passwordForm] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    try { setUsers(await api.users()) } catch { message.error('Ошибка загрузки') } finally { setLoading(false) }
  }, [message])

  useEffect(() => { load() }, [load])

  const handleCreate = async (values: { login: string; password: string; scopes: string[] }) => {
    try {
      await api.createUser(values)
      message.success('Пользователь создан')
      setCreateOpen(false)
      createForm.resetFields()
      load()
    } catch (e: any) { message.error(e?.response?.data?.message ?? 'Ошибка') }
  }

  const handleScopes = async (values: { scopes: string[] }) => {
    if (!selected) return
    await api.updateUserScopes(selected.id, values.scopes)
    message.success('Scopes обновлены')
    setScopesOpen(false)
    load()
  }

  const handlePassword = async (values: { newPassword: string }) => {
    if (!selected) return
    await api.changeUserPassword(selected.id, values.newPassword)
    message.success('Пароль изменён')
    setPasswordOpen(false)
    passwordForm.resetFields()
  }

  const handleToggleActive = async (user: UserDto) => {
    await api.setUserActive(user.id, !user.isActive)
    message.success(user.isActive ? 'Пользователь деактивирован' : 'Пользователь активирован')
    load()
  }

  const handleDelete = async (user: UserDto) => {
    await api.deleteUser(user.id)
    message.success('Пользователь удалён')
    load()
  }

  const cols: ColDef<UserDto>[] = [
    { field: 'login', headerName: 'Логин', flex: 1 },
    {
      field: 'scopes', headerName: 'Scopes', flex: 2,
      cellRenderer: ({ value }: { value: string[] }) => (
        <Space wrap size={4}>
          {value.map((s) => <Tag key={s} color={s === 'admin' ? 'red' : 'blue'}>{s}</Tag>)}
        </Space>
      ),
    },
    {
      field: 'createdAt', headerName: 'Создан', width: 130,
      valueFormatter: ({ value }) => new Date(value).toLocaleDateString('ru'),
    },
    {
      field: 'isActive', headerName: 'Статус', width: 100,
      cellRenderer: ({ value }: { value: boolean }) =>
        <Tag color={value ? 'green' : 'default'}>{value ? 'активен' : 'выкл'}</Tag>,
    },
    {
      headerName: 'Действия', width: 220, sortable: false,
      cellRenderer: ({ data }: { data: UserDto }) => (
        <Space>
          <Button size="small" icon={<EditOutlined />} onClick={() => {
            setSelected(data)
            scopesForm.setFieldsValue({ scopes: data.scopes })
            setScopesOpen(true)
          }}>Scopes</Button>
          <Button size="small" icon={<EditOutlined />} onClick={() => {
            setSelected(data)
            setPasswordOpen(true)
          }}>Пароль</Button>
          <Button size="small"
            icon={data.isActive ? <StopOutlined /> : <PlayCircleOutlined />}
            onClick={() => handleToggleActive(data)}
          />
          <Popconfirm title="Удалить пользователя?" onConfirm={() => handleDelete(data)} okText="Да" cancelText="Нет">
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>Пользователи</Title>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
          Создать
        </Button>
      </div>

      <div className="ag-theme-alpine-dark" style={{ height: 500 }}>
        <AgGridReact rowData={users} columnDefs={cols} loading={loading} rowHeight={48} />
      </div>

      {/* Создать */}
      <Modal title="Создать пользователя" open={createOpen} onCancel={() => setCreateOpen(false)} footer={null}>
        <Form form={createForm} layout="vertical" onFinish={handleCreate}>
          <Form.Item name="login" label="Логин" rules={[{ required: true }]}>
            <Input placeholder="admin" />
          </Form.Item>
          <Form.Item name="password" label="Пароль" rules={[{ required: true, min: 6 }]}>
            <Input.Password />
          </Form.Item>
          <Form.Item name="scopes" label="Scopes" initialValue={[]}>
            <Select mode="multiple" options={ALL_SCOPES.map((s) => ({ value: s, label: s }))} placeholder="Выберите права" />
          </Form.Item>
          <Button type="primary" htmlType="submit" block>Создать</Button>
        </Form>
      </Modal>

      {/* Scopes */}
      <Modal title={`Scopes — ${selected?.login}`} open={scopesOpen} onCancel={() => setScopesOpen(false)} footer={null}>
        <Form form={scopesForm} layout="vertical" onFinish={handleScopes}>
          <Form.Item name="scopes" label="Scopes">
            <Select mode="multiple" options={ALL_SCOPES.map((s) => ({ value: s, label: s }))} />
          </Form.Item>
          <Button type="primary" htmlType="submit" block>Сохранить</Button>
        </Form>
      </Modal>

      {/* Password */}
      <Modal title={`Пароль — ${selected?.login}`} open={passwordOpen} onCancel={() => setPasswordOpen(false)} footer={null}>
        <Form form={passwordForm} layout="vertical" onFinish={handlePassword}>
          <Form.Item name="newPassword" label="Новый пароль" rules={[{ required: true, min: 6 }]}>
            <Input.Password />
          </Form.Item>
          <Button type="primary" htmlType="submit" block>Сохранить</Button>
        </Form>
      </Modal>
    </div>
  )
}
