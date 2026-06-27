// Shape model + serializer for the built-in SVG maker (panels/SvgMaker.svelte).
// Kept separate from the component so the export string can be built the same
// way regardless of whether the live DOM is mounted (used by the panel itself
// and, incidentally, testable in isolation).
export type ShapeType = 'rect' | 'ellipse' | 'line' | 'path' | 'text';

interface ShapeBase {
  id: string;
  fill: string;
  stroke: string;
  strokeWidth: number;
}
export interface RectShape extends ShapeBase {
  type: 'rect';
  x: number;
  y: number;
  w: number;
  h: number;
}
export interface EllipseShape extends ShapeBase {
  type: 'ellipse';
  cx: number;
  cy: number;
  rx: number;
  ry: number;
}
export interface LineShape extends ShapeBase {
  type: 'line';
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}
export interface PathShape extends ShapeBase {
  type: 'path';
  d: string;
}
export interface TextShape extends ShapeBase {
  type: 'text';
  x: number;
  y: number;
  text: string;
  fontSize: number;
}
export type Shape = RectShape | EllipseShape | LineShape | PathShape | TextShape;

function esc(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

export function shapeToSvgString(s: Shape): string {
  const common = `fill="${s.type === 'line' || s.type === 'path' ? 'none' : s.fill}" stroke="${s.stroke}" stroke-width="${s.strokeWidth}"`;
  switch (s.type) {
    case 'rect':
      return `<rect x="${s.x}" y="${s.y}" width="${s.w}" height="${s.h}" ${common} />`;
    case 'ellipse':
      return `<ellipse cx="${s.cx}" cy="${s.cy}" rx="${s.rx}" ry="${s.ry}" ${common} />`;
    case 'line':
      return `<line x1="${s.x1}" y1="${s.y1}" x2="${s.x2}" y2="${s.y2}" stroke="${s.stroke}" stroke-width="${s.strokeWidth}" stroke-linecap="round" />`;
    case 'path':
      return `<path d="${s.d}" fill="none" stroke="${s.stroke}" stroke-width="${s.strokeWidth}" stroke-linecap="round" stroke-linejoin="round" />`;
    case 'text':
      return `<text x="${s.x}" y="${s.y}" font-size="${s.fontSize}" font-family="sans-serif" ${common}>${esc(s.text)}</text>`;
  }
}

export function serializeSvgDocument(shapes: Shape[], width: number, height: number): string {
  const body = shapes.map(shapeToSvgString).join('\n  ');
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}" width="${width}" height="${height}">\n  ${body}\n</svg>`;
}
