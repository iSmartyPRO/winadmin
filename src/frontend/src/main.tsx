import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App as AntApp, ConfigProvider } from 'antd'
import ruRU from 'antd/locale/ru_RU'
import { AllCommunityModule, ModuleRegistry } from 'ag-grid-community'
import { WinAdminTheme } from './theme'
import App from './App'
import './index.css'

// AG Grid v35: модули регистрируются один раз глобально.
ModuleRegistry.registerModules([AllCommunityModule])

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ConfigProvider theme={WinAdminTheme} locale={ruRU}>
      <AntApp>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </AntApp>
    </ConfigProvider>
  </StrictMode>,
)
