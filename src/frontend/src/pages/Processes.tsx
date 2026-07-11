import { useMemo, useState } from 'react'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef } from 'ag-grid-community'
import { Button, Input, Popconfirm, Tag, App } from 'antd'
import { CloseCircleOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { ProcessInfo } from '../api/types'
import PageHeader from '../components/PageHeader'
import { defaultColDef, gridTheme } from '../components/grid'
import { formatBytes } from '../utils/format'

export default function Processes() {
  const { data, loading, refresh } = useApi(api.processes)
  const { message } = App.useApp()
  const [quickFilter, setQuickFilter] = useState('')
  const [busy, setBusy] = useState<number>()

  const kill = async (p: ProcessInfo) => {
    setBusy(p.pid)
    try {
      const r = await api.killProcess(p.pid)
      r.success ? message.success(r.message) : message.error(r.message)
      await refresh(true)
    } catch (e) {
      message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Не удалось завершить процесс')
    } finally {
      setBusy(undefined)
    }
  }

  const columns = useMemo<ColDef<ProcessInfo>[]>(() => [
    { field: 'pid', headerName: 'PID', flex: 0.6, filter: 'agNumberColumnFilter' },
    { field: 'name', headerName: 'Процесс', flex: 1.4, filter: 'agTextColumnFilter' },
    { field: 'mainWindowTitle', headerName: 'Окно', flex: 1.6 },
    {
      field: 'workingSetBytes', headerName: 'Память', flex: 0.9,
      valueFormatter: (p) => formatBytes(p.value), filter: 'agNumberColumnFilter',
      sort: 'desc',
    },
    { field: 'threadCount', headerName: 'Потоки', flex: 0.7 },
    {
      field: 'hasWindow', headerName: 'Тип', flex: 0.7, filter: false,
      cellRenderer: (p: { value: boolean }) =>
        p.value ? <Tag color="blue">Приложение</Tag> : <Tag>Фоновый</Tag>,
    },
    {
      headerName: '', flex: 0.6, sortable: false, filter: false,
      cellRenderer: (p: { data: ProcessInfo }) => (
        <Popconfirm
          title={`Завершить «${p.data.name}» (PID ${p.data.pid})?`}
          description="Несохранённые данные процесса будут потеряны."
          onConfirm={() => kill(p.data)} okText="Завершить" okButtonProps={{ danger: true }} cancelText="Отмена"
        >
          <Button size="small" type="text" danger icon={<CloseCircleOutlined />} loading={busy === p.data.pid} />
        </Popconfirm>
      ),
    },
  ], [busy])

  return (
    <>
      <PageHeader
        title="Процессы"
        subtitle={data ? `${data.length} процессов · ${data.filter((p) => p.hasWindow).length} с окном` : undefined}
        onRefresh={refresh}
        loading={loading}
        extra={<Input.Search allowClear placeholder="Поиск процесса" style={{ width: 240 }} onChange={(e) => setQuickFilter(e.target.value)} />}
      />
      <div style={{ height: 'calc(100vh - 200px)' }}>
        <AgGridReact<ProcessInfo>
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
