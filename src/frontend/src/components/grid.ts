import { themeQuartz } from 'ag-grid-community'

// Единая тёмная тема AG Grid v35 (Theming API), согласованная с AntD.
export const gridTheme = themeQuartz.withParams({
  backgroundColor: '#161b22',
  foregroundColor: '#e6edf3',
  headerBackgroundColor: '#11161e',
  headerTextColor: '#9aa4b2',
  borderColor: '#222a36',
  oddRowBackgroundColor: '#12171f',
  rowHoverColor: 'rgba(79,124,255,0.08)',
  accentColor: '#4f7cff',
  fontFamily: "'Inter', sans-serif",
  fontSize: 13,
  headerFontWeight: 600,
})

export const defaultColDef = {
  sortable: true,
  filter: true,
  resizable: true,
  flex: 1,
}
