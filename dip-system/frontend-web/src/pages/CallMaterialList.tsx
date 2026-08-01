import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const STATUS_MAP: Record<number, string> = { 0: '待处理', 1: '已处理', 2: '已取消' };
const PAGE_SIZE = 20;

export default function CallMaterialList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [isManager, setIsManager] = useState(false);
  const [msg, setMsg] = useState('');
  const [partNo, setPartNo] = useState('');
  const [locationCode, setLocationCode] = useState('');
  const [status, setStatus] = useState<string>('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);

  const fetchData = useCallback(async (p?: number) => {
    setLoading(true);
    try {
      const params: any = { page: p ?? page, page_size: PAGE_SIZE };
      if (partNo) params.part_no = partNo;
      if (locationCode) params.location_code = locationCode;
      if (status !== '') params.status = Number(status);
      if (startDate) params.start_date = startDate;
      if (endDate) params.end_date = endDate;
      const res = await api.get('/call-material', { params });
      setData(res.data?.items || []);
      setTotal(res.data?.total || 0);
    } catch { setMsg('加载失败'); }
    finally { setLoading(false); }
  }, [page, partNo, locationCode, status, startDate, endDate]);

  useEffect(() => { fetchData(1); }, []);

  useEffect(() => {
    api.get('/auth/me').then(r => {
      if (r.code === 0 && r.data) {
        const role = (r.data.role_code || '').toLowerCase();
        setIsManager(role === 'admin' || role === 'leader');
      }
    }).catch(() => {});
  }, []);

  const handleSearch = () => { setPage(1); fetchData(1); };

  const handleStatusChange = async (id: number, newStatus: number) => {
    try {
      await api.put(`/call-material/${id}/status`, { status: newStatus });
      showToast('状态更新成功', 'success');
      fetchData(page);
    } catch {}
  };

  const handleDelete = async (id: number) => {
    if (!confirm('确认删除此叫料记录？')) return;
    try {
      await api.delete(`/call-material/${id}`);
      showToast('删除成功', 'success');
      fetchData(page);
    } catch {}
  };

  const handleExport = async () => {
    try {
      const params: any = {};
      if (partNo) params.part_no = partNo;
      if (locationCode) params.location_code = locationCode;
      if (status !== '') params.status = Number(status);
      if (startDate) params.start_date = startDate;
      if (endDate) params.end_date = endDate;
      const blob = await api.get('/call-material/export', { params, responseType: 'blob' });
      const url = URL.createObjectURL(blob as any);
      const a = document.createElement('a'); a.href = url; a.download = 'call_material_export.xlsx'; a.click();
      URL.revokeObjectURL(url);
    } catch {}
  };

  const totalPages = Math.ceil(total / PAGE_SIZE);

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">叫料管理</h1>
        <HelpButton title="叫料管理" sections={[
          { title: '功能概述', items: ['管理手机端提交的叫料请求', '库位上料不足时，操作员通过手机端提交叫料请求', '部管根据叫料记录及时补货'] },
          { title: '状态说明', items: ['待处理(0)：刚提交，部管未处理', '已处理(1)：部管已完成补料', '已取消(2)：叫料请求被取消'] }
        ]} />
      </div>

      {msg && <div className="bg-blue-50 text-blue-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      {/* 搜索栏 */}
      <div className="bg-white rounded-lg shadow p-4 mb-4">
        <div className="flex flex-wrap gap-4 items-end">
          <div>
            <label className="block text-sm text-gray-600 mb-1">料号</label>
            <input className="border rounded px-3 py-1.5 w-40" placeholder="模糊搜索料号"
              value={partNo} onChange={e => setPartNo(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">库位</label>
            <input className="border rounded px-3 py-1.5 w-36" placeholder="模糊搜索库位"
              value={locationCode} onChange={e => setLocationCode(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">状态</label>
            <select className="border rounded px-3 py-1.5 w-28" value={status}
              onChange={e => setStatus(e.target.value)}>
              <option value="">全部</option>
              <option value="0">待处理</option>
              <option value="1">已处理</option>
              <option value="2">已取消</option>
            </select>
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">开始日期</label>
            <input type="date" className="border rounded px-3 py-1.5 w-36"
              value={startDate} onChange={e => setStartDate(e.target.value)} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">结束日期</label>
            <input type="date" className="border rounded px-3 py-1.5 w-36"
              value={endDate} onChange={e => setEndDate(e.target.value)} />
          </div>
          <div className="flex gap-2">
            <button onClick={handleSearch} className="bg-blue-600 text-white px-4 py-1.5 rounded hover:bg-blue-700">搜索</button>
            <button onClick={() => { setPartNo(''); setLocationCode(''); setStatus(''); setStartDate(''); setEndDate(''); setPage(1); }}
              className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
            <button onClick={handleExport} className="bg-green-600 text-white px-4 py-1.5 rounded hover:bg-green-700">导出Excel</button>
          </div>
        </div>
      </div>

      {loading ? <p>加载中...</p> : (
        <>
          <table className="w-full bg-white rounded-lg shadow">
            <thead>
              <tr className="bg-gray-50 text-left text-sm">
                <th className="p-3">编号</th>
                <th className="p-3">料号</th>
                <th className="p-3">库位</th>
                <th className="p-3">状态</th>
                <th className="p-3">叫料时间</th>
                <th className="p-3 w-40">操作</th>
              </tr>
            </thead>
            <tbody>
              {data.map((r: any) => (
                <tr key={r.id} className="border-t hover:bg-gray-50">
                  <td className="p-3 text-sm font-mono">{r.id}</td>
                  <td className="p-3 font-mono text-sm">{r.part_no}</td>
                  <td className="p-3 text-sm">{r.location_code}</td>
                  <td className="p-3">
                    <span className={`text-xs px-2 py-0.5 rounded ${
                      r.status === 0 ? 'bg-yellow-100 text-yellow-800' :
                      r.status === 1 ? 'bg-green-100 text-green-800' :
                      'bg-gray-100 text-gray-500'}`}>
                      {STATUS_MAP[r.status] || r.status}
                    </span>
                  </td>
                  <td className="p-3 text-sm text-gray-500">{r.created_at}</td>
                  <td className="p-3 space-x-1 whitespace-nowrap">
                    {r.status === 0 && (
                      <>
                        <button onClick={() => handleStatusChange(r.id, 1)}
                          className="text-green-600 hover:text-green-800 text-sm">标记已处理</button>
                        <button onClick={() => handleStatusChange(r.id, 2)}
                          className="text-gray-500 hover:text-gray-700 text-sm">取消</button>
                      </>
                    )}
                    {r.status === 1 && isManager && (
                      <button onClick={() => handleStatusChange(r.id, 0)}
                        className="text-yellow-600 hover:text-yellow-800 text-sm">撤销</button>
                    )}
                    {r.status === 2 && isManager && (
                      <button onClick={() => handleStatusChange(r.id, 0)}
                        className="text-blue-600 hover:text-blue-800 text-sm">恢复待处理</button>
                    )}
                    {isManager && (
                      <button onClick={() => handleDelete(r.id)}
                        className="text-red-500 hover:text-red-700 text-sm">删除</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* 分页 */}
          <div className="flex justify-between items-center mt-4 text-sm text-gray-600">
            <span>共 <strong>{total}</strong> 条，第 <strong>{page}</strong> / <strong>{totalPages || 1}</strong> 页</span>
            <div className="flex gap-2">
              <button onClick={() => { const p = page - 1; setPage(p); fetchData(p); }}
                disabled={page <= 1}
                className="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-30 disabled:cursor-not-allowed">上一页</button>
              <button onClick={() => { const p = page + 1; setPage(p); fetchData(p); }}
                disabled={page >= totalPages}
                className="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-30 disabled:cursor-not-allowed">下一页</button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
