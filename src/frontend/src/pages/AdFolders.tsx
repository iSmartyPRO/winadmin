import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, App, Button, Card, Checkbox, Col, Drawer, Empty, Form, Input, List, Modal, Popconfirm, Row, Select, Space, Table, Tag, Typography } from 'antd'
import { DeleteOutlined, FolderAddOutlined, ReloadOutlined, SafetyOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import { api } from '../api/client'
import type { AdFolder, AdProject, FolderAclState, FolderGroupView, FolderPreview, ScenarioStep } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import PageHeader from '../components/PageHeader'

const errorText = (e: any, fallback: string) => e?.response?.data?.message ?? fallback

function Steps({ steps }: { steps: ScenarioStep[] }) {
  const color = { Ok: 'green', Skipped: 'default', Failed: 'red' } as const
  return <List size="small" dataSource={steps} renderItem={(s) => (
    <List.Item><Space><Tag color={color[s.status]}>{s.status === 'Ok' ? 'ок' : s.status === 'Skipped' ? 'пропущен' : 'ошибка'}</Tag>{s.name}<Typography.Text type="secondary">{s.message}</Typography.Text></Space></List.Item>
  )} />
}

function MemberColumn({ title, group, other, onChanged }: { title: string; group: FolderGroupView | null; other: FolderGroupView | null; onChanged: (steps: ScenarioStep[]) => void }) {
  const { message } = App.useApp()
  const { can } = useAuth()
  const [options, setOptions] = useState<{ value: string; label: string }[]>([])
  const [busy, setBusy] = useState(false)
  const search = useMemo(() => {
    let timer: number | undefined
    return (q: string) => {
      window.clearTimeout(timer)
      if (q.trim().length < 2) { setOptions([]); return }
      timer = window.setTimeout(async () => {
        try { setOptions((await api.directory.search(q, 'user')).map((u) => ({ value: u.samAccountName, label: `${u.displayName ?? u.samAccountName} (${u.samAccountName})` }))) }
        catch (e) { message.error(errorText(e, 'Поиск в AD не выполнен')) }
      }, 300)
    }
  }, [message])

  const change = async (member: string, add: boolean) => {
    if (!group) return
    setBusy(true)
    try { onChanged(await api.folders.membership({ groupDn: group.dn, member, add, removeFromOther: true })) }
    catch (e) { message.error(errorText(e, 'Не удалось изменить участников')) }
    finally { setBusy(false) }
  }

  return (
    <Card size="small" title={<Space>{title}<Tag>{group?.members.length ?? 0}</Tag></Space>} extra={group && <Typography.Text type="secondary">{group.name}</Typography.Text>}>
      {!group ? <Empty description={`Нет группы ${title}`} /> : (
        <>
          {can('ad-folders.membership') && (
            <Select showSearch filterOption={false} onSearch={search} options={options} value={null as unknown as string}
              placeholder="Добавить пользователя (от 2 символов)" style={{ width: '100%', marginBottom: 8 }} loading={busy}
              onChange={(v) => change(v, true)} notFoundContent="Ничего не найдено" />
          )}
          <List size="small" dataSource={group.members} locale={{ emptyText: 'Нет участников' }} renderItem={(m) => (
            <List.Item actions={can('ad-folders.membership') ? [
              <Popconfirm key="del" title="Убрать из группы?" onConfirm={() => change(m.sam ?? m.dn, false)}><Button size="small" type="text" danger icon={<DeleteOutlined />} /></Popconfirm>,
            ] : []}>
              <Space>{m.name}{m.isGroup && <Tag>группа</Tag>}{!m.enabled && <Tag>отключён</Tag>}
                {other?.members.some((o) => o.dn === m.dn) && <Tag color="gold">есть и в другой группе</Tag>}</Space>
            </List.Item>
          )} />
        </>
      )}
    </Card>
  )
}

function NewFolder({ projects, open, onClose, onDone }: { projects: AdProject[]; open: boolean; onClose: () => void; onDone: () => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm()
  const [preview, setPreview] = useState<FolderPreview>()
  const [steps, setSteps] = useState<ScenarioStep[]>()
  const [busy, setBusy] = useState(false)

  const values = () => ({ ...form.getFieldsValue(), orgCode: form.getFieldValue('orgCode') || undefined })
  const doPreview = async () => {
    try { await form.validateFields(); setPreview(await api.folders.preview(values())) } catch (e: any) { if (e?.response) message.error(errorText(e, 'Проверка не прошла')) }
  }
  const create = async () => {
    setBusy(true)
    try { setSteps(await api.folders.create(values())); onDone() } catch (e) { message.error(errorText(e, 'Не удалось создать')) } finally { setBusy(false) }
  }

  return (
    <Modal title="Новая папка" open={open} width={640} onCancel={() => { onClose(); setPreview(undefined); setSteps(undefined); form.resetFields() }}
      footer={steps ? <Button onClick={() => { onClose(); setPreview(undefined); setSteps(undefined); form.resetFields() }}>Закрыть</Button> : (
        <Space><Button onClick={doPreview}>Проверить</Button><Button type="primary" disabled={!preview} loading={busy} onClick={create}>Создать</Button></Space>
      )}>
      {steps ? <Steps steps={steps} /> : (
        <Form form={form} layout="vertical" initialValues={{ createDirectory: true }} onValuesChange={() => setPreview(undefined)}>
          <Form.Item name="projectDn" label="Проект" rules={[{ required: true }]}><Select options={projects.map((p) => ({ value: p.dn, label: p.name }))} /></Form.Item>
          <Form.Item name="path" label="Путь к папке (как в описании групп)" rules={[{ required: true }]}><Input placeholder="A:\Проект\Документы" /></Form.Item>
          <Row gutter={12}>
            <Col span={12}><Form.Item name="baseName" label="Имя для групп (латиница)" rules={[{ required: true }]}><Input placeholder="docs" /></Form.Item></Col>
            <Col span={12}><Form.Item name="orgCode" label="Код организации (пусто — авто)"><Input placeholder="co" /></Form.Item></Col>
          </Row>
          <Form.Item name="createDirectory" valuePropName="checked"><Checkbox>Создать папку, если её нет</Checkbox></Form.Item>
          {preview && <Alert type="info" message="Будет создано" description={<>Группы: <b>{preview.fullGroup}</b>, <b>{preview.readGroup}</b> в {preview.groupsOuDn}<br />Папка: {preview.unc}</>} />}
        </Form>
      )}
    </Modal>
  )
}

export default function AdFolders() {
  const { message } = App.useApp()
  const { can } = useAuth()
  const [projects, setProjects] = useState<AdProject[]>([])
  const [project, setProject] = useState<string>()
  const [q, setQ] = useState('')
  const [folders, setFolders] = useState<AdFolder[]>([])
  const [unparsed, setUnparsed] = useState(0)
  const [loading, setLoading] = useState(false)
  const [selected, setSelected] = useState<string>()
  const [steps, setSteps] = useState<ScenarioStep[]>()
  const [acl, setAcl] = useState<FolderAclState>()
  const [wizard, setWizard] = useState(false)

  useEffect(() => { api.ad.projects().then(setProjects).catch((e) => message.error(errorText(e, 'Не удалось загрузить проекты'))) }, [message])

  const load = useCallback(async () => {
    setLoading(true)
    try { const r = await api.folders.list({ project, q: q || undefined }); setFolders(r.folders); setUnparsed(r.unparsed.length) }
    catch (e) { message.error(errorText(e, 'Не удалось загрузить папки')) }
    finally { setLoading(false) }
  }, [project, q, message])

  useEffect(() => { load() }, [load])

  const folder = folders.find((f) => f.path === selected)
  const columns: ColumnsType<AdFolder> = [
    { title: 'Папка', dataIndex: 'path', sorter: (a, b) => a.path.localeCompare(b.path, 'ru') },
    { title: 'Проект', dataIndex: 'projectName', width: 140 },
    { title: 'Доступ', width: 230, render: (_, f) => <Space>
      <Tag color={f.full ? 'green' : undefined}>Full ({f.full?.members.length ?? 0})</Tag>
      <Tag color={f.read ? 'blue' : undefined}>Read ({f.read?.members.length ?? 0})</Tag></Space> },
    { title: 'Предупреждения', render: (_, f) => f.warnings.map((w) => <Tag key={w} color="gold">{w}</Tag>) },
  ]

  const checkAcl = async (path: string) => {
    try { setAcl(await api.folders.acl(path)) } catch (e) { message.error(errorText(e, 'Не удалось прочитать права')) }
  }

  return (
    <>
      <PageHeader title="Папки" subtitle="Доступ к сетевым папкам через группы безопасности"
        extra={<Space>
          {can('ad-folders.create') && <Button type="primary" icon={<FolderAddOutlined />} onClick={() => setWizard(true)}>Новая папка</Button>}
          <Button icon={<ReloadOutlined />} onClick={load}>Обновить</Button>
        </Space>} />
      <Card className="sp-glass">
        <Space wrap style={{ marginBottom: 12 }}>
          <Select allowClear placeholder="Все проекты" style={{ minWidth: 220 }} value={project} onChange={setProject}
            options={projects.map((p) => ({ value: p.dn, label: p.name }))} />
          <Input.Search allowClear placeholder="Путь или слова (например: документы проект)" style={{ width: 360 }} onSearch={setQ} />
          {unparsed > 0 && <Tag color="gold">групп без пути в описании: {unparsed}</Tag>}
        </Space>
        <Table<AdFolder> rowKey="path" size="small" loading={loading} dataSource={folders} columns={columns}
          pagination={{ pageSize: 50, showSizeChanger: false }}
          onRow={(f) => ({ onClick: () => { setSelected(f.path); setSteps(undefined); setAcl(undefined) }, style: { cursor: 'pointer' } })} />
      </Card>
      <Drawer title={folder?.path} open={Boolean(folder)} onClose={() => setSelected(undefined)} width={820}>
        {folder && (
          <Space direction="vertical" style={{ width: '100%' }}>
            {folder.warnings.length > 0 && <Alert type="warning" message={folder.warnings.join('; ')} />}
            {steps && <Alert type="info" message="Результат" description={<Steps steps={steps} />} closable onClose={() => setSteps(undefined)} />}
            <Row gutter={12}>
              <Col span={12}><MemberColumn title="Full Access" group={folder.full} other={folder.read} onChanged={(s) => { setSteps(s); load() }} /></Col>
              <Col span={12}><MemberColumn title="Read Only" group={folder.read} other={folder.full} onChanged={(s) => { setSteps(s); load() }} /></Col>
            </Row>
            <Space>
              <Button icon={<SafetyOutlined />} onClick={() => checkAcl(folder.path)}>Проверить права NTFS</Button>
              {can('ad-folders.create') && acl && !acl.ok && acl.exists && (
                <Popconfirm title="Добавить недостающие права NTFS?" onConfirm={async () => {
                  try { setSteps(await api.folders.fixAcl(folder.path)); await checkAcl(folder.path) } catch (e) { message.error(errorText(e, 'Не удалось исправить')) }
                }}><Button type="primary">Исправить права NTFS</Button></Popconfirm>
              )}
            </Space>
            {acl && <Alert type={acl.ok ? (acl.warnings.length ? 'warning' : 'success') : 'error'}
              message={acl.ok ? 'Права NTFS в порядке' : acl.exists ? 'Не хватает прав NTFS' : 'Папка не найдена на файловом сервере'}
              description={[...acl.missing, ...acl.warnings].join('; ') || undefined} />}
          </Space>
        )}
      </Drawer>
      <NewFolder projects={projects} open={wizard} onClose={() => setWizard(false)} onDone={load} />
    </>
  )
}
