import { useEffect, useState, useRef } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';

export default function LocationList() {
  const [data, setData] = useState<any[]>([]);
  const [msg, setMsg] = useState('');
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState({ location_code: '', warehouse: '', zone: '', row: '', column: '', max_capacity: 10000, status: 1 });
  const fileRef = useRef<HTMLInputElement>(null);

  const fetchData = async () => {
    try { setData((await api.get('/locations?page=1&page_size=100')).data?.items || []); } catch {}
  };
  useEffect(() => { fetchData(); }, []);

  const openCreate = () => {
    setEditId(null);
    setForm({ location_code: '', warehouse: '线边仓', zone: 'A', row: '01', column: '01', max_capacity: 10000, status: 1 });
    setShowDialog(true);
  };

  const openEdit = (loc: any) => {
    setEditId(loc.id);
    setForm({ location_code: loc.location_code, warehouse: loc.warehouse, zone: loc.zone, row: loc.row, column: loc.column, max_capacity: loc.max_capacity, status: loc.status });
    setShowDialog(true);
  };

  const handleSubmit = async () => {
    if (!form.location_code) return alert('请输入库位编码');
    try {
      if (editId) {
        await api.put(`/locations/${editId}`, { warehouse: form.warehouse, zone: form.zone, row: form.row, column: form.column, max_capacity: form.max_capacity, status: form.status });
        setMsg('库位更新成功');
      } else {
        await api.post('/locations', form);
        setMsg('库位创建成功');
      }
      setShowDialog(false);
      fetchData();
    } catch {}
  };

  const handleDelete = async (id: number) => {
    if (!confirm('确认删除此库位？')) return;
    try { await api.delete(`/locations/${id}`); setMsg('已删除'); fetchData(); } catch {}
  };

  const handleImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]; if (!file) return;
    const fd = new FormData(); fd.append('file', file);
    try {
      const res = await api.post('/locations/import', fd, { headers: { 'Content-Type': 'multipart/form-data' }, timeout: 60000 });
      setMsg(`导入成功: ${res.data?.count || 0} 条`); fetchData();
    } catch (err: any) { setMsg('导入失败: ' + (err.response?.data?.message || err.message)); }
    e.target.value = '';
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">库位管理</h1>
        <div className="flex gap-2">
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">新增库位</button>
          <button onClick={() => fileRef.current?.click()} className="bg-green-600 text-white px-4 py-2 rounded hover:bg-green-700">导入库位</button>
          <a href="/api/v1/locations/template" className="bg-gray-500 text-white px-4 py-2 rounded hover:bg-gray-600">下载模板</a>
          <input ref={fileRef} type="file" accept=".xlsx,.xls" className="hidden" onChange={handleImport} />
        </div>
      </div>
      {msg && <div className="bg-blue-50 text-blue-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      <table className="w-full bg-white rounded-lg shadow">
        <thead><tr className="bg-gray-50 text-left text-sm">
          <th className="p-3">库位编码</th><th className="p-3">仓库</th><th className="p-3">库区</th><th className="p-3">排-列</th><th className="p-3">容量</th><th className="p-3">当前数量</th><th className="p-3">状态</th><th className="p-3 w-28">操作</th>
        </tr></thead>
        <tbody>{data.map(l => (
          <tr key={l.id} className="border-t hover:bg-gray-50">
            <td className="p-3 font-mono text-sm">{l.location_code}</td>
            <td className="p-3">{l.warehouse}</td><td className="p-3">{l.zone}</td>
            <td className="p-3 text-sm">{l.row}-{l.column}</td>
            <td className="p-3">{l.max_capacity}</td><td className="p-3">{l.current_qty}</td>
            <td className="p-3">{l.status === 1 ? <span className="text-green-600">启用</span> : <span className="text-red-500">禁用</span>}</td>
            <td className="p-3 space-x-1">
              <button onClick={() => openEdit(l)} className="text-blue-600 hover:text-blue-800 text-sm">编辑</button>
              <button onClick={() => handleDelete(l.id)} className="text-red-500 hover:text-red-700 text-sm">删除</button>
            </td>
          </tr>
        ))}</tbody>
      </table>

      {showDialog && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[500px]">
            <h2 className="text-xl font-bold mb-4">{editId ? '编辑库位' : '新增库位'}</h2>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-sm mb-1">库位编码</label>
                <input className="w-full border p-2 rounded" value={form.location_code}
                  onChange={e => setForm({ ...form, location_code: e.target.value })} disabled={!!editId} />
              </div>
              <div>
                <label className="block text-sm mb-1">仓库</label>
                <input className="w-full border p-2 rounded" value={form.warehouse}
                  onChange={e => setForm({ ...form, warehouse: e.target.value })} />
              </div>
              <div>
                <label className="block text-sm mb-1">库区</label>
                <input className="w-full border p-2 rounded" value={form.zone}
                  onChange={e => setForm({ ...form, zone: e.target.value })} />
              </div>
              <div>
                <label className="block text-sm mb-1">排</label>
                <input className="w-full border p-2 rounded" value={form.row}
                  onChange={e => setForm({ ...form, row: e.target.value })} />
              </div>
              <div>
                <label className="block text-sm mb-1">列</label>
                <input className="w-full border p-2 rounded" value={form.column}
                  onChange={e => setForm({ ...form, column: e.target.value })} />
              </div>
              <div>
                <label className="block text-sm mb-1">最大容量</label>
                <input type="number" className="w-full border p-2 rounded" value={form.max_capacity}
                  onChange={e => setForm({ ...form, max_capacity: Number(e.target.value) })} />
              </div>
              {editId && (
                <div>
                  <label className="block text-sm mb-1">状态</label>
                  <select className="w-full border p-2 rounded" value={form.status}
                    onChange={e => setForm({ ...form, status: Number(e.target.value) })}>
                    <option value={1}>启用</option><option value={0}>禁用</option>
                  </select>
                </div>
              )}
            </div>
            <div className="flex justify-end gap-3 mt-6">
              <button onClick={() => setShowDialog(false)} className="px-4 py-2 border rounded hover:bg-gray-50">取消</button>
              <button onClick={handleSubmit} className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">{editId ? '保存' : '创建'}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
