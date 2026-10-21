import { Injectable, signal } from '@angular/core';

/** The withdrawal being told apart as a card payment: EUR amount (positive) and day. */
export interface CardPaymentTarget {
  id: string;
  broker: string;
  amount: number;
  on: string;
}

/** Opens the global "card payment" dialog for a withdrawal a broker sync reported. */
@Injectable({ providedIn: 'root' })
export class CardPayment {
  readonly target = signal<CardPaymentTarget | null>(null);

  open(target: CardPaymentTarget) {
    this.target.set(target);
  }

  close() {
    this.target.set(null);
  }
}
