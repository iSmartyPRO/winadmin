import { Layout, Menu, Typography, Button, Tooltip, Grid } from 'antd'
import {
  DashboardOutlined, HddOutlined, ApiOutlined, AppstoreOutlined,
  PrinterOutlined, PoweroffOutlined, KeyOutlined, FileSearchOutlined,
  BookOutlined, LogoutOutlined, DesktopOutlined, TeamOutlined,
} from '@ant-design/icons'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthProvider'

const { Sider, Header, Content, Footer } = Layout
const { Text } = Typography

export default function AppLayout({ machine }: { machine?: string; onLogout: () => void }) {
  const location = useLocation()
  const navigate = useNavigate()
  const screens = Grid.useBreakpoint()
  const collapsed = !screens.lg
  const { user, logout } = useAuth()
  const isAdmin = user?.scopes.includes('admin') ?? false

  const items = [
    { key: '/', icon: <DashboardOutlined />, label: 'Дашборд' },
    { key: '/disks', icon: <HddOutlined />, label: 'Диски' },
    { key: '/services', icon: <ApiOutlined />, label: 'Службы' },
    { key: '/processes', icon: <AppstoreOutlined />, label: 'Процессы' },
    { key: '/printers', icon: <PrinterOutlined />, label: 'Принтеры' },
    { key: '/power', icon: <PoweroffOutlined />, label: 'Питание' },
    { type: 'divider' as const },
    ...(isAdmin ? [{ key: '/cp/users', icon: <TeamOutlined />, label: 'Пользователи' }] : []),
    { key: '/cp/apikeys', icon: <KeyOutlined />, label: 'API-ключи' },
    { key: '/cp/audit', icon: <FileSearchOutlined />, label: 'Аудит' },
    { key: '/docs', icon: <BookOutlined />, label: 'API-документация' },
  ]

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
          <DesktopOutlined style={{ fontSize: 22, color: '#4f7cff' }} />
          {!collapsed && <Text strong style={{ fontSize: 18, letterSpacing: 0.3 }}>WinAdmin</Text>}
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[location.pathname]}
          items={items}
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
