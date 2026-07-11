import { useEffect, useState, useCallback, useMemo } from 'react';
import api from '../lib/api';
import HelpButton from '../lib/HelpButton';

const STEP_MAP: Record<number, string> = { 1: '待取料', 2: '已取料', 3: '已核对' };

export default function RefillList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [partNo, setPartNo] = useState('');
  const [locationCode, setLocationCode] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [msg, setMsg] = useState('');
  const [viewBatch, setViewBatch] = useState<string | null>(null); // 展开哪个批次

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params: any = { page: 1, page_size: 500 };
      if (partNo) params.part_no = partNo;
      if (locationCode) params.location_code = locationCode;
      if (startDate) params.start_date = startDate;
      if (endDate) params.end_date = endDate;
      setData((await api.get('/refill', { params })).data?.items || []);
    } catch (err: any) {
      setMsg('查询失败: ' + (err.response?.data?.message || err.message));
    } finally { setLoading(false); }
  }, [partNo, locationCode, startDate, endDate]);

  useEffect(() => { fetchData(); }, []);

  const handleSearch = () => fetchData();
  const handleClear = () => { setPartNo(''); setLocationCode(''); setStartDate(''); setEndDate(''); };

  // 按 product_name + prep_order_id 分组
  const batches = useMemo(() => {
    const groups: Record<string, any[]> = {};
    data.forEach(r => {
      const key = r.product_name ? `${r.product_name}|${r.prep_order_id}` : `未知|${r.prep_order_id}`;
      if (!groups[key]) groups[key] = [];
      groups[key].push(r);
    });
    return Object.entries(groups).map(([key, items]) => {
      const [product, prepId] = key.split('|');
      const detailIds = new Set(items.map(i => i.prep_detail_id));
      const totalParts = detailIds.size;
      const picked = new Set(items.filter(i => i.step >= 2).map(i => i.prep_detail_id)).size;
      const verified = new Set(items.filter(i => i.step >= 3).map(i => i.prep_detail_id)).size;
      const done = verified >= totalParts && totalParts > 0;
      const latest = items.reduce((a, b) => a.created_at > b.created_at ? a : b);
      return { key, product, prepId, items, totalParts, picked, verified, done, latest };
    }).sort((a, b) => b.latest.created_at?.localeCompare(a.latest.created_at || '') || 0);
  }, [data]);

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">补料记录</h1>
        <HelpButton title="补料管理" sections={[
          { title: '功能概述', items: ['生产线补料三步流程：勾选料号→取料扫描→核对扫描', '不涉及库存数量变更（订单创建时已冻结）', '按产品分组显示补料进度'] },
          { title: '监控说明', items: ['每个产品批次显示取料/核对进度条', '绿色=已完成，蓝色=进行中', '总零件数=该产品BOM料号数，每个料号独立计数'] }
        ]} />
      </div>

      <div className="bg-white rounded-lg shadow p-4 mb-4">
        <div className="flex flex-wrap gap-4 items-end">
          <div>
            <label className="block text-sm text-gray-600 mb-1">料号</label>
            <input className="border rounded px-3 py-1.5 w-40" placeholder="输入料号"
              value={partNo} onChange={e => setPartNo(e.target.value)} onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">库位</label>
            <input className="border rounded px-3 py-1.5 w-36" placeholder="输入库位"
              value={locationCode} onChange={e => setLocationCode(e.target.value)} onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">开始时间</label>
            <input type="date" className="border rounded px-3 py-1.5 w-36" value={startDate} onChange={e => setStartDate(e.target.value)} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">结束时间</label>
            <input type="date" className="border rounded px-3 py-1.5 w-36" value={endDate} onChange={e => setEndDate(e.target.value)} />
          </div>
          <button onClick={handleSearch} className="bg-blue-600 text-white px-4 py-1.5 rounded hover:bg-blue-700">查询</button>
          <button onClick={handleClear} className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
        </div>
      </div>

      {msg && <div className="bg-red-50 text-red-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      {loading ? <p>加载中...</p> : (
        <div className="space-y-4">
          {batches.length === 0 && <p className="text-center text-gray-400 py-8">暂无补料记录</p>}
          {batches.map(batch => (
            <div key={batch.key} className="bg-white rounded-lg shadow overflow-hidden">
              <div className={`p-4 cursor-pointer hover:bg-gray-50 ${viewBatch === batch.key ? 'border-b' : ''}`}
                onClick={() => setViewBatch(viewBatch === batch.key ? null : batch.key)}>
                <div className="flex justify-between items-center mb-2">
                  <h3 className="font-bold">{batch.product || `备料单 ${batch.prepId}`}</h3>
                  <span className={`px-2 py-0.5 rounded text-xs text-white ${batch.done ? 'bg-green-500' : 'bg-blue-500'}`}>
                    {batch.done ? '已完成' : '进行中'}
                  </span>
                </div>
                <div className="text-xs text-gray-500 mb-2">总零件: {batch.totalParts} 种 | 最近更新: {batch.latest.created_at?.slice(0, 19)}</div>
                {/* 取料进度 */}
                <div className="flex items-center gap-2 mb-1">
                  <span className="text-xs w-16">取料</span>
                  <div className="flex-1 bg-gray-200 rounded-full h-3">
                    <div className="bg-blue-500 h-3 rounded-full transition-all" style={{ width: `${batch.totalParts > 0 ? (batch.picked / batch.totalParts * 100) : 0}%` }} />
                  </div>
                  <span className="text-xs w-12 text-right">{batch.picked}/{batch.totalParts}</span>
                </div>
                {/* 核对进度 */}
                <div className="flex items-center gap-2">
                  <span className="text-xs w-16">核对</span>
                  <div className="flex-1 bg-gray-200 rounded-full h-3">
                    <div className="bg-green-500 h-3 rounded-full transition-all" style={{ width: `${batch.totalParts > 0 ? (batch.verified / batch.totalParts * 100) : 0}%` }} />
                  </div>
                  <span className="text-xs w-12 text-right">{batch.verified}/{batch.totalParts}</span>
                </div>
              </div>
              {viewBatch === batch.key && (
                <table className="w-full text-sm">
                  <thead><tr className="bg-gray-50 text-left">
                    <th className="p-2">料号</th><th className="p-2">库位</th><th className="p-2">步骤</th><th className="p-2">取料时间</th><th className="p-2">核对时间</th>
                  </tr></thead>
                  <tbody>{batch.items.map((r: any) => (
                    <tr key={r.id} className="border-t hover:bg-gray-50">
                      <td className="p-2 font-mono text-xs">{r.part_no}</td>
                      <td className="p-2 font-mono text-xs">{r.location_code || '-'}</td>
                      <td className="p-2"><span className={`px-2 py-0.5 rounded text-xs text-white ${r.step === 1 ? 'bg-yellow-500' : r.step === 2 ? 'bg-blue-500' : 'bg-green-500'}`}>{STEP_MAP[r.step]}</span></td>
                      <td className="p-2 text-xs text-gray-500">{r.picked_at?.slice(0, 19) || '-'}</td>
                      <td className="p-2 text-xs text-gray-500">{r.verified_at?.slice(0, 19) || '-'}</td>
                    </tr>
                  ))}</tbody>
                </table>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
