import { useEffect, useState } from 'react';
import api from '../lib/api';

const SMAP = ['', '待确认', '已确认'];

export default function SubstituteList() {
  const [tab, setTab] = useState<'create' | 'history'>('history');
  const [parts, setParts] = useState<any[]>([]);
  const [subInventories, setSubInventories] = useState<any[]>([]);
  const [origInventories, setOrigInventories] = useState<any[]>([]);
  const [records, setRecords] = useState<any[]>([]);
  const [form, setForm] = useState({ original_part_id: 0, substitute_part_id: 0, source_location_id: 0, target_location_id: 0, quantity: 0 });
  const [editId, setEditId] = useState<number | null>(null);
  const [detailData, setDetailData] = useState<any>(null);
  const [showDetail, setShowDetail] = useState(false);
  const [loading, setLoading] = useState(false);
  const [msg, setMsg] = useState('');

  useEffect(() => {
    api.get('/parts?page=1&page_size=200').then(r => setParts(r.data?.items || []));
  }, []);

  const fetchRecords = () => {
    api.get('/inventory/substitute?page=1&page_size=50').then(r => setRecords(r.data?.items || []));
  };
  useEffect(() => { fetchRecords(); }, []);

  const loadSubInvs = (partId: number) => {
    if (!partId) { setSubInventories([]); return; }
    api.get(`/inventory/available/${partId}`).then(r => setSubInventories(r.data || []));
  };
  const loadOrigInvs = (partId: number) => {
    if (!partId) { setOrigInventories([]); return; }
    api.get(`/inventory/available/${partId}`).then(r => setOrigInventories(r.data || []));
  };

  const openCreate = () => {
    setEditId(null);
    setForm({ original_part_id: 0, substitute_part_id: 0, source_location_id: 0, target_location_id: 0, quantity: 0 });
    setSubInventories([]);
    setOrigInventories([]);
    setTab('create');
  };

  const openEdit = (r: any) => {
    setEditId(r.id);
    setForm({ original_part_id: r.original_part_id, substitute_part_id: r.substitute_part_id,
              source_location_id: r.source_location_id, target_location_id: r.target_location_id, quantity: r.quantity });
    loadSubInvs(r.substitute_part_id);
    loadOrigInvs(r.original_part_id);
    setTab('create');
  };

  const showDetailFn = async (id: number) => {
    try {
      const res = await api.get(`/inventory/substitute/${id}`);
      setDetailData(res.data || {});
      setShowDetail(true);
    } catch {}
  };

  const handleSubmit = async () => {
    if (!form.original_part_id || !form.substitute_part_id || !form.source_location_id || !form.target_location_id || form.quantity <= 0) {
      setMsg('请填写所有字段'); return;
    }
    if (form.original_part_id === form.substitute_part_id) {
      setMsg('原部品和替代部品不能相同'); return;
    }
    if (editId) {
      setLoading(true); setMsg('');
      try {
        await api.put(`/inventory/substitute/${editId}`, form);
        setMsg('更新成功');
        setEditId(null);
        fetchRecords();
        setTab('history');
      } catch (err: any) { setMsg('更新失败: ' + (err.response?.data?.message || err.message)); }
      finally { setLoading(false); }
    } else {
      const selected = subInventories.find(i => i.location_id === form.source_location_id);
      if (selected && form.quantity > selected.available_qty) {
        setMsg(`替代料库存不足（可用: ${selected.available_qty}）`); return;
      }
      setLoading(true); setMsg('');
      try {
        const res = await api.post('/inventory/substitute', form);
        setMsg(`移库记录已创建，待手机端确认（ID: ${res.data?.id}）`);
        setForm({ original_part_id: 0, substitute_part_id: 0, source_location_id: 0, target_location_id: 0, quantity: 0 });
        setEditId(null);
        fetchRecords();
        setTab('history');
      } catch (err: any) { setMsg('操作失败: ' + (err.response?.data?.message || err.message)); }
      finally { setLoading(false); }
    }
  };

  const handleConfirm = async (id: number) => {
    if (!confirm('确认执行此移库？库存将立即变更。')) return;
    try { await api.post(`/inventory/substitute/${id}/confirm`); setMsg('确认成功！'); fetchRecords(); }
    catch (err: any) { setMsg('确认失败: ' + (err.response?.data?.message || err.message)); }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('确认删除此移库记录？')) return;
    try { await api.delete(`/inventory/substitute/${id}`); setMsg('已删除'); fetchRecords(); }
    catch (err: any) { setMsg('删除失败: ' + (err.response?.data?.message || err.message)); }
  };

  const statusTag = (s: number) => {
    const cls = s === 1 ? 'bg-yellow-100 text-yellow-700' : 'bg-green-100 text-green-700';
    return <span className={`px-2 py-0.5 rounded text-xs ${cls}`}>{SMAP[s] || s}</span>;
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">替代料移库</h1>
        <div className="flex gap-2">
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-1.5 rounded text-sm">新建移库</button>
          <button onClick={() => { setTab('history'); fetchRecords(); setMsg(''); }}
            className={`px-4 py-1.5 rounded text-sm ${tab === 'history' ? 'bg-gray-700 text-white' : 'bg-gray-200'}`}>
            移库记录
          </button>
        </div>
      </div>

      {msg && <div className={`p-3 rounded mb-4 text-sm ${msg.includes('成功') || msg.includes('已创建') ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-700'}`}>{msg}</div>}

      {tab === 'create' ? (
        <div className="bg-white rounded-lg shadow p-6 max-w-2xl">
          <h2 className="text-lg font-bold mb-4">{editId ? '编辑移库记录' : '新建移库记录'}</h2>
          <div className="grid grid-cols-2 gap-4 mb-4">
            <div>
              <label className="block text-sm font-medium mb-1">缺货部品（入库身份）</label>
              <select className="w-full border p-2 rounded" value={form.original_part_id}
                onChange={e => { setForm({ ...form, original_part_id: Number(e.target.value), target_location_id: 0 }); loadOrigInvs(Number(e.target.value)); }}>
                <option value={0}>-- 请选择 --</option>
                {parts.map(p => <option key={p.id} value={p.id}>{p.part_no} - {p.part_name}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">替代部品（实际扣减）</label>
              <select className="w-full border p-2 rounded" value={form.substitute_part_id}
                onChange={e => { setForm({ ...form, substitute_part_id: Number(e.target.value), source_location_id: 0 }); loadSubInvs(Number(e.target.value)); }}>
                <option value={0}>-- 请选择 --</option>
                {parts.map(p => <option key={p.id} value={p.id}>{p.part_no} - {p.part_name}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">替代料来源库位</label>
              <select className="w-full border p-2 rounded" value={form.source_location_id}
                onChange={e => setForm({ ...form, source_location_id: Number(e.target.value) })}>
                <option value={0}>-- 请选择 --</option>
                {subInventories.map(inv => <option key={inv.location_id} value={inv.location_id}>{inv.location_code}（可用: {inv.available_qty}）</option>)}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">目标库位（缺货部品线边仓）</label>
              <select className="w-full border p-2 rounded" value={form.target_location_id}
                onChange={e => setForm({ ...form, target_location_id: Number(e.target.value) })}>
                <option value={0}>-- 请选择 --</option>
                {origInventories.map(inv => <option key={inv.location_id} value={inv.location_id}>{inv.location_code}（现存: {inv.available_qty}）</option>)}
              </select>
            </div>
            <div className="col-span-2">
              <label className="block text-sm font-medium mb-1">移库数量</label>
              <input type="number" className="w-full border p-2 rounded" min={1} value={form.quantity || ''}
                onChange={e => setForm({ ...form, quantity: Number(e.target.value) })} />
            </div>
          </div>
          <div className="flex gap-3">
            <button onClick={handleSubmit} disabled={loading}
              className="flex-1 bg-blue-600 text-white py-2 rounded hover:bg-blue-700 disabled:opacity-50">
              {loading ? '提交中...' : editId ? '保存修改' : '创建移库记录'}
            </button>
            <button onClick={() => { setTab('history'); setEditId(null); }}
              className="px-6 py-2 border rounded hover:bg-gray-50">取消</button>
          </div>
          <p className="text-gray-400 text-xs mt-2">创建后需手机端确认才会执行库存变更</p>
        </div>
      ) : (
        <table className="w-full bg-white rounded-lg shadow">
          <thead><tr className="bg-gray-50 text-left text-sm">
            <th className="p-3">ID</th><th className="p-3">缺货部品</th><th className="p-3">替代部品</th>
            <th className="p-3">数量</th><th className="p-3">来源库位</th><th className="p-3">目标库位</th>
            <th className="p-3">状态</th><th className="p-3">创建时间</th><th className="p-3 w-32">操作</th>
          </tr></thead>
          <tbody>{records.length === 0 ? <tr><td colSpan={9} className="p-6 text-center text-gray-400">暂无记录</td></tr> :
            records.map(r => (
            <tr key={r.id} className="border-t hover:bg-gray-50 text-sm">
              <td className="p-3">{r.id}</td>
              <td className="p-3 font-mono">{r.original_part_no}</td>
              <td className="p-3 font-mono">{r.substitute_part_no}</td>
              <td className="p-3">{r.quantity}</td>
              <td className="p-3 text-xs text-gray-500">{r.source_location_code || '-'}</td>
              <td className="p-3 text-xs text-gray-500">{r.target_location_code || '-'}</td>
              <td className="p-3">{statusTag(r.status)}</td>
              <td className="p-3 text-xs text-gray-500">{r.created_at?.slice(0, 19)}</td>
              <td className="p-3 space-x-1 whitespace-nowrap">
                <button onClick={() => showDetailFn(r.id)} className="text-blue-600 hover:text-blue-800 text-sm">详情</button>
                {r.status === 1 && <>
                  <button onClick={() => openEdit(r)} className="text-orange-500 hover:text-orange-700 text-sm">编辑</button>
                  <button onClick={() => handleDelete(r.id)} className="text-red-500 hover:text-red-700 text-sm">删除</button>
                  <button onClick={() => handleConfirm(r.id)} className="text-green-600 hover:text-green-800 text-sm">确认</button>
                </>}
              </td>
            </tr>
          ))}</tbody>
        </table>
      )}

      {/* Detail Dialog */}
      {showDetail && detailData && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[500px] max-h-[80vh] overflow-auto">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-bold">移库详情</h2>
              <button onClick={() => { setShowDetail(false); setDetailData(null); }}
                className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
            </div>
            <div className="grid grid-cols-2 gap-3 text-sm">
              <div><span className="text-gray-500">记录ID</span><p>{detailData.id}</p></div>
              <div><span className="text-gray-500">状态</span><p>{statusTag(detailData.status)}</p></div>
              <div><span className="text-gray-500">缺货部品</span><p className="font-mono">{detailData.original_part_no}</p></div>
              <div><span className="text-gray-500">替代部品</span><p className="font-mono">{detailData.substitute_part_no}</p></div>
              <div><span className="text-gray-500">移库数量</span><p className="font-bold">{detailData.quantity}</p></div>
              <div><span className="text-gray-500">操作人ID</span><p>{detailData.operator_id}</p></div>
              <div className="col-span-2"><span className="text-gray-500">来源库位</span><p>{detailData.source_location_code}</p></div>
              <div className="col-span-2"><span className="text-gray-500">目标库位</span><p>{detailData.target_location_code}</p></div>
              <div><span className="text-gray-500">创建时间</span><p className="text-xs">{detailData.created_at?.slice(0, 19)}</p></div>
              <div><span className="text-gray-500">确认时间</span><p className="text-xs">{detailData.confirmed_at?.slice(0, 19) || '-'}</p></div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
