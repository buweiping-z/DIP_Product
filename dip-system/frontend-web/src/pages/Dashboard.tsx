import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';

export default function Dashboard() {
  const [stats, setStats] = useState<any>(null);
  const [error, setError] = useState(false);
  const [lines, setLines] = useState<any[]>([]);
  const [selectedLine, setSelectedLine] = useState<number | null>(null);

  const fetchStats = useCallback((lineId?: number | null) => {
    setError(false);
    const params = lineId ? `?line_id=${lineId}` : '';
    api.get(`/dashboard/stats${params}`).then(r => setStats(r.data)).catch(() => setError(true));
  }, []);

  useEffect(() => {
    api.get('/lines').then(r => setLines(r.data?.items || r.data || [])).catch(() => {});
    fetchStats(null);
  }, [fetchStats]);

  const onLineChange = (val: string) => {
    const id = val ? Number(val) : null;
    setSelectedLine(id);
    fetchStats(id);
  };

  if (error) return (
    <div className="flex flex-col items-center justify-center h-64 gap-4">
      <p className="text-red-500">数据加载失败</p>
      <button onClick={() => fetchStats(selectedLine)} className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">重试</button>
    </div>
  );

  if (!stats) return <p className="text-gray-400">加载中...</p>;

  const { order_stats, prep_stats, prep_rate, prep_today_done, inventory_alerts, today_ops, refill_stats, changeover_stats } = stats;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-6">生产看板</h1>

      {/* Row 1: Production Status */}
      <div className="grid grid-cols-3 gap-6 mb-6">
        <div className="bg-white rounded-lg shadow p-6">
          <div className="flex justify-between items-center mb-4">
            <h2 className="text-lg font-bold">生产订单</h2>
            <select
              value={selectedLine ?? ''}
              onChange={e => onLineChange(e.target.value)}
              className="text-sm border rounded px-2 py-1 text-gray-600"
            >
              <option value="">全部产线</option>
              {lines.map((l: any) => (
                <option key={l.id} value={l.id}>{l.line_name || l.lineName}</option>
              ))}
            </select>
          </div>
          <table className="w-full text-sm text-center">
            <thead>
              <tr className="text-gray-500 text-xs">
                <th className="pb-2 text-left"></th>
                <th className="pb-2">待备料</th>
                <th className="pb-2">待上线</th>
                <th className="pb-2">已完成</th>
                <th className="pb-2">完成率</th>
              </tr>
            </thead>
            <tbody>
              {[
                { label: '本日', data: order_stats.today },
                { label: '本周', data: order_stats.week },
                { label: '本月', data: order_stats.month },
              ].map(row => (
                <tr key={row.label} className="border-t">
                  <td className="py-2 text-left font-medium text-gray-700">{row.label}</td>
                  <td className="py-2 text-yellow-600 font-bold">{row.data.pending}</td>
                  <td className="py-2 text-blue-600 font-bold">{row.data.in_progress}</td>
                  <td className="py-2 text-green-600 font-bold">{row.data.done}</td>
                  <td className="py-2 text-gray-600">{row.data.rate}%</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="bg-white rounded-lg shadow p-6">
          <h2 className="text-lg font-bold mb-4">备料状态</h2>
          <div className="grid grid-cols-4 gap-4 text-center">
            {[
              { label: '待备料', value: prep_stats.pending, color: 'text-yellow-600' },
              { label: '已完成', value: prep_stats.done, color: 'text-green-600' },
              { label: '完成率', value: `${prep_rate}%`, color: 'text-blue-600' },
              { label: '今日完成', value: prep_today_done, color: 'text-purple-600' },
            ].map(s => (
              <div key={s.label}>
                <div className={`text-3xl font-bold ${s.color}`}>{s.value}</div>
                <div className="text-xs text-gray-500 mt-1">{s.label}</div>
              </div>
            ))}
          </div>
        </div>

        <div className="bg-white rounded-lg shadow p-6">
          <div className="flex justify-between items-center mb-4">
            <h2 className="text-lg font-bold">补料任务</h2>
            <a href="/refill" className="text-blue-600 text-xs hover:underline">查看详情 →</a>
          </div>
          <div className="grid grid-cols-3 gap-4 text-center">
            {[
              { label: '未完成', value: refill_stats?.active || 0, color: 'text-orange-600' },
              { label: '已完成', value: refill_stats?.done || 0, color: 'text-green-600' },
              { label: '今日', value: refill_stats?.today || 0, color: 'text-blue-600' },
            ].map(s => (
              <div key={s.label}>
                <div className={`text-3xl font-bold ${s.color}`}>{s.value}</div>
                <div className="text-xs text-gray-500 mt-1">{s.label}</div>
              </div>
            ))}
          </div>
        </div>

        <div className="bg-white rounded-lg shadow p-6">
          <div className="flex justify-between items-center mb-4">
            <h2 className="text-lg font-bold">途中切替</h2>
            <a href="/changeover" className="text-blue-600 text-xs hover:underline">查看详情 →</a>
          </div>
          <div className="grid grid-cols-3 gap-4 text-center">
            {[
              { label: '进行中', value: changeover_stats?.active || 0, color: 'text-orange-600' },
              { label: '已完成', value: changeover_stats?.done || 0, color: 'text-green-600' },
              { label: '今日', value: changeover_stats?.today || 0, color: 'text-blue-600' },
            ].map(s => (
              <div key={s.label}>
                <div className={`text-3xl font-bold ${s.color}`}>{s.value}</div>
                <div className="text-xs text-gray-500 mt-1">{s.label}</div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Row 2: Inventory Alerts + Today Operations */}
      <div className="grid grid-cols-2 gap-6">
        {/* Inventory Alerts */}
        <div className="bg-white rounded-lg shadow p-6">
          <h2 className="text-lg font-bold mb-4">库存预警</h2>
          <div className="flex gap-6">
            <div className={`flex-1 rounded-lg p-4 text-center ${inventory_alerts.low_stock > 0 ? 'bg-yellow-50' : 'bg-gray-50'}`}>
              <div className={`text-3xl font-bold ${inventory_alerts.low_stock > 0 ? 'text-yellow-600' : 'text-gray-400'}`}>
                {inventory_alerts.low_stock}
              </div>
              <div className="text-sm text-gray-500 mt-1">低库存（&lt;10）</div>
            </div>
            <div className={`flex-1 rounded-lg p-4 text-center ${inventory_alerts.out_of_stock > 0 ? 'bg-red-50' : 'bg-gray-50'}`}>
              <div className={`text-3xl font-bold ${inventory_alerts.out_of_stock > 0 ? 'text-red-600' : 'text-gray-400'}`}>
                {inventory_alerts.out_of_stock}
              </div>
              <div className="text-sm text-gray-500 mt-1">已缺料</div>
            </div>
            <div className={`flex-1 rounded-lg p-4 text-center ${inventory_alerts.pending_replenish > 0 ? 'bg-orange-50' : 'bg-gray-50'}`}>
              <div className={`text-3xl font-bold ${inventory_alerts.pending_replenish > 0 ? 'text-orange-600' : 'text-gray-400'}`}>
                {inventory_alerts.pending_replenish}
              </div>
              <div className="text-sm text-gray-500 mt-1">待补货</div>
            </div>
          </div>
        </div>

        {/* Pending Replenish Table */}
        {(inventory_alerts.pending_replenish_items?.length > 0) && (
          <div className="bg-white rounded-lg shadow p-6 mb-6">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-bold">待补货清单</h2>
              <a href="/api/v1/dashboard/export-replenish" className="bg-green-600 text-white px-3 py-1 rounded text-sm hover:bg-green-700">导出Excel</a>
            </div>
            <table className="w-full text-sm">
              <thead><tr className="bg-orange-50 text-left">
                <th className="p-2">订单号</th>
                <th className="p-2">产品</th>
                <th className="p-2">料号</th>
                <th className="p-2">库位</th>
                <th className="p-2 text-right">需求</th>
                <th className="p-2 text-right">已冻结</th>
                <th className="p-2 text-right text-red-600">缺料</th>
              </tr></thead>
              <tbody>{inventory_alerts.pending_replenish_items.map((item: any, idx: number) => (
                <tr key={idx} className="border-t">
                  <td className="p-2 font-mono text-xs">{item.order_no}</td>
                  <td className="p-2">{item.product_name}</td>
                  <td className="p-2 font-mono text-xs">{item.part_no}</td>
                  <td className="p-2 font-mono text-xs">{(item.location_codes || []).join(', ')}</td>
                  <td className="p-2 text-right">{item.required_qty}</td>
                  <td className="p-2 text-right">{item.frozen_qty}</td>
                  <td className="p-2 text-right text-red-600 font-bold">{item.shortage}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}

        {/* Today Operations */}
        <div className="bg-white rounded-lg shadow p-6">
          <h2 className="text-lg font-bold mb-4">今日操作统计</h2>
          <div className="grid grid-cols-3 gap-4 text-center">
            {[
              { label: '备料', value: today_ops.prep_scans, icon: '📦' },
              { label: '退料', value: today_ops.returns, icon: '↩️' },
              { label: '上架', value: today_ops.shelving, icon: '📋' },
            ].map(s => (
              <div key={s.label} className="bg-gray-50 rounded-lg p-3">
                <div className="text-2xl">{s.icon}</div>
                <div className="text-2xl font-bold mt-1">{s.value}</div>
                <div className="text-xs text-gray-500">{s.label}</div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
