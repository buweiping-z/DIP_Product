import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import HelpButton from '../lib/HelpButton';

const STATUS_MAP: Record<number, string> = { 1: '正常', 2: '异常', 3: '已修正' };

export default function OnlineList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [partNo, setPartNo] = useState('');
  const [prodOrderNo, setProdOrderNo] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [msg, setMsg] = useState('');

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params: any = { page: 1, page_size: 100 };
      if (partNo) params.part_no = partNo;
      if (startDate) params.start_date = startDate;
      if (endDate) params.end_date = endDate;
      setData((await api.get('/online', { params })).data?.items || []);
    } catch (err: any) {
      setMsg('查询失败: ' + (err.response?.data?.message || err.message));
    } finally { setLoading(false); }
  }, [partNo, startDate, endDate]);

  useEffect(() => { fetchData(); }, []);

  const handleSearch = () => fetchData();

  const handleClear = () => {
    setPartNo('');
    setProdOrderNo('');
    setStartDate('');
    setEndDate('');
  };

  // 前端按订单号过滤（后端暂未加该筛选参数）
  const filtered = prodOrderNo
    ? data.filter((c: any) => c.prod_order_no?.includes(prodOrderNo) || c.prep_order_no?.includes(prodOrderNo))
    : data;

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">上线确认记录</h1>
        <HelpButton title="上线确认" sections={[
          { title: '功能概述', items: ['查看所有上线核销记录', '按料号、工位、订单号、日期范围筛选', '状态列：正常(绿)/异常(黄)/已修正(灰)', '每条记录显示生产订单号、产品名称、备料单号等完整上下文'] },
          { title: '操作流程', items: ['手机端上线功能扫描核销后自动生成记录', '上线完成后订单状态自动变为"已完成"', '异常数据启动时自动清洗（清空无效库位/工位）'] }
        ]} />
      </div>

      <div className="bg-white rounded-lg shadow p-4 mb-4">
        <div className="flex flex-wrap gap-4 items-end">
          <div>
            <label className="block text-sm text-gray-600 mb-1">料号</label>
            <input className="border rounded px-3 py-1.5 w-40" placeholder="输入料号"
              value={partNo} onChange={e => setPartNo(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">订单号</label>
            <input className="border rounded px-3 py-1.5 w-44" placeholder="生产/备料单号"
              value={prodOrderNo} onChange={e => setProdOrderNo(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">开始时间</label>
            <input type="date" className="border rounded px-3 py-1.5 w-36"
              value={startDate} onChange={e => setStartDate(e.target.value)} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">结束时间</label>
            <input type="date" className="border rounded px-3 py-1.5 w-36"
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
        <table className="w-full bg-white rounded-lg shadow text-sm">
          <thead><tr className="bg-gray-50 text-left">
            <th className="p-3">生产订单</th>
            <th className="p-3">产品名称</th>
            <th className="p-3">备料单号</th>
            <th className="p-3">料号</th>
            <th className="p-3">上线数量</th>
            <th className="p-3">状态</th>
            <th className="p-3">确认时间</th>
          </tr></thead>
          <tbody>{filtered.map(c => (
            <tr key={c.id} className={`border-t hover:bg-gray-50 ${c.status !== 1 ? 'bg-yellow-50' : ''}`}>
              <td className="p-3 font-mono text-xs">{c.prod_order_no || '-'}</td>
              <td className="p-3">{c.product_name || '-'}</td>
              <td className="p-3 font-mono text-xs">{c.prep_order_no || c.prep_order_id}</td>
              <td className="p-3 font-mono text-xs">{c.part_no}</td>
              <td className="p-3 text-center">{c.loaded_qty}</td>
              <td className="p-3">
                <span className={`px-2 py-0.5 rounded text-xs text-white ${c.status === 1 ? 'bg-green-500' : c.status === 2 ? 'bg-yellow-500' : 'bg-gray-500'}`}>
                  {STATUS_MAP[c.status] || c.status}
                </span>
              </td>
              <td className="p-3 text-xs text-gray-500 whitespace-nowrap">{c.confirmed_at?.slice(0, 19)}</td>
            </tr>
          ))}</tbody>
        </table>
      )}
      {!loading && filtered.length === 0 && (
        <p className="text-gray-400 text-center mt-8">暂无上线确认记录</p>
      )}
    </div>
  );
}
