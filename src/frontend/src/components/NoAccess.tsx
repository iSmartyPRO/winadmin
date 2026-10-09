import { Result } from 'antd'

export default function NoAccess() {
  return (
    <Result
      status="403"
      title="Нет доступа"
      subTitle="У вашей учётной записи нет прав на этот раздел, или модуль выключен. Обратитесь к администратору WinAdmin."
    />
  )
}
