import { useEffect, useState, type ReactNode } from 'react';
import { ArrowRight, Check, LoaderCircle, X } from 'lucide-react';
import { Link } from 'react-router-dom';
import { api, errorMessage, money } from './api';
import type { CatalogItem } from './types';

export function useApi<T>(path: string | null) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!path) { setLoading(false); return; }
    let active = true; setLoading(true); setError('');
    api<T>(path).then(result => { if (active) setData(result); }).catch(e => { if (active) setError(errorMessage(e)); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [path, revision]);
  return { data, error, loading, refresh: () => setRevision(value => value + 1), setData };
}
export function Loading({ label = 'A moment, please' }: { label?: string }) { return <div className="loading" role="status"><LoaderCircle className="spin" size={22} /><span>{label}</span></div>; }
export function ErrorBox({ message, retry }: { message: string; retry?: () => void }) { if (!message) return null; return <div className="error-box" role="alert"><span>{message}</span>{retry && <button className="text-button" onClick={retry}>Try again <ArrowRight size={15} /></button>}</div>; }
export function SuccessBox({ children }: { children: ReactNode }) { return <div className="success-box" role="status"><Check size={18} />{children}</div>; }
export function Empty({ title, children }: { title: string; children?: ReactNode }) { return <div className="empty-state"><span className="eyebrow">A little room for possibility</span><h3>{title}</h3>{children}</div>; }
export function Status({ value }: { value: string }) { return <span className={`status status-${value.toLowerCase()}`}>{value}</span>; }
export function PageTitle({ eyebrow, title, children }: { eyebrow: string; title: string; children?: ReactNode }) { return <div className="page-title"><span className="eyebrow">{eyebrow}</span><h1>{title}</h1>{children && <p>{children}</p>}</div>; }
export function Modal({ title, children, onClose }: { title: string; children: ReactNode; onClose: () => void }) {
  useEffect(() => { function escape(e: KeyboardEvent) { if (e.key === 'Escape') onClose(); } window.addEventListener('keydown', escape); const previous = document.body.style.overflow; document.body.style.overflow = 'hidden'; return () => { window.removeEventListener('keydown', escape); document.body.style.overflow = previous; }; }, [onClose]);
  return <div className="modal-backdrop" onClick={onClose}><div className="modal" role="dialog" aria-modal="true" aria-label={title} onClick={e => e.stopPropagation()}><div className="modal-header"><h2>{title}</h2><button className="icon-button" onClick={onClose} aria-label="Close dialog"><X /></button></div>{children}</div></div>;
}
export function Pagination({ page, total, pageSize, onChange }: { page: number; total: number; pageSize: number; onChange: (page: number) => void }) { return <div className="pagination"><span>{total ? `${(page - 1) * pageSize + 1}–${Math.min(page * pageSize, total)} of ${total}` : '0 results'}</span><div><button className="button button-outline small" disabled={page <= 1} onClick={() => onChange(page - 1)}>Previous</button><button className="button button-outline small" disabled={page * pageSize >= total} onClick={() => onChange(page + 1)}>Next</button></div></div>; }
export function ResourceCard({ item, index = 0 }: { item: CatalogItem; index?: number }) {
  return <article className="resource-card"><Link to={`/discover/${item.id}`} className="resource-photo"><img src={item.imageUrl || '/images/suite.jpg'} alt={item.name} loading="lazy" /><span className="photo-number">0{index + 1}</span><span className="photo-link"><ArrowRight size={21} /></span></Link><div className="resource-meta"><span className="eyebrow">{item.tagline || (item.kind === 'Room' ? 'Your private retreat' : 'The Vesper collection')}</span><span className="price">{money(item.basePrice)} <small>/ {item.priceUnit}</small></span></div><h3><Link to={`/discover/${item.id}`}>{item.name}</Link></h3><p>{item.description}</p><div className="resource-features">{item.features.slice(0, 3).map(feature => <span key={feature}>{feature}</span>)}</div></article>;
}
