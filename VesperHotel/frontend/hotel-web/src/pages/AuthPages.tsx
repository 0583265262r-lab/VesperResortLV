import { useState, type FormEvent, type ReactNode } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ArrowRight, Eye, EyeOff } from 'lucide-react';
import { errorMessage, send } from '../api';
import { useAuth } from '../auth';
import { ErrorBox, SuccessBox } from '../components';
import type { AuthResponse } from '../types';

function AuthShell({ children }: { children: ReactNode }) {
  return <div className="auth-layout"><div className="auth-image"><img src="/images/suite.jpg" alt="A beautifully appointed Vesper suite" /><div><span className="eyebrow light">Your world, at Vesper</span><h2>A little more<br /><em>to look forward to.</em></h2></div></div><section className="auth-form">{children}</section></div>;
}
function PasswordInput({ value, onChange, label = 'Password', autoComplete = 'current-password' }: { value: string; onChange: (v: string) => void; label?: string; autoComplete?: string }) {
  const [show, setShow] = useState(false);
  return <label>{label}<div className="password-input"><input type={show ? 'text' : 'password'} required minLength={8} maxLength={100} value={value} onChange={e => onChange(e.target.value)} autoComplete={autoComplete} /><button type="button" className="icon-button" aria-label={show ? 'Hide password' : 'Show password'} onClick={() => setShow(!show)}>{show ? <EyeOff size={18} /> : <Eye size={18} />}</button></div></label>;
}
export function AuthPage({ register = false }: { register?: boolean }) {
  const [params] = useSearchParams(); const navigate = useNavigate(); const { accept } = useAuth();
  const [email, setEmail] = useState(''); const [password, setPassword] = useState(''); const [firstName, setFirst] = useState(''); const [lastName, setLast] = useState(''); const [phone, setPhone] = useState(''); const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const returnTo = params.get('returnTo'); const destination = returnTo?.startsWith('/') && !returnTo.startsWith('//') ? returnTo : '/account';
  async function submit(event: FormEvent) { event.preventDefault(); setError(''); setBusy(true); try { const response = await send<AuthResponse>(register ? '/auth/register' : '/auth/login', { email, password, ...(register ? { firstName, lastName, phoneNumber: phone } : {}) }); accept(response); navigate(destination); } catch (e) { setError(errorMessage(e)); } finally { setBusy(false); } }
  return <AuthShell><span className="eyebrow">My Vesper</span><h1>{register ? 'Make yourself at home.' : 'Welcome back.'}</h1><p>{register ? 'One account for every stay, experience and journey.' : 'Your next extraordinary moment is waiting.'}</p><form className="form-stack" onSubmit={submit}>{register && <div className="form-row"><label>First name<input required maxLength={80} autoComplete="given-name" value={firstName} onChange={e => setFirst(e.target.value)} /></label><label>Last name<input required maxLength={80} autoComplete="family-name" value={lastName} onChange={e => setLast(e.target.value)} /></label></div>}<label>Email address<input type="email" required autoComplete="email" maxLength={200} value={email} onChange={e => setEmail(e.target.value)} /></label><PasswordInput value={password} onChange={setPassword} autoComplete={register ? 'new-password' : 'current-password'} />{register ? <><small className="field-help">Use at least 8 characters, including uppercase, lowercase, a number and a symbol.</small><label>Phone number<input type="tel" autoComplete="tel" maxLength={30} value={phone} onChange={e => setPhone(e.target.value)} /></label></> : <Link className="form-sub-link" to="/forgot-password">Forgot your password?</Link>}<ErrorBox message={error} /><button className="button full" disabled={busy}>{busy ? 'A moment, please…' : register ? 'Create your account' : 'Sign in'}<ArrowRight size={17} /></button></form><p className="auth-switch">{register ? 'Already part of our world?' : 'Your first time here?'} <Link to={`${register ? '/login' : '/register'}${returnTo ? `?returnTo=${encodeURIComponent(returnTo)}` : ''}`}>{register ? 'Sign in' : 'Create an account'}</Link></p><p className="auth-footnote">Your details help us manage your reservations and keep you informed about your stay.</p></AuthShell>;
}

export function AdminLoginPage() {
  const navigate = useNavigate(); const { accept } = useAuth();
  const [email, setEmail] = useState('admin@vesper.local'); const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try {
      const response = await send<AuthResponse>('/auth/login', { email, password });
      if (response.user.role !== 'Admin') { await send('/auth/logout'); throw new Error('This account does not have administrator access.'); }
      accept(response); navigate('/admin');
    } catch (e) { setError(errorMessage(e)); } finally { setBusy(false); }
  }
  return <AuthShell><span className="eyebrow">Vesper management</span><h1>Administrator access.</h1><p>Sign in with the administrator account created by the local or Docker startup script.</p><form className="form-stack" onSubmit={submit}><label>Administrator email<input type="email" required autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} /></label><PasswordInput value={password} onChange={setPassword} /><ErrorBox message={error} /><button className="button full" disabled={busy}>{busy ? 'Verifying access…' : 'Enter management'}<ArrowRight size={17} /></button></form><div className="admin-login-help"><strong>Local development</strong><p>The email is <code>admin@vesper.local</code>. The generated password is stored as <code>ADMIN_PASSWORD</code> in the project’s <code>.env</code> file.</p></div><Link className="text-link" to="/login">Guest sign in <ArrowRight size={16} /></Link></AuthShell>;
}
export function ForgotPasswordPage() {
  const [email, setEmail] = useState(''); const [busy, setBusy] = useState(false); const [done, setDone] = useState(false); const [error, setError] = useState('');
  async function submit(e: FormEvent) { e.preventDefault(); setBusy(true); setError(''); try { await send('/auth/forgot-password', { email }); setDone(true); } catch (error) { setError(errorMessage(error)); } finally { setBusy(false); } }
  return <AuthShell><span className="eyebrow">A fresh start</span><h1>Forgot your password?</h1><p>Enter the email address associated with your Vesper account.</p>{done ? <SuccessBox>If an account exists for this address, password reset instructions have been sent.</SuccessBox> : <form className="form-stack" onSubmit={submit}><label>Email address<input type="email" required value={email} onChange={e => setEmail(e.target.value)} /></label><ErrorBox message={error} /><button className="button full" disabled={busy}>{busy ? 'Sending…' : 'Send reset instructions'}<ArrowRight size={17} /></button></form>}<Link className="text-link" to="/login">Return to sign in <ArrowRight size={16} /></Link></AuthShell>;
}
export function ResetPasswordPage() {
  const [params] = useSearchParams(); const [email, setEmail] = useState(params.get('email') || ''); const [token, setToken] = useState(params.get('token') || ''); const [password, setPassword] = useState(''); const [confirmation, setConfirmation] = useState(''); const [busy, setBusy] = useState(false); const [done, setDone] = useState(false); const [error, setError] = useState('');
  async function submit(e: FormEvent) { e.preventDefault(); if (password !== confirmation) { setError('Your passwords do not match.'); return; } setBusy(true); setError(''); try { await send('/auth/reset-password', { email, token, newPassword: password }); setDone(true); } catch (error) { setError(errorMessage(error)); } finally { setBusy(false); } }
  return <AuthShell><span className="eyebrow">My Vesper</span><h1>A new beginning.</h1><p>Choose a new password for your account.</p>{done ? <><SuccessBox>Your password has been updated.</SuccessBox><Link className="button full" to="/login">Sign in <ArrowRight size={17} /></Link></> : <form className="form-stack" onSubmit={submit}><label>Email address<input type="email" required value={email} onChange={e => setEmail(e.target.value)} /></label>{!params.get('token') && <label>Reset token<input required value={token} onChange={e => setToken(e.target.value)} /></label>}<PasswordInput value={password} onChange={setPassword} label="New password" autoComplete="new-password" /><PasswordInput value={confirmation} onChange={setConfirmation} label="Confirm new password" autoComplete="new-password" /><small className="field-help">Use at least 8 characters with uppercase, lowercase, a number and a symbol.</small><ErrorBox message={error} /><button className="button full" disabled={busy}>{busy ? 'Updating…' : 'Set new password'}<ArrowRight size={17} /></button></form>}</AuthShell>;
}
