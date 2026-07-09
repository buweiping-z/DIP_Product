import { useEffect, useState, useRef, useCallback } from 'react';
import api from '../lib/api';

export default function InventoryList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [msg, setMsg] = useState('');
  const [partNo, setPartNo] = useState('');
  const [locationCode, setLocationCode] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);
  const timerRef = useRef<any>(null);

  const fetchData = useCallback(async (pn?: string, lc?: string) => {
    setLoading(true);
    try {
      const params: any = { page: 1, page_size: 100 };
      if (pn ?? partNo) params.part_no = pn ?? partNo;
      if (lc ?? locationCode) params.location_code = lc ?? locationCode;
      setData((await api.get('/inventory', { params })).data?.items || []);
    } finally { setLoading(false); }
  }, [partNo, locationCode]);

  useEffect(() => { fetchData(); }, []);

  // 输入即搜，300ms 防抖
  useEffect(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => fetchData(), 300);
  }, [partNo, locationCode]);

  const handleImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]; if (!file) return;
    const fd = new FormData(); fd.append('file', file);
    try {
      const res = await api.post('/inventory/import', fd, { headers: { 'Content-Type': 'multipart/form-data' }, timeout: 60000 });
      setMsg(`导入成功: ${res.data?.success_count || 0} 条`);
      fetchData();
    } catch (err: any) { setMsg('导入失败: ' + (err.response?.data?.message || err.message)); }
    e.target.value = '';
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">库存管理</h1>
        <div className="flex gap-2">
          <button onClick={() => fileRef.current?.click()} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">导入库存</button>
          <a href="/api/v1/inventory/template" className="bg-gray-500 text-white px-4 py-2 rounded hover:bg-gray-600">下载模板</a>
          <input ref={fileRef} type="file" accept=".xlsx,.xls" className="hidden" onChange={handleImport} />
        </div>
      </div>

      {/* Search bar */}
      <div className="bg-white rounded-lg shadow p-4 mb-4 flex gap-4 items-end">
        <div>
          <label className="block text-sm text-gray-600 mb-1">料号</label>
          <input className="border rounded px-3 py-1.5 w-48" placeholder="输入料号搜索" value={partNo}
            onChange={e => setPartNo(e.target.value)} onKeyDown={e => e.key === 'Enter' && handleSearch()} />
        </div>
        <div>
          <label className="block text-sm text-gray-600 mb-1">库位编码</label>
          <input className="border rounded px-3 py-1.5 w-48" placeholder="输入库位搜索" value={locationCode}
            onChange={e => setLocationCode(e.target.value)} onKeyDown={e => e.key === 'Enter' && handleSearch()} />
        </div>
        <button onClick={() => { setPartNo(''); setLocationCode(''); }}
          className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
      </div>

      {msg && <div className="bg-green-50 text-green-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      {loading ? <p>加载中...</p> : (
        <table className="w-full bg-white rounded-lg shadow">
          <thead><tr className="bg-gray-50 text-left text-sm">
            <th className="p-3">料号</th><th className="p-3">物料名称</th><th className="p-3">库位</th>
            <th className="p-3 text-right">总数量</th><th className="p-3 text-right">可用</th><th className="p-3 text-right">冻结</th>
          </tr></thead>
          <tbody>{data.map(i => (
            <tr key={i.id} className="border-t hover:bg-gray-50">
              <td className="p-3 font-mono text-sm">{i.part_no}</td>
              <td className="p-3">{i.part_name}</td>
              <td className="p-3 font-mono text-sm">{i.location_code}</td>
              <td className="p-3 text-right">{i.total_qty}</td>
              <td className="p-3 text-right text-green-600">{i.available_qty}</td>
              <td className="p-3 text-right text-orange-600">{i.frozen_qty}</td>
            </tr>
          ))}</tbody>
        </table>
      )}
    </div>
  );
}
