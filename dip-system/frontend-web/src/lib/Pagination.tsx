interface PaginationProps {
  page: number;
  pageSize: number;
  total: number;
  onChange: (page: number) => void;
}

export default function Pagination({ page, pageSize, total, onChange }: PaginationProps) {
  const totalPages = Math.ceil(total / pageSize) || 1;
  return (
    <div className="flex items-center justify-between mt-4 text-sm text-gray-600">
      <span>共 {total} 条记录</span>
      <div className="flex gap-2">
        <button
          disabled={page <= 1}
          onClick={() => onChange(page - 1)}
          className="px-3 py-1 border rounded disabled:opacity-40 hover:bg-gray-50"
        >
          上一页
        </button>
        <span className="px-2 py-1">{page} / {totalPages}</span>
        <button
          disabled={page >= totalPages}
          onClick={() => onChange(page + 1)}
          className="px-3 py-1 border rounded disabled:opacity-40 hover:bg-gray-50"
        >
          下一页
        </button>
      </div>
    </div>
  );
}
