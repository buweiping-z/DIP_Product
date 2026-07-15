import { useEffect, useState } from 'react';
import api from '../lib/api';
import HelpButton from '../lib/HelpButton';

export default function ReturnList() {
  const [data, setData] = useState<any[]>([]);
  const [detailData, setDetailData] = useState<any>(null);
  const [showDetail, setShowDetail] = useState(false);

  const fetchData = () => {
    api.get('/return?page=1&page_size=50').then(r => setData(r.data?.items || []));
  };
  useEffect(() => { fetchData(); }, []);

  const showDetailFn = async (id: number) => {
    try {
      const res = await api.get(`/return/${id}`);
      setDetailData(res.data);
      setShowDetail(true);
    } catch {}
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">退料记录</h1>
        <HelpButton title="退料记录" sections={[
          { title: '功能概述', items: ['查看手机端退料操作记录', '每种物料为一条记录，显示料号、数量、退回库位', '点击"详情"查看完整明细'] },
          { title: '操作流程', items: ['手机端：扫部品→扫库位→逐件扫退料→退料完成', '在本页面查看退料记录'] }
        ]} />
      </div>
      <p className="text-gray-500 text-sm mb-4">手机端扫码退料的操作记录</p>

      <table className="w-full bg-white rounded-lg shadow">
        <thead><tr className="bg-gray-50 text-left text-sm">
          <th className="p-3">料号</th>
          <th className="p-3 text-right">数量</th>
          <th className="p-3">退回库位</th>
          <th className="p-3">时间</th>
          <th className="p-3 w-16">操作</th>
        </tr></thead>
        <tbody>{data.length === 0 ? (
          <tr><td colSpan={5} className="p-6 text-center text-gray-400">暂无退料记录</td></tr>
        ) : data.map(o => (
          o.items?.map((item: any, idx: number) => (
            <tr key={`${o.id}-${idx}`} className="border-t hover:bg-gray-50 text-sm">
              <td className="p-3 font-mono">{item.part_no}</td>
              <td className="p-3 text-right">{item.quantity}</td>
              <td className="p-3 font-mono text-xs">{item.location_code || `ID:${item.target_location_id}`}</td>
              <td className="p-3 text-xs text-gray-500">{o.created_at?.slice(0, 19)}</td>
              <td className="p-3">
                {idx === 0 && (
                  <button onClick={() => showDetailFn(o.id)} className="text-blue-600 hover:text-blue-800 text-sm">详情</button>
                )}
              </td>
            </tr>
          ))
        ))}</tbody>
      </table>

      {showDetail && detailData && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[400px] max-h-[80vh] overflow-auto">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-bold">退料详情</h2>
              <button onClick={() => { setShowDetail(false); setDetailData(null); }}
                className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
            </div>
            <div className="text-sm mb-2">
              <span className="text-gray-500">退料时间</span>
              <p>{detailData.created_at?.slice(0, 19)}</p>
            </div>
            <h3 className="font-medium mt-4 mb-2">退料明细</h3>
            <table className="w-full border text-sm">
              <thead><tr className="bg-gray-100"><th className="p-2 text-left">料号</th><th className="p-2 text-right">数量</th><th className="p-2 text-left">库位</th></tr></thead>
              <tbody>{(detailData.items || []).map((item: any, idx: number) => (
                <tr key={idx} className="border-t">
                  <td className="p-2 font-mono">{item.part_no}</td>
                  <td className="p-2 text-right">{item.quantity}</td>
                  <td className="p-2 font-mono text-xs">{item.location_code || `ID:${item.target_location_id}`}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
