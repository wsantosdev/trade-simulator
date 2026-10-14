import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { Account, AccountApi } from './account-api';
import { AccountPage } from './account-page';

describe('AccountPage', () => {
  let component: AccountPage;
  let fixture: ReturnType<typeof TestBed.createComponent<AccountPage>>;
  let operationResponse: Subject<Account>;
  let operate: ReturnType<typeof vi.fn>;
  let get: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    operationResponse = new Subject<Account>();
    const api = {
      open: vi.fn(),
      get: vi.fn(),
      operate: vi.fn(() => operationResponse.asObservable()),
    };
    operate = api.operate;
    get = api.get;
    TestBed.configureTestingModule({
      imports: [AccountPage],
      providers: [
        provideRouter([]),
        {
          provide: AccountApi,
          useValue: api,
        },
      ],
    });
    fixture = TestBed.createComponent(AccountPage);
    component = fixture.componentInstance;
  });

  it('shows no balance composition when no account is selected', () => {
    expect(component.availableBalanceRatio).toBe(0);
    expect(component.provisionedBalanceRatio).toBe(0);
  });

  it('calculates available and provisioned balance proportions', () => {
    component.account = {
      accountId: 'account-1',
      availableBalance: 75,
      provisionedBalance: 25,
    } satisfies Account;

    expect(component.availableBalanceRatio).toBe(75);
    expect(component.provisionedBalanceRatio).toBe(25);
  });

  it('avoids dividing by zero for a newly opened account', () => {
    component.account = {
      accountId: 'account-1',
      availableBalance: 0,
      provisionedBalance: 0,
    };

    expect(component.availableBalanceRatio).toBe(0);
    expect(component.provisionedBalanceRatio).toBe(0);
  });

  it('shows the provisioned balance summary without a provisioning form', () => {
    component.account = {
      accountId: 'account-1',
      availableBalance: 75,
      provisionedBalance: 25,
    };
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    const forms = fixture.nativeElement.querySelectorAll('form');

    expect(text).toContain('Valor provisionado');
    expect(text).not.toContain('Provisionar fundos');
    expect(forms).toHaveLength(3);
  });

  it('re-enables the deposit action as soon as the API responds successfully', () => {
    component.account = {
      accountId: 'account-1',
      availableBalance: 75,
      provisionedBalance: 0,
    };
    fixture.detectChanges();
    component.amounts.deposit = 10;
    fixture.detectChanges();

    component.submitOperation('deposits');
    fixture.detectChanges();
    let depositButton = fixture.nativeElement.querySelectorAll('button[type="submit"]')[1] as HTMLButtonElement;
    expect(depositButton.disabled).toBe(true);

    operationResponse.next({
      accountId: 'account-1',
      availableBalance: 85,
      provisionedBalance: 0,
    });
    fixture.detectChanges();
    depositButton = fixture.nativeElement.querySelectorAll('button[type="submit"]')[1] as HTMLButtonElement;

    expect(operate).toHaveBeenCalledOnce();
    expect(component.busy).toBe(false);
    expect(component.amounts.deposit).toBe(0);
    expect(depositButton.disabled).toBe(true);

  });

  it('re-enables the withdrawal action as soon as the API responds successfully', () => {
    component.account = {
      accountId: 'account-1',
      availableBalance: 75,
      provisionedBalance: 0,
    };
    fixture.detectChanges();
    component.amounts.withdrawal = 10;
    fixture.detectChanges();

    component.submitOperation('withdrawals');
    fixture.detectChanges();
    let withdrawalButton = fixture.nativeElement.querySelectorAll('button[type="submit"]')[2] as HTMLButtonElement;
    expect(withdrawalButton.disabled).toBe(true);

    operationResponse.next({
      accountId: 'account-1',
      availableBalance: 65,
      provisionedBalance: 0,
    });
    fixture.detectChanges();
    withdrawalButton = fixture.nativeElement.querySelectorAll('button[type="submit"]')[2] as HTMLButtonElement;

    expect(operate).toHaveBeenCalledOnce();
    expect(component.busy).toBe(false);
    expect(component.amounts.withdrawal).toBe(0);
    expect(withdrawalButton.disabled).toBe(true);

  });

  it('refreshes the balance every 2 seconds while an account is loaded and stops on destroy', () => {
    vi.useFakeTimers();
    try {
      const first: Account = { accountId: 'account-1', availableBalance: 10, provisionedBalance: 0 };
      const second: Account = { accountId: 'account-1', availableBalance: 4, provisionedBalance: 6 };
      get.mockReturnValueOnce(of(first)).mockReturnValue(of(second));

      vi.advanceTimersByTime(4_000);
      expect(get).not.toHaveBeenCalled();

      component.accountIdInput = 'account-1';
      component.loadAccount();
      expect(get).toHaveBeenCalledTimes(1);
      expect(component.account).toEqual(first);

      vi.advanceTimersByTime(2_000);
      expect(get).toHaveBeenCalledTimes(2);
      expect(component.account).toEqual(second);
      expect(component.successMessage).toBe('Conta carregada com sucesso.');

      fixture.destroy();
      vi.advanceTimersByTime(6_000);
      expect(get).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });
});
