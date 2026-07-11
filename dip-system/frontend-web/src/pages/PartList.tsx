import { useEffect, useState, useRef } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const PART_TYPES: Record<number, string> = { 1: '电子元器件', 2: 'PCB', 3: '结构件', 4: '包装材料', 5: '辅料' };

export default function PartList() {
  const [data, setData] = useState<any[]>([]);
  const [msg, setMsg] = useState('');
  const [keyword, setKeyword] = useState('');
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState({ part_no: '', part_name: '', part_type: 1, unit: 'PCS', specification: '', msl_level: 0, status: 1 });
  const fileRef = useRef<HTMLInputElement>(null);

  const fetchData = async () => {
    const params: any = { page: 1, page_size: 100 };
    if (keyword) params.keyword = keyword;
    try { setData((await api.get('/parts', { params })).data?.items || []); } catch {}
  };
  useEffect(() => { fetchData(); }, []);

  const handleSearch = () => fetchData();

  const openCreate = () => {
    setEditId(null); setForm({ part_no: '', part_name: '', part_type: 1, unit: 'PCS', specification: '', msl_level: 0, status: 1 }); setShowDialog(true);
  };
  const openEdit = (p: any) => {
    setEditId(p.id); setForm({ part_no: p.part_no, part_name: p.part_name, part_type: p.part_type, unit: p.unit, specification: p.specification || '', msl_level: p.msl_level || 0, status: p.status }); setShowDialog(true);
  };

  const handleSubmit = async () => {
    if (!form.part_no || !form.part_name) return alert('料号和名称为必填');
    try {
      if (editId) { await api.put(`/parts/${editId}`, form); setMsg('更新成功'); }
      else { await api.post('/parts', form); setMsg('创建成功'); }
      setShowDialog(false); fetchData();
    } catch {}
  };

  const handleImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]; if (!file) return;
    const fd = new FormData(); fd.append('file', file);
    try {
      const res = await api.post('/parts/import', fd, { headers: { 'Content-Type': 'multipart/form-data' }, timeout: 60000 });
      setMsg(`导入成功: ${res.data?.count || 0} 条`); fetchData();
    } catch (err: any) { setMsg('导入失败: ' + (err.response?.data?.message || err.message)); }
    e.target.value = '';
  };

  const handleDelete = async (id: number) => {
    if (!confirm('确认删除？')) return;
    try { await api.delete(`/parts/${id}`); setMsg('已删除'); fetchData(); } catch {}
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">物料管理</h1>
        <div className="flex gap-2">
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">新增物料</button>
          <button onClick={() => fileRef.current?.click()} className="bg-green-600 text-white px-4 py-2 rounded hover:bg-green-700">导入物料</button>
          <a href="/api/v1/parts/template" className="bg-gray-500 text-white px-4 py-2 rounded hover:bg-gray-600">下载模板</a>
          <input ref={fileRef} type="file" accept=".xlsx,.xls" className="hidden" onChange={handleImport} />
          <HelpButton title="物料管理" sections={[
            { title: '功能概述', items: ['管理SMT生产用物料档案，包括料号、名称、规格、MSL等级等属性', '支持Excel批量导入和模板下载', '支持软删除保护数据完整性，可恢复已删除物料'] },
            { title: '操作流程', items: ['点击"新增物料"或"导入物料"创建物料档案', '填写料号、名称、规格、MSL等级等必填信息', '编辑或禁用不再使用的物料'] }
          ]} />
        </div>
      </div>

      <div className="bg-white rounded-lg shadow p-4 mb-4 flex gap-4 items-end">
        <div><label className="block text-sm text-gray-600 mb-1">搜索</label>
          <input className="border rounded px-3 py-1.5 w-64" placeholder="料号或名称" value={keyword}
            onChange={e => setKeyword(e.target.value)} onKeyDown={e => e.key === 'Enter' && handleSearch()} />
        </div>
        <button onClick={handleSearch} className="bg-blue-500 text-white px-4 py-1.5 rounded hover:bg-blue-600">查询</button>
        <button onClick={() => { setKeyword(''); setTimeout(fetchData, 0); }} className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
      </div>
      {msg && <div className="bg-blue-50 text-blue-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      <table className="w-full bg-white rounded-lg shadow">
        <thead><tr className="bg-gray-50 text-left text-sm">
          <th className="p-3">料号</th><th className="p-3">名称</th><th className="p-3">规格</th><th className="p-3">单位</th><th className="p-3">类型</th><th className="p-3">状态</th><th className="p-3 w-24">操作</th>
        </tr></thead>
        <tbody>{data.map(p => (
          <tr key={p.id} className="border-t hover:bg-gray-50">
            <td className="p-3 font-mono text-sm">{p.part_no}</td><td className="p-3">{p.part_name}</td>
            <td className="p-3 text-sm text-gray-600">{p.specification || '-'}</td><td className="p-3">{p.unit}</td>
            <td className="p-3 text-sm">{PART_TYPES[p.part_type] || p.part_type}</td>
            <td className="p-3">{p.status === 1 ? <span className="text-green-600">启用</span> : <span className="text-red-500">禁用</span>}</td>
            <td className="p-3 space-x-1">
              <button onClick={() => openEdit(p)} className="text-blue-600 hover:text-blue-800 text-sm">编辑</button>
              <button onClick={() => handleDelete(p.id)} className="text-red-500 hover:text-red-700 text-sm">删除</button>
            </td>
          </tr>))}</tbody>
      </table>

      {showDialog && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[500px]">
            <h2 className="text-xl font-bold mb-4">{editId ? '编辑物料' : '新增物料'}</h2>
            <div className="grid grid-cols-2 gap-3">
              <div><label className="block text-sm mb-1">料号 *</label>
                <input className="w-full border p-2 rounded" value={form.part_no}
                  onChange={e => setForm({ ...form, part_no: e.target.value })} disabled={!!editId} /></div>
              <div><label className="block text-sm mb-1">名称 *</label>
                <input className="w-full border p-2 rounded" value={form.part_name}
                  onChange={e => setForm({ ...form, part_name: e.target.value })} /></div>
              <div><label className="block text-sm mb-1">规格</label>
                <input className="w-full border p-2 rounded" value={form.specification}
                  onChange={e => setForm({ ...form, specification: e.target.value })} /></div>
              <div><label className="block text-sm mb-1">单位</label>
                <input className="w-full border p-2 rounded" value={form.unit}
                  onChange={e => setForm({ ...form, unit: e.target.value })} /></div>
              <div><label className="block text-sm mb-1">类型</label>
                <select className="w-full border p-2 rounded" value={form.part_type}
                  onChange={e => setForm({ ...form, part_type: Number(e.target.value) })}>
                  {Object.entries(PART_TYPES).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></div>
              <div><label className="block text-sm mb-1">MSL等级</label>
                <input type="number" className="w-full border p-2 rounded" value={form.msl_level}
                  onChange={e => setForm({ ...form, msl_level: Number(e.target.value) })} /></div>
              {editId && <div><label className="block text-sm mb-1">状态</label>
                <select className="w-full border p-2 rounded" value={form.status}
                  onChange={e => setForm({ ...form, status: Number(e.target.value) })}>
                  <option value={1}>启用</option><option value={0}>禁用</option></select></div>}
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
