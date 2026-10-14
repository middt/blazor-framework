// Parse/serialize of layout documents like UiJson (Middt.Framework.Ui.Abstractions): comments and trailing commas are
// accepted, the document is normalized (defaults, property order, nulls omitted) and written indented.

import { UI_NODE_RESERVED, type UiLayoutDocument, type UiMenu, type UiNode } from '@middt/ui-core';
import { isJsonObject, type JsonObject } from './widget-base';

/** Removes `//` and `/* *\/` comments and trailing commas outside strings (JsonCommentHandling.Skip, AllowTrailingCommas). */
function stripJsonExtensions(text: string): string {
  let result = '';
  let i = 0;
  while (i < text.length) {
    const c = text[i]!;
    if (c === '"') {
      const start = i++;
      while (i < text.length && text[i] !== '"') {
        i += text[i] === '\\' ? 2 : 1;
      }

      result += text.slice(start, ++i);
    } else if (c === '/' && text[i + 1] === '/') {
      while (i < text.length && text[i] !== '\n') {
        i++;
      }
    } else if (c === '/' && text[i + 1] === '*') {
      const end = text.indexOf('*/', i + 2);
      i = end < 0 ? text.length : end + 2;
    } else if (c === ',') {
      let j = i + 1;
      while (j < text.length && /\s/.test(text[j]!)) {
        j++;
      }

      if (text[j] !== '}' && text[j] !== ']') {
        result += c;
      }

      i++;
    } else {
      result += c;
      i++;
    }
  }

  return result;
}

const stringValue = (value: unknown, name: string): string | null => {
  if (value === null || value === undefined) {
    return null;
  }

  if (typeof value !== 'string') {
    throw new SyntaxError(`'${name}' must be a string.`);
  }

  return value;
};

/** Case-insensitive property lookup (JsonSerializerDefaults.Web). */
function prop(obj: JsonObject, name: string): unknown {
  if (Object.prototype.hasOwnProperty.call(obj, name)) {
    return obj[name];
  }

  const lower = name.toLowerCase();
  const key = Object.keys(obj).find(k => k.toLowerCase() === lower);
  return key === undefined ? undefined : obj[key];
}

function normalizeNode(value: unknown): UiNode {
  if (!isJsonObject(value)) {
    throw new SyntaxError('A widget must be a JSON object.');
  }

  const node: UiNode = { widget: stringValue(value['widget'], 'widget') ?? '' };
  const id = stringValue(value['id'], 'id');
  if (id !== null) {
    node.id = id;
  }

  const visibleWhen = stringValue(value['visibleWhen'], 'visibleWhen');
  if (visibleWhen !== null) {
    node.visibleWhen = visibleWhen;
  }

  for (const [name, prop] of Object.entries(value)) {
    if (!UI_NODE_RESERVED.includes(name)) {
      node[name] = prop;
    }
  }

  const children = value['children'];
  if (children !== null && children !== undefined) {
    if (!Array.isArray(children)) {
      throw new SyntaxError("'children' must be an array.");
    }

    node.children = children.map(normalizeNode);
  }

  return node;
}

function normalizeMenu(value: unknown): UiMenu | null {
  if (value === null || value === undefined) {
    return null;
  }

  if (!isJsonObject(value)) {
    throw new SyntaxError("'menu' must be an object.");
  }

  const menu: UiMenu = {};
  const title = stringValue(prop(value, 'title'), 'menu.title');
  const icon = stringValue(prop(value, 'icon'), 'menu.icon');
  if (title !== null) {
    menu.title = title;
  }

  if (icon !== null) {
    menu.icon = icon;
  }

  const order = prop(value, 'order');
  menu.order = typeof order === 'number' && Number.isInteger(order) ? order : 0;
  return menu;
}

/** Normalized copy of a layout document (defaults and property order of UiLayoutDocument). */
export function normalizeLayout(value: unknown): UiLayoutDocument {
  if (!isJsonObject(value)) {
    throw new SyntaxError('Layout is empty.');
  }

  const document = {} as UiLayoutDocument;
  const schema = stringValue(prop(value, '$schema'), '$schema');
  if (schema !== null) {
    document.$schema = schema;
  }

  document.key = stringValue(prop(value, 'key'), 'key') ?? '';
  const title = stringValue(prop(value, 'title'), 'title');
  if (title !== null) {
    document.title = title;
  }

  const description = stringValue(prop(value, 'description'), 'description');
  if (description !== null) {
    document.description = description;
  }

  document.kind = stringValue(prop(value, 'kind'), 'kind') ?? 'page';
  const version = prop(value, 'version');
  if (version !== null && version !== undefined && !(typeof version === 'number' && Number.isInteger(version))) {
    throw new SyntaxError("'version' must be an integer.");
  }

  document.version = typeof version === 'number' ? version : 0;
  const menu = normalizeMenu(prop(value, 'menu'));
  if (menu !== null) {
    document.menu = menu;
  }

  const roles = prop(value, 'roles') ?? [];
  if (!Array.isArray(roles) || roles.some(r => typeof r !== 'string')) {
    throw new SyntaxError("'roles' must be an array of strings.");
  }

  document.roles = roles as string[];
  const body = prop(value, 'body') ?? [];
  if (!Array.isArray(body)) {
    throw new SyntaxError("'body' must be an array.");
  }

  document.body = body.map(normalizeNode);
  return document;
}

/** Parses layout JSON (UiJson.Parse); throws a SyntaxError with the reason. */
export function parseLayout(json: string): UiLayoutDocument {
  return normalizeLayout(JSON.parse(stripJsonExtensions(json)) as unknown);
}

/** Indented layout JSON (UiJson.Serialize). */
export function serializeLayout(document: UiLayoutDocument): string {
  return JSON.stringify(normalizeLayout(document), null, 2);
}
