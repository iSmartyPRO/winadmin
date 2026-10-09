import { useEffect, useState } from 'react'
import { App, Button, Card, Form, Input, Radio, Select, Space, Typography } from 'antd'
import { api } from '../api/client'
import type { AdStructureSettings, SaveAdStructure } from '../api/types'

export default function AdStructureCard() {
  const { message } = App.useApp()
  const [form] = Form.useForm<SaveAdStructure>()
  const [current, setCurrent] = useState<AdStructureSettings>()
  const [saving, setSaving] = useState(false)
  const mode = Form.useWatch('writeMode', form)

  useEffect(() => {
    api.adStructure.get().then((s) => { setCurrent(s); form.setFieldsValue(s) })
      .catch(() => message.error('Не удалось загрузить настройки AD'))
  }, [form, message])

  const save = async (values: SaveAdStructure) => {
    setSaving(true)
    try {
      const payload = { ...values, writePassword: values.writePassword ? values.writePassword : null }
      const saved = await api.adStructure.save(payload)
      setCurrent(saved)
      form.setFieldsValue({ ...saved, writePassword: undefined })
      message.success('Настройки AD сохранены')
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card title="Active Directory: управление" className="sp-glass">
      <Typography.Paragraph type="secondary">
        Структура каталога и учётка, которой модули «Пользователи AD» и «Папки» вносят изменения.
        Проекты — подразделения первого уровня под корневой OU.
      </Typography.Paragraph>
      <Form form={form} layout="vertical" onFinish={save}>
        <Form.Item name="rootOu" label="Корневая OU (DN)"><Input placeholder="OU=Accounts,DC=pcs-msk,DC=com" /></Form.Item>
        <Form.Item name="usersOuName" label="OU пользователей внутри проекта"><Input placeholder="Users" /></Form.Item>
        <Form.Item name="hiddenOus" label="Скрытые OU (первый уровень)">
          <Select mode="tags" tokenSeparators={[',']} placeholder="IT, Others" />
        </Form.Item>
        <Form.Item name="writeMode" label="Учётка для записи в AD">
          <Radio.Group>
            <Radio value="ServiceAccount">Служебная учётка</Radio>
            <Radio value="ProcessAccount">Учётка службы WinAdmin</Radio>
          </Radio.Group>
        </Form.Item>
        {mode !== 'ProcessAccount' && (
          <>
            <Form.Item name="writeLogin" label="Логин"><Input placeholder="PCS\svc-winadmin" /></Form.Item>
            <Form.Item name="writePassword" label={current?.hasWritePassword ? 'Пароль (задан — введите, чтобы заменить)' : 'Пароль'}>
              <Input.Password autoComplete="new-password" />
            </Form.Item>
          </>
        )}
        {mode === 'ProcessAccount' && (
          <Typography.Paragraph type="warning">
            На контроллере домена учётка службы имеет права администратора домена — предпочтительна служебная учётка с делегированием.
          </Typography.Paragraph>
        )}
        <Space><Button type="primary" htmlType="submit" loading={saving}>Сохранить</Button></Space>
      </Form>
    </Card>
  )
}
