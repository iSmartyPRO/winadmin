import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Alert, App, Avatar, Button, Card, Checkbox, Col, Descriptions, Drawer, Empty, Form, Input, List, Modal,
  Popconfirm, Row, Segmented, Select, Space, Table, Tabs, Tag, Typography, Upload,
} from 'antd'
import { CopyOutlined, ReloadOutlined, UserOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import { api } from '../api/client'
import type { AdProject, AdUserCard, AdUserStatus, AdUserView, AuditEntryDto, ScenarioStep, UserFolderAccess } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import PageHeader from '../components/PageHeader'

const ATTRS: [string, string][] = [
  ['displayName', 'Отображаемое имя'], ['givenName', 'Имя'], ['sn', 'Фамилия'], ['mail', 'Почта'],
  ['department', 'Отдел'], ['title', 'Должность'], ['telephoneNumber', 'Телефон'],
  ['physicalDeliveryOfficeName', 'Офис'], ['company', 'Компания'], ['description', 'Описание'],
]

const errorText = (e: any, fallback: string) => e?.response?.data?.message ?? fallback

function Steps({ steps }: { steps: ScenarioStep[] }) {
  const color = { Ok: 'green', Skipped: 'default', Failed: 'red' } as const
  return (
    <List size="small" dataSource={steps} renderItem={(s) => (
      <List.Item><Space><Tag color={color[s.status]}>{s.status === 'Ok' ? 'ок' : s.status === 'Skipped' ? 'пропущен' : 'ошибка'}</Tag>{s.name}<Typography.Text type="secondary">{s.message}</Typography.Text></Space></List.Item>
    )} />
  )
}

/** Вписать изображение в 256×256 и закодировать JPEG (≈ 20–40 КБ). */
async function toJpeg(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file)
  const scale = Math.min(1, 256 / Math.max(bitmap.width, bitmap.height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(bitmap.width * scale)
  canvas.height = Math.round(bitmap.height * scale)
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  return new Promise((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('jpeg'))), 'image/jpeg', 0.85))
}

function UserCard({ sam, projects, onClose, onChanged }: {
  sam?: string; projects: AdProject[]; onClose: () => void; onChanged: () => void
}) {
  const { message, modal } = App.useApp()
  const { can, moduleOn } = useAuth()
  const [card, setCard] = useState<AdUserCard>()
  const [folderAccess, setFolderAccess] = useState<UserFolderAccess[]>()
  const [photoUrl, setPhotoUrl] = useState<string>()
  const [history, setHistory] = useState<AuditEntryDto[]>([])
  const [form] = Form.useForm()
  const [busy, setBusy] = useState<string>()
  const [steps, setSteps] = useState<ScenarioStep[]>()
  const [target, setTarget] = useState<string>()

  const load = useCallback(async () => {
    if (!sam) return
    try {
      const c = await api.ad.user(sam)
      setCard(c)
      form.setFieldsValue(c.user.attributes)
      setHistory(await api.ad.history(sam).catch(() => []))
      if (moduleOn('ad-folders') && can('ad-folders.read')) setFolderAccess(await api.folders.userAccess(sam).catch(() => []))
      if (c.user.hasPhoto) setPhotoUrl(URL.createObjectURL(await api.ad.photo(sam)))
      else setPhotoUrl(undefined)
    } catch (e) {
      message.error(errorText(e, 'Не удалось загрузить пользователя'))
    }
  }, [sam, form, message, moduleOn, can])

  useEffect(() => { setSteps(undefined); setCard(undefined); load() }, [load])

  const run = async (key: string, action: () => Promise<void>) => {
    setBusy(key)
    try { await action(); onChanged(); await load() } catch (e) { message.error(errorText(e, 'Операция не выполнена')) } finally { setBusy(undefined) }
  }

  const showPassword = (password: string) => modal.info({
    title: 'Новый пароль — показывается один раз',
    content: <Space><Typography.Text code copyable={{ icon: <CopyOutlined /> }}>{password}</Typography.Text></Space>,
  })

  const u = card?.user
  return (
    <Drawer title={u?.displayName ?? sam} open={Boolean(sam)} onClose={onClose} width={720}>
      {!u ? <Empty /> : (
        <>
          <Space align="start" style={{ marginBottom: 16 }}>
            <Avatar size={72} src={photoUrl} icon={<UserOutlined />} />
            <Descriptions size="small" column={1} items={[
              { key: 'sam', label: 'Логин', children: u.sam },
              { key: 'p', label: 'Проект', children: u.terminated ? <Tag color="red">уволен</Tag> : u.projectName },
              { key: 's', label: 'Состояние', children: u.enabled ? <Tag color="green">активен</Tag> : <Tag>отключён</Tag> },
              { key: 'l', label: 'Последний вход', children: u.lastLogon ? new Date(u.lastLogon).toLocaleString('ru-RU') : '—' },
            ]} />
          </Space>
          {steps && <Alert type="info" style={{ marginBottom: 12 }} message="Результат" description={<Steps steps={steps} />} closable onClose={() => setSteps(undefined)} />}
          <Tabs items={[
            {
              key: 'attrs', label: 'Атрибуты', children: (
                <Form form={form} layout="vertical" disabled={!can('ad-users.edit') || u.terminated}
                  onFinish={(values) => run('attrs', async () => { await api.ad.updateAttributes(u.sam, values); message.success('Сохранено') })}>
                  <Row gutter={12}>
                    {ATTRS.map(([k, label]) => (
                      <Col span={k === 'description' ? 24 : 12} key={k}><Form.Item name={k} label={label}><Input /></Form.Item></Col>
                    ))}
                  </Row>
                  {can('ad-users.edit') && !u.terminated && (
                    <Space wrap>
                      <Button type="primary" htmlType="submit" loading={busy === 'attrs'}>Сохранить</Button>
                      <Upload accept="image/*" showUploadList={false} beforeUpload={(file) => {
                        run('photo', async () => { await api.ad.setPhoto(u.sam, await toJpeg(file)); message.success('Фото обновлено') })
                        return false
                      }}><Button loading={busy === 'photo'}>Загрузить фото</Button></Upload>
                      {u.hasPhoto && <Popconfirm title="Удалить фото?" onConfirm={() => run('photo', async () => { await api.ad.removePhoto(u.sam) })}><Button danger>Удалить фото</Button></Popconfirm>}
                    </Space>
                  )}
                </Form>
              ),
            },
            { key: 'groups', label: `Группы (${card.groups.length})`, children: (
              <List size="small" dataSource={card.groups} renderItem={(g) => (
                <List.Item><Space>{g.name}{g.isPrimary && <Tag>основная</Tag>}</Space></List.Item>
              )} />
            ) },
            { key: 'actions', label: 'Действия', children: (
              <Space direction="vertical" style={{ width: '100%' }}>
                {can('ad-users.move') && !u.terminated && (
                  <Space.Compact style={{ width: '100%' }}>
                    <Select style={{ flex: 1 }} placeholder="Перенести в проект" value={target} onChange={setTarget}
                      options={projects.filter((p) => p.dn !== u.projectDn).map((p) => ({ value: p.dn, label: p.name }))} />
                    <Button disabled={!target} loading={busy === 'move'} onClick={() => run('move', async () => { await api.ad.move(u.sam, target!); message.success('Перенесён') })}>Перенести</Button>
                  </Space.Compact>
                )}
                {can('ad-users.password') && !u.terminated && (
                  <Space wrap>
                    <Popconfirm title="Сгенерировать новый пароль? Пользователь сменит его при входе."
                      onConfirm={() => run('pwd', async () => { const r = await api.ad.password(u.sam, { generate: true, mustChange: true }); if (r.password) showPassword(r.password) })}>
                      <Button loading={busy === 'pwd'}>Сгенерировать пароль</Button>
                    </Popconfirm>
                    <ManualPassword onSubmit={(password, mustChange) => run('pwd', async () => { await api.ad.password(u.sam, { password, generate: false, mustChange }); message.success('Пароль изменён') })} />
                  </Space>
                )}
                {can('ad-users.offboard') && !u.terminated && (
                  <Popconfirm title="Уволить пользователя?" description="Снять все группы, отключить, перенести в OU уволенных."
                    okButtonProps={{ danger: true }} onConfirm={() => run('off', async () => setSteps(await api.ad.deactivate(u.sam)))}>
                    <Button danger loading={busy === 'off'}>Уволить</Button>
                  </Popconfirm>
                )}
                {can('ad-users.offboard') && (u.terminated || !u.enabled) && (
                  <Space.Compact style={{ width: '100%' }}>
                    <Select style={{ flex: 1 }} placeholder="Восстановить в проект" value={target} onChange={setTarget}
                      options={projects.map((p) => ({ value: p.dn, label: p.name }))} />
                    <Button type="primary" disabled={!target} loading={busy === 'on'}
                      onClick={() => run('on', async () => { const r = await api.ad.activate(u.sam, target!); setSteps(r.steps); if (r.password) showPassword(r.password) })}>Восстановить</Button>
                  </Space.Compact>
                )}
              </Space>
            ) },
            ...(folderAccess ? [{ key: 'folders', label: `Папки (${folderAccess.length})`, children: (
              <List size="small" dataSource={folderAccess} locale={{ emptyText: 'Нет доступа к папкам' }} renderItem={(f) => (
                <List.Item><Space>{f.path}<Typography.Text type="secondary">{f.projectName}</Typography.Text>
                  {f.hasFull && <Tag color="green">Full</Tag>}{f.hasRead && <Tag color="blue">Read</Tag>}</Space></List.Item>
              )} />
            ) }] : []),
            { key: 'history', label: 'История', children: (
              <List size="small" dataSource={history} locale={{ emptyText: 'Нет записей' }} renderItem={(h) => (
                <List.Item><Space direction="vertical" size={0}>
                  <Space><Tag color={h.success ? 'green' : 'red'}>{h.action}</Tag><Typography.Text type="secondary">{new Date(h.timestamp).toLocaleString('ru-RU')} · {h.actor}</Typography.Text></Space>
                  <Typography.Text>{h.details}</Typography.Text>
                </Space></List.Item>
              )} />
            ) },
          ]} />
        </>
      )}
    </Drawer>
  )
}

function ManualPassword({ onSubmit }: { onSubmit: (password: string, mustChange: boolean) => void }) {
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm()
  return (
    <>
      <Button onClick={() => setOpen(true)}>Задать пароль</Button>
      <Modal title="Задать пароль" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} destroyOnHidden>
        <Form form={form} layout="vertical" initialValues={{ mustChange: true }}
          onFinish={(v) => { onSubmit(v.password, v.mustChange); setOpen(false); form.resetFields() }}>
          <Form.Item name="password" label="Пароль" rules={[{ required: true, min: 8, message: 'Не короче 8 символов' }]}><Input.Password autoComplete="new-password" /></Form.Item>
          <Form.Item name="confirm" label="Повтор" dependencies={['password']} rules={[{ required: true }, ({ getFieldValue }) => ({
            validator: (_, v) => (v === getFieldValue('password') ? Promise.resolve() : Promise.reject(new Error('Пароли не совпадают'))),
          })]}><Input.Password autoComplete="new-password" /></Form.Item>
          <Form.Item name="mustChange" valuePropName="checked"><Checkbox>Сменить при следующем входе</Checkbox></Form.Item>
        </Form>
      </Modal>
    </>
  )
}

export default function AdUsers() {
  const { message } = App.useApp()
  const { can } = useAuth()
  const [projects, setProjects] = useState<AdProject[]>([])
  const [project, setProject] = useState<string>()
  const [status, setStatus] = useState<AdUserStatus>('active')
  const [q, setQ] = useState('')
  const [users, setUsers] = useState<AdUserView[]>([])
  const [loading, setLoading] = useState(false)
  const [selected, setSelected] = useState<string>()

  useEffect(() => { api.ad.projects().then(setProjects).catch((e) => message.error(errorText(e, 'Не удалось загрузить проекты'))) }, [message])

  const load = useCallback(async () => {
    setLoading(true)
    try { setUsers(await api.ad.users({ project, status, q: q || undefined })) }
    catch (e) { message.error(errorText(e, 'Не удалось загрузить пользователей')) }
    finally { setLoading(false) }
  }, [project, status, q, message])

  useEffect(() => { load() }, [load])

  const columns: ColumnsType<AdUserView> = useMemo(() => [
    { title: 'Имя', dataIndex: 'displayName', render: (v, r) => v ?? r.sam, sorter: (a, b) => (a.displayName ?? a.sam).localeCompare(b.displayName ?? b.sam, 'ru') },
    { title: 'Логин', dataIndex: 'sam', width: 160 },
    { title: 'Должность', render: (_, r) => r.attributes.title },
    { title: 'Отдел', render: (_, r) => r.attributes.department },
    { title: 'Проект', dataIndex: 'projectName', width: 160, render: (v, r) => (r.terminated ? <Tag color="red">уволен</Tag> : v) },
    { title: 'Состояние', dataIndex: 'enabled', width: 110, render: (v) => (v ? <Tag color="green">активен</Tag> : <Tag>отключён</Tag>) },
  ], [])

  const statuses = [
    { value: 'active', label: 'Активные' }, { value: 'disabled', label: 'Отключённые' },
    ...(can('ad-users.offboard') ? [{ value: 'terminated', label: 'Уволенные' }] : []),
    { value: 'all', label: 'Все' },
  ]

  return (
    <>
      <PageHeader title="Пользователи AD" subtitle="Пользователи Active Directory по проектам"
        extra={<Button icon={<ReloadOutlined />} onClick={load}>Обновить</Button>} />
      <Card className="sp-glass">
        <Space wrap style={{ marginBottom: 12 }}>
          <Select allowClear placeholder="Все проекты" style={{ minWidth: 220 }} value={project} onChange={setProject}
            disabled={status === 'terminated'} options={projects.map((p) => ({ value: p.dn, label: p.name }))} />
          <Segmented value={status} onChange={(v) => setStatus(v as AdUserStatus)} options={statuses} />
          <Input.Search allowClear placeholder="ФИО, логин, почта" style={{ width: 280 }} onSearch={setQ} />
        </Space>
        <Table<AdUserView> rowKey="sam" size="small" loading={loading} dataSource={users} columns={columns}
          pagination={{ pageSize: 50, showSizeChanger: false }}
          onRow={(r) => ({ onClick: () => setSelected(r.sam), style: { cursor: 'pointer' } })} />
      </Card>
      <UserCard sam={selected} projects={projects} onClose={() => setSelected(undefined)} onChanged={load} />
    </>
  )
}
