import type { ReactNode } from 'react';

export function Icon({ name }: { name: 'compass' | 'plan' | 'chart' | 'settings' | 'coins' | 'clock' | 'hammer' | 'warning' | 'info' | 'swap' }) {
  const paths: Record<typeof name, ReactNode> = {
    compass: <><circle cx="12" cy="12" r="8.5" /><path d="m15.8 8.2-2.4 5.2-5.2 2.4 2.4-5.2 5.2-2.4Z" /><path d="M12 2v2M22 12h-2M12 22v-2M2 12h2" /></>,
    plan: <><path d="M7 3.5h8l4 4V20a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 5 20V5a1.5 1.5 0 0 1 1.5-1.5Z" /><path d="M14.5 3.8V8H19M8 12h8M8 16h8" /></>,
    chart: <><path d="M4 20V11M10 20V5M16 20v-7M22 20H2" /><path d="m4 8 6-5 6 7 5-4" /></>,
    settings: <><circle cx="12" cy="12" r="3" /><path d="m19.4 15 .1.1 1.4 1.1-1.4 2.4-1.7-.7a7.8 7.8 0 0 1-1.7 1l-.3 1.8h-2.8l-.3-1.8a7.8 7.8 0 0 1-1.7-1l-1.7.7-1.4-2.4 1.4-1.1a7.5 7.5 0 0 1 0-2l-1.4-1.1 1.4-2.4 1.7.7a7.8 7.8 0 0 1 1.7-1l.3-1.8h2.8l.3 1.8a7.8 7.8 0 0 1 1.7 1l1.7-.7 1.4 2.4-1.4 1.1a7.5 7.5 0 0 1 0 2Z" /></>,
    coins: <><ellipse cx="9" cy="7" rx="6" ry="2.5" /><path d="M3 7v4c0 1.4 2.7 2.5 6 2.5M15 7v4M3 11v4c0 1.4 2.7 2.5 6 2.5" /><ellipse cx="16" cy="14" rx="5" ry="2.2" /><path d="M11 14v4c0 1.2 2.2 2.2 5 2.2s5-1 5-2.2v-4" /></>,
    clock: <><circle cx="12" cy="12" r="8.5" /><path d="M12 7v5l3.5 2M12 2v2M22 12h-2" /></>,
    hammer: <><path d="m14.5 6.5 3-3 4 4-3 3M14.5 6.5l-8 8M4 18l2-2 4 4-2 2-4-4Z" /><path d="m12 9 3 3M18 4l2 2" /></>,
    warning: <><path d="m12 3 10 18H2L12 3Z" /><path d="M12 9v5M12 17.5v.1" /></>,
    info: <><circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7.5v.1" /></>,
    swap: <><path d="M4 7h15l-3-3M20 17H5l3 3M17 4l3 3-3 3M7 14l-3 3 3 3" /></>,
  };

  return <svg className="p05a-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>;
}
