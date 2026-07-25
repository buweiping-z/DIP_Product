import { useEffect, useState, useRef } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const PAGE_SIZE = 50;
const STATUS_MAP: Record<number, string> = { 1: '进行中', 2: '已完成' };

export default function ChangeoverList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [filterProductName, setFilterProductName] = useState('');
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const fetchData = async (p?: number, pn?: string) => {
    setLoading(true);
    try {
      const params: any = { page: p ?? page, page_size: PAGE_SIZE };
      const searchPn = pn !== undefined ? pn : filterProductName;
      if (searchPn) params.product_name = searchPn;
      const res = await api.get('/changeover/batches/list', { params });
      setData(res.data?.items || []);
      setTotal(res.data?.total || 0);
    } finally { setLoading(false); }
  };

  useEffect(() => { fetchData(); }, []);

  // 搜索防抖：直接调 api.get，不通过外部闭包
  useEffect(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => {
      setPage(1);
      fetchData(1, filterProductName);
    }, 300);
    return () => { if (timerRef.current) clearTimeout(timerRef.current); };
  }, [filterProductName]);

  const handleClear = () => {
    setFilterProductName('');
  };

  const handleDelete = async (batchNo: string) => {
    if (!confirm('确认删除此切替批次？关联的扫描记录也会一并删除。')) return;
    try {
      const res = await api.delete(`/changeover/batches/${batchNo}`);
      if (res.code !== 0) { showToast(res.message || '删除失败', 'error'); return; }
      showToast('批次已删除', 'success');
      fetchData(page);
    } catch {}
  };

  const statusTag = (s: number) => {
    const cls = s === 1 ? 'bg-yellow-100 text-yellow-700' : 'bg-green-100 text-green-700';
    return <span className={`px-2 py-0.5 rounded text-xs ${cls}`}>{STATUS_MAP[s] || s}</span>;
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">途中切替订单</h1>
        <HelpButton title="途中切替" sections={[
          { title: '功能概述', items: ['管理途中切替批次订单，手机端扫描订单号创建批次后逐袋确认', '网页端可查看、搜索、删除多余的切替批次'] },
          { title: '操作流程', items: ['1. 手机端扫订单号条码 → 自动创建切替批次', '2. 逐袋扫部品条码确认', '3. 全部确认后自动完成', '4. 网页端查看/删除批次'] }
        ]} />
      </div>

      {/* 搜索栏 */}
      <div className="bg-white rounded-lg shadow p-4 mb-4 flex gap-4 items-end">
        <div>
          <label className="block text-sm text-gray-600 mb-1">产品名称</label>
          <input className="border rounded px-3 py-1.5 w-56" placeholder="模糊搜索产品名称"
            value={filterProductName}
            onChange={e => setFilterProductName(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && (() => { setPage(1); fetchData(1); })()} />
        </div>
        <button onClick={handleClear}
          className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
      </div>

      {loading ? <p>加载中...</p> : (
        <>
          <table className="w-full bg-white rounded-lg shadow text-sm">
            <thead><tr className="bg-gray-50 text-left">
              <th className="p-3">批次号</th>
              <th className="p-3">产品名称</th>
              <th className="p-3 text-center">状态</th>
              <th className="p-3 text-center">料号数</th>
              <th className="p-3 text-center">已确认</th>
              <th className="p-3">创建时间</th>
              <th className="p-3 w-20">操作</th>
            </tr></thead>
            <tbody>{data.length === 0 ? (
              <tr><td colSpan={7} className="p-6 text-center text-gray-400">暂无记录</td></tr>
            ) : data.map((b: any) => (
              <tr key={b.id} className="border-t hover:bg-gray-50">
                <td className="p-3 font-mono text-xs">{b.batch_no}</td>
                <td className="p-3">{b.product_name}</td>
                <td className="p-3 text-center">{statusTag(b.status)}</td>
                <td className="p-3 text-center">{b.bom_count}</td>
                <td className="p-3 text-center">{b.scanned_count}</td>
                <td className="p-3 text-xs text-gray-500">{b.created_at?.slice(0, 19)}</td>
                <td className="p-3">
                  <button onClick={() => handleDelete(b.batch_no)}
                    className="text-red-500 hover:text-red-700 text-xs">删除</button>
                </td>
              </tr>
            ))}</tbody>
          </table>

          <div className="flex justify-between items-center mt-4 text-sm text-gray-600">
            <span>共 <strong>{total}</strong> 条，第 <strong>{page}</strong> / <strong>{Math.ceil(total / PAGE_SIZE) || 1}</strong> 页</span>
            <div className="flex gap-2">
              <button onClick={() => { const p = page - 1; setPage(p); fetchData(p); }}
                disabled={page <= 1}
                className="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-30">上一页</button>
              <button onClick={() => { const p = page + 1; setPage(p); fetchData(p); }}
                disabled={page >= Math.ceil(total / PAGE_SIZE)}
                className="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-30">下一页</button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
