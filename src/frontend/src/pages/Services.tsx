import { useMemo, useState } from 'react'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef } from 'ag-grid-community'
import { Button, Input, Popconfirm, Space, Tag, App } from 'antd'
import {
  CaretRightOutlined, PauseOutlined, ReloadOutlined,
} from '@ant-design/icons'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { ServiceInfo } from '../api/types'
import PageHeader from '../components/PageHeader'
import { defaultColDef, gridTheme } from '../components/grid'

const statusColor: Record<string, string> = {
  Running: 'success', Stopped: 'default', Paused: 'warning',
  StartPending: 'processing', StopPending: 'processing',
}

export default function Services() {
  const { data, loading, refresh } = useApi(api.services)
  const { message } = App.useApp()
  const [quickFilter, setQuickFilter] = useState('')
  const [busy, setBusy] = useState<string>()

  const act = async (svc: ServiceInfo, action: 'start' | 'stop' | 'restart') => {
    setBusy(svc.name)
    try {
      const r = await api.controlService(svc.name, action)
      r.success ? message.success(r.message) : message.error(r.message)
      await refresh(true)
    } catch (e) {
      message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Ошибка действия')
    } finally {
      setBusy(undefined)
    }
  }

  const columns = useMemo<ColDef<ServiceInfo>[]>(() => [
    { field: 'displayName', headerName: 'Служба', flex: 2, filter: 'agTextColumnFilter' },
    { field: 'name', headerName: 'Имя', flex: 1.2 },
    {
      field: 'status', headerName: 'Статус', flex: 0.9,
      cellRenderer: (p: { value: string }) => <Tag color={statusColor[p.value] ?? 'default'}>{p.value}</Tag>,
    },
    { field: 'startType', headerName: 'Запуск', flex: 0.8 },
    { field: 'account', headerName: 'Учётная запись', flex: 1.2 },
    {
      headerName: 'Действия', flex: 1.1, sortable: false, filter: false,
      cellRenderer: (p: { data: ServiceInfo }) => {
        const s = p.data
        const loadingThis = busy === s.name
        return (
          <Space size={4}>
            <Popconfirm title={`Запустить «${s.displayName}»?`} onConfirm={() => act(s, 'start')} okText="Да" cancelText="Нет">
              <Button size="small" type="text" icon={<CaretRightOutlined />} disabled={s.status === 'Running'} loading={loadingThis} />
            </Popconfirm>
            <Popconfirm title={`Остановить «${s.displayName}»?`} onConfirm={() => act(s, 'stop')} okText="Да" cancelText="Нет">
              <Button size="small" type="text" danger icon={<PauseOutlined />} disabled={s.status === 'Stopped' || !s.canStop} loading={loadingThis} />
            </Popconfirm>
            <Popconfirm title={`Перезапустить «${s.displayName}»?`} onConfirm={() => act(s, 'restart')} okText="Да" cancelText="Нет">
              <Button size="small" type="text" icon={<ReloadOutlined />} loading={loadingThis} />
            </Popconfirm>
          </Space>
        )
      },
    },
  ], [busy])

  return (
    <>
      <PageHeader
        title="Службы Windows"
        subtitle={data ? `${data.length} служб · ${data.filter((s) => s.status === 'Running').length} запущено` : undefined}
        onRefresh={refresh}
        loading={loading}
        extra={<Input.Search allowClear placeholder="Поиск службы" style={{ width: 240 }} onChange={(e) => setQuickFilter(e.target.value)} />}
      />
      <div style={{ height: 'calc(100vh - 200px)' }}>
        <AgGridReact<ServiceInfo>
          theme={gridTheme}
          rowData={data ?? []}
          columnDefs={columns}
          defaultColDef={defaultColDef}
          quickFilterText={quickFilter}
          animateRows
          loading={loading && !data}
        />
      </div>
    </>
  )
}
