import { useEffect, useRef } from 'react';
import JsBarcode from 'jsbarcode';

interface BarcodeProps {
  value: string;
  height?: number;
  fontSize?: number;
}

export default function Barcode({ value, height = 30, fontSize = 10 }: BarcodeProps) {
  const svgRef = useRef<SVGSVGElement>(null);

  useEffect(() => {
    if (svgRef.current && value) {
      try {
        JsBarcode(svgRef.current, value, {
          format: 'CODE128',
          height,
          fontSize,
          displayValue: true,
          margin: 2,
        });
      } catch {
        // 非法字符则静默跳过
      }
    }
  }, [value, height, fontSize]);

  if (!value) return null;
  return <svg ref={svgRef} />;
}
