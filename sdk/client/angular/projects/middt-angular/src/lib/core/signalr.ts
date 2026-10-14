import { inject, Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { CSRF_HEADER } from './bff-fetch';
import { MIDDT_CONFIG } from './config';

/** Wire contract of BaseSignalRHub<T>.Send (Middt.Framework.Model.SignalR.BaseSignalRRequestModel). */
export interface SignalRRequest<T> {
  /** 0 = broadcast, 1 = group, 2 = user. */
  messageType: 0 | 1 | 2;
  messageTo: string[];
  data: T;
}

/**
 * Connections to Middt SignalR hubs through the BFF (/hubs/** is proxied with the session's token, so no token is
 * handled in the browser). Counterpart of BaseSignalRClient in the .NET client SDK.
 */
@Injectable({ providedIn: 'root' })
export class MiddtSignalRService {
  readonly #config = inject(MIDDT_CONFIG);

  /** Creates (not starts) a connection to `hubPath` (e.g. /hubs/testhub) that calls `onReceive` for every message. */
  connect<T>(hubPath: string, onReceive: (message: T) => void): HubConnection {
    const connection = new HubConnectionBuilder()
      .withUrl(`${this.#config.apiBaseUrl}${hubPath}`, { headers: { [CSRF_HEADER]: '1' }, withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on('Receive', onReceive);
    return connection;
  }

  /** Sends a message through BaseSignalRHub.Send. */
  send<T>(connection: HubConnection, request: SignalRRequest<T>): Promise<void> {
    return connection.invoke('Send', request);
  }
}
