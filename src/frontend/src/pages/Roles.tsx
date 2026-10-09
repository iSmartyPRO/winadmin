import { useMemo, useState } from 'react'
import {
  Alert, App, Button, Card, Drawer, Empty, Form, Input, List, Popconfirm, Select, Space, Table, Tag, Tree, Typography,
} from 'antd'
import type { DataNode } from 'antd/es/tree'
import { DeleteOutlined, EditOutlined, PlusOutlined, TeamOutlined } from '@ant-design/icons'
import { useApi } from '../hooks/useApi'
import { api } from '../api/client'
import type { PermissionGroupDto, PrincipalType, RoleAssignmentDto, RoleDto, RoleGrantDto } from '../api/types'
import PageHeader from '../components/PageHeader'

const { Text } = Typography

interface EditorValues { name: string; description?: string }

function errorText(e: any, fallback: string) {
  const d = e?.response?.data
  return d?.violations?.length ? `${d.message} ${d.violations.join('; ')}` : d?.message ?? fallback
}

function RoleEditor({ role, groups, open, onClose, onSaved }: {
  role?: RoleDto; groups: PermissionGroupDto[]; open: boolean; onClose: () => void; onSaved: () => void
}) {
  const { message } = App.useApp()
  const [form] = Form.useForm<EditorValues>()
  const [checked, setChecked] = useState<string[]>([])
  const [scopes, setScopes] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)
  const moduleOf = (permId: string) => groups.find((g) => g.permissions.some((p) => p.id === permId))

  const reset = () => {
    form.setFieldsValue({ name: role?.name ?? '', description: role?.description })
    setChecked(role?.permissions.map((p) => p.permissionId) ?? [])
    const s: Record<string, string[]> = {}
    for (const p of role?.permissions ?? []) {
      const g = moduleOf(p.permissionId)
      if (g?.scopable && p.scope) s[g.id] = Array.from(new Set([...(s[g.id] ?? []), ...p.scope]))
    }
    setScopes(s)
  }

  const treeData: DataNode[] = groups.map((g) => ({
    key: `group:${g.id}`,
    title: g.title,
    children: g.permissions.map((p) => ({
      key: p.id,
      title: <span>{p.title} {p.dangerous && <Tag color="volcano" style={{ marginLeft: 4 }}>опасное</Tag>}</span>,
    })),
  }))

  const save = async () => {
    const values = await form.validateFields()
    const permissions: RoleGrantDto[] = checked.filter((k) => !k.startsWith('group:')).map((id) => {
      const g = moduleOf(id)
      const p = g?.permissions.find((x) => x.id === id)
      const scope = g?.scopable && p?.scopable && scopes[g.id]?.length ? scopes[g.id] : null
      return { permissionId: id, scope }
    })
    setSaving(true)
    try {
      if (role) await api.roles.update(role.id, { ...values, permissions })
      else await api.roles.create({ ...values, permissions })
      message.success('Роль сохранена')
      onSaved()
      onClose()
    } catch (e) {
      message.error(errorText(e, 'Не удалось сохранить роль'))
    } finally {
      setSaving(false)
    }
  }

  const scopedGroups = groups.filter((g) => g.scopable && checked.some((k) => g.permissions.some((p) => p.id === k)))

  return (
    <Drawer
      title={role ? `Роль «${role.name}»` : 'Новая роль'}
      open={open}
      onClose={onClose}
      width={560}
      afterOpenChange={(v) => v && reset()}
      extra={<Button type="primary" onClick={save} loading={saving}>Сохранить</Button>}
    >
      <Form form={form} layout="vertical">
        <Form.Item name="name" label="Название" rules={[{ required: true, message: 'Укажите название' }, { max: 100 }]}>
          <Input placeholder="Например: Кадры ТЕХНО-ЦЕНТР" />
        </Form.Item>
        <Form.Item name="description" label="Описание">
          <Input.TextArea rows={2} />
        </Form.Item>
      </Form>
      <Text strong>Права</Text>
      <Tree
        checkable
        defaultExpandAll
        selectable={false}
        treeData={treeData}
        checkedKeys={checked}
        onCheck={(keys) => setChecked((Array.isArray(keys) ? keys : keys.checked).map(String))}
        style={{ margin: '8px 0 16px' }}
      />
      {scopedGroups.map((g) => (
        <div key={g.id} style={{ marginBottom: 12 }}>
          <Text>{g.scopeTitle ?? 'Область'} — {g.title}</Text>
          <Select
            mode="tags"
            open={false}
            style={{ width: '100%', marginTop: 4 }}
            placeholder="Пусто — без ограничений"
            value={scopes[g.id] ?? []}
            onChange={(v) => setScopes((s) => ({ ...s, [g.id]: v }))}
          />
        </div>
      ))}
    </Drawer>
  )
}

function AssignmentsDrawer({ role, open, onClose, onChanged }: {
  role?: RoleDto; open: boolean; onClose: () => void; onChanged: () => void
}) {
  const { message } = App.useApp()
  const [items, setItems] = useState<RoleAssignmentDto[]>([])
  const [type, setType] = useState<PrincipalType>('LocalUser')
  const [principal, setPrincipal] = useState<string>()
  const { data: users } = useApi(api.users)
  const { data: keys } = useApi(api.apiKeys)

  const load = async () => role && setItems(await api.assignments.list({ roleId: role.id }))

  const add = async () => {
    if (!role || !principal) return
    try {
      await api.assignments.create(role.id, type, principal)
      setPrincipal(undefined)
      await load()
      onChanged()
    } catch (e) {
      message.error(errorText(e, 'Не удалось назначить роль'))
    }
  }

  const remove = async (a: RoleAssignmentDto) => {
    try {
      await api.assignments.remove(a.id)
      await load()
      onChanged()
    } catch (e) {
      message.error(errorText(e, 'Не удалось снять роль'))
    }
  }

  const options = type === 'LocalUser'
    ? (users ?? []).map((u) => ({ value: u.id, label: u.login }))
    : (keys ?? []).filter((k) => !k.isRevoked).map((k) => ({ value: k.id, label: k.name }))

  return (
    <Drawer title={`Кому назначена «${role?.name ?? ''}»`} open={open} onClose={onClose} width={520} afterOpenChange={(v) => v && load()}>
      <Space.Compact style={{ width: '100%', marginBottom: 16 }}>
        <Select value={type} style={{ width: 190 }} onChange={(v) => { setType(v); setPrincipal(undefined) }}
          options={[{ value: 'LocalUser', label: 'Пользователь WinAdmin' }, { value: 'ApiKey', label: 'API-ключ' }]} />
        <Select value={principal} onChange={setPrincipal} options={options} showSearch optionFilterProp="label"
          placeholder="Выберите" style={{ flex: 1 }} />
        <Button type="primary" icon={<PlusOutlined />} onClick={add} disabled={!principal}>Назначить</Button>
      </Space.Compact>
      <Alert type="info" showIcon style={{ marginBottom: 12 }} message="Пользователи и группы Active Directory появятся после настройки подключения к домену." />
      {items.length === 0 ? <Empty description="Роль никому не назначена" /> : (
        <List
          dataSource={items}
          renderItem={(a) => (
            <List.Item actions={[
              <Popconfirm key="del" title="Снять роль?" onConfirm={() => remove(a)} okText="Снять" cancelText="Отмена">
                <Button size="small" danger icon={<DeleteOutlined />} />
              </Popconfirm>,
            ]}>
              <Space>
                <Tag>{a.principalType === 'LocalUser' ? 'пользователь' : a.principalType === 'ApiKey' ? 'API-ключ' : a.principalType}</Tag>
                {a.displayName}
              </Space>
            </List.Item>
          )}
        />
      )}
    </Drawer>
  )
}

export default function Roles() {
  const { data, loading, refresh } = useApi(api.roles.list)
  const { data: groups } = useApi(api.permissions)
  const { message } = App.useApp()
  const [editing, setEditing] = useState<RoleDto | undefined>()
  const [editorOpen, setEditorOpen] = useState(false)
  const [assigning, setAssigning] = useState<RoleDto | undefined>()

  const titleOf = useMemo(() => {
    const map = new Map<string, string>()
    for (const g of groups ?? []) for (const p of g.permissions) map.set(p.id, `${g.title}: ${p.title}`)
    return (id: string) => map.get(id) ?? id
  }, [groups])

  const remove = async (r: RoleDto) => {
    try {
      await api.roles.remove(r.id)
      message.success('Роль удалена')
      await refresh(true)
    } catch (e) {
      message.error(errorText(e, 'Не удалось удалить роль'))
    }
  }

  return (
    <>
      <PageHeader
        title="Роли"
        subtitle="Наборы прав по модулям; выдавать можно только права, которые есть у вас самих"
        onRefresh={refresh}
        loading={loading}
        extra={<Button type="primary" icon={<PlusOutlined />} onClick={() => { setEditing(undefined); setEditorOpen(true) }}>Создать роль</Button>}
      />
      <Card variant="borderless" className="sp-glass">
        <Table<RoleDto>
          rowKey="id"
          loading={loading}
          dataSource={data ?? []}
          pagination={false}
          columns={[
            {
              title: 'Роль', dataIndex: 'name',
              render: (name: string, r) => (
                <Space direction="vertical" size={0}>
                  <Space>{name}{r.isBuiltin && <Tag color="gold">встроенная</Tag>}</Space>
                  {r.description && <Text type="secondary" style={{ fontSize: 12 }}>{r.description}</Text>}
                </Space>
              ),
            },
            {
              title: 'Права',
              render: (_, r) => r.isBuiltin ? <Tag color="red">все права</Tag> : (
                <Space size={[4, 4]} wrap>
                  {r.permissions.map((p) => (
                    <Tag key={p.permissionId} color={p.scope ? 'purple' : 'blue'}>
                      {titleOf(p.permissionId)}{p.scope ? ` (${p.scope.length} обл.)` : ''}
                    </Tag>
                  ))}
                </Space>
              ),
            },
            { title: 'Назначена', dataIndex: 'assignmentCount', width: 110 },
            {
              title: '', width: 170,
              render: (_, r) => (
                <Space>
                  <Button size="small" icon={<TeamOutlined />} onClick={() => setAssigning(r)}>Кому</Button>
                  {!r.isBuiltin && <Button size="small" icon={<EditOutlined />} onClick={() => { setEditing(r); setEditorOpen(true) }} />}
                  {!r.isBuiltin && (
                    <Popconfirm title="Удалить роль?" description="Все её назначения будут сняты." onConfirm={() => remove(r)} okText="Удалить" okButtonProps={{ danger: true }} cancelText="Отмена">
                      <Button size="small" danger icon={<DeleteOutlined />} />
                    </Popconfirm>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>
      <RoleEditor role={editing} groups={groups ?? []} open={editorOpen} onClose={() => setEditorOpen(false)} onSaved={() => refresh(true)} />
      <AssignmentsDrawer role={assigning} open={Boolean(assigning)} onClose={() => setAssigning(undefined)} onChanged={() => refresh(true)} />
    </>
  )
}
