import { TestBed } from '@angular/core/testing';
import { WindowManager } from './window-manager';

describe('WindowManager', () => {
  let manager: WindowManager;

  beforeEach(() => {
    manager = TestBed.inject(WindowManager);
    manager.setArea({ width: 1400, height: 800 });
  });

  it('limita cotações a 10', () => {
    for (let i = 0; i < 10; i++) expect(manager.open('quote')).not.toBeNull();
    expect(manager.open('quote')).toBeNull();
    expect(manager.counts().quote).toBe(10);
    expect(manager.canOpen('quote')).toBe(false);
  });

  it('limita boletas a 3', () => {
    for (let i = 0; i < 3; i++) manager.open('ticket');
    expect(manager.open('ticket')).toBeNull();
    expect(manager.counts().ticket).toBe(3);
  });

  it('mantém uma única janela dos demais tipos', () => {
    manager.open('orders');
    manager.open('orders');
    manager.open('portfolio');
    manager.open('portfolio');
    expect(manager.counts().orders).toBe(1);
    expect(manager.counts().portfolio).toBe(1);
  });

  it('permite reabrir após fechar', () => {
    const win = manager.open('account')!;
    manager.close(win);
    expect(manager.counts().account).toBe(0);
    expect(manager.open('account')).not.toBeNull();
  });

  it('acopla e desacopla janelas', () => {
    const win = manager.open('quote')!;
    manager.dock(win, 'left');
    expect(manager.windows().find((w) => w.id === win)!.zone).toBe('left');
    manager.float(win);
    expect(manager.windows().find((w) => w.id === win)!.zone).toBeNull();
  });

  it('restaura o layout padrão respeitando os limites', () => {
    manager.closeAll();
    manager.resetLayout();
    expect(manager.counts().quote).toBe(2);
    expect(manager.counts().ticket).toBe(1);
    expect(manager.windows().length).toBeGreaterThan(5);
  });
});
