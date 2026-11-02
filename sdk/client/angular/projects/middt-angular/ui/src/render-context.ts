import { inject, Injectable } from '@angular/core';
import { UiRenderContext } from '@middt/ui-core';
import { AuthService } from '@middt/angular/core';
import { MIDDT_CONFIG } from '@middt/angular/core';

/** Creates interactive render contexts for the signed-in user (ExpressionUser.FromPrincipal of the Blazor hosts). */
@Injectable({ providedIn: 'root' })
export class UiRenderContextFactory {
  readonly #auth = inject(AuthService);
  readonly #config = inject(MIDDT_CONFIG);

  /** Loads the user once (the auth guard usually did already). */
  ready(): Promise<unknown> {
    return this.#auth.load();
  }

  create(
    variables: Record<string, unknown>,
    idPrefix: string,
    availableTransitions?: Iterable<string>,
    executeTransition?: (key: string, input: Record<string, unknown> | null) => void | Promise<void>,
  ): UiRenderContext {
    const user = this.#auth.user();
    return new UiRenderContext({
      user: { id: user.id ?? null, name: user.name ?? null, roles: user.roles },
      variables,
      interactive: true,
      idPrefix,
      locale: this.#config.locale,
      availableTransitions,
      executeTransition,
    });
  }
}
