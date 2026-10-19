// Wire types of the Middt server driven UI (Middt.Framework.Ui.Abstractions/Model, Middt.Framework.Model response
// envelope). JSON is camelCase.

/** Layout kinds: a routable page (`/ui/{key}`) or a view embedded elsewhere (e.g. a workflow state). */
export const UiLayoutKind = {
  page: 'page',
  view: 'view',
} as const;

export type UiLayoutKindName = (typeof UiLayoutKind)[keyof typeof UiLayoutKind];

/** A server driven screen: a tree of {@link UiNode}s (registered widgets). */
export interface UiLayoutDocument {
  /** Optional JSON Schema reference for editors; ignored at runtime. */
  $schema?: string | null;
  /** Unique key (`a-z 0-9 - _ .`), also the page route `/ui/{key}`. */
  key: string;
  title?: string | null;
  description?: string | null;
  /** `page` (default) or `view`. */
  kind?: UiLayoutKindName | string;
  /** Revision number; on save it is the version the change is based on (0 = new layout). */
  version?: number;
  /** Menu entry of a page (omitted for pages reached by links only). */
  menu?: UiMenu | null;
  /** Roles allowed to open the layout (any of them); empty = every authenticated user. */
  roles?: string[];
  body: UiNode[];
}

export interface UiMenu {
  title?: string | null;
  /** Material icon name. */
  icon?: string | null;
  order?: number;
}

/** Reserved node properties; everything else is a widget property. */
export const UI_NODE_RESERVED: readonly string[] = Object.freeze(['widget', 'id', 'visibleWhen', 'children']);

/**
 * One widget instance with its properties inline:
 * `{ "widget": "heading", "visibleWhen": "data.amount > 0", "text": "Hello {{ user.Name }}" }`.
 */
export interface UiNode {
  /** Registered widget name. */
  widget: string;
  /** Optional id (unique per layout), used for element ids. */
  id?: string | null;
  /** Boolean expression; the node is hidden when it evaluates to false. */
  visibleWhen?: string | null;
  children?: UiNode[] | null;
  /** Widget properties. */
  [prop: string]: unknown;
}

/** Widget properties of a node (everything except widget/id/visibleWhen/children). */
export function uiNodeProps(node: UiNode): Record<string, unknown> {
  const props: Record<string, unknown> = {};
  for (const [name, value] of Object.entries(node)) {
    if (!UI_NODE_RESERVED.includes(name)) {
      props[name] = value;
    }
  }

  return props;
}

/** List entry of a stored layout (`GET api/ui/layouts`). */
export interface UiLayoutSummary {
  key: string;
  title?: string | null;
  kind: UiLayoutKindName | string;
  version: number;
  menu?: UiMenu | null;
  /** ISO date time. */
  updatedAt: string;
  updatedBy?: string | null;
}

/** A previous version of a layout (`GET api/ui/layouts/{key}/revisions`). */
export interface UiLayoutRevision {
  version: number;
  /** ISO date time. */
  createdAt: string;
  createdBy?: string | null;
  /** Layout JSON text. */
  content: string;
}

/** Result of a validation: empty `errors` means valid. */
export interface UiValidationResult {
  errors: string[];
  isValid?: boolean;
}

/** Built-in widget names (the server catalog may register more; see `GET api/ui/widgets`). */
export const UiWidgetNames = {
  heading: 'heading',
  text: 'text',
  fields: 'fields',
  alert: 'alert',
  badge: 'badge',
  section: 'section',
  columns: 'columns',
  divider: 'divider',
  list: 'list',
  link: 'link',
  grid: 'grid',
  record: 'record',
  layout: 'layout',
} as const;

/** Form/search field types. */
export const UI_FIELD_TYPES: readonly string[] = Object.freeze(['text', 'textarea', 'number', 'integer', 'date', 'datetime', 'checkbox', 'select', 'picker']);

export type UiPropTypeName =
  | 'string'
  | 'template'
  | 'expression'
  | 'number'
  | 'integer'
  | 'boolean'
  | 'enum'
  | 'object'
  | 'array'
  | 'templateMap'
  | 'options'
  | 'format';

/** Widget description for editors (`GET api/ui/widgets`). */
export interface UiWidgetSpecModel {
  name: string;
  description: string;
  children: 'none' | 'optional' | 'required';
  /** Interactive widgets (data grids, forms) need an interactive host. */
  interactive: boolean;
  props: UiPropSpecModel[];
}

export interface UiPropSpecModel {
  name: string;
  type: UiPropTypeName | string;
  description?: string | null;
  required: boolean;
  /** Allowed values of an `enum`. */
  values?: string[] | null;
  /** Properties of an `object`, of the items of an `array` or of object `options`. */
  properties?: UiPropSpecModel[] | null;
}

/** `ResultEnum` of the response envelope (serialized as a number). */
export const ResultEnum = {
  success: 0,
  info: 1,
  error: -1,
} as const;

export type ResultValue = (typeof ResultEnum)[keyof typeof ResultEnum];

/** Response envelope of the Middt APIs (`BaseResponseModel`). */
export interface BaseResponseModel {
  result: ResultValue | number;
  messageList: string[];
}

/** Response envelope with data (`BaseResponseDataModel<T>`). */
export interface BaseResponseDataModel<T> extends BaseResponseModel {
  count: number;
  data?: T | null;
}

export function isSuccess(response: BaseResponseModel): boolean {
  return response.result === ResultEnum.success;
}

/** Error text of a response (the messages joined by a space). */
export function errorText(response: BaseResponseModel): string {
  return (response.messageList ?? []).join(' ');
}

/** CRUD search request (`BaseSearchRequestModel<T>` wire format). */
export interface SearchRequest<TFilter = Record<string, unknown>> {
  currentPage: number;
  /** Page size; zero or negative returns every matching row. */
  requestItemSize: number;
  sortProperty: string | null;
  /** Legacy wire semantics: `true` sorts ascending. */
  sortReverse: boolean;
  requestModel: TFilter;
}
