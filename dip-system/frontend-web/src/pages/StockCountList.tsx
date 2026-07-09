import { useEffect, useRef, useState } from 'react';
import api from '../lib/api';

export default function StockCountList() {
  const [data, setData] = useState<any[]>([]);
  const [detailData, setDetailData] = useState<any>(null);
  const [showDetail, setShowDetail] = useState(false);
  const [msg, setMsg] = useState('');
  const [uploading, setUploading] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const fetchData = () => {
    api.get('/stockcount?page=1&page_size=50').then(r => setData(r.data?.items || []));
  };
  useEffect(() => { fetchData(); }, []);

  const handleDownload = async () => {
    try {
      const res = await api.get('/stockcount/export/template', { responseType: 'blob' });
      const url = window.URL.createObjectURL(new Blob([res]));
      const link = document.createElement('a');
      link.href = url;
      link.download = 'stock_count_template.xlsx';
      link.click();
      window.URL.revokeObjectURL(url);
    } catch { setMsg('下载模板失败'); }
  };

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploading(true); setMsg('');
    const fd = new FormData();
    fd.append('file', file);
    try {
      const res = await api.post('/stockcount/import', fd, {
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 120000,
      });
      setMsg(`盘点完成！更新 ${res.data?.updated} 项，清零 ${res.data?.zeroed} 项`);
      fetchData();
    } catch (err: any) {
      setMsg('导入失败: ' + (err.response?.data?.message || err.message));
    } finally {
      setUploading(false);
      e.target.value = '';
    }
  };

  const showDetailFn = async (id: number) => {
    try {
      const res = await api.get(`/stockcount/${id}`);
      setDetailData(res.data);
      setShowDetail(true);
    } catch {}
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">盘点管理</h1>
        <div className="flex gap-2">
          <button onClick={handleDownload}
            className="bg-blue-600 text-white px-4 py-2 rounded text-sm hover:bg-blue-700">
            下载盘点模板
          </button>
          <button onClick={() => fileRef.current?.click()} disabled={uploading}
            className="bg-green-600 text-white px-4 py-2 rounded text-sm hover:bg-green-700 disabled:opacity-50">
            {uploading ? '导入中...' : '导入盘点结果'}
          </button>
          <input ref={fileRef} type="file" accept=".xlsx,.xls" className="hidden" onChange={handleUpload} />
        </div>
      </div>

      {msg && <div className={`p-3 rounded mb-4 text-sm ${msg.includes('完成') ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-700'}`}>{msg}</div>}

      <p className="text-gray-500 text-sm mb-4">
        下载模板 → 填写实盘数量 → 导入。Excel中未出现的库存项将自动清零。
      </p>

      <table className="w-full bg-white rounded-lg shadow">
        <thead><tr className="bg-gray-50 text-left text-sm">
          <th className="p-3">盘点单号</th><th className="p-3">状态</th>
          <th className="p-3">项目数</th><th className="p-3">时间</th><th className="p-3 w-16">操作</th>
        </tr></thead>
        <tbody>{data.length === 0 ? (
          <tr><td colSpan={5} className="p-6 text-center text-gray-400">暂无盘点记录</td></tr>
        ) : data.map(o => (
          <tr key={o.id} className="border-t hover:bg-gray-50 text-sm">
            <td className="p-3 font-mono">{o.count_no}</td>
            <td className="p-3">
              <span className={`px-2 py-0.5 rounded text-xs ${o.status === 2 ? 'bg-green-100 text-green-700' : 'bg-gray-100'}`}>
                {['', '已完成', '已完成'][o.status] || o.status}
              </span>
            </td>
            <td className="p-3">{o.items?.length || 0}</td>
            <td className="p-3 text-xs text-gray-500">{o.created_at?.slice(0, 19)}</td>
            <td className="p-3">
              <button onClick={() => showDetailFn(o.id)} className="text-blue-600 hover:text-blue-800 text-sm">详情</button>
            </td>
          </tr>
        ))}</tbody>
      </table>

      {showDetail && detailData && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[700px] max-h-[85vh] overflow-auto">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-bold">盘点详情 — {detailData.count_no}</h2>
              <button onClick={() => { setShowDetail(false); setDetailData(null); }}
                className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
            </div>
            <table className="w-full border text-sm">
              <thead><tr className="bg-gray-100">
                <th className="p-2 text-left">#</th><th className="p-2 text-left">部品</th>
                <th className="p-2 text-right">系统库存</th><th className="p-2 text-right">实盘数量</th>
                <th className="p-2 text-right">差异</th>
              </tr></thead>
              <tbody>{(detailData.items || []).map((item: any, idx: number) => {
                const diff = item.difference_qty || 0;
                const cls = diff > 0 ? 'text-green-600' : diff < 0 ? 'text-red-600' : '';
                return (
                  <tr key={idx} className="border-t">
                    <td className="p-2">{idx + 1}</td><td className="p-2 font-mono">{item.part_no}</td>
                    <td className="p-2 text-right">{item.system_qty}</td>
                    <td className="p-2 text-right">{item.actual_qty}</td>
                    <td className={`p-2 text-right font-medium ${cls}`}>
                      {diff > 0 ? `+${diff}` : diff}
                    </td>
                  </tr>
                );
              })}</tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
