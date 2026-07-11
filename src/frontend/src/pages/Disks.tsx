import { Card, Col, Row, Skeleton, Tag, Typography, Empty } from 'antd'
import { HddOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import PageHeader from '../components/PageHeader'
import DiskCard from '../components/DiskCard'
import { formatBytes } from '../utils/format'

const { Text } = Typography

export default function Disks() {
  const { data: disks, loading, refresh } = useApi(api.disks)

  return (
    <>
      <PageHeader title="Диски и разделы" subtitle="Физические диски и логические тома" onRefresh={refresh} loading={loading} />
      {loading && !disks ? (
        <Skeleton active paragraph={{ rows: 6 }} />
      ) : !disks?.length ? (
        <Empty description="Диски не найдены" />
      ) : (
        disks.map((disk, i) => (
          <Card
            key={i}
            variant="borderless"
            className="sp-glass"
            style={{ marginBottom: 16 }}
            title={
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <HddOutlined style={{ color: '#7aa0ff' }} />
                <span>{disk.model}</span>
                {disk.interfaceType && <Tag>{disk.interfaceType}</Tag>}
                {disk.mediaType && <Tag color="blue">{disk.mediaType}</Tag>}
                <Text type="secondary" style={{ fontWeight: 400 }}>{formatBytes(disk.sizeBytes)}</Text>
              </div>
            }
          >
            {disk.volumes.length === 0 ? (
              <Text type="secondary">Нет смонтированных томов (служебные разделы)</Text>
            ) : (
              <Row gutter={[16, 16]}>
                {disk.volumes.map((v) => (
                  <Col xs={24} sm={12} lg={8} key={v.drive}>
                    <DiskCard volume={v} />
                  </Col>
                ))}
              </Row>
            )}
          </Card>
        ))
      )}
    </>
  )
}
