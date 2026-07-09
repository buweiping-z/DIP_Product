import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';

export default function ShelvingList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [partName, setPartName] = useState('');
  const [locationCode, setLocationCode] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [msg, setMsg] = useState('');

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params: any = { page: 1, page_size: 100 };
      if (partName) params.part_name = partName;
      if (locationCode) params.location_code = locationCode;
      if (startDate) params.start_date = startDate;
      if (endDate) params.end_date = endDate;
      setData((await api.get('/shelving/records', { params })).data?.items || []);
    } catch (err: any) {
      setMsg('查询失败: ' + (err.response?.data?.message || err.message));
    } finally { setLoading(false); }
  }, [partName, locationCode, startDate, endDate]);

  useEffect(() => { fetchData(); }, []);

  const handleSearch = () => fetchData();

  const handleClear = () => {
    setPartName('');
    setLocationCode('');
    setStartDate('');
    setEndDate('');
  };

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">上架管理</h1>

      {/* Search bar */}
      <div className="bg-white rounded-lg shadow p-4 mb-4">
        <div className="flex flex-wrap gap-4 items-end">
          <div>
            <label className="block text-sm text-gray-600 mb-1">部品名称</label>
            <input className="border rounded px-3 py-1.5 w-40" placeholder="输入部品名称"
              value={partName} onChange={e => setPartName(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">库位</label>
            <input className="border rounded px-3 py-1.5 w-36" placeholder="输入库位编码"
              value={locationCode} onChange={e => setLocationCode(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">开始时间</label>
            <input type="date" className="border rounded px-3 py-1.5 w-40"
              value={startDate} onChange={e => setStartDate(e.target.value)} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">结束时间</label>
            <input type="date" className="border rounded px-3 py-1.5 w-40"
              value={endDate} onChange={e => setEndDate(e.target.value)} />
          </div>
          <button onClick={handleSearch}
            className="bg-blue-600 text-white px-4 py-1.5 rounded hover:bg-blue-700">查询</button>
          <button onClick={handleClear}
            className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
        </div>
      </div>

      {msg && <div className="bg-red-50 text-red-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      {loading ? <p>加载中...</p> : (
        <table className="w-full bg-white rounded-lg shadow">
          <thead><tr className="bg-gray-50 text-left text-sm">
            <th className="p-3">部品编号</th>
            <th className="p-3">部品名称</th>
            <th className="p-3">库位</th>
            <th className="p-3 text-right">数量</th>
            <th className="p-3">上架时间</th>
            <th className="p-3">担当者</th>
          </tr></thead>
          <tbody>{data.map((r: any) => (
            <tr key={r.id} className="border-t hover:bg-gray-50">
              <td className="p-3 font-mono text-sm">{r.part_no}</td>
              <td className="p-3">{r.part_name}</td>
              <td className="p-3 font-mono text-sm">{r.target_location_code}</td>
              <td className="p-3 text-right">{r.quantity}</td>
              <td className="p-3 text-sm text-gray-500">{r.loaded_at?.slice(0, 19) || '-'}</td>
              <td className="p-3">{r.operator_name || '-'}</td>
            </tr>
          ))}</tbody>
        </table>
      )}
    </div>
  );
}
