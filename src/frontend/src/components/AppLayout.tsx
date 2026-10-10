import { Layout, Menu, Typography, Button, Tooltip, Grid, type MenuProps } from 'antd'
import {
  DashboardOutlined, HddOutlined, ApiOutlined, AppstoreOutlined,
  PrinterOutlined, PoweroffOutlined, KeyOutlined, FileSearchOutlined,
  BookOutlined, LogoutOutlined, DesktopOutlined, TeamOutlined,
  FileTextOutlined, SettingOutlined, CodeOutlined, SafetyCertificateOutlined, AppstoreAddOutlined, SafetyOutlined, IdcardOutlined, FolderOpenOutlined,
} from '@ant-design/icons'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthProvider'

type Item = {
  key?: string
  type?: 'divider'
  icon?: ReactNode
  label?: string
  perm?: string | string[]
  module?: string
  children?: Item[]
}

const { Sider, Header, Content, Footer } = Layout
const { Text } = Typography

export default function AppLayout({ machine }: { machine?: string; onLogout: () => void }) {
  const location = useLocation()
  const navigate = useNavigate()
  const screens = Grid.useBreakpoint()
  const collapsed = !screens.lg
  const { can, moduleOn, logout } = useAuth()

  const allowed = (i: Item) =>
    (!i.module || moduleOn(i.module)) && (!i.perm || (Array.isArray(i.perm) ? i.perm.some(can) : can(i.perm)))
  const filter = (list: Item[]): Item[] =>
    list.filter(allowed)
      .map((i) => (i.children ? { ...i, children: filter(i.children) } : i))
      .filter((i) => !i.children || i.children.length > 0)
      // Разделитель не нужен в начале, в конце и подряд с другим разделителем.
      .filter((i, idx, arr) => i.type !== 'divider' || (idx > 0 && idx < arr.length - 1 && arr[idx - 1].type !== 'divider'))
  // perm/module — только для фильтра, в Menu (и в DOM) их не передаём.
  const toMenu = (list: Item[]): MenuProps['items'] =>
    list.map(({ perm: _perm, module: _module, children, ...rest }) =>
      (children ? { ...rest, children: toMenu(children) } : rest) as NonNullable<MenuProps['items']>[number])

  const items = filter([
    { key: '/', icon: <DashboardOutlined />, label: 'Дашборд', perm: 'system.read', module: 'system' },
    { key: '/disks', icon: <HddOutlined />, label: 'Диски', perm: 'system.read', module: 'system' },
    { key: '/services', icon: <ApiOutlined />, label: 'Службы', perm: 'services.read', module: 'services' },
    { key: '/processes', icon: <AppstoreOutlined />, label: 'Процессы', perm: 'processes.read', module: 'processes' },
    {
      key: '/software', icon: <CodeOutlined />, label: 'Software', perm: 'software.read', module: 'software',
      children: [
        { key: '/software/apps', label: 'Applications' },
        { key: '/software/updates', label: 'Updates' },
      ],
    },
    { key: '/printers', icon: <PrinterOutlined />, label: 'Принтеры', perm: 'printers.read', module: 'printers' },
    { key: '/power', icon: <PoweroffOutlined />, label: 'Питание', perm: 'power.manage', module: 'power' },
    {
      key: '/logs', icon: <FileTextOutlined />, label: 'Журналы Windows', perm: 'eventlogs.read', module: 'eventlogs',
      children: [
        { key: '/logs/auth', label: 'Авторизация' },
        { key: '/logs/security', label: 'Security (все события)' },
        { key: '/logs/system', label: 'Система' },
        { key: '/logs/application', label: 'Приложения' },
        { key: '/logs/powershell', label: 'PowerShell' },
        { key: '/logs/setup', label: 'Установка ПО' },
        { type: 'divider' },
        { key: '/logs/custom', label: 'Произвольный журнал' },
      ],
    },
    { key: '/ad/users', icon: <IdcardOutlined />, label: 'Пользователи AD', perm: 'ad-users.read', module: 'ad-users' },
    { key: '/ad/folders', icon: <FolderOpenOutlined />, label: 'Папки', perm: 'ad-folders.read', module: 'ad-folders' },
    { type: 'divider' },
    { key: '/cp/users', icon: <TeamOutlined />, label: 'Пользователи', perm: 'platform.users.manage' },
    { key: '/cp/roles', icon: <SafetyCertificateOutlined />, label: 'Роли', perm: 'platform.roles.manage' },
    { key: '/cp/modules', icon: <AppstoreAddOutlined />, label: 'Модули', perm: 'platform.modules.manage' },
    { key: '/cp/environment', icon: <SafetyOutlined />, label: 'Проверка окружения', perm: 'platform.environment.check' },
    { key: '/cp/apikeys', icon: <KeyOutlined />, label: 'API-ключи', perm: 'platform.apikeys.manage' },
    { key: '/cp/audit', icon: <FileSearchOutlined />, label: 'Аудит', perm: 'platform.audit.read' },
    { key: '/cp/settings', icon: <SettingOutlined />, label: 'Настройки', perm: ['platform.network.manage', 'eventlogs.manage'] },
    { key: '/docs', icon: <BookOutlined />, label: 'API-документация' },
  ])


  return (
    <Layout style={{ minHeight: '100vh', background: 'transparent' }}>
      <Sider
        theme="dark"
        collapsed={collapsed}
        collapsedWidth={64}
        width={236}
        style={{ borderRight: '1px solid #1b212c', position: 'sticky', top: 0, height: '100vh' }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: collapsed ? '18px 0' : '18px 20px', justifyContent: collapsed ? 'center' : 'flex-start' }}>
          <img src="/favicon.svg" alt="" width={22} height={22} />
          {!collapsed && <Text strong style={{ fontSize: 18, letterSpacing: 0.3 }}>WinAdmin</Text>}
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[location.pathname]}
          defaultOpenKeys={location.pathname.startsWith('/software') ? ['/logs', '/software'] : ['/logs']}
          items={toMenu(items)}
          onClick={({ key }) => navigate(key)}
          style={{ background: 'transparent', borderInlineEnd: 'none' }}
        />
      </Sider>

      <Layout style={{ background: 'transparent' }}>
        <Header
          className="sp-glass"
          style={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            padding: '0 20px', position: 'sticky', top: 0, zIndex: 10, height: 56,
          }}
        >
          <Text type="secondary">
            <DesktopOutlined style={{ marginRight: 8 }} />
            {machine ?? 'локальная машина'}
          </Text>
          <Tooltip title="Выйти из аккаунта">
            <Button type="text" icon={<LogoutOutlined />} onClick={logout}>
              Выйти
            </Button>
          </Tooltip>
        </Header>
        <Content style={{ padding: 24 }}>
          <div className="sp-fade-in" key={location.pathname}>
            <Outlet />
          </div>
        </Content>
        <Footer style={{ textAlign: 'center', padding: '12px 24px' }}>
          <Text type="secondary" style={{ fontSize: 12 }}>
            WinAdmin v{__APP_VERSION__} · Powered by{' '}
            <a href="https://www.ismarty.pro" target="_blank" rel="noopener noreferrer">
              iSmartyPro
            </a>
            ®
          </Text>
        </Footer>
      </Layout>
    </Layout>
  )
}
