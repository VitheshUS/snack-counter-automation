import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Session } from './session.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  session = inject(Session);
  constructor() {
    void this.session.load();
  }
  officeName(id: number) {
    return this.session.offices().find((o) => o.id === id)?.name ?? 'Office';
  }
}
