import { Injectable, inject, NgZone } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../auth/auth.service';

export interface PriceUpdate {
  symbol: string;
  bid: number;
  ask: number;
  timestamp: Date;
}

export interface AlertNotification {
  alertId: number;
  symbol: string;
  message: string;
  severity: string;
  triggeredAt: Date;
  aiEnrichment?: string;
}

export interface PositionUpdate {
  positionId: number;
  symbol: string;
  direction: string;
  volume: number;
  entryPrice: number;
  currentPrice: number;
  pnL: number;
}

export interface AccountUpdate {
  balance: number;
  equity: number;
  unrealizedPnL: number;
  margin: number;
  freeMargin: number;
  marginLevel: number;
}

export interface MarginWarning {
  level: string; // 'MarginCall' | 'StopOut'
  marginLevel: number;
  equity: number;
  usedMargin: number;
  freeMargin: number;
  message: string;
}

@Injectable({
  providedIn: 'root'
})
export class SignalRService {
  private hubConnection: signalR.HubConnection | null = null;
  private authService = inject(AuthService);
  private ngZone = inject(NgZone);

  private connectionStateSubject = new BehaviorSubject<boolean>(false);
  private priceUpdatesSubject = new BehaviorSubject<Map<string, PriceUpdate>>(new Map());
  private alertsSubject = new BehaviorSubject<AlertNotification[]>([]);
  private positionsSubject = new BehaviorSubject<PositionUpdate[]>([]);
  private accountUpdatesSubject = new BehaviorSubject<AccountUpdate | null>(null);
  private marginWarningSubject = new BehaviorSubject<MarginWarning | null>(null);

  connectionState$ = this.connectionStateSubject.asObservable();
  priceUpdates$ = this.priceUpdatesSubject.asObservable();
  alerts$ = this.alertsSubject.asObservable();
  positions$ = this.positionsSubject.asObservable();
  accountUpdates$ = this.accountUpdatesSubject.asObservable();
  marginWarning$ = this.marginWarningSubject.asObservable();

  async connect(): Promise<void> {
    if (this.hubConnection) return;

    const token = this.authService.getToken();
    if (!token) {
        console.error('SignalR cannot connect: No access token available');
        return;
    }

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hub`, {
        accessTokenFactory: () => this.authService.getToken() || '',
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .build();

    this.registerHandlers();

    try {
      await this.hubConnection.start();
      this.connectionStateSubject.next(true);
      console.log('SignalR connected');
    } catch (err) {
      console.error('SignalR connection error:', err);
      this.connectionStateSubject.next(false);
    }

    this.hubConnection.onreconnecting(() => {
      this.connectionStateSubject.next(false);
    });

    this.hubConnection.onreconnected(() => {
      this.connectionStateSubject.next(true);
    });

    this.hubConnection.onclose(() => {
      this.connectionStateSubject.next(false);
    });
  }

  disconnect(): void {
    if (this.hubConnection) {
      this.hubConnection.stop();
      this.hubConnection = null;
      this.connectionStateSubject.next(false);
    }
  }

  async subscribeToSymbol(symbol: string): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.invoke('SubscribeToSymbol', symbol);
    }
  }

  async unsubscribeFromSymbol(symbol: string): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.invoke('UnsubscribeFromSymbol', symbol);
    }
  }

  private registerHandlers(): void {
    if (!this.hubConnection) return;

    this.hubConnection.on('ReceivePriceUpdate', (update: PriceUpdate) => {
      this.ngZone.run(() => {
        const prices = this.priceUpdatesSubject.value;
        prices.set(update.symbol, update);
        this.priceUpdatesSubject.next(new Map(prices));
      });
    });

    this.hubConnection.on('ReceiveAlert', (alert: AlertNotification) => {
      this.ngZone.run(() => {
        const alerts = this.alertsSubject.value;
        // If this is an AI enrichment follow-up, update the existing alert
        const existing = alerts.findIndex(a => a.alertId === alert.alertId);
        if (existing >= 0 && alert.aiEnrichment) {
          alerts[existing] = { ...alerts[existing], aiEnrichment: alert.aiEnrichment };
          this.alertsSubject.next([...alerts]);
        } else if (existing < 0) {
          this.alertsSubject.next([alert, ...alerts].slice(0, 50));
        }
      });
    });

    this.hubConnection.on('ReceivePositionUpdate', (position: PositionUpdate) => {
      this.ngZone.run(() => {
        const positions = this.positionsSubject.value;
        const index = positions.findIndex(p => p.positionId === position.positionId);
        if (index >= 0) {
          positions[index] = position;
        } else {
          positions.push(position);
        }
        this.positionsSubject.next([...positions]);
      });
    });

    this.hubConnection.on('ReceiveAccountUpdate', (account: AccountUpdate) => {
      this.ngZone.run(() => {
        this.accountUpdatesSubject.next(account);
      });
    });

    this.hubConnection.on('ReceiveTradeExecuted', (trade: any) => {
      this.ngZone.run(() => {
        // Remove closed position from list
        const positions = this.positionsSubject.value.filter(
          p => p.positionId !== trade.tradeId
        );
        this.positionsSubject.next(positions);
      });
    });

    this.hubConnection.on('ReceiveMarginWarning', (warning: MarginWarning) => {
      this.ngZone.run(() => {
        this.marginWarningSubject.next(warning);
      });
    });
  }
}
