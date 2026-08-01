import { useEffect, useState, useRef } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const PAGE_SIZE = 50;

export default function LocationList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [isManager, setIsManager] = useState(false);
  const [msg, setMsg] = useState('');
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState({ location_code: '', warehouse: '', zone: '', row: '', column: '', max_capacity: 10000, status: 1 });
  const fileRef = useRef<HTMLInputElement>(null);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [searchCode, setSearchCode] = useState('');
  const [suggestions, setSuggestions] = useState<any[]>([]);
  const [showSuggestions, setShowSuggestions] = useState(false);

  const fetchData = async (p?: number, search?: string) => {
    setLoading(true);
    const code = search ?? searchCode;
    try {
      const res = await api.get('/locations', { params: { page: p ?? page, page_size: PAGE_SIZE, ...(code ? { location_code: code } : {}) } });
      setData(res.data?.items || []);
      setTotal(res.data?.total || 0);
    } finally { setLoading(false); }
  };

  // 输入时自动匹配下拉（300ms 防抖）
  useEffect(() => {
    if (!searchCode.trim()) { setSuggestions([]); setShowSuggestions(false); return; }
    const timer = setTimeout(async () => {
      try {
        const res = await api.get('/locations', { params: { location_code: searchCode, page: 1, page_size: 10 } });
        setSuggestions(res.data?.items || []);
        setShowSuggestions(true);
      } catch { setSuggestions([]); }
    }, 300);
    return () => clearTimeout(timer);
  }, [searchCode]);

  const selectSuggestion = (loc: any) => {
    setSearchCode(loc.location_code);
    setShowSuggestions(false);
    setPage(1);
    fetchData(1, loc.location_code);
  };

  const handleSearch = () => {
    setShowSuggestions(false);
    setPage(1);
    fetchData(1, searchCode);
  };
  useEffect(() => { fetchData(); }, []);

  useEffect(() => {
    api.get('/auth/me').then(r => {
      if (r.code === 0 && r.data) {
        const role = (r.data.role_code || '').toLowerCase();
        setIsManager(role === 'admin' || role === 'leader');
      }
    }).catch(() => {});
  }, []);

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

  const handleExport = async () => {
    try {
      const res = await api.get('/locations/export', { responseType: 'blob' });
      const url = window.URL.createObjectURL(new Blob([res as unknown as BlobPart]));
      const a = document.createElement('a');
      a.href = url;
      a.download = 'locations_export.xlsx';
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    } catch (err: any) {
      setMsg('导出失败: ' + (err.message || ''));
    }
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">库位管理</h1>
        <div className="flex gap-2">
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">新增库位</button>
          <button onClick={() => fileRef.current?.click()} className="bg-green-600 text-white px-4 py-2 rounded hover:bg-green-700">导入库位</button>
          <button onClick={handleExport} className="bg-green-700 text-white px-4 py-2 rounded hover:bg-green-800">导出库位</button>
          <a href="/api/v1/locations/template" className="bg-gray-500 text-white px-4 py-2 rounded hover:bg-gray-600">下载模板</a>
          <input ref={fileRef} type="file" accept=".xlsx,.xls" className="hidden" onChange={handleImport} />
          <HelpButton title="库位管理" sections={[
            { title: '功能概述', items: ['管理线边仓库位编码，记录库位容量和当前库存数量', '支持Excel批量导入库位', '库位启用/禁用管理'] },
            { title: '操作流程', items: ['新增或导入库位，填写库位编码、仓库、排/列信息', '编辑库位属性（容量、仓库区域等）', '禁用不再使用的库位'] }
          ]} />
        </div>
      </div>
      {msg && <div className="bg-blue-50 text-blue-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      <div className="flex gap-2 mb-3 relative">
        <div className="relative">
          <input
            type="text"
            className="border p-2 rounded w-64 text-sm"
            placeholder="搜索库位编码（自动匹配）"
            value={searchCode}
            onChange={e => { setSearchCode(e.target.value); }}
            onFocus={() => { if (suggestions.length > 0) setShowSuggestions(true); }}
            onBlur={() => setTimeout(() => setShowSuggestions(false), 200)}
            onKeyDown={e => { if (e.key === 'Enter') handleSearch(); }}
          />
          {showSuggestions && suggestions.length > 0 && (
            <div className="absolute z-10 w-full bg-white border rounded shadow-lg max-h-48 overflow-auto mt-1">
              {suggestions.map((loc: any) => (
                <div key={loc.id}
                  className="px-3 py-2 cursor-pointer hover:bg-blue-50 text-sm flex justify-between"
                  onMouseDown={() => selectSuggestion(loc)}>
                  <span className="font-mono">{loc.location_code}</span>
                  <span className="text-gray-400">{loc.warehouse} {loc.zone}{loc.row}-{loc.column}</span>
                </div>
              ))}
            </div>
          )}
        </div>
        <button onClick={handleSearch} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700 text-sm">搜索</button>
        {searchCode && <button onClick={() => { setSearchCode(''); setSuggestions([]); setPage(1); fetchData(1, ''); }} className="text-gray-500 hover:text-gray-700 text-sm">清除</button>}
      </div>

      {loading ? <p>加载中...</p> : (
        <>
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
                  {isManager && <button onClick={() => openEdit(l)} className="text-blue-600 hover:text-blue-800 text-sm">编辑</button>}
                  {isManager && <button onClick={() => handleDelete(l.id)} className="text-red-500 hover:text-red-700 text-sm">删除</button>}
                </td>
              </tr>
            ))}</tbody>
          </table>

          <div className="flex justify-between items-center mt-4 text-sm text-gray-600">
            <span>共 <strong>{total}</strong> 条记录，第 <strong>{page}</strong> / <strong>{Math.ceil(total / PAGE_SIZE) || 1}</strong> 页</span>
            <div className="flex gap-2">
              <button onClick={() => { const p = page - 1; setPage(p); fetchData(p, searchCode); }}
                disabled={page <= 1}
                className="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-30 disabled:cursor-not-allowed">上一页</button>
              <button onClick={() => { const p = page + 1; setPage(p); fetchData(p, searchCode); }}
                disabled={page >= Math.ceil(total / PAGE_SIZE)}
                className="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-30 disabled:cursor-not-allowed">下一页</button>
            </div>
          </div>
        </>
      )}

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
