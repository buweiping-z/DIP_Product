import { useEffect, useState } from 'react';
import api from '../lib/api';

const STATUS_MAP = ['', '待备料', '已完成', '已撤销'];
const PREP_DETAIL_STATUS = ['', '待备料', '已完成'];
const KIT_MAP = ['', '齐套', '短缺', '严重短缺'];

export default function PrepList() {
  const [data, setData] = useState<any[]>([]);
  const [detailData, setDetailData] = useState<any>(null);
  const [showDetail, setShowDetail] = useState(false);

  const fetchData = async () => {
    try {
      const r = await api.get('/prep?page=1&page_size=50');
      setData(r.data?.items || []);
    } catch {}
  };
  useEffect(() => { fetchData(); }, []);

  const fetchDetail = async (id: number) => {
    try {
      const res = await api.get(`/prep/${id}/details`);
      setDetailData(res.data || {});
      setShowDetail(true);
    } catch (err: any) {
      alert('获取详情失败: ' + (err.response?.data?.message || err.message));
    }
  };

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">备料管理</h1>
      <table className="w-full bg-white rounded-lg shadow">
        <thead><tr className="bg-gray-50 text-left text-sm">
          <th className="p-3">备料单号</th>
          <th className="p-3">关联工单ID</th>
          <th className="p-3">产线ID</th>
          <th className="p-3">状态</th>
          <th className="p-3">齐套结果</th>
          <th className="p-3 w-20">操作</th>
        </tr></thead>
        <tbody>{data.map(p => (
          <tr key={p.id} className="border-t hover:bg-gray-50">
            <td className="p-3 font-mono text-sm">{p.order_no}</td>
            <td className="p-3">{p.production_order_id}</td>
            <td className="p-3">{p.line_id}</td>
            <td className="p-3">{STATUS_MAP[p.status] || p.status}</td>
            <td className="p-3">{KIT_MAP[p.kit_check_result] || '-'}</td>
            <td className="p-3">
              <button onClick={() => fetchDetail(p.id)} className="text-blue-600 hover:text-blue-800 text-sm">详情</button>
            </td>
          </tr>
        ))}</tbody>
      </table>

      {/* Detail Dialog */}
      {showDetail && detailData && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[700px] max-h-[90vh] overflow-auto">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-xl font-bold">备料单详情</h2>
              <button onClick={() => { setShowDetail(false); setDetailData(null); }}
                className="text-gray-400 hover:text-gray-600 text-2xl leading-none">&times;</button>
            </div>

            {/* Basic Info */}
            <div className="grid grid-cols-3 gap-4 mb-6">
              <div><span className="text-gray-500 text-sm">备料单号</span><p className="font-mono text-sm">{detailData.order_no}</p></div>
              <div><span className="text-gray-500 text-sm">关联工单ID</span><p>{detailData.production_order_id}</p></div>
              <div><span className="text-gray-500 text-sm">产线ID</span><p>{detailData.line_id}</p></div>
              <div><span className="text-gray-500 text-sm">产品名称</span><p className="font-medium">{detailData.product_name || '-'}</p></div>
              <div><span className="text-gray-500 text-sm">计划数量</span><p>{detailData.plan_qty}</p></div>
              <div><span className="text-gray-500 text-sm">状态</span><p>{STATUS_MAP[detailData.status] || detailData.status}</p></div>
              <div><span className="text-gray-500 text-sm">齐套结果</span><p>{KIT_MAP[detailData.kit_check_result] || '-'}</p></div>
              <div><span className="text-gray-500 text-sm">创建时间</span><p className="text-sm">{detailData.created_at?.slice(0, 19)}</p></div>
            </div>

            {/* Prep Details */}
            <h3 className="font-medium mb-2">备料明细</h3>
            {(detailData.details || []).length > 0 ? (
              <table className="w-full border text-sm">
                <thead><tr className="bg-gray-100">
                  <th className="p-2 text-left">#</th>
                  <th className="p-2 text-left">料号</th>
                  <th className="p-2 text-right">总需求数量</th>
                  <th className="p-2 text-right">已备数量</th>
                  <th className="p-2 text-left">库存位置</th>
                  <th className="p-2 text-right">库存数量</th>
                  <th className="p-2 text-center">状态</th>
                </tr></thead>
                <tbody>{(detailData.details || []).map((d: any, idx: number) => (
                  <tr key={d.id} className="border-t">
                    <td className="p-2">{idx + 1}</td>
                    <td className="p-2 font-mono">{d.part_no}</td>
                    <td className="p-2 text-right font-medium">{d.total_required_qty}</td>
                    <td className="p-2 text-right">{d.actual_qty}</td>
                    <td className="p-2 text-sm text-gray-500">
                      {(d.stocks || []).length > 0
                        ? d.stocks.map((s: any) => s.location_code).join(', ')
                        : '-'}
                    </td>
                    <td className="p-2 text-right">
                      {(d.stocks || []).length > 0
                        ? d.stocks.map((s: any) => s.available_qty).join(', ')
                        : '-'}
                    </td>
                    <td className="p-2 text-center">{PREP_DETAIL_STATUS[d.status] || d.status}</td>
                  </tr>
                ))}</tbody>
              </table>
            ) : <p className="text-gray-400 text-sm">无备料明细</p>}
          </div>
        </div>
      )}
    </div>
  );
}
