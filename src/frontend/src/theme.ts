import { theme, type ThemeConfig } from 'antd'

// Премиальная тёмная тема: глубокий фон, акцентный индиго, мягкие скругления.
export const WinAdminTheme: ThemeConfig = {
  algorithm: theme.darkAlgorithm,
  token: {
    colorPrimary: '#4f7cff',
    colorInfo: '#4f7cff',
    colorBgBase: '#0d1117',
    colorBgContainer: '#161b22',
    colorBgElevated: '#1c2230',
    colorBorder: '#2a3242',
    colorBorderSecondary: '#222a36',
    borderRadius: 10,
    fontSize: 14,
    fontFamily:
      "'Inter', -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
    wireframe: false,
  },
  components: {
    Layout: {
      siderBg: '#0b0f16',
      headerBg: 'rgba(13,17,23,0.85)',
      bodyBg: '#0d1117',
    },
    Menu: {
      itemBg: 'transparent',
      itemSelectedBg: 'rgba(79,124,255,0.16)',
      itemSelectedColor: '#7aa0ff',
      itemHoverBg: 'rgba(255,255,255,0.04)',
    },
    Card: {
      colorBgContainer: '#161b22',
    },
    Table: {
      headerBg: '#11161e',
    },
  },
}
