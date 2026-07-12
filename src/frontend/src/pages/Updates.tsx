import { useEffect, useMemo, useState } from 'react'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef } from 'ag-grid-community'
import { App, Button, Input, Popconfirm, Space } from 'antd'
import { DeleteOutlined, RollbackOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import { useSoftwareJob } from '../hooks/useSoftwareJob'
import type { InstalledUpdate } from '../api/types'
import PageHeader from '../components/PageHeader'
import SoftwareOperationDrawer from '../components/SoftwareOperationDrawer'
import { defaultColDef, gridTheme } from '../components/grid'
import { formatDateTime } from '../utils/format'

export default function Updates() {
  const { data, loading, refresh } = useApi(api.software.updates)
  const { message } = App.useApp()
  const [quickFilter, setQuickFilter] = useState('')
  const { job, drawerOpen, startPolling, restoreActive, closeDrawer, minimize } = useSoftwareJob({
    onTerminal: () => {
      void refresh(true)
    },
  })

  useEffect(() => {
    void restoreActive()
  }, [restoreActive])

  const uninstall = async (update: InstalledUpdate) => {
    try {
      const nextJob = await api.software.uninstallUpdate(update.id)
      startPolling(nextJob)
    } catch (e) {
      message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Ошибка удаления')
    }
  }

  const rollback = async (update: InstalledUpdate) => {
    try {
      const nextJob = await api.software.rollbackUpdate(update.id)
      startPolling(nextJob)
    } catch (e) {
      message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Ошибка отката')
    }
  }

  const columns = useMemo<ColDef<InstalledUpdate>[]>(() => [
    { field: 'kbArticle', headerName: 'KB', flex: 0.7, filter: 'agTextColumnFilter' },
    { field: 'title', headerName: 'Обновление', flex: 2, filter: 'agTextColumnFilter' },
    {
      field: 'installedOn',
      headerName: 'Установлено',
      flex: 0.9,
      valueFormatter: (p) => formatDateTime(p.value),
    },
    {
      headerName: 'Действия',
      flex: 1,
      sortable: false,
      filter: false,
      cellRenderer: (p: { data: InstalledUpdate }) => {
        const update = p.data
        return (
          <Space size="small">
            <Popconfirm
              title={`Удалить «${update.title}»?`}
              description="Операция может занять несколько минут."
              onConfirm={() => uninstall(update)}
              okText="Удалить"
              cancelText="Отмена"
              okButtonProps={{ danger: true }}
            >
              <Button
                size="small"
                type="text"
                danger
                icon={<DeleteOutlined />}
                disabled={!update.canUninstall}
              />
            </Popconfirm>
            <Popconfirm
              title={`Откатить «${update.title}»?`}
              description="Система может перезагрузиться после отката."
              onConfirm={() => rollback(update)}
              okText="Откатить"
              cancelText="Отмена"
            >
              <Button
                size="small"
                type="text"
                icon={<RollbackOutlined />}
                disabled={!update.canRollback}
              />
            </Popconfirm>
          </Space>
        )
      },
    },
  ], [])

  const subtitle = data ? `${data.length} обновлений` : undefined

  return (
    <>
      <PageHeader
        title="Обновления"
        subtitle={subtitle}
        onRefresh={refresh}
        loading={loading}
        extra={(
          <Input.Search
            allowClear
            placeholder="Поиск обновления"
            style={{ width: 240 }}
            onChange={(e) => setQuickFilter(e.target.value)}
          />
        )}
      />
      <div style={{ height: 'calc(100vh - 200px)' }}>
        <AgGridReact<InstalledUpdate>
          theme={gridTheme}
          rowData={data ?? []}
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
