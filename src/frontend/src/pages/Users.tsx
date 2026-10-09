import { useEffect, useState, useCallback } from 'react'
import { Button, Modal, Form, Input, Select, Space, Tag, Typography, App, Popconfirm } from 'antd'
import { PlusOutlined, EditOutlined, DeleteOutlined, StopOutlined, PlayCircleOutlined, SafetyCertificateOutlined } from '@ant-design/icons'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef } from 'ag-grid-community'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthProvider'
import type { RoleAssignmentDto, RoleDto, UserDto } from '../api/types'

const { Title } = Typography

function errorText(e: any, fallback: string) {
  const d = e?.response?.data
  return d?.violations?.length ? `${d.message} ${d.violations.join('; ')}` : d?.message ?? fallback
}

export default function Users() {
  const { message } = App.useApp()
  const { can, reload } = useAuth()
  const canGrant = can('platform.roles.manage')
  const [users, setUsers] = useState<UserDto[]>([])
  const [roles, setRoles] = useState<RoleDto[]>([])
  const [loading, setLoading] = useState(true)

  const [createOpen, setCreateOpen] = useState(false)
  const [rolesOpen, setRolesOpen] = useState(false)
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [selected, setSelected] = useState<UserDto | null>(null)
  const [userAssignments, setUserAssignments] = useState<RoleAssignmentDto[]>([])

  const [createForm] = Form.useForm()
  const [rolesForm] = Form.useForm()
  const [passwordForm] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [u, r] = await Promise.all([api.users(), api.roles.list().catch(() => [] as RoleDto[])])
      setUsers(u)
      setRoles(r)
    } catch {
      message.error('Ошибка загрузки')
    } finally {
      setLoading(false)
    }
  }, [message])

  useEffect(() => { load() }, [load])

  const roleOptions = roles.map((r) => ({ value: r.id, label: r.name }))

  const handleCreate = async (values: { login: string; password: string; roleIds: string[] }) => {
    try {
      await api.createUser({ ...values, roleIds: values.roleIds ?? [] })
      message.success('Пользователь создан')
      setCreateOpen(false)
      createForm.resetFields()
      load()
    } catch (e) { message.error(errorText(e, 'Ошибка')) }
  }

  const openRoles = async (user: UserDto) => {
    setSelected(user)
    try {
      const list = await api.assignments.list({ principalType: 'LocalUser', principalId: user.id })
      setUserAssignments(list)
      rolesForm.setFieldsValue({ roleIds: list.map((a) => a.roleId) })
      setRolesOpen(true)
    } catch (e) { message.error(errorText(e, 'Нет доступа к ролям')) }
  }

  const saveRoles = async (values: { roleIds: string[] }) => {
    if (!selected) return
    try {
      const current = new Set(userAssignments.map((a) => a.roleId))
      const wanted = new Set(values.roleIds)
      for (const a of userAssignments.filter((x) => !wanted.has(x.roleId))) await api.assignments.remove(a.id)
      for (const id of [...wanted].filter((x) => !current.has(x))) await api.assignments.create(id, 'LocalUser', selected.id)
      message.success('Роли обновлены')
      setRolesOpen(false)
      reload()
    } catch (e) {
      message.error(errorText(e, 'Не удалось изменить роли'))
    } finally {
      load()
    }
  }

  const handlePassword = async (values: { newPassword: string }) => {
    if (!selected) return
    try {
      await api.changeUserPassword(selected.id, values.newPassword)
      message.success('Пароль изменён')
      setPasswordOpen(false)
      passwordForm.resetFields()
    } catch (e) { message.error(errorText(e, 'Ошибка')) }
  }

  const handleToggleActive = async (user: UserDto) => {
    try {
      await api.setUserActive(user.id, !user.isActive)
      message.success(user.isActive ? 'Пользователь деактивирован' : 'Пользователь активирован')
      load()
    } catch (e) { message.error(errorText(e, 'Ошибка')) }
  }

  const handleDelete = async (user: UserDto) => {
    try {
      await api.deleteUser(user.id)
      message.success('Пользователь удалён')
      load()
    } catch (e) { message.error(errorText(e, 'Ошибка')) }
  }

  const cols: ColDef<UserDto>[] = [
    { field: 'login', headerName: 'Логин', flex: 1 },
    {
      field: 'roles', headerName: 'Роли', flex: 2,
      cellRenderer: ({ value }: { value: string[] }) => (
        <Space wrap size={4}>
          {value.length === 0
            ? <Tag>нет ролей</Tag>
            : value.map((r) => <Tag key={r} color={r === 'Администратор' ? 'red' : 'blue'}>{r}</Tag>)}
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
      headerName: 'Действия', width: 230, sortable: false,
      cellRenderer: ({ data }: { data: UserDto }) => (
        <Space>
          {canGrant && <Button size="small" icon={<SafetyCertificateOutlined />} onClick={() => openRoles(data)}>Роли</Button>}
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

      <Modal title="Создать пользователя" open={createOpen} onCancel={() => setCreateOpen(false)} footer={null}>
        <Form form={createForm} layout="vertical" onFinish={handleCreate}>
          <Form.Item name="login" label="Логин" rules={[{ required: true }]}>
            <Input placeholder="operator" />
          </Form.Item>
          <Form.Item name="password" label="Пароль" rules={[{ required: true, min: 6 }]}>
            <Input.Password />
          </Form.Item>
          {canGrant && (
            <Form.Item name="roleIds" label="Роли" initialValue={[]}>
              <Select mode="multiple" options={roleOptions} placeholder="Выберите роли" />
            </Form.Item>
          )}
          <Button type="primary" htmlType="submit" block>Создать</Button>
        </Form>
      </Modal>

      <Modal title={`Роли — ${selected?.login}`} open={rolesOpen} onCancel={() => setRolesOpen(false)} footer={null}>
        <Form form={rolesForm} layout="vertical" onFinish={saveRoles}>
          <Form.Item name="roleIds" label="Роли">
            <Select mode="multiple" options={roleOptions} />
          </Form.Item>
          <Button type="primary" htmlType="submit" block>Сохранить</Button>
        </Form>
      </Modal>

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
