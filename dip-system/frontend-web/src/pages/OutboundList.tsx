import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const STATUS_MAP: Record<number, string> = { 1: '待出库', 2: '已出库', 3: '已取消' };

interface DetailRow {
  part_id: number;
  part_no: string;
  part_name: string;
  location_id: number;
  location_code: string;
  available_qty: number;
  quantity: number;
}

export default function OutboundList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [partNo, setPartNo] = useState('');
  const [locationCode, setLocationCode] = useState('');
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [availableParts, setAvailableParts] = useState<any[]>([]);
  const [details, setDetails] = useState<DetailRow[]>([]);
  const [isManager, setIsManager] = useState(false);
  const [searchText, setSearchText] = useState('');

  useEffect(() => {
    api.get('/auth/me').then(r => {
      if (r.code === 0 && r.data) {
        const role = (r.data.role_code || '').toLowerCase();
        setIsManager(role === 'admin' || role === 'leader');
      }
    }).catch(() => {});
  }, []);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params: any = { page: 1, page_size: 100 };
      if (partNo) params.part_no = partNo;
      if (locationCode) params.location_code = locationCode;
      setData((await api.get('/outbound', { params })).data?.items || []);
    } finally { setLoading(false); }
  }, [partNo, locationCode]);

  useEffect(() => { fetchData(); }, []);

  const addDetail = (p: any) => {
    // 避免重复添加同一库位的同种部品
    const exists = details.find(d => d.part_id === p.part_id && d.location_id === p.location_id);
    if (exists) return;
    setDetails([...details, { part_id: p.part_id, part_no: p.part_no, part_name: p.part_name, location_id: p.location_id, location_code: p.location_code, available_qty: p.available_qty, quantity: p.available_qty }]);
    setSearchText('');
  };

  const removeDetail = (idx: number) => {
    setDetails(details.filter((_, i) => i !== idx));
  };

  const updateDetailQty = (idx: number, qty: number) => {
    const newDetails = [...details];
    newDetails[idx].quantity = Math.min(qty, newDetails[idx].available_qty);
    setDetails(newDetails);
  };

  const openCreate = async () => {
    setEditId(null); setDetails([]); setSearchText('');
    try { setAvailableParts((await api.get('/outbound/available-parts')).data || []); } catch {}
    setShowDialog(true);
  };

  const openEdit = async (order: any) => {
    setEditId(order.id);
    setSearchText('');
    try {
      // 加载订单详情获取明细
      const res = await api.get(`/outbound/${order.id}`);
      if (res.code === 0 && res.data?.details) {
        const ds = res.data.details.map((d: any) => ({
          part_id: d.part_id, part_no: d.part_no, part_name: d.part_name,
          location_id: d.location_id, location_code: d.location_code,
          available_qty: d.quantity, quantity: d.quantity
        }));
        setDetails(ds);
      }
      setAvailableParts((await api.get('/outbound/available-parts')).data || []);
    } catch {}
    setShowDialog(true);
  };

  const handleSubmit = async () => {
    if (details.length === 0) return alert('请至少添加一条出库明细');
    const incomplete = details.findIndex(d => d.quantity <= 0 || d.quantity > d.available_qty);
    if (incomplete >= 0) return alert(`第${incomplete + 1}行数量无效或超出可用库存`);
    try {
      const payload = {
        details: details.map(d => ({
          part_id: d.part_id, part_no: d.part_no, part_name: d.part_name,
          location_id: d.location_id, location_code: d.location_code, quantity: d.quantity
        }))
      };
      if (editId) {
        await api.put(`/outbound/${editId}`, payload);
        showToast('出库单更新成功', 'success');
      } else {
        await api.post('/outbound', payload);
        showToast('出库单创建成功', 'success');
      }
      setShowDialog(false); fetchData();
    } catch {}
  };

  const handleDelete = async (id: number) => {
    if (!confirm('确认删除此出库单？')) return;
    try { await api.delete(`/outbound/${id}`); fetchData(); } catch {}
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">出库管理</h1>
        <div className="flex gap-2">
          <HelpButton title="出库管理" sections={[
            { title: '功能概述', items: ['兄弟单位领料出库管理', '管理员新增出库单：可添加多种部品明细，每种选料号→库位→数量', '待出库单可编辑和删除，已出库不可操作', '手机端逐种扫码核销后直接扣减库存（不经过冻结）'] },
            { title: '操作流程', items: ['1. 点击"新增出库单"→在可用库存列表中点击部品行→"添加明细"→修改数量', '2. 可添加多种部品到同一个出库单', '3. 手机端：出库管理→选择待出库订单→逐种扫码核销→整单完成'] }
          ]} />
          {isManager && (
            <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">新增出库单</button>
          )}
        </div>
      </div>

      <div className="bg-white rounded-lg shadow p-4 mb-4 flex gap-4 items-end">
        <div>
          <label className="block text-sm text-gray-600 mb-1">料号</label>
          <input className="border rounded px-3 py-1.5 w-48" placeholder="输入料号" value={partNo}
            onChange={e => setPartNo(e.target.value)} onKeyDown={e => e.key === 'Enter' && fetchData()} />
        </div>
        <div>
          <label className="block text-sm text-gray-600 mb-1">库位</label>
          <input className="border rounded px-3 py-1.5 w-48" placeholder="输入库位" value={locationCode}
            onChange={e => setLocationCode(e.target.value)} onKeyDown={e => e.key === 'Enter' && fetchData()} />
        </div>
        <button onClick={() => { setPartNo(''); setLocationCode(''); }}
          className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
      </div>

      {loading ? <p>加载中...</p> : (
        <table className="w-full bg-white rounded-lg shadow text-sm">
          <thead><tr className="bg-gray-50 text-left">
            <th className="p-3">订单号</th><th className="p-3 text-center">明细数</th>
            <th className="p-3">状态</th><th className="p-3">创建时间</th>
            {isManager && <th className="p-3 w-24">操作</th>}
          </tr></thead>
          <tbody>{data.map(o => (
            <tr key={o.id} className="border-t hover:bg-gray-50">
              <td className="p-3 font-mono text-xs">{o.order_no}</td>
              <td className="p-3 text-center">{o.detail_count ?? 0}</td>
              <td className="p-3">
                <span className={`px-2 py-0.5 rounded text-xs text-white ${o.status === 1 ? 'bg-yellow-500' : o.status === 2 ? 'bg-green-500' : 'bg-gray-500'}`}>
                  {STATUS_MAP[o.status] || o.status}
                </span>
              </td>
              <td className="p-3 text-xs text-gray-500">{o.created_at?.slice(0, 19)}</td>
              {isManager && o.status === 1 && (
                <td className="p-3 space-x-1 whitespace-nowrap">
                  <button onClick={() => openEdit(o)} className="text-blue-600 hover:text-blue-800 text-xs">编辑</button>
                  <button onClick={() => handleDelete(o.id)} className="text-red-500 hover:text-red-700 text-xs">删除</button>
                </td>
              )}
              {isManager && o.status !== 1 && <td className="p-3"></td>}
              {!isManager && <td className="p-3"></td>}
            </tr>
          ))}</tbody>
        </table>
      )}

      {showDialog && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[700px] max-h-[85vh] overflow-auto">
            <h2 className="text-xl font-bold mb-4">{editId ? '编辑出库单' : '新增出库单'}</h2>

            {/* 可用库存列表 */}
            <div className="mb-3">
              <label className="block text-sm font-medium mb-1">可用库存（点击行选中，再点"添加明细"）</label>
              <input
                className="w-full border rounded px-3 py-1.5 mb-2 text-sm"
                placeholder="输入料号或名称筛选..."
                value={searchText}
                onChange={e => setSearchText(e.target.value)}
              />
              <div className="border rounded max-h-40 overflow-auto">
                <table className="w-full text-sm">
                  <thead><tr className="bg-gray-50 sticky top-0">
                    <th className="p-2 text-left">料号</th><th className="p-2 text-left">名称</th><th className="p-2 text-left">库位</th><th className="p-2 text-right">可用数量</th>
                  </tr></thead>
                  <tbody>
                    {availableParts.filter((p: any) => {
                      if (!searchText) return true;
                      const s = searchText.toLowerCase();
                      return (p.part_no || '').toLowerCase().includes(s)
                        || (p.part_name || '').toLowerCase().includes(s)
                        || (p.location_code || '').toLowerCase().includes(s);
                    }).map((p: any) => (
                      <tr key={`${p.part_id}-${p.location_id}`}
                        onClick={() => addDetail(p)}
                        className="border-t cursor-pointer hover:bg-blue-50">
                        <td className="p-2 font-mono text-xs">{p.part_no}</td>
                        <td className="p-2">{p.part_name}</td>
                        <td className="p-2 font-mono text-xs">{p.location_code}</td>
                        <td className="p-2 text-right text-green-600 font-medium">{p.available_qty}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>

            {/* 已添加明细列表 */}
            <div className="mb-4">
              <label className="block text-sm font-medium mb-1">出库明细（{details.length} 种部品）</label>
              {details.length === 0 ? (
                <p className="text-gray-400 text-sm py-4 text-center border rounded">从上方库存列表中点击部品行添加</p>
              ) : (
                <table className="w-full text-sm border rounded">
                  <thead><tr className="bg-gray-50">
                    <th className="p-2 text-left">料号</th><th className="p-2 text-left">名称</th><th className="p-2 text-left">库位</th><th className="p-2 text-right w-24">数量</th><th className="p-2 w-12"></th>
                  </tr></thead>
                  <tbody>
                    {details.map((d, idx) => (
                      <tr key={idx} className="border-t">
                        <td className="p-2 font-mono text-xs">{d.part_no}</td>
                        <td className="p-2 text-xs">{d.part_name}</td>
                        <td className="p-2 font-mono text-xs">{d.location_code}</td>
                        <td className="p-2">
                          <input type="number" className="w-full border rounded px-2 py-1 text-right text-xs"
                            min={1} max={d.available_qty} value={d.quantity}
                            onChange={e => updateDetailQty(idx, Number(e.target.value))} />
                        </td>
                        <td className="p-2 text-center">
                          <button onClick={() => removeDetail(idx)} className="text-red-400 hover:text-red-600 text-xs">✕</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            <div className="flex justify-end gap-3">
              <button onClick={() => setShowDialog(false)} className="px-4 py-2 border rounded hover:bg-gray-50">取消</button>
              <button onClick={handleSubmit} className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">
                {editId ? '保存修改' : '确认创建'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
