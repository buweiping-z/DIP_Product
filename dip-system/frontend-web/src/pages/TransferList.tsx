import { useEffect, useState } from 'react';
import api from '../lib/api';
const SMAP = ['', '待执行', '已执行', '已取消'];

export default function TransferList() {
  const [data, setData] = useState<any[]>([]);
  useEffect(() => { api.get('/transfer?page=1&page_size=50').then(r => setData(r.data?.items || [])); }, []);
  return (
    <div><h1 className="text-2xl font-bold mb-4">调拨管理</h1>
      <table className="w-full bg-white rounded-lg shadow"><thead><tr className="bg-gray-50 text-left text-sm">
        <th className="p-3">调拨单号</th><th className="p-3">状态</th><th className="p-3">物料数</th><th className="p-3">创建时间</th></tr></thead>
        <tbody>{data.map(o => (<tr key={o.id} className="border-t hover:bg-gray-50"><td className="p-3 font-mono text-sm">{o.order_no}</td>
          <td className="p-3">{SMAP[o.status] || o.status}</td><td className="p-3">{o.items?.length || 0}</td>
          <td className="p-3 text-sm text-gray-500">{o.created_at?.slice(0,19)}</td></tr>))}</tbody></table>
    </div>
  );
}
