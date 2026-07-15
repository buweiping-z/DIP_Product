import { useState } from 'react';
import { HelpCircle } from 'lucide-react';

interface HelpSection {
  title: string;
  items: string[];
}

interface Props {
  title: string;
  sections: HelpSection[];
}

export default function HelpButton({ title, sections }: Props) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        onClick={() => setOpen(true)}
        className="flex items-center gap-1 text-gray-400 hover:text-blue-600 text-sm px-3 py-1.5 border border-gray-300 rounded hover:border-blue-400 transition-colors"
        title="功能说明"
      >
        <HelpCircle size={16} /> 说明
      </button>

      {open && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50" onClick={() => setOpen(false)}>
          <div className="bg-white rounded-lg p-6 w-[560px] max-h-[80vh] overflow-auto" onClick={e => e.stopPropagation()}>
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-xl font-bold">{title} — 功能说明</h2>
              <button onClick={() => setOpen(false)} className="text-gray-400 hover:text-gray-600 text-2xl leading-none">&times;</button>
            </div>
            {sections.map((s, i) => (
              <div key={i} className="mb-4">
                <h3 className="font-semibold text-blue-700 mb-2">{s.title}</h3>
                <ul className="list-disc list-inside space-y-1 text-sm text-gray-700">
                  {s.items.map((item, j) => (
                    <li key={j}>{item}</li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </div>
      )}
    </>
  );
}
