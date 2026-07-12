import { useEffect, useMemo, useState } from 'react'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef } from 'ag-grid-community'
import { App, Button, Input, Popconfirm, Space, Switch, Tag, Typography } from 'antd'
import { DeleteOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import { useSoftwareJob } from '../hooks/useSoftwareJob'
import type { InstalledApp } from '../api/types'
import PageHeader from '../components/PageHeader'
import SoftwareOperationDrawer from '../components/SoftwareOperationDrawer'
import { defaultColDef, gridTheme } from '../components/grid'
import { formatBytes, formatDateTime } from '../utils/format'

const { Text } = Typography

export default function Applications() {
  const { data, loading, refresh } = useApi(api.software.applications)
  const { message } = App.useApp()
  const [quickFilter, setQuickFilter] = useState('')
  const [showSystem, setShowSystem] = useState(false)
  const { job, drawerOpen, startPolling, restoreActive, closeDrawer, minimize } = useSoftwareJob({
    onTerminal: () => {
      void refresh(true)
    },
  })

  useEffect(() => {
    void restoreActive().catch(() => undefined)
  }, [restoreActive])

  const rows = useMemo(
    () => (showSystem ? data : data?.filter((app) => !app.isSystem)) ?? [],
    [data, showSystem],
  )

  const uninstall = async (app: InstalledApp) => {
    try {
      const nextJob = await api.software.uninstallApp(app.id)
      startPolling(nextJob)
    } catch (e) {
      message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Ошибка удаления')
    }
  }

  const columns = useMemo<ColDef<InstalledApp>[]>(() => [
    { field: 'name', headerName: 'Приложение', flex: 2, filter: 'agTextColumnFilter' },
    { field: 'version', headerName: 'Версия', flex: 0.9 },
    { field: 'publisher', headerName: 'Издатель', flex: 1.2 },
    {
      field: 'installDate',
      headerName: 'Установлено',
      flex: 0.9,
      valueFormatter: (p) => formatDateTime(p.value),
    },
    {
      field: 'sizeBytes',
      headerName: 'Размер',
      flex: 0.8,
      filter: 'agNumberColumnFilter',
      valueFormatter: (p) => formatBytes(p.value),
    },
    {
      field: 'source',
      headerName: 'Источник',
      flex: 0.8,
      cellRenderer: (p: { value: string }) => <Tag color={p.value === 'Store' ? 'blue' : 'default'}>{p.value}</Tag>,
    },
    {
      headerName: 'Действия',
      flex: 0.8,
      sortable: false,
      filter: false,
      cellRenderer: (p: { data: InstalledApp }) => {
        const app = p.data
        return (
          <Popconfirm
            title={`Удалить «${app.name}»?`}
            description="Операция может занять несколько минут."
            onConfirm={() => uninstall(app)}
            okText="Удалить"
            cancelText="Отмена"
            okButtonProps={{ danger: true }}
          >
            <Button
              size="small"
              type="text"
              danger
              icon={<DeleteOutlined />}
              disabled={!app.canUninstall}
            />
          </Popconfirm>
        )
      },
    },
  ], [])

  const subtitle = data ? `${rows.length} из ${data.length} приложений` : undefined

  return (
    <>
      <PageHeader
        title="Приложения"
        subtitle={subtitle}
        onRefresh={refresh}
        loading={loading}
        extra={(
          <Space wrap>
            <Input.Search
              allowClear
              placeholder="Поиск приложения"
              style={{ width: 240 }}
              onChange={(e) => setQuickFilter(e.target.value)}
            />
            <Space>
              <Text type="secondary">Системные</Text>
              <Switch checked={showSystem} onChange={setShowSystem} />
            </Space>
          </Space>
        )}
      />
      <div style={{ height: 'calc(100vh - 200px)' }}>
        <AgGridReact<InstalledApp>
          theme={gridTheme}
          rowData={rows}
          columnDefs={columns}
          defaultColDef={defaultColDef}
          quickFilterText={quickFilter}
          animateRows
          loading={loading && !data}
        />
      </div>
      <SoftwareOperationDrawer
        job={job}
        open={drawerOpen}
        onClose={closeDrawer}
        onMinimize={minimize}
      />
    </>
  )
}
