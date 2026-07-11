import { useEffect, useState, useRef, useCallback } from 'react';
import api from '../lib/api';
import HelpButton from '../lib/HelpButton';

export default function InventoryList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [msg, setMsg] = useState('');
  const [partNo, setPartNo] = useState('');
  const [locationCode, setLocationCode] = useState('');
  const [isManager, setIsManager] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const timerRef = useRef<any>(null);

  // import report
  const [report, setReport] = useState<any>(null);

  useEffect(() => {
    api.get('/auth/me').then(r => {
      if (r.code === 0 && r.data) {
        const role = (r.data.role_code || '').toLowerCase();
        setIsManager(role === 'admin' || role === 'leader');
      }
    }).catch(() => {});
  }, []);

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

  useEffect(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => fetchData(), 300);
  }, [partNo, locationCode]);

  const handleImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]; if (!file) return;
    const fd = new FormData(); fd.append('file', file);
    try {
      const res = await api.post('/inventory/import', fd, { headers: { 'Content-Type': 'multipart/form-data' }, timeout: 60000 });
      const result = res.data;
      setReport(result);
      setMsg(`导入完成: 成功 ${result.success_count || 0} 条, 跳过 ${result.skip_count || 0} 条`);
      fetchData();
    } catch (err: any) { setMsg('导入失败: ' + (err.message || '')); }
    e.target.value = '';
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">库存管理</h1>
        <div className="flex gap-2">
          <HelpButton title="库存管理" sections={[{ title: '功能概述', items: ['查看所有物料的库存状态：总数量、可用数量、冻结数量', '按料号或库位编码搜索过滤', '管理员可通过 Excel 导入库存数据', '库存数量由系统自动计算（备料冻结/上线消耗/出库扣减），禁止手动修改'] }, { title: '操作流程', items: ['1. 点击"导入库存"上传 Excel（管理员）→ 系统自动校验并写入', '2. 下载模板 → 按模板格式填写 → 上传', '3. 搜索栏输入料号或库位快速定位', '4. 可用数量 = 总数量 - 冻结数量，冻结来自备料扫描'] }]} />
          {isManager && (
            <button onClick={() => fileRef.current?.click()} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">导入库存</button>
          )}
          <a href="/api/v1/inventory/template" className="bg-gray-500 text-white px-4 py-2 rounded hover:bg-gray-600">下载模板</a>
          <input ref={fileRef} type="file" accept=".xlsx,.xls" className="hidden" onChange={handleImport} />
        </div>
      </div>

      {/* Search bar */}
      <div className="bg-white rounded-lg shadow p-4 mb-4 flex gap-4 items-end">
        <div>
          <label className="block text-sm text-gray-600 mb-1">料号</label>
          <input className="border rounded px-3 py-1.5 w-48" placeholder="输入料号搜索" value={partNo}
            onChange={e => setPartNo(e.target.value)} onKeyDown={e => e.key === 'Enter' && fetchData()} />
        </div>
        <div>
          <label className="block text-sm text-gray-600 mb-1">库位编码</label>
          <input className="border rounded px-3 py-1.5 w-48" placeholder="输入库位搜索" value={locationCode}
            onChange={e => setLocationCode(e.target.value)} onKeyDown={e => e.key === 'Enter' && fetchData()} />
        </div>
        <button onClick={() => { setPartNo(''); setLocationCode(''); }}
          className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
      </div>

      {msg && <div className="bg-blue-50 text-blue-800 p-2 rounded mb-3 text-sm" onClick={() => setMsg('')}>{msg}</div>}

      {/* Import Report */}
      {report && report.details && report.details.length > 0 && (
        <div className="bg-yellow-50 rounded-lg shadow p-4 mb-4">
          <div className="flex justify-between items-center mb-2">
            <h3 className="font-bold text-sm">导入报告: 成功 {report.success_count} / 跳过 {report.skip_count}</h3>
            <button onClick={() => setReport(null)} className="text-gray-400 hover:text-gray-600">&times;</button>
          </div>
          <table className="w-full text-xs">
            <thead><tr className="text-left bg-yellow-100">
              <th className="p-1">行</th><th className="p-1">料号</th><th className="p-1">库位</th><th className="p-1">跳过原因</th>
            </tr></thead>
            <tbody>
              {report.details.map((d: any, i: number) => (
                <tr key={i} className="border-t border-yellow-200">
                  <td className="p-1">{d.row}</td><td className="p-1 font-mono">{d.part_no}</td>
                  <td className="p-1 font-mono">{d.location_code}</td><td className="p-1 text-red-600">{d.reason}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

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
