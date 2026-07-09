import { useEffect, useState } from 'react';
import api from '../lib/api';
const SMAP = ['', '待处理', '已处理'];
const TMAP: Record<number, string> = { 1: '库存短缺', 2: '质量异常', 3: '设备故障', 4: '操作失误', 5: '其他' };

export default function AbnormalList() {
  const [data, setData] = useState<any[]>([]);
  useEffect(() => { api.get('/abnormal?page=1&page_size=50').then(r => setData(r.data?.items || [])); }, []);
  return (
    <div><h1 className="text-2xl font-bold mb-4">异常管理</h1>
      <table className="w-full bg-white rounded-lg shadow"><thead><tr className="bg-gray-50 text-left text-sm">
        <th className="p-3">类型</th><th className="p-3">描述</th><th className="p-3">严重程度</th><th className="p-3">状态</th><th className="p-3">创建时间</th></tr></thead>
        <tbody>{data.map(r => (<tr key={r.id} className="border-t hover:bg-gray-50"><td className="p-3">{TMAP[r.type] || r.type}</td>
          <td className="p-3 max-w-xs truncate">{r.description}</td><td className="p-3">{['','低','中','高'][r.severity] || r.severity}</td>
          <td className="p-3">{SMAP[r.status] || r.status}</td>
          <td className="p-3 text-sm text-gray-500">{r.created_at?.slice(0,19)}</td></tr>))}</tbody></table>
    </div>
  );
}
