import { useEffect, useState } from 'react';
import api from '../lib/api';

export default function Dashboard() {
  const [stats, setStats] = useState<any>(null);

  useEffect(() => {
    api.get('/dashboard/stats').then(r => setStats(r.data)).catch(() => {});
  }, []);

  if (!stats) return <p className="text-gray-400">加载中...</p>;

  const { order_stats, prep_stats, prep_rate, inventory_alerts, today_ops } = stats;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-6">生产看板</h1>

      {/* Row 1: Production Status */}
      <div className="grid grid-cols-2 gap-6 mb-6">
        {/* Order Status */}
        <div className="bg-white rounded-lg shadow p-6">
          <h2 className="text-lg font-bold mb-4">生产订单状态</h2>
          <div className="grid grid-cols-4 gap-4 text-center">
            {[
              { label: '总数', value: order_stats.total, color: 'text-gray-700' },
              { label: '待处理', value: order_stats.pending, color: 'text-yellow-600' },
              { label: '进行中', value: order_stats.in_progress, color: 'text-blue-600' },
              { label: '已完成', value: order_stats.done, color: 'text-green-600' },
            ].map(s => (
              <div key={s.label}>
                <div className={`text-3xl font-bold ${s.color}`}>{s.value}</div>
                <div className="text-xs text-gray-500 mt-1">{s.label}</div>
              </div>
            ))}
          </div>
        </div>

        {/* Prep Status */}
        <div className="bg-white rounded-lg shadow p-6">
          <h2 className="text-lg font-bold mb-4">备料状态</h2>
          <div className="grid grid-cols-3 gap-4 text-center">
            {[
              { label: '待备料', value: prep_stats.pending, color: 'text-yellow-600' },
              { label: '已完成', value: prep_stats.done, color: 'text-green-600' },
              { label: '完成率', value: `${prep_rate}%`, color: 'text-blue-600' },
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
