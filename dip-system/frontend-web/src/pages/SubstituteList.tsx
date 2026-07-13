import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const STATUS_MAP: Record<number, string> = { 1: '待确认', 2: '已完成', 3: '已取消' };

interface DetailRow {
  key: number; // 前端临时ID
  original_part_id: number; substitute_part_id: number;
  source_location_id: number; target_location_id: number;
  quantity: number;
}

export default function SubstituteList() {
  const [orders, setOrders] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [showDetail, setShowDetail] = useState(false);
  const [detailData, setDetailData] = useState<any>(null);
  const [msg, setMsg] = useState('');
  // 新建/编辑表单
  const [parts, setParts] = useState<any[]>([]);
  const [rows, setRows] = useState<DetailRow[]>([emptyRow(0)]);
  const [searchText, setSearchText] = useState('');
  // 编辑时已确认的明细（只读）
  const [existingConfirmed, setExistingConfirmed] = useState<any[]>([]);

  function emptyRow(key: number): DetailRow {
    return { key, original_part_id: 0, substitute_part_id: 0, source_location_id: 0, target_location_id: 0, quantity: 0 };
  }

  // 加载库存（用于某部品的可选库位）
  const loadStocks = useCallback(async (partId: number): Promise<any[]> => {
    if (!partId) return [];
    try { return (await api.get(`/inventory/available/${partId}`)).data || []; } catch { return []; }
  }, []);

  // 加载部品列表
  const loadParts = useCallback(async () => {
    try { setParts((await api.get('/parts?page=1&page_size=500')).data?.items || []); } catch {}
  }, []);

  const fetchOrders = useCallback(async () => {
    setLoading(true);
    try { setOrders((await api.get('/substitute/orders?page=1&page_size=50')).data?.items || []); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { fetchOrders(); }, []);

  const openCreate = () => {
    setEditId(null); setExistingConfirmed([]);
    setRows([emptyRow(0)]); setSearchText('');
    loadParts(); setShowDialog(true);
  };

  const openEdit = async (orderId: number) => {
    try {
      const res = await api.get(`/substitute/orders/${orderId}`);
      if (res.code !== 0) return;
      const order = res.data;
      setEditId(orderId);
      await loadParts();
      // 区分已确认和未确认
      const details = order.details || [];
      const confirmed = details.filter((d: any) => d.status === 2);
      const unconfirmed = details.filter((d: any) => d.status === 1);
      setExistingConfirmed(confirmed);
      setRows(unconfirmed.length > 0
        ? unconfirmed.map((d: any, i: number) => ({
            key: i,
            original_part_id: d.original_part_id, substitute_part_id: d.substitute_part_id,
            source_location_id: d.source_location_id, target_location_id: d.target_location_id,
            quantity: d.quantity
          }))
        : [emptyRow(0)]);
      setSearchText('');
      setShowDialog(true);
    } catch {}
  };

  const showDetailFn = async (id: number) => {
    try {
      const res = await api.get(`/substitute/orders/${id}`);
      if (res.code !== 0) { showToast(res.message || '加载失败', 'error'); return; }
      setDetailData(res.data || {});
      setShowDetail(true);
    } catch {}
  };

  const addRow = () => setRows(prev => [...prev, emptyRow(Math.max(...prev.map(r => r.key), 0) + 1)]);

  const delRow = (key: number) => {
    if (rows.length <= 1 && existingConfirmed.length === 0) return;
    setRows(prev => prev.filter(r => r.key !== key));
  };

  const updateRow = (key: number, field: keyof DetailRow, value: number) => {
    setRows(prev => prev.map(r => r.key === key ? { ...r, [field]: value } : r));
  };

  // 按料号搜索过滤部品
  const filteredParts = searchText
    ? parts.filter((p: any) =>
        (p.part_no || '').toLowerCase().includes(searchText.toLowerCase()) ||
        (p.part_name || '').toLowerCase().includes(searchText.toLowerCase()))
    : parts;

  const handleSubmit = async () => {
    const validRows = rows.filter(r =>
      r.original_part_id > 0 && r.substitute_part_id > 0 &&
      r.source_location_id > 0 && r.target_location_id > 0 && r.quantity > 0);
    if (validRows.length === 0 && existingConfirmed.length === 0) {
      setMsg('至少需要一条有效明细'); return;
    }
    setMsg('');
    try {
      const payload = { details: validRows.map(r => ({
        original_part_id: r.original_part_id, substitute_part_id: r.substitute_part_id,
        source_location_id: r.source_location_id, target_location_id: r.target_location_id,
        quantity: r.quantity
      })) };
      if (editId) {
        const res = await api.put(`/substitute/orders/${editId}`, payload);
        if (res.code !== 0) { setMsg(res.message || '操作失败'); return; }
        showToast('订单更新成功', 'success');
      } else {
        const res = await api.post('/substitute/orders', payload);
        if (res.code !== 0) { setMsg(res.message || '操作失败'); return; }
        showToast('订单创建成功', 'success');
      }
      setShowDialog(false); fetchOrders();
    } catch {}
  };

  const handleCancel = async (id: number) => {
    if (!confirm('确认取消此订单？')) return;
    try { const res = await api.post(`/substitute/orders/${id}/cancel`); if (res.code !== 0) { showToast(res.message || '操作失败', 'error'); return; } showToast('订单已取消', 'success'); fetchOrders(); } catch {}
  };

  const statusTag = (s: number) => {
    const cls = s === 1 ? 'bg-yellow-100 text-yellow-700' : s === 2 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-600';
    return <span className={`px-2 py-0.5 rounded text-xs ${cls}`}>{STATUS_MAP[s] || s}</span>;
  };

  // ===== 渲染 =====
  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">替代料移库</h1>
        <div className="flex gap-2">
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-1.5 rounded text-sm">新建移库订单</button>
          <HelpButton title="替代料移库" sections={[
            { title: '功能概述', items: ['管理替代料移库订单，一个订单可包含多条移库明细', '网页端创建订单后，手机端逐袋扫码确认', '全部确认后系统自动执行移库并刷新冻结'] },
            { title: '操作流程', items: ['1. 网页端新建订单 → 添加多行明细（替代部品+来源库位 → 缺料部品+目标库位 → 数量）', '2. 手机端选择订单 → 扫替代部品条码匹配明细 → 逐一确认', '3. 全部确认后提交 → 系统自动完成库存移库'] }
          ]} />
        </div>
      </div>

      {msg && <div className={`p-3 rounded mb-4 text-sm ${msg.includes('成功') ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-700'}`}>{msg}</div>}

      {/* 订单列表 */}
      {loading ? <p>加载中...</p> : (
        <table className="w-full bg-white rounded-lg shadow text-sm">
          <thead><tr className="bg-gray-50 text-left">
            <th className="p-3">订单号</th><th className="p-3 text-center">明细数/已确认</th>
            <th className="p-3">状态</th><th className="p-3">创建时间</th><th className="p-3 w-40">操作</th>
          </tr></thead>
          <tbody>{orders.length === 0 ? <tr><td colSpan={5} className="p-6 text-center text-gray-400">暂无记录</td></tr> :
            orders.map(o => (
            <tr key={o.id} className="border-t hover:bg-gray-50">
              <td className="p-3 font-mono text-xs">{o.order_no}</td>
              <td className="p-3 text-center">{o.confirmed_count}/{o.detail_count}</td>
              <td className="p-3">{statusTag(o.status)}</td>
              <td className="p-3 text-xs text-gray-500">{o.created_at?.slice(0, 19)}</td>
              <td className="p-3 space-x-1 whitespace-nowrap">
                <button onClick={() => showDetailFn(o.id)} className="text-blue-600 hover:text-blue-800 text-xs">详情</button>
                {o.status === 1 && <>
                  <button onClick={() => openEdit(o.id)} className="text-orange-500 hover:text-orange-700 text-xs">编辑</button>
                  <button onClick={() => handleCancel(o.id)} className="text-red-500 hover:text-red-700 text-xs">取消</button>
                </>}
              </td>
            </tr>
          ))}</tbody>
        </table>
      )}

      {/* 新建/编辑弹窗 */}
      {showDialog && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[900px] max-h-[85vh] overflow-auto">
            <h2 className="text-xl font-bold mb-4">{editId ? '编辑移库订单' : '新建移库订单'}</h2>

            {/* 已确认明细（只读） */}
            {existingConfirmed.length > 0 && (
              <div className="mb-4">
                <p className="text-sm font-medium text-gray-500 mb-1">已确认明细（只读）</p>
                <table className="w-full text-sm border">
                  <thead><tr className="bg-gray-100">
                    <th className="p-1">替代部品</th><th className="p-1">来源库位</th>
                    <th className="p-1">缺料部品</th><th className="p-1">目标库位</th>
                    <th className="p-1">数量</th>
                  </tr></thead>
                  <tbody>
                    {existingConfirmed.map((d: any) => (
                      <tr key={d.id} className="text-gray-400 bg-gray-50">
                        <td className="p-1 font-mono text-xs">{d.substitute_part_no}</td>
                        <td className="p-1 font-mono text-xs">{d.source_location_code}</td>
                        <td className="p-1 font-mono text-xs">{d.original_part_no}</td>
                        <td className="p-1 font-mono text-xs">{d.target_location_code}</td>
                        <td className="p-1 text-right">{d.quantity}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {/* 搜索筛选 */}
            <input
              className="w-full border rounded px-3 py-1.5 mb-2 text-sm"
              placeholder="输入料号或名称筛选部品..."
              value={searchText}
              onChange={e => setSearchText(e.target.value)}
            />

            {/* 可编辑明细表 */}
            <div className="border rounded max-h-48 overflow-auto mb-2">
              <table className="w-full text-sm">
                <thead><tr className="bg-gray-50 sticky top-0">
                  <th className="p-1 text-left">替代部品</th><th className="p-1 text-left">来源库位</th>
                  <th className="p-1 text-left">缺料部品</th><th className="p-1 text-left">目标库位</th>
                  <th className="p-1 text-right w-16">数量</th>
                  <th className="p-1 w-10"></th>
                </tr></thead>
                <tbody>
                  {rows.map((row) => (
                    <RowEditor key={row.key} row={row} parts={filteredParts}
                      loadStocks={loadStocks} updateRow={updateRow} delRow={delRow} />
                  ))}
                </tbody>
              </table>
            </div>
            <button onClick={addRow} className="text-blue-600 text-sm hover:text-blue-800 mb-4">+ 添加一行</button>

            <div className="flex justify-end gap-3">
              <button onClick={() => setShowDialog(false)} className="px-4 py-2 border rounded hover:bg-gray-50">取消</button>
              <button onClick={handleSubmit} className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">
                {editId ? '保存修改' : '创建订单'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 详情弹窗 */}
      {showDetail && detailData && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[700px] max-h-[80vh] overflow-auto">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-bold">移库订单详情</h2>
              <button onClick={() => { setShowDetail(false); setDetailData(null); }}
                className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
            </div>
            <div className="grid grid-cols-2 gap-3 text-sm mb-4">
              <div><span className="text-gray-500">订单号</span><p className="font-mono">{detailData.order_no}</p></div>
              <div><span className="text-gray-500">状态</span><p>{statusTag(detailData.status)}</p></div>
              <div><span className="text-gray-500">明细数</span><p>{detailData.detail_count}</p></div>
              <div><span className="text-gray-500">已确认</span><p>{detailData.confirmed_count}</p></div>
            </div>
            <table className="w-full text-sm border">
              <thead><tr className="bg-gray-50">
                <th className="p-2 text-left">替代部品</th><th className="p-2 text-left">来源库位</th>
                <th className="p-2 text-left">缺料部品</th><th className="p-2 text-left">目标库位</th>
                <th className="p-2 text-right">数量</th>
                <th className="p-2">状态</th>
              </tr></thead>
              <tbody>{(detailData.details || []).map((d: any) => (
                <tr key={d.id} className="border-t">
                  <td className="p-2 font-mono text-xs">{d.substitute_part_no}</td>
                  <td className="p-2 font-mono text-xs">{d.source_location_code}</td>
                  <td className="p-2 font-mono text-xs">{d.original_part_no}</td>
                  <td className="p-2 font-mono text-xs">{d.target_location_code}</td>
                  <td className="p-2 text-right">{d.quantity}</td>
                  <td className="p-2">{d.status === 2 ? <span className="text-green-600">✓</span> : '待确认'}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

// ===== 单行编辑器子组件 =====
function RowEditor({ row, parts, loadStocks, updateRow, delRow }: {
  row: DetailRow;
  parts: any[];
  loadStocks: (partId: number) => Promise<any[]>;
  updateRow: (key: number, field: keyof DetailRow, value: number) => void;
  delRow: (key: number) => void;
}) {
  const [subStocks, setSubStocks] = useState<any[]>([]);
  const [origStocks, setOrigStocks] = useState<any[]>([]);

  useEffect(() => {
    loadStocks(row.substitute_part_id).then(setSubStocks);
  }, [row.substitute_part_id]);

  useEffect(() => {
    loadStocks(row.original_part_id).then(setOrigStocks);
  }, [row.original_part_id]);

  return (
    <tr className="border-t">
      <td className="p-1">
        <select className="w-full border rounded text-xs p-1" value={row.substitute_part_id}
          onChange={e => { const v = Number(e.target.value); updateRow(row.key, 'substitute_part_id', v); updateRow(row.key, 'source_location_id', 0); }}>
          <option value={0}>--</option>
          {parts.map(p => <option key={p.id} value={p.id}>{p.part_no}</option>)}
        </select>
      </td>
      <td className="p-1">
        <select className="w-full border rounded text-xs p-1" value={row.source_location_id}
          onChange={e => updateRow(row.key, 'source_location_id', Number(e.target.value))}>
          <option value={0}>--</option>
          {subStocks.map((s: any) => <option key={s.location_id} value={s.location_id}>{s.location_code}(可用{s.available_qty})</option>)}
        </select>
      </td>
      <td className="p-1">
        <select className="w-full border rounded text-xs p-1" value={row.original_part_id}
          onChange={e => { const v = Number(e.target.value); updateRow(row.key, 'original_part_id', v); updateRow(row.key, 'target_location_id', 0); }}>
          <option value={0}>--</option>
          {parts.map(p => <option key={p.id} value={p.id}>{p.part_no}</option>)}
        </select>
      </td>
      <td className="p-1">
        <select className="w-full border rounded text-xs p-1" value={row.target_location_id}
          onChange={e => updateRow(row.key, 'target_location_id', Number(e.target.value))}>
          <option value={0}>--</option>
          {origStocks.map((s: any) => <option key={s.location_id} value={s.location_id}>{s.location_code}(现存{s.available_qty})</option>)}
        </select>
      </td>
      <td className="p-1">
        <input type="number" className="w-16 border rounded text-xs p-1 text-right" min={0}
          value={row.quantity || ''} onChange={e => updateRow(row.key, 'quantity', Number(e.target.value))} />
      </td>
      <td className="p-1">
        <button onClick={() => delRow(row.key)} className="text-red-400 hover:text-red-600 text-xs">✕</button>
      </td>
    </tr>
  );
}
