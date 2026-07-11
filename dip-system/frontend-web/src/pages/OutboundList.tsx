import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const STATUS_MAP: Record<number, string> = { 1: '待出库', 2: '已出库', 3: '已取消' };

export default function OutboundList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [partNo, setPartNo] = useState('');
  const [locationCode, setLocationCode] = useState('');
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [availableParts, setAvailableParts] = useState<any[]>([]);
  const [selectedPart, setSelectedPart] = useState<any>(null);
  const [quantity, setQuantity] = useState<number>(0);
  const [isManager, setIsManager] = useState(false);

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

  const openCreate = async () => {
    setEditId(null); setSelectedPart(null); setQuantity(0);
    try { setAvailableParts((await api.get('/outbound/available-parts')).data || []); } catch {}
    setShowDialog(true);
  };

  const openEdit = (order: any) => {
    setEditId(order.id);
    setSelectedPart({ part_id: order.part_id, part_no: order.part_no, part_name: order.part_name, location_id: order.location_id, location_code: order.location_code, available_qty: order.quantity });
    setQuantity(order.quantity);
    setShowDialog(true);
  };

  const handleSubmit = async () => {
    if (!selectedPart) return alert('请选择出库部品');
    if (quantity <= 0 || quantity > selectedPart.available_qty) return alert('数量无效或超出可用库存');
    try {
      const payload = {
        part_id: selectedPart.part_id, part_no: selectedPart.part_no, part_name: selectedPart.part_name,
        location_id: selectedPart.location_id, location_code: selectedPart.location_code, quantity
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
            { title: '功能概述', items: ['兄弟单位领料出库管理', '管理员新增出库单：选择料号→库位→输入数量（不可超可用库存）', '待出库单可编辑和删除，已出库不可操作', '手机端扫描核销后直接扣减库存（不经过冻结）'] },
            { title: '操作流程', items: ['1. 点击"新增出库单"→在可用库存列表中选择部品（点击行）→输入数量→确认', '2. 数量默认填满全部可用，可根据实际需求修改（不能超过可用数量）', '3. 手机端：出库管理→选择待出库订单→扫码核销→库存实时扣减'] }
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
            <th className="p-3">订单号</th><th className="p-3">料号</th><th className="p-3">物料名称</th>
            <th className="p-3">库位</th><th className="p-3 text-right">出库数量</th>
            <th className="p-3">状态</th><th className="p-3">创建时间</th>
            {isManager && <th className="p-3 w-24">操作</th>}
          </tr></thead>
          <tbody>{data.map(o => (
            <tr key={o.id} className="border-t hover:bg-gray-50">
              <td className="p-3 font-mono text-xs">{o.order_no}</td>
              <td className="p-3 font-mono text-xs">{o.part_no}</td>
              <td className="p-3">{o.part_name}</td>
              <td className="p-3 font-mono text-xs">{o.location_code}</td>
              <td className="p-3 text-right">{o.quantity}</td>
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
          <div className="bg-white rounded-lg p-6 w-[600px] max-h-[80vh] overflow-auto">
            <h2 className="text-xl font-bold mb-4">{editId ? '编辑出库单' : '新增出库单'}</h2>

            <div className="mb-4">
              <label className="block text-sm font-medium mb-1">选择出库部品（点击行选择）</label>
              <div className="border rounded max-h-48 overflow-auto">
                <table className="w-full text-sm">
                  <thead><tr className="bg-gray-50 sticky top-0">
                    <th className="p-2 text-left">料号</th><th className="p-2 text-left">名称</th><th className="p-2 text-left">库位</th><th className="p-2 text-right">可用数量</th>
                  </tr></thead>
                  <tbody>
                    {availableParts.map((p: any) => (
                      <tr key={`${p.part_id}-${p.location_id}`}
                        onClick={() => { setSelectedPart(p); setQuantity(p.available_qty); }}
                        className={`border-t cursor-pointer hover:bg-blue-50 ${selectedPart?.part_id === p.part_id && selectedPart?.location_id === p.location_id ? 'bg-blue-100' : ''}`}>
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

            {selectedPart && (
              <div className="mb-4 p-3 bg-blue-50 rounded">
                <p className="text-sm">已选: <strong>{selectedPart.part_no}</strong> / {selectedPart.part_name} / 库位: {selectedPart.location_code}</p>
                <p className="text-sm text-gray-500">可用数量: {selectedPart.available_qty}</p>
              </div>
            )}

            <div className="mb-4">
              <label className="block text-sm font-medium mb-1">出库数量（不可超过可用数量）</label>
              <input type="number" className="w-full border p-2 rounded" min={0} max={selectedPart?.available_qty || 0}
                value={quantity} onChange={e => setQuantity(Number(e.target.value))} />
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
