import { Alert, App, Button, Drawer, Progress, Space, Spin, Tag, Typography } from 'antd'
import type { SoftwareJob } from '../api/types'

const { Paragraph, Text } = Typography

type SoftwareOperationDrawerProps = {
  job?: SoftwareJob
  open: boolean
  onClose: () => void
  onMinimize: () => void
}

const typeColor: Record<SoftwareJob['type'], string> = {
  UninstallApp: 'blue',
  UninstallUpdate: 'geekblue',
  RollbackUpdate: 'purple',
}

const statusColor: Record<SoftwareJob['status'], string> = {
  Queued: 'default',
  Running: 'processing',
  Succeeded: 'success',
  Failed: 'error',
}

const typeLabel: Record<SoftwareJob['type'], string> = {
  UninstallApp: 'Удаление приложения',
  UninstallUpdate: 'Удаление обновления',
  RollbackUpdate: 'Откат обновления',
}

const statusLabel: Record<SoftwareJob['status'], string> = {
  Queued: 'В очереди',
  Running: 'Выполняется',
  Succeeded: 'Завершено',
  Failed: 'Ошибка',
}

const isTerminal = (job: SoftwareJob) => job.status === 'Succeeded' || job.status === 'Failed'
const needsReboot = (message: string) => /reboot|restart|перезагруз/i.test(message)

export default function SoftwareOperationDrawer({
  job,
  open,
  onClose,
  onMinimize,
}: SoftwareOperationDrawerProps) {
  const { modal } = App.useApp()
  const running = job?.status === 'Queued' || job?.status === 'Running'
  const failed = job?.status === 'Failed'
  const progressStatus = failed ? 'exception' : running ? 'active' : 'success'
  const showIndeterminateHint = running && job?.progressPercent == null
  const showRebootAlert = job?.status === 'Succeeded' && needsReboot(job.statusMessage)

  const requestClose = () => {
    if (running) {
      modal.confirm({
        title: 'Операция ещё выполняется',
        content: 'Скрыть панель? Опрос статуса продолжится в фоне.',
        okText: 'Скрыть',
        cancelText: 'Отмена',
        onOk: onMinimize,
      })
      return
    }

    onClose()
  }

  return (
    <Drawer
      title={job?.targetName ?? 'Операция'}
      placement="right"
      width={400}
      open={open}
      onClose={requestClose}
      footer={(
        <Space style={{ display: 'flex', justifyContent: 'flex-end' }}>
          {running && <Button onClick={onMinimize}>Свернуть</Button>}
          <Button type={job && isTerminal(job) ? 'primary' : 'default'} onClick={requestClose}>
            Закрыть
          </Button>
        </Space>
      )}
    >
      {job ? (
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <Space wrap>
            <Tag color={typeColor[job.type]}>{typeLabel[job.type]}</Tag>
            <Tag color={statusColor[job.status]}>{statusLabel[job.status]}</Tag>
          </Space>

          <Progress
            percent={job.progressPercent ?? undefined}
            status={progressStatus}
          />

          {showIndeterminateHint && (
            <Space>
              <Spin size="small" />
              <Text type="secondary">Ожидание обновления прогресса...</Text>
            </Space>
          )}

          <Paragraph>{job.statusMessage}</Paragraph>

          {failed && job.error && (
            <Alert type="error" showIcon message="Операция завершилась с ошибкой" description={job.error} />
          )}

          {showRebootAlert && (
            <Alert type="warning" showIcon message="Может потребоваться перезагрузка" description={job.statusMessage} />
          )}
        </Space>
      ) : (
        <Text type="secondary">Нет активной операции.</Text>
      )}
    </Drawer>
  )
}
