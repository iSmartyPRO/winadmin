import { useState } from 'react'
import {
  Alert, Button, Card, Col, Form, Input, InputNumber, Row, Switch, Typography, App,
} from 'antd'
import {
  PoweroffOutlined, ReloadOutlined, StopOutlined, WarningOutlined,
} from '@ant-design/icons'
import { api } from '../api/client'
import type { PowerRequest } from '../api/types'
import PageHeader from '../components/PageHeader'

const { Text, Paragraph } = Typography

export default function Power() {
  const { message, modal } = App.useApp()
  const [form] = Form.useForm()
  const [busy, setBusy] = useState(false)

  const run = async (kind: 'reboot' | 'shutdown') => {
    const values = form.getFieldsValue() as PowerRequest
    const titleVerb = kind === 'reboot' ? 'Перезагрузить' : 'Выключить'
    modal.confirm({
      title: `${titleVerb} компьютер?`,
      icon: <WarningOutlined style={{ color: '#f59e0b' }} />,
      content: (
        <Paragraph>
          Действие будет выполнено через <b>{values.delaySeconds ?? 30} c</b>.
          {values.force && <> Приложения будут закрыты принудительно.</>}
          {' '}Отменить можно кнопкой «Отменить действие» до истечения задержки.
        </Paragraph>
      ),
      okText: titleVerb,
      okButtonProps: { danger: true },
      cancelText: 'Отмена',
      onOk: async () => {
        setBusy(true)
        try {
          const r = kind === 'reboot' ? await api.reboot(values) : await api.shutdown(values)
          r.success ? message.success(r.message) : message.error(r.message)
        } catch (e) {
          message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Ошибка действия')
        } finally {
          setBusy(false)
        }
      },
    })
  }

  const cancel = async () => {
    setBusy(true)
    try {
      const r = await api.cancelPower()
      r.success ? message.success(r.message) : message.warning(r.message)
    } catch (e) {
      message.error((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Ошибка отмены')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <PageHeader title="Управление питанием" subtitle="Перезагрузка и выключение машины" />
      <Alert
        type="warning"
        showIcon
        style={{ marginBottom: 16 }}
        message="Действия влияют на всю систему"
        description="Перезагрузка и выключение затронут всех пользователей машины. Требуется scope power.manage и права администратора у пула приложения."
      />
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={14}>
          <Card variant="borderless" className="sp-glass" title="Параметры">
            <Form form={form} layout="vertical" initialValues={{ delaySeconds: 30, force: false, comment: '' }}>
              <Form.Item name="delaySeconds" label="Задержка (секунд)">
                <InputNumber min={0} max={86400} style={{ width: 200 }} />
              </Form.Item>
              <Form.Item name="comment" label="Комментарий (для пользователей и аудита)">
                <Input placeholder="Плановое обслуживание" maxLength={256} />
              </Form.Item>
              <Form.Item name="force" label="Принудительно закрыть приложения" valuePropName="checked">
                <Switch />
              </Form.Item>
            </Form>
          </Card>
        </Col>
        <Col xs={24} lg={10}>
          <Card variant="borderless" className="sp-glass" title="Действия">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <Button size="large" icon={<ReloadOutlined />} loading={busy} onClick={() => run('reboot')} block>
                Перезагрузить
              </Button>
              <Button size="large" danger icon={<PoweroffOutlined />} loading={busy} onClick={() => run('shutdown')} block>
                Выключить
              </Button>
              <Button size="large" type="dashed" icon={<StopOutlined />} loading={busy} onClick={cancel} block>
                Отменить запланированное действие
              </Button>
              <Text type="secondary" style={{ fontSize: 12 }}>
                Совет: задайте задержку ≥ 30 c, чтобы успеть отменить ошибочное действие.
              </Text>
            </div>
          </Card>
        </Col>
      </Row>
    </>
  )
}
