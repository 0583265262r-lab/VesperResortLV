import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, send, setAccessToken } from './api';
import type { AuthResponse, User } from './types';

interface AuthContextValue { user: User | null; loading: boolean; accept: (response: AuthResponse) => void; updateUser: (user: User) => void; logout: () => Promise<void>; }
const AuthContext = createContext<AuthContextValue | null>(null);
let bootstrap: Promise<AuthResponse | null> | null = null;

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  function accept(response: AuthResponse) { setAccessToken(response.accessToken); setUser(response.user); }
  useEffect(() => {
    let active = true;
    if (!bootstrap) bootstrap = api<AuthResponse>('/auth/refresh', { method: 'POST' }, false).catch(() => null);
    bootstrap.then(response => { if (active) { if (response) accept(response); setLoading(false); } });
    return () => { active = false; };
  }, []);
  async function logout() { await send('/auth/logout'); setAccessToken(null); setUser(null); bootstrap = null; }
  return <AuthContext.Provider value={{ user, loading, accept, updateUser: setUser, logout }}>{children}</AuthContext.Provider>;
}
export function useAuth() { return useContext(AuthContext)!; }
