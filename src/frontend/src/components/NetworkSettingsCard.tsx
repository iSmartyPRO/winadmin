import { useCallback, useEffect, useState } from 'react'
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Divider,
  Form,
  Input,
  InputNumber,
  Radio,
  Select,
  Space,
  Switch,
  Tag,
  Typography,
} from 'antd'
import { GlobalOutlined, LockOutlined, ReloadOutlined, SafetyCertificateOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import type {
  CertificateInfo,
  CertificateStoreLocation,
  HttpsCertificateSource,
  NetworkMode,
  NetworkSettingsDto,
} from '../api/types'

const { Paragraph, Text } = Typography

interface FormValues {
  mode: NetworkMode
  port: number
  allow: string[]
  httpsEnabled: boolean
  source?: HttpsCertificateSource
  /** «LocalMachine:ОТПЕЧАТОК» — хранилище и отпечаток в одном значении Select. */
  storeCertificate?: string
  pfxPath?: string
  pfxPassword?: string
  httpRedirectPort?: number | null
  selfSignedDns?: string[]
}

const DAY = 24 * 60 * 60 * 1000

const formatDate = (iso: string) => new Date(iso).toLocaleDateString('ru-RU')

function ExpiryTag({ notAfter }: { notAfter: string }) {
  const left = new Date(notAfter).getTime() - Date.now()
  if (left <= 0) return <Tag color="red">истёк {formatDate(notAfter)}</Tag>
  if (left < 30 * DAY) return <Tag color="orange">до {formatDate(notAfter)}</Tag>
  return <Tag color="green">до {formatDate(notAfter)}</Tag>
}

function storeValue(location: CertificateStoreLocation | null | undefined, thumbprint: string) {
  return `${location ?? 'LocalMachine'}:${thumbprint}`
}

export default function NetworkSettingsCard() {
  const { message, modal } = App.useApp()
  const [form] = Form.useForm<FormValues>()
  const [current, setCurrent] = useState<NetworkSettingsDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [certificates, setCertificates] = useState<CertificateInfo[] | null>(null)
  const [certificatesLoading, setCertificatesLoading] = useState(false)
  const [creating, setCreating] = useState(false)
  const [created, setCreated] = useState<CertificateInfo | null>(null)
  const mode = Form.useWatch('mode', form)
  const httpsEnabled = Form.useWatch('httpsEnabled', form)
  const source = Form.useWatch('source', form)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api.settings.network()
      setCurrent(data)
      const https = data.https
      form.setFieldsValue({
        mode: data.mode,
        port: data.port,
        allow: data.allow,
        httpsEnabled: https?.enabled ?? false,
        source: https?.source ?? 'SelfSigned',
        storeCertificate:
          https?.source === 'Store' && https.thumbprint
            ? storeValue(https.storeLocation, https.thumbprint)
            : undefined,
        pfxPath: https?.pfxPath ?? undefined,
        pfxPassword: undefined,
        httpRedirectPort: https?.httpRedirectPort ?? null,
      })
    } catch {
      message.error('Не удалось загрузить сетевые настройки')
    } finally {
      setLoading(false)
    }
  }, [form, message])

  const loadCertificates = useCallback(async () => {
    setCertificatesLoading(true)
    try {
      setCertificates(await api.settings.certificates())
    } catch {
      message.error('Не удалось получить список сертификатов')
    } finally {
      setCertificatesLoading(false)
    }
  }, [message])

  useEffect(() => {
    load()
  }, [load])

  useEffect(() => {
    if (httpsEnabled && source === 'Store' && certificates === null && !certificatesLoading) loadCertificates()
  }, [httpsEnabled, source, certificates, certificatesLoading, loadCertificates])

  const createSelfSigned = async () => {
    setCreating(true)
    try {
      const info = await api.settings.createSelfSigned(form.getFieldValue('selfSignedDns') ?? [])
      setCreated(info)
      message.success('Самоподписанный сертификат создан')
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось создать сертификат')
    } finally {
      setCreating(false)
    }
  }

  const save = async (values: FormValues) => {
    setSaving(true)
    try {
      const [storeLocation, thumbprint] = (values.storeCertificate ?? '').split(':')
      const { url } = await api.settings.updateNetwork({
        mode: values.mode,
        port: values.port,
        allow: values.allow ?? [],
        https: {
          enabled: values.httpsEnabled ?? false,
          source: values.source ?? null,
          thumbprint: values.source === 'Store' ? thumbprint || null : null,
          storeLocation: values.source === 'Store' ? ((storeLocation || null) as CertificateStoreLocation | null) : null,
          pfxPath: values.source === 'Pfx' ? values.pfxPath ?? null : null,
          // Пустое поле — оставить сохранённый пароль.
          pfxPassword: values.source === 'Pfx' && values.pfxPassword ? values.pfxPassword : null,
          httpRedirectPort: values.httpRedirectPort ?? null,
        },
      })
      message.success(`Настройки сохранены. Переход на ${url}`)
      setTimeout(() => window.location.assign(url + window.location.pathname), 1500)
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить сетевые настройки')
      setSaving(false)
    }
  }

  const handleFinish = (values: FormValues) => {
    modal.confirm({
      title: 'Применить сетевые настройки?',
      content: (
        <>
          <p>
            Панель переедет на новый адрес, войти потребуется заново. Если вы подключены не с этого компьютера,
            доступ может пропасть. Если сервер не сможет начать работу с новыми настройками, прежние вернутся автоматически.
          </p>
          {values.httpsEnabled && values.source === 'SelfSigned' && (
            <p>
              Сертификат самоподписанный — браузер покажет предупреждение о недоверенном сертификате; подтвердите
              переход.
            </p>
          )}
        </>
      ),
      okText: 'Применить',
      cancelText: 'Отмена',
      onOk: () => save(values),
    })
  }

  const https = current?.https
  const shownCertificate = source === 'SelfSigned' && created ? created : https?.certificate

  return (
    <Card
      loading={loading}
      title={
        <Space>
          <GlobalOutlined />
          <span>Сеть</span>
        </Space>
      }
      style={{ maxWidth: 720, marginBottom: 16 }}
    >
      <Paragraph type="secondary" style={{ marginTop: 0 }}>
        Откуда доступна панель. Изменения применяются без перезапуска службы. Текущий адрес:{' '}
        <Text code>{current?.url}</Text>
      </Paragraph>

      <Form form={form} layout="vertical" onFinish={handleFinish} requiredMark={false}>
        <Form.Item name="mode" label="Доступ">
          <Radio.Group>
            <Radio.Button value="Local">Только этот компьютер</Radio.Button>
            <Radio.Button value="Network">Сеть</Radio.Button>
          </Radio.Group>
        </Form.Item>

        <Form.Item name="port" label="Порт" rules={[{ required: true, message: 'Укажите порт' }]}>
          <InputNumber min={1} max={65535} style={{ width: 160 }} />
        </Form.Item>

        {/* Поле остаётся в форме и в режиме «Только этот компьютер» (скрыто) — иначе список
            подсетей не попал бы в запрос и потерялся при переключении туда-обратно. */}
        <Form.Item
          name="allow"
          label="Разрешённые адреса и подсети"
          extra="Например 10.77.77.0/24 или 192.168.88.5. Остальным брандмауэр закроет доступ."
          hidden={mode !== 'Network'}
          rules={[
            {
              required: mode === 'Network',
              type: 'array',
              min: mode === 'Network' ? 1 : 0,
              message: 'Укажите хотя бы одну подсеть',
            },
          ]}
        >
          <Select mode="tags" tokenSeparators={[',', ' ']} placeholder="10.0.0.0/24" open={false} />
        </Form.Item>

        {mode === 'Network' && !httpsEnabled && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            message="Трафик не шифруется (HTTP). Пароли учёток домена по сети недоступны (работают вход Windows и локальные учётки). Включите HTTPS."
          />
        )}

        <Divider titlePlacement="start" plain>
          <Space>
            <LockOutlined />
            HTTPS
          </Space>
        </Divider>

        <Form.Item name="httpsEnabled" label="Шифрование (HTTPS)" valuePropName="checked">
          <Switch checkedChildren="Вкл" unCheckedChildren="Выкл" />
        </Form.Item>

        <div style={{ display: httpsEnabled ? undefined : 'none' }}>
          <Form.Item name="source" label="Сертификат">
            <Radio.Group>
              <Radio.Button value="Store">Из хранилища Windows</Radio.Button>
              <Radio.Button value="Pfx">Файл PFX</Radio.Button>
              <Radio.Button value="SelfSigned">Самоподписанный</Radio.Button>
            </Radio.Group>
          </Form.Item>

          {source === 'Store' && (
            <Form.Item label="Сертификат из хранилища «Личные»" required>
              <Space.Compact style={{ width: '100%' }}>
                <Form.Item
                  name="storeCertificate"
                  noStyle
                  rules={[{ required: httpsEnabled && source === 'Store', message: 'Выберите сертификат' }]}
                >
                  <Select
                    showSearch
                    loading={certificatesLoading}
                    placeholder="Сертификаты с закрытым ключом"
                    optionFilterProp="label"
                    notFoundContent="Нет сертификатов с закрытым ключом"
                    options={(certificates ?? []).map((c) => ({
                      value: storeValue(c.storeLocation, c.thumbprint),
                      label: `${c.dnsNames[0] ?? c.subject} — до ${formatDate(c.notAfter)} (${c.storeLocation === 'CurrentUser' ? 'пользователь' : 'компьютер'}, ${c.thumbprint.slice(0, 8)}…)`,
                    }))}
                  />
                </Form.Item>
                <Button icon={<ReloadOutlined />} onClick={loadCertificates} title="Обновить список" />
              </Space.Compact>
            </Form.Item>
          )}

          {source === 'Pfx' && (
            <>
              <Form.Item
                name="pfxPath"
                label="Путь к файлу PFX на сервере"
                rules={[{ required: httpsEnabled && source === 'Pfx', message: 'Укажите путь к PFX' }]}
              >
                <Input placeholder="C:\certs\panel.pfx" />
              </Form.Item>
              <Form.Item
                name="pfxPassword"
                label="Пароль PFX"
                extra={
                  https?.pfxPasswordSet
                    ? 'Пароль сохранён (зашифрован). Оставьте поле пустым, чтобы не менять.'
                    : 'Хранится зашифрованным и не показывается.'
                }
              >
                <Input.Password autoComplete="new-password" />
              </Form.Item>
            </>
          )}

          {source === 'SelfSigned' && (
            <>
              <Alert
                type="info"
                showIcon
                style={{ marginBottom: 16 }}
                message="Самоподписанный сертификат"
                description="Браузер покажет предупреждение о недоверенном сертификате. Для входа Windows (SSO) открывайте панель по DNS-имени сервера и добавьте сайт в зону «Местная интрасеть». Для постоянной работы лучше сертификат корпоративного ЦС."
              />
              <Form.Item
                name="selfSignedDns"
                label="Дополнительные DNS-имена"
                extra="Имя компьютера (полное и короткое) и localhost добавляются всегда."
              >
                <Select mode="tags" tokenSeparators={[',', ' ']} placeholder="panel.corp.local" open={false} />
              </Form.Item>
              <Form.Item>
                <Button icon={<SafetyCertificateOutlined />} loading={creating} onClick={createSelfSigned}>
                  {https?.source === 'SelfSigned' && https.certificate ? 'Создать новый' : 'Создать'}
                </Button>
              </Form.Item>
            </>
          )}

          <Form.Item
            name="httpRedirectPort"
            label="Порт перенаправления HTTP"
            extra="Необязательно. Запросы на http://…:порт получат перенаправление (308) на HTTPS."
          >
            <InputNumber min={1} max={65535} style={{ width: 160 }} placeholder="нет" />
          </Form.Item>

          {https?.certificateError && https.source === source && (
            <Alert type="error" showIcon style={{ marginBottom: 16 }} message={https.certificateError} />
          )}

          {shownCertificate && (https?.source === source || created) && (
            <Descriptions size="small" column={1} bordered style={{ marginBottom: 16 }}>
              <Descriptions.Item label="Субъект">{shownCertificate.subject}</Descriptions.Item>
              <Descriptions.Item label="DNS-имена">
                {shownCertificate.dnsNames.length ? shownCertificate.dnsNames.join(', ') : '—'}
              </Descriptions.Item>
              <Descriptions.Item label="Срок действия">
                <ExpiryTag notAfter={shownCertificate.notAfter} />
              </Descriptions.Item>
              <Descriptions.Item label="Отпечаток">
                <Text code copyable>
                  {shownCertificate.thumbprint}
                </Text>
              </Descriptions.Item>
            </Descriptions>
          )}
        </div>

        <Form.Item style={{ marginBottom: 0 }}>
          <Button type="primary" htmlType="submit" loading={saving}>
            Сохранить
          </Button>
        </Form.Item>
      </Form>
    </Card>
  )
}
