/* @ds-bundle: {"format":4,"namespace":"AtlasLabs_ee6870","components":[{"name":"AiFace","sourcePath":"components/ai/AiFace.jsx"},{"name":"ChatPanel","sourcePath":"components/ai/ChatPanel.jsx"},{"name":"Badge","sourcePath":"components/core/Badge.jsx"},{"name":"Button","sourcePath":"components/core/Button.jsx"},{"name":"Card","sourcePath":"components/core/Card.jsx"},{"name":"Icon","sourcePath":"components/core/Icon.jsx"},{"name":"IconButton","sourcePath":"components/core/IconButton.jsx"},{"name":"Tag","sourcePath":"components/core/Tag.jsx"},{"name":"Avatar","sourcePath":"components/data/Avatar.jsx"},{"name":"AvatarStack","sourcePath":"components/data/Avatar.jsx"},{"name":"DataGrid","sourcePath":"components/data/DataGrid.jsx"},{"name":"KanbanBoard","sourcePath":"components/data/KanbanBoard.jsx"},{"name":"SignedAmount","sourcePath":"components/data/SignedAmount.jsx"},{"name":"StatCard","sourcePath":"components/data/StatCard.jsx"},{"name":"Table","sourcePath":"components/data/Table.jsx"},{"name":"Timeline","sourcePath":"components/data/Timeline.jsx"},{"name":"Dialog","sourcePath":"components/feedback/Dialog.jsx"},{"name":"Drawer","sourcePath":"components/feedback/Drawer.jsx"},{"name":"EmptyState","sourcePath":"components/feedback/EmptyState.jsx"},{"name":"NotificationList","sourcePath":"components/feedback/NotificationList.jsx"},{"name":"Toast","sourcePath":"components/feedback/Toast.jsx"},{"name":"Tooltip","sourcePath":"components/feedback/Tooltip.jsx"},{"name":"Checkbox","sourcePath":"components/forms/Checkbox.jsx"},{"name":"Combobox","sourcePath":"components/forms/Combobox.jsx"},{"name":"DateRangePicker","sourcePath":"components/forms/DateRangePicker.jsx"},{"name":"Field","sourcePath":"components/forms/Field.jsx"},{"name":"Input","sourcePath":"components/forms/Input.jsx"},{"name":"MultiSelect","sourcePath":"components/forms/MultiSelect.jsx"},{"name":"Radio","sourcePath":"components/forms/Radio.jsx"},{"name":"Select","sourcePath":"components/forms/Select.jsx"},{"name":"Switch","sourcePath":"components/forms/Switch.jsx"},{"name":"Textarea","sourcePath":"components/forms/Textarea.jsx"},{"name":"AppShell","sourcePath":"components/navigation/AppShell.jsx"},{"name":"Breadcrumbs","sourcePath":"components/navigation/Breadcrumbs.jsx"},{"name":"CommandPalette","sourcePath":"components/navigation/CommandPalette.jsx"},{"name":"FilterBar","sourcePath":"components/navigation/FilterBar.jsx"},{"name":"Menu","sourcePath":"components/navigation/Menu.jsx"},{"name":"Pagination","sourcePath":"components/navigation/Pagination.jsx"},{"name":"SegmentedControl","sourcePath":"components/navigation/SegmentedControl.jsx"},{"name":"SideNav","sourcePath":"components/navigation/SideNav.jsx"},{"name":"Stepper","sourcePath":"components/navigation/Stepper.jsx"},{"name":"Tabs","sourcePath":"components/navigation/Tabs.jsx"},{"name":"TopBar","sourcePath":"components/navigation/TopBar.jsx"}],"sourceHashes":{"components/ai/AiFace.jsx":"70c8750ada94","components/ai/ChatPanel.jsx":"0e0643dfc164","components/core/Badge.jsx":"1f507a1cdeed","components/core/Button.jsx":"2d010522ca72","components/core/Card.jsx":"1fff9856eca2","components/core/Icon.jsx":"115d8e4671e4","components/core/IconButton.jsx":"cc1992a5193d","components/core/Tag.jsx":"6c11aded00f9","components/data/Avatar.jsx":"04e8d569425e","components/data/DataGrid.jsx":"6d9070a76bc8","components/data/KanbanBoard.jsx":"ceb69f107af7","components/data/SignedAmount.jsx":"5d63e3aaa664","components/data/StatCard.jsx":"2b18b0eb5a44","components/data/Table.jsx":"5ed23444cf86","components/data/Timeline.jsx":"730ac0bd139d","components/feedback/Dialog.jsx":"964cc03ffb78","components/feedback/Drawer.jsx":"777a751397d4","components/feedback/EmptyState.jsx":"a43dea92062f","components/feedback/NotificationList.jsx":"53169c88b357","components/feedback/Toast.jsx":"0efd412882cf","components/feedback/Tooltip.jsx":"009ab77d6d34","components/forms/Checkbox.jsx":"86ec9d23eda7","components/forms/Combobox.jsx":"f782e754b692","components/forms/DateRangePicker.jsx":"73ec4dd3a10a","components/forms/Field.jsx":"9ff566fa0252","components/forms/Input.jsx":"b2e99a11a8f9","components/forms/MultiSelect.jsx":"f805d5ef8800","components/forms/Radio.jsx":"cba33d016d23","components/forms/Select.jsx":"fba7bc0115d9","components/forms/Switch.jsx":"ea29726143f4","components/forms/Textarea.jsx":"5abd1ae7cabb","components/navigation/AppShell.jsx":"ebfa5e0774ca","components/navigation/Breadcrumbs.jsx":"0c7f85d76be7","components/navigation/CommandPalette.jsx":"ed22611d83ed","components/navigation/FilterBar.jsx":"7810a2df1765","components/navigation/Menu.jsx":"651ea9335fc5","components/navigation/Pagination.jsx":"20b078cae6b9","components/navigation/SegmentedControl.jsx":"4f39e17c673b","components/navigation/SideNav.jsx":"602aad6104a8","components/navigation/Stepper.jsx":"50c32b07e9cb","components/navigation/Tabs.jsx":"b7bd5d94b8e7","components/navigation/TopBar.jsx":"6bb8b4a2efa0"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.AtlasLabs_ee6870 = window.AtlasLabs_ee6870 || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/ai/AiFace.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="AI" subtitle="Animated mascot avatar for the AI channel — reacts to chat state" viewport="200x160"
 */
function AiFace({
  state = "idle",
  size = 26,
  className = "",
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("span", _extends({
    className: ["atl-face", `atl-face--${state}`, className].filter(Boolean).join(" "),
    style: {
      width: size,
      height: size,
      ...style
    },
    role: "img",
    "aria-label": "Asistente"
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "atl-face__shape"
  }), /*#__PURE__*/React.createElement("span", {
    className: "atl-face__eye atl-face__eye--l"
  }), /*#__PURE__*/React.createElement("span", {
    className: "atl-face__eye atl-face__eye--r"
  }));
}
Object.assign(__ds_scope, { AiFace });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/ai/AiFace.jsx", error: String((e && e.message) || e) }); }

// components/core/Card.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Core" subtitle="Surface container with header, body and footer" viewport="700x220"
 */
function Card({
  title,
  description,
  actions,
  footer,
  variant = "default",
  interactive = false,
  flush = false,
  children,
  className = "",
  ...rest
}) {
  const cls = ["atl-card", variant !== "default" ? `atl-card--${variant}` : "", interactive ? "atl-card--interactive" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("section", _extends({
    className: cls
  }, rest), title || actions ? /*#__PURE__*/React.createElement("header", {
    className: "atl-card__head"
  }, /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    className: "atl-card__title"
  }, title), description ? /*#__PURE__*/React.createElement("div", {
    className: "atl-card__desc"
  }, description) : null), actions ? /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: "var(--space-4)"
    }
  }, actions) : null) : null, /*#__PURE__*/React.createElement("div", {
    className: `atl-card__body${flush ? " atl-card__body--flush" : ""}`
  }, children), footer ? /*#__PURE__*/React.createElement("footer", {
    className: "atl-card__foot"
  }, footer) : null);
}
Object.assign(__ds_scope, { Card });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Card.jsx", error: String((e && e.message) || e) }); }

// components/core/Icon.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const BASE = "https://unpkg.com/lucide-static@0.470.0/icons/";

/** Monochrome icon rendered from the Lucide static CDN via CSS mask, so it inherits currentColor. */
function Icon({
  name,
  size = 16,
  strokeWidth,
  className = "",
  style,
  title,
  ...rest
}) {
  const url = `url("${BASE}${name}.svg")`;
  return /*#__PURE__*/React.createElement("span", _extends({
    className: `atl-icon ${className}`,
    role: title ? "img" : "presentation",
    "aria-label": title,
    style: {
      width: size,
      height: size,
      WebkitMaskImage: url,
      maskImage: url,
      ...style
    }
  }, rest));
}
Object.assign(__ds_scope, { Icon });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Icon.jsx", error: String((e && e.message) || e) }); }

// components/core/Badge.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Badge({
  children,
  tone = "neutral",
  dot = false,
  icon,
  square = false,
  className = "",
  ...rest
}) {
  const cls = ["atl-badge", `atl-badge--${tone}`, square ? "atl-badge--square" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("span", _extends({
    className: cls
  }, rest), dot ? /*#__PURE__*/React.createElement("span", {
    className: "atl-badge__dot"
  }) : null, icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 11
  }) : null, children);
}
Object.assign(__ds_scope, { Badge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Badge.jsx", error: String((e && e.message) || e) }); }

// components/core/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Core" subtitle="Primary, secondary, ghost and danger buttons" viewport="700x150"
 */
function Button({
  children,
  variant = "secondary",
  size = "md",
  icon,
  trailingIcon,
  block = false,
  disabled = false,
  as = "button",
  className = "",
  ...rest
}) {
  const Tag = as;
  const cls = ["atl-btn", `atl-btn--${variant}`, size !== "md" ? `atl-btn--${size}` : "", block ? "atl-btn--block" : "", className].filter(Boolean).join(" ");
  const glyph = size === "sm" ? 13 : size === "lg" ? 16 : 14;
  return /*#__PURE__*/React.createElement(Tag, _extends({
    className: cls,
    disabled: Tag === "button" ? disabled : undefined
  }, rest), icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: glyph
  }) : null, children, trailingIcon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: trailingIcon,
    size: glyph
  }) : null);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Button.jsx", error: String((e && e.message) || e) }); }

// components/core/IconButton.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function IconButton({
  icon,
  label,
  variant = "ghost",
  size = "md",
  selected = false,
  disabled = false,
  className = "",
  ...rest
}) {
  const cls = ["atl-iconbtn", variant === "outline" ? "atl-iconbtn--outline" : "", size === "sm" ? "atl-iconbtn--sm" : "", selected ? "atl-iconbtn--selected" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("button", _extends({
    className: cls,
    "aria-label": label,
    title: label,
    disabled: disabled
  }, rest), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: size === "sm" ? 14 : 16
  }));
}
Object.assign(__ds_scope, { IconButton });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/IconButton.jsx", error: String((e && e.message) || e) }); }

// components/core/Tag.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Tag({
  children,
  icon,
  onRemove,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("span", _extends({
    className: `atl-tag ${className}`.trim()
  }, rest), icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 12
  }) : null, children, onRemove ? /*#__PURE__*/React.createElement("button", {
    className: "atl-tag__x",
    onClick: onRemove,
    "aria-label": "Remove"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "x",
    size: 10
  })) : null);
}
Object.assign(__ds_scope, { Tag });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Tag.jsx", error: String((e && e.message) || e) }); }

// components/data/Avatar.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const SIZES = {
  sm: 20,
  md: 26,
  lg: 36,
  xl: 56
};
function Avatar({
  name = "",
  src,
  size = "md",
  tone = "neutral",
  className = "",
  style,
  ...rest
}) {
  const px = SIZES[size] || SIZES.md;
  const initials = name.split(" ").filter(Boolean).slice(0, 2).map(w => w[0]).join("").toUpperCase();
  return /*#__PURE__*/React.createElement("span", _extends({
    className: ["atl-avatar", tone === "accent" ? "atl-avatar--accent" : "", className].filter(Boolean).join(" "),
    title: name || undefined,
    style: {
      width: px,
      height: px,
      fontSize: px <= 20 ? 9 : px <= 26 ? 11 : px <= 36 ? 13 : 18,
      ...style
    }
  }, rest), src ? /*#__PURE__*/React.createElement("img", {
    src: src,
    alt: name
  }) : initials);
}
function AvatarStack({
  people = [],
  max = 4,
  size = "sm"
}) {
  const shown = people.slice(0, max);
  const rest = people.length - shown.length;
  return /*#__PURE__*/React.createElement("span", {
    className: "atl-avatarstack"
  }, shown.map((p, i) => /*#__PURE__*/React.createElement(Avatar, {
    key: i,
    name: typeof p === "string" ? p : p.name,
    src: typeof p === "string" ? undefined : p.src,
    size: size
  })), rest > 0 ? /*#__PURE__*/React.createElement(Avatar, {
    name: `+${rest}`,
    size: size,
    tone: "accent"
  }) : null);
}
Object.assign(__ds_scope, { Avatar, AvatarStack });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/Avatar.jsx", error: String((e && e.message) || e) }); }

// components/data/DataGrid.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const cellValue = (row, col) => col.value ? col.value(row) : row[col.key];
const asText = v => v == null ? "" : String(v);
const DOW = ["L", "M", "X", "J", "V", "S", "D"];
const MONTHS = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
const pad2 = n => String(n).padStart(2, "0");
const toDMY = d => `${pad2(d.getDate())}-${pad2(d.getMonth() + 1)}-${d.getFullYear()}`;
const parseDMY = s => {
  const m = /^(\d{2})-(\d{2})-(\d{4})$/.exec(s || "");
  return m ? new Date(+m[3], +m[2] - 1, +m[1]) : null;
};
function monthGrid(year, month) {
  const first = new Date(year, month, 1);
  const lead = (first.getDay() + 6) % 7;
  const days = [];
  for (let i = 0; i < lead; i++) days.push(null);
  const dim = new Date(year, month + 1, 0).getDate();
  for (let d = 1; d <= dim; d++) days.push(new Date(year, month, d));
  return days;
}
function DateFilter({
  col,
  rows,
  range,
  onChange
}) {
  const [cursor, setCursor] = React.useState(() => {
    const dates = rows.map(r => parseDMY(asText(cellValue(r, col)))).filter(Boolean);
    return dates.length ? dates[0] : new Date();
  });
  const [anchor, setAnchor] = React.useState(null);
  const from = parseDMY(range && range.from);
  const to = parseDMY(range && range.to);
  const days = monthGrid(cursor.getFullYear(), cursor.getMonth());
  const pick = d => {
    if (!anchor && !from) {
      setAnchor(d);
      onChange({
        from: toDMY(d),
        to: toDMY(d)
      });
      return;
    }
    const start = anchor || from;
    const a = start < d ? start : d;
    const b = start < d ? d : start;
    setAnchor(null);
    onChange({
      from: toDMY(a),
      to: toDMY(b)
    });
  };
  return /*#__PURE__*/React.createElement("div", {
    className: "atl-cal atl-datagrid__cal"
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-cal__head"
  }, /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "atl-iconbtn atl-iconbtn--sm",
    onClick: () => setCursor(new Date(cursor.getFullYear(), cursor.getMonth() - 1, 1))
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron-left",
    size: 15
  })), /*#__PURE__*/React.createElement("span", {
    className: "atl-cal__month"
  }, MONTHS[cursor.getMonth()], " ", cursor.getFullYear()), /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "atl-iconbtn atl-iconbtn--sm",
    onClick: () => setCursor(new Date(cursor.getFullYear(), cursor.getMonth() + 1, 1))
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron-right",
    size: 15
  }))), /*#__PURE__*/React.createElement("div", {
    className: "atl-cal__grid"
  }, DOW.map(d => /*#__PURE__*/React.createElement("span", {
    className: "atl-cal__dow",
    key: d
  }, d)), days.map((d, i) => {
    if (!d) return /*#__PURE__*/React.createElement("span", {
      key: "e" + i
    });
    const s = toDMY(d);
    const isEdge = s === (range && range.from) || s === (range && range.to);
    const inRange = from && to && d > from && d < to;
    return /*#__PURE__*/React.createElement("button", {
      key: s,
      type: "button",
      className: ["atl-cal__day", inRange ? "atl-cal__day--range" : "", isEdge ? "atl-cal__day--sel" : ""].filter(Boolean).join(" "),
      onClick: () => pick(d)
    }, d.getDate());
  })), /*#__PURE__*/React.createElement("div", {
    className: "atl-cal__foot"
  }, /*#__PURE__*/React.createElement("span", null, range && range.from || "—", " \u2192 ", range && range.to || "—"), /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "atl-datagrid__menu-clear",
    onClick: () => {
      setAnchor(null);
      onChange({
        from: "",
        to: ""
      });
    }
  }, "Limpiar")));
}
function DataGrid({
  columns = [],
  rows = [],
  idKey = "id",
  title,
  height = 420,
  compact = false,
  dividers = true,
  stickyColumns = 1,
  columnSettings = true,
  selectedId,
  onRowClick,
  empty,
  actions,
  className = "",
  ...rest
}) {
  const [hidden, setHidden] = React.useState([]);
  const [text, setText] = React.useState({});
  const [picked, setPicked] = React.useState({});
  const [dateRange, setDateRange] = React.useState({});
  const [open, setOpen] = React.useState(null);
  const [anchor, setAnchor] = React.useState(null);
  const root = React.useRef(null);
  React.useEffect(() => {
    const away = e => {
      if (root.current && !root.current.contains(e.target)) setOpen(null);
    };
    document.addEventListener("mousedown", away);
    return () => document.removeEventListener("mousedown", away);
  }, []);
  const visible = columns.filter(c => hidden.indexOf(c.key) < 0);
  const options = React.useMemo(() => {
    const o = {};
    columns.filter(c => c.filter === "select").forEach(c => {
      o[c.key] = Array.from(new Set(rows.map(r => asText(cellValue(r, c))))).filter(Boolean).sort();
    });
    return o;
  }, [rows, columns.length]);
  const data = rows.filter(r => columns.every(c => {
    if (!c.filter) return true;
    const v = asText(cellValue(r, c));
    if (c.filter === "text") {
      const q = (text[c.key] || "").trim().toLowerCase();
      return !q || v.toLowerCase().indexOf(q) > -1;
    }
    if (c.filter === "date") {
      const range = dateRange[c.key];
      if (!range || !range.from && !range.to) return true;
      const d = parseDMY(v);
      const from = parseDMY(range.from) || d;
      const to = parseDMY(range.to) || d;
      return d && d >= from && d <= to;
    }
    const sel = picked[c.key] || [];
    return !sel.length || sel.indexOf(v) > -1;
  }));
  const toggle = (key, v) => setPicked(f => {
    const cur = f[key] || [];
    return {
      ...f,
      [key]: cur.indexOf(v) > -1 ? cur.filter(x => x !== v) : cur.concat(v)
    };
  });
  const activeCount = Object.keys(text).filter(k => (text[k] || "").trim()).length + Object.keys(picked).filter(k => (picked[k] || []).length).length + Object.keys(dateRange).filter(k => dateRange[k] && (dateRange[k].from || dateRange[k].to)).length;
  const widths = visible.map(c => c.width || 168);
  const left = [];
  widths.reduce((acc, w, i) => {
    left[i] = acc;
    return acc + w;
  }, 0);
  const stick = i => i < stickyColumns ? {
    position: "sticky",
    left: left[i],
    zIndex: 2
  } : null;
  const openMenu = (id, e) => {
    if (open === id) return setOpen(null);
    const r = e.currentTarget.getBoundingClientRect();
    setAnchor({
      top: r.bottom + 6,
      left: Math.min(r.left, window.innerWidth - 240)
    });
    setOpen(id);
  };
  const hasFilterRow = columns.some(c => c.filter);
  return /*#__PURE__*/React.createElement("div", _extends({
    ref: root,
    className: ["atl-datagrid", className].filter(Boolean).join(" ")
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "atl-datagrid__bar"
  }, title ? /*#__PURE__*/React.createElement("span", {
    className: "atl-datagrid__title"
  }, title) : null, /*#__PURE__*/React.createElement("div", {
    className: "atl-datagrid__tools"
  }, actions, /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "atl-datagrid__chip",
    disabled: !activeCount,
    onClick: () => {
      setText({});
      setPicked({});
      setDateRange({});
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "filter-x",
    size: 13
  }), "Borrar filtros", activeCount ? /*#__PURE__*/React.createElement("span", {
    className: "atl-datagrid__chip-val"
  }, activeCount) : null), columnSettings ? /*#__PURE__*/React.createElement("div", {
    className: "atl-datagrid__pop"
  }, /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: ["atl-datagrid__chip", hidden.length ? "atl-datagrid__chip--on" : ""].filter(Boolean).join(" "),
    "aria-expanded": open === "cols",
    onClick: e => openMenu("cols", e)
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "columns-3",
    size: 13
  }), "Columnas", hidden.length ? /*#__PURE__*/React.createElement("span", {
    className: "atl-datagrid__chip-val"
  }, hidden.length) : null, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron-down",
    size: 13
  })), open === "cols" ? /*#__PURE__*/React.createElement("div", {
    className: "atl-datagrid__menu",
    style: {
      top: anchor.top,
      left: anchor.left
    }
  }, columns.map(c => /*#__PURE__*/React.createElement("label", {
    key: c.key,
    className: "atl-datagrid__opt"
  }, /*#__PURE__*/React.createElement("input", {
    type: "checkbox",
    disabled: c.alwaysVisible,
    checked: hidden.indexOf(c.key) < 0,
    onChange: () => setHidden(h => h.indexOf(c.key) > -1 ? h.filter(k => k !== c.key) : h.concat(c.key))
  }), /*#__PURE__*/React.createElement("span", null, c.header)))) : null) : null)), /*#__PURE__*/React.createElement("div", {
    className: "atl-datagrid__scroll",
    style: {
      maxHeight: height
    }
  }, /*#__PURE__*/React.createElement("table", {
    className: ["atl-datagrid__table", dividers ? "atl-datagrid__table--dividers" : "", compact ? "atl-datagrid__table--compact" : ""].filter(Boolean).join(" "),
    style: {
      minWidth: widths.reduce((a, b) => a + b, 0)
    }
  }, /*#__PURE__*/React.createElement("colgroup", null, visible.map((c, i) => /*#__PURE__*/React.createElement("col", {
    key: c.key,
    style: {
      width: widths[i]
    }
  }))), /*#__PURE__*/React.createElement("thead", null, /*#__PURE__*/React.createElement("tr", null, visible.map((c, i) => /*#__PURE__*/React.createElement("th", {
    key: c.key,
    className: [i < stickyColumns ? "is-stuck" : "", c.align === "right" ? "is-num" : ""].filter(Boolean).join(" "),
    style: stick(i)
  }, c.header))), hasFilterRow ? /*#__PURE__*/React.createElement("tr", {
    className: "atl-datagrid__frow"
  }, visible.map((c, i) => {
    const sel = picked[c.key] || [];
    return /*#__PURE__*/React.createElement("th", {
      key: c.key,
      className: i < stickyColumns ? "is-stuck" : "",
      style: stick(i)
    }, c.filter === "text" ? /*#__PURE__*/React.createElement("div", {
      className: "atl-datagrid__search"
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "search",
      size: 13
    }), /*#__PURE__*/React.createElement("input", {
      value: text[c.key] || "",
      placeholder: "Filtrar",
      onChange: e => setText(t => ({
        ...t,
        [c.key]: e.target.value
      }))
    }), text[c.key] || "" ? /*#__PURE__*/React.createElement("button", {
      type: "button",
      onClick: () => setText(t => ({
        ...t,
        [c.key]: ""
      })),
      "aria-label": "Limpiar"
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "x",
      size: 12
    })) : null) : c.filter === "date" ? /*#__PURE__*/React.createElement("button", {
      type: "button",
      className: ["atl-datagrid__pick", dateRange[c.key] && (dateRange[c.key].from || dateRange[c.key].to) ? "atl-datagrid__pick--on" : ""].filter(Boolean).join(" "),
      "aria-expanded": open === c.key,
      onClick: e => openMenu(c.key, e)
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "calendar-days",
      size: 13
    }), /*#__PURE__*/React.createElement("span", null, dateRange[c.key] && dateRange[c.key].from ? dateRange[c.key].from + (dateRange[c.key].to && dateRange[c.key].to !== dateRange[c.key].from ? " → " + dateRange[c.key].to : "") : "Todas")) : c.filter ? /*#__PURE__*/React.createElement("button", {
      type: "button",
      className: ["atl-datagrid__pick", sel.length ? "atl-datagrid__pick--on" : ""].filter(Boolean).join(" "),
      "aria-expanded": open === c.key,
      onClick: e => openMenu(c.key, e)
    }, /*#__PURE__*/React.createElement("span", null, sel.length ? sel.join(", ") : "Todos"), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "chevron-down",
      size: 13
    })) : null, open === c.key && c.filter === "date" ? /*#__PURE__*/React.createElement("div", {
      className: "atl-datagrid__menu atl-datagrid__menu--cal",
      style: {
        top: anchor.top,
        left: anchor.left
      }
    }, /*#__PURE__*/React.createElement(DateFilter, {
      col: c,
      rows: rows,
      range: dateRange[c.key],
      onChange: r => setDateRange(d => ({
        ...d,
        [c.key]: r
      }))
    })) : open === c.key ? /*#__PURE__*/React.createElement("div", {
      className: "atl-datagrid__menu",
      style: {
        top: anchor.top,
        left: anchor.left
      }
    }, sel.length ? /*#__PURE__*/React.createElement("button", {
      type: "button",
      className: "atl-datagrid__menu-clear",
      onClick: () => setPicked(f => ({
        ...f,
        [c.key]: []
      }))
    }, "Quitar selecci\xF3n") : null, (options[c.key] || []).map(v => /*#__PURE__*/React.createElement("label", {
      key: v,
      className: "atl-datagrid__opt"
    }, /*#__PURE__*/React.createElement("input", {
      type: "checkbox",
      checked: sel.indexOf(v) > -1,
      onChange: () => toggle(c.key, v)
    }), /*#__PURE__*/React.createElement("span", null, v)))) : null);
  })) : null), /*#__PURE__*/React.createElement("tbody", null, data.map((r, ri) => /*#__PURE__*/React.createElement("tr", {
    key: r[idKey] != null ? r[idKey] : ri,
    "aria-selected": selectedId != null && r[idKey] === selectedId ? "true" : undefined,
    onClick: onRowClick ? () => onRowClick(r) : undefined
  }, visible.map((c, i) => /*#__PURE__*/React.createElement("td", {
    key: c.key,
    className: [i < stickyColumns ? "is-stuck" : "", c.align === "right" ? "is-num" : ""].filter(Boolean).join(" "),
    style: stick(i)
  }, c.render ? c.render(r) : cellValue(r, c))))))), !data.length ? /*#__PURE__*/React.createElement("div", {
    className: "atl-datagrid__empty"
  }, empty || "No hay filas que coincidan con los filtros.") : null), /*#__PURE__*/React.createElement("div", {
    className: "atl-datagrid__foot"
  }, /*#__PURE__*/React.createElement("span", null, data.length, " de ", rows.length, " filas"), /*#__PURE__*/React.createElement("span", null, visible.length, " de ", columns.length, " columnas")));
}
Object.assign(__ds_scope, { DataGrid });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/DataGrid.jsx", error: String((e && e.message) || e) }); }

// components/data/KanbanBoard.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Data" subtitle="Task board with fixed state columns" viewport="1080x520"
 */
function KanbanBoard({
  columns = [],
  onCardClick,
  onAdd,
  onMove,
  renderCard,
  className = "",
  ...rest
}) {
  const [dragId, setDragId] = React.useState(null);
  const [overCol, setOverCol] = React.useState(null);
  const draggable = typeof onMove === "function";
  function findCard(id) {
    for (const col of columns) {
      const card = col.cards.find(c => c.id === id);
      if (card) return {
        card,
        column: col
      };
    }
    return null;
  }
  function handleDrop(col) {
    setOverCol(null);
    if (!dragId) return;
    const found = findCard(dragId);
    if (!found || found.column.id === col.id) {
      setDragId(null);
      return;
    }
    onMove(dragId, found.column.id, col.id);
    setDragId(null);
  }
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-kanban", className].filter(Boolean).join(" ")
  }, rest), columns.map(col => /*#__PURE__*/React.createElement("div", {
    className: ["atl-kcol", overCol === col.id ? "atl-kcol--over" : ""].filter(Boolean).join(" "),
    key: col.id,
    onDragOver: draggable ? e => {
      e.preventDefault();
      setOverCol(col.id);
    } : undefined,
    onDragLeave: draggable ? () => setOverCol(v => v === col.id ? null : v) : undefined,
    onDrop: draggable ? e => {
      e.preventDefault();
      handleDrop(col);
    } : undefined
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-kcol__head"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-kcol__title"
  }, col.label), /*#__PURE__*/React.createElement("span", {
    className: "atl-kcol__count"
  }, col.cards.length)), col.cards.map(c => renderCard ? /*#__PURE__*/React.createElement(React.Fragment, {
    key: c.id
  }, renderCard(c, col)) : /*#__PURE__*/React.createElement("article", {
    className: ["atl-kcard", dragId === c.id ? "atl-kcard--dragging" : ""].filter(Boolean).join(" "),
    key: c.id,
    draggable: draggable,
    onDragStart: draggable ? () => setDragId(c.id) : undefined,
    onDragEnd: draggable ? () => {
      setDragId(null);
      setOverCol(null);
    } : undefined,
    onClick: () => onCardClick && onCardClick(c, col)
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-kcard__title"
  }, c.title), c.meta ? /*#__PURE__*/React.createElement("span", {
    className: "atl-kcard__meta"
  }, c.meta) : null, /*#__PURE__*/React.createElement("span", {
    className: "atl-kcard__foot"
  }, c.badge, c.trailing))), onAdd ? /*#__PURE__*/React.createElement("button", {
    className: "atl-kcol__add",
    onClick: () => onAdd(col)
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "plus",
    size: 13
  }), " A\xF1adir tarea") : null)));
}
Object.assign(__ds_scope, { KanbanBoard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/KanbanBoard.jsx", error: String((e && e.message) || e) }); }

// components/data/SignedAmount.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const fmt = (n, currency, decimals) => new Intl.NumberFormat("es-ES", {
  style: currency ? "currency" : "decimal",
  currency: currency || undefined,
  minimumFractionDigits: decimals,
  maximumFractionDigits: decimals
}).format(Math.abs(n));

/**
 * @startingPoint section="Data" subtitle="Money in tabular mono, coloured by meaning" viewport="700x160"
 */
function SignedAmount({
  value = 0,
  currency = "EUR",
  decimals = 2,
  showSign = true,
  tone,
  size,
  className = "",
  ...rest
}) {
  const t = tone || (value > 0 ? "pos" : value < 0 ? "neg" : "muted");
  const sign = value < 0 ? "−" : showSign && value > 0 ? "+" : "";
  return /*#__PURE__*/React.createElement("span", _extends({
    className: ["atl-amount", "atl-amount--" + t, size === "lg" ? "atl-amount--lg" : "", className].filter(Boolean).join(" ")
  }, rest), sign, fmt(value, currency, decimals));
}
Object.assign(__ds_scope, { SignedAmount });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/SignedAmount.jsx", error: String((e && e.message) || e) }); }

// components/data/StatCard.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function StatCard({
  label,
  value,
  delta,
  direction = "flat",
  note,
  variant = "default",
  sparkline,
  className = "",
  ...rest
}) {
  const icon = direction === "up" ? "arrow-up-right" : direction === "down" ? "arrow-down-right" : "minus";
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-stat", variant === "featured" ? "atl-stat--featured" : "", className].filter(Boolean).join(" ")
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "atl-stat__label"
  }, label), /*#__PURE__*/React.createElement("div", {
    className: "atl-stat__row"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-stat__value"
  }, value), delta ? /*#__PURE__*/React.createElement("span", {
    className: `atl-stat__delta atl-stat__delta--${direction}`
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 12
  }), delta) : null), sparkline ? /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: "var(--space-2)"
    }
  }, sparkline) : null, note ? /*#__PURE__*/React.createElement("div", {
    className: "atl-stat__note"
  }, note) : null);
}
Object.assign(__ds_scope, { StatCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/StatCard.jsx", error: String((e && e.message) || e) }); }

// components/data/Table.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Table({
  columns = [],
  rows = [],
  hover = true,
  selectedId,
  onRowClick,
  empty,
  className = "",
  ...rest
}) {
  if (!rows.length && empty) return empty;
  return /*#__PURE__*/React.createElement("table", _extends({
    className: ["atl-table", hover ? "atl-table--hover" : "", className].filter(Boolean).join(" ")
  }, rest), /*#__PURE__*/React.createElement("thead", null, /*#__PURE__*/React.createElement("tr", null, columns.map(c => /*#__PURE__*/React.createElement("th", {
    key: c.key,
    style: {
      width: c.width,
      textAlign: c.align === "right" ? "right" : undefined
    }
  }, c.header)))), /*#__PURE__*/React.createElement("tbody", null, rows.map((r, i) => /*#__PURE__*/React.createElement("tr", {
    key: r.id ?? i,
    "aria-selected": selectedId != null && r.id === selectedId ? "true" : undefined,
    onClick: onRowClick ? () => onRowClick(r) : undefined
  }, columns.map(c => /*#__PURE__*/React.createElement("td", {
    key: c.key,
    className: c.align === "right" ? "atl-table__num" : undefined,
    style: {
      textAlign: c.align === "right" ? "right" : undefined
    }
  }, c.render ? c.render(r) : r[c.key]))))));
}
Object.assign(__ds_scope, { Table });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/Table.jsx", error: String((e && e.message) || e) }); }

// components/data/Timeline.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Data" subtitle="Audit trail with actor, time and field diffs" viewport="760x460"
 */
function Timeline({
  items = [],
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-timeline", className].filter(Boolean).join(" ")
  }, rest), items.map((it, i) => /*#__PURE__*/React.createElement("div", {
    className: "atl-tl",
    key: it.id || i
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__time"
  }, it.time), /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__rail"
  }, /*#__PURE__*/React.createElement("span", {
    className: ["atl-tl__dot", it.tone ? "atl-tl__dot--" + it.tone : ""].filter(Boolean).join(" ")
  }), i < items.length - 1 ? /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__line"
  }) : null), /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__body"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__title"
  }, it.title), it.from != null || it.to != null ? /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__diff"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__from"
  }, it.from), /*#__PURE__*/React.createElement("span", null, "\u2192"), /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__to"
  }, it.to)) : null, /*#__PURE__*/React.createElement("span", {
    className: "atl-tl__meta"
  }, it.actor ? /*#__PURE__*/React.createElement("span", null, it.actor) : null, it.source ? /*#__PURE__*/React.createElement("span", null, "\xB7 ", it.source) : null, it.ip ? /*#__PURE__*/React.createElement("code", null, it.ip) : null)))));
}
Object.assign(__ds_scope, { Timeline });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/Timeline.jsx", error: String((e && e.message) || e) }); }

// components/feedback/Dialog.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Dialog({
  title,
  description,
  open = true,
  onClose,
  footer,
  wide = false,
  children,
  className = "",
  ...rest
}) {
  if (!open) return null;
  return /*#__PURE__*/React.createElement("div", {
    className: "atl-scrim",
    onClick: onClose,
    role: "presentation"
  }, /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-dialog", wide ? "atl-dialog--wide" : "", className].filter(Boolean).join(" "),
    role: "dialog",
    "aria-modal": "true",
    "aria-label": typeof title === "string" ? title : undefined,
    onClick: e => e.stopPropagation()
  }, rest), /*#__PURE__*/React.createElement("header", {
    className: "atl-dialog__head"
  }, /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    className: "atl-dialog__title"
  }, title), description ? /*#__PURE__*/React.createElement("div", {
    className: "atl-dialog__desc"
  }, description) : null), onClose ? /*#__PURE__*/React.createElement("button", {
    className: "atl-iconbtn atl-iconbtn--sm",
    onClick: onClose,
    "aria-label": "Close"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "x",
    size: 14
  })) : null), children ? /*#__PURE__*/React.createElement("div", {
    className: "atl-dialog__body"
  }, children) : null, footer ? /*#__PURE__*/React.createElement("footer", {
    className: "atl-dialog__foot"
  }, footer) : null));
}
Object.assign(__ds_scope, { Dialog });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/Dialog.jsx", error: String((e && e.message) || e) }); }

// components/feedback/Drawer.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Feedback" subtitle="Right-side detail panel over the current view" viewport="900x520"
 */
function Drawer({
  open = false,
  onClose,
  eyebrow,
  title,
  actions,
  footer,
  children,
  className = "",
  ...rest
}) {
  if (!open) return null;
  return /*#__PURE__*/React.createElement("div", {
    className: "atl-drawerwrap",
    onClick: onClose
  }, /*#__PURE__*/React.createElement("aside", _extends({
    className: ["atl-drawer", className].filter(Boolean).join(" "),
    role: "dialog",
    "aria-label": title,
    onClick: e => e.stopPropagation()
  }, rest), /*#__PURE__*/React.createElement("header", {
    className: "atl-drawer__head"
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      minWidth: 0,
      flex: 1
    }
  }, eyebrow ? /*#__PURE__*/React.createElement("div", {
    className: "atl-drawer__eyebrow"
  }, eyebrow) : null, /*#__PURE__*/React.createElement("div", {
    className: "atl-drawer__title"
  }, title)), actions, /*#__PURE__*/React.createElement(__ds_scope.IconButton, {
    icon: "x",
    label: "Cerrar",
    onClick: onClose
  })), /*#__PURE__*/React.createElement("div", {
    className: "atl-drawer__body"
  }, children), footer ? /*#__PURE__*/React.createElement("footer", {
    className: "atl-drawer__foot"
  }, footer) : null));
}
Object.assign(__ds_scope, { Drawer });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/Drawer.jsx", error: String((e && e.message) || e) }); }

// components/feedback/EmptyState.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function EmptyState({
  icon = "inbox",
  title,
  description,
  action,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `atl-empty ${className}`.trim()
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "atl-empty__mark"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 18
  })), /*#__PURE__*/React.createElement("div", {
    className: "atl-empty__title"
  }, title), description ? /*#__PURE__*/React.createElement("div", {
    className: "atl-empty__desc"
  }, description) : null, action ? /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: "var(--space-3)"
    }
  }, action) : null);
}
Object.assign(__ds_scope, { EmptyState });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/EmptyState.jsx", error: String((e && e.message) || e) }); }

// components/feedback/NotificationList.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Feedback" subtitle="Alert centre list in a topbar popover" viewport="700x420"
 */
function NotificationList({
  items = [],
  onSelect,
  onMarkAll,
  className = "",
  ...rest
}) {
  const unread = items.filter(i => i.unread).length;
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-popover", className].filter(Boolean).join(" "),
    style: {
      padding: 0,
      overflow: "hidden"
    }
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "atl-toolbar",
    style: {
      borderRadius: 0
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-card__title"
  }, "Alertas"), /*#__PURE__*/React.createElement("span", {
    className: "atl-toolbar__count"
  }, unread, " sin leer"), /*#__PURE__*/React.createElement("span", {
    className: "atl-toolbar__spacer"
  }), /*#__PURE__*/React.createElement("button", {
    className: "atl-filters__clear",
    onClick: onMarkAll
  }, "Marcar todas")), /*#__PURE__*/React.createElement("div", _extends({
    className: "atl-notiflist"
  }, rest), items.map(n => /*#__PURE__*/React.createElement("button", {
    key: n.id,
    className: ["atl-notif", n.unread ? "atl-notif--unread" : ""].filter(Boolean).join(" "),
    onClick: () => onSelect && onSelect(n)
  }, /*#__PURE__*/React.createElement("span", {
    className: ["atl-notif__icon", n.tone ? "atl-notif__icon--" + n.tone : ""].filter(Boolean).join(" ")
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: n.icon || "bell",
    size: 14
  })), /*#__PURE__*/React.createElement("span", {
    className: "atl-notif__body"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-notif__title"
  }, n.title), /*#__PURE__*/React.createElement("span", {
    className: "atl-notif__msg"
  }, n.message)), /*#__PURE__*/React.createElement("span", {
    className: "atl-notif__time"
  }, n.time)))));
}
Object.assign(__ds_scope, { NotificationList });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/NotificationList.jsx", error: String((e && e.message) || e) }); }

// components/feedback/Toast.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const ICONS = {
  success: "circle-check",
  danger: "circle-alert",
  warning: "triangle-alert",
  info: "info",
  neutral: "bell"
};
function Toast({
  title,
  message,
  tone = "neutral",
  action,
  onClose,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-toast", `atl-toast--${tone}`, className].filter(Boolean).join(" "),
    role: "status"
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "atl-toast__icon",
    style: {
      background: `var(--status-${tone}-bg, var(--surface-muted))`,
      color: `var(--status-${tone}-text, var(--text-tertiary))`
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: ICONS[tone],
    size: 15
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-toast__title"
  }, title), message ? /*#__PURE__*/React.createElement("div", {
    className: "atl-toast__msg"
  }, message) : null, action ? /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: "var(--space-4)"
    }
  }, action) : null), onClose ? /*#__PURE__*/React.createElement("button", {
    className: "atl-iconbtn atl-iconbtn--sm",
    onClick: onClose,
    "aria-label": "Dismiss"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "x",
    size: 13
  })) : null);
}
Object.assign(__ds_scope, { Toast });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/Toast.jsx", error: String((e && e.message) || e) }); }

// components/feedback/Tooltip.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Tooltip({
  label,
  shortcut,
  placement = "top",
  children,
  className = "",
  ...rest
}) {
  const [open, setOpen] = React.useState(false);
  return /*#__PURE__*/React.createElement("span", _extends({
    className: `atl-tipwrap ${className}`.trim(),
    onMouseEnter: () => setOpen(true),
    onMouseLeave: () => setOpen(false),
    onFocus: () => setOpen(true),
    onBlur: () => setOpen(false)
  }, rest), children, open ? /*#__PURE__*/React.createElement("span", {
    className: `atl-tip atl-tip--${placement}`,
    role: "tooltip"
  }, label, shortcut ? /*#__PURE__*/React.createElement("span", {
    className: "atl-tip__key"
  }, shortcut) : null) : null);
}
Object.assign(__ds_scope, { Tooltip });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/Tooltip.jsx", error: String((e && e.message) || e) }); }

// components/forms/Checkbox.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Checkbox({
  label,
  description,
  checked,
  defaultChecked,
  disabled = false,
  onChange,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("label", {
    className: ["atl-check", disabled ? "atl-check--disabled" : "", className].filter(Boolean).join(" ")
  }, /*#__PURE__*/React.createElement("input", _extends({
    type: "checkbox",
    checked: checked,
    defaultChecked: defaultChecked,
    disabled: disabled,
    onChange: onChange
  }, rest)), /*#__PURE__*/React.createElement("span", {
    className: "atl-check__box"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "check",
    size: 10,
    className: "atl-check__mark"
  })), label || description ? /*#__PURE__*/React.createElement("span", {
    className: "atl-check__text"
  }, /*#__PURE__*/React.createElement("span", null, label), description ? /*#__PURE__*/React.createElement("span", {
    className: "atl-check__desc"
  }, description) : null) : null);
}
Object.assign(__ds_scope, { Checkbox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Checkbox.jsx", error: String((e && e.message) || e) }); }

// components/forms/Combobox.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const {
  useEffect,
  useMemo,
  useRef,
  useState
} = React;
/**
 * @startingPoint section="Forms" subtitle="Type-ahead single select for long lists" viewport="700x340"
 */
function Combobox({
  options = [],
  value,
  onChange,
  placeholder = "Buscar…",
  emptyText = "Sin resultados",
  className = "",
  ...rest
}) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState("");
  const [active, setActive] = useState(0);
  const ref = useRef(null);
  const selected = options.find(o => o.value === value);
  const list = useMemo(() => {
    const t = q.trim().toLowerCase();
    return t ? options.filter(o => (o.label + " " + (o.meta || "")).toLowerCase().includes(t)) : options;
  }, [options, q]);
  useEffect(() => {
    const away = e => {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false);
    };
    document.addEventListener("mousedown", away);
    return () => document.removeEventListener("mousedown", away);
  }, []);
  const pick = o => {
    if (onChange) onChange(o.value);
    setOpen(false);
    setQ("");
  };
  const keys = e => {
    if (e.key === "ArrowDown") {
      setActive(Math.min(list.length - 1, active + 1));
      e.preventDefault();
    } else if (e.key === "ArrowUp") {
      setActive(Math.max(0, active - 1));
      e.preventDefault();
    } else if (e.key === "Enter" && list[active]) {
      pick(list[active]);
      e.preventDefault();
    } else if (e.key === "Escape") setOpen(false);
  };
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-combo", className].filter(Boolean).join(" "),
    ref: ref
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "atl-inputwrap atl-inputwrap--icon"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "search",
    size: 14
  }), /*#__PURE__*/React.createElement("input", {
    className: "atl-input",
    value: open ? q : selected ? selected.label : "",
    placeholder: selected ? selected.label : placeholder,
    onChange: e => {
      setQ(e.target.value);
      setActive(0);
      setOpen(true);
    },
    onFocus: () => setOpen(true),
    onKeyDown: keys,
    role: "combobox",
    "aria-expanded": open
  })), open ? /*#__PURE__*/React.createElement("div", {
    className: "atl-popover atl-combo__pop",
    role: "listbox"
  }, list.length === 0 ? /*#__PURE__*/React.createElement("div", {
    className: "atl-combo__empty"
  }, emptyText) : null, list.map((o, i) => /*#__PURE__*/React.createElement("button", {
    key: o.value,
    role: "option",
    "aria-selected": o.value === value,
    className: ["atl-combo__opt", i === active ? "atl-combo__opt--active" : ""].filter(Boolean).join(" "),
    onMouseEnter: () => setActive(i),
    onClick: () => pick(o)
  }, o.icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: o.icon,
    size: 14
  }) : null, o.label, o.meta ? /*#__PURE__*/React.createElement("span", {
    className: "atl-combo__opt-meta"
  }, o.meta) : null))) : null);
}
Object.assign(__ds_scope, { Combobox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Combobox.jsx", error: String((e && e.message) || e) }); }

// components/forms/DateRangePicker.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const {
  useEffect,
  useRef,
  useState
} = React;
const DOW = ["L", "M", "X", "J", "V", "S", "D"];
const MONTHS = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
const iso = d => d.toISOString().slice(0, 10);
const parse = s => s ? new Date(s + "T00:00:00") : null;
function grid(year, month) {
  const first = new Date(year, month, 1);
  const lead = (first.getDay() + 6) % 7;
  const days = [];
  for (let i = 0; i < lead; i++) days.push(null);
  const dim = new Date(year, month + 1, 0).getDate();
  for (let d = 1; d <= dim; d++) days.push(new Date(year, month, d));
  return days;
}

/**
 * @startingPoint section="Forms" subtitle="Period picker with presets and range selection" viewport="760x420"
 */
function DateRangePicker({
  from,
  to,
  onChange,
  presets,
  className = "",
  ...rest
}) {
  const [open, setOpen] = useState(false);
  const [cursor, setCursor] = useState(() => parse(from) || new Date());
  const [anchor, setAnchor] = useState(null);
  const ref = useRef(null);
  useEffect(() => {
    const away = e => {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false);
    };
    document.addEventListener("mousedown", away);
    return () => document.removeEventListener("mousedown", away);
  }, []);
  const list = presets || [{
    id: "7d",
    label: "Últimos 7 días",
    days: 7
  }, {
    id: "30d",
    label: "Últimos 30 días",
    days: 30
  }, {
    id: "mtd",
    label: "Mes en curso",
    mtd: true
  }, {
    id: "90d",
    label: "Último trimestre",
    days: 90
  }, {
    id: "ytd",
    label: "Año en curso",
    ytd: true
  }];
  const applyPreset = p => {
    const end = new Date();
    let start = new Date();
    if (p.days) start.setDate(end.getDate() - p.days + 1);
    if (p.mtd) start = new Date(end.getFullYear(), end.getMonth(), 1);
    if (p.ytd) start = new Date(end.getFullYear(), 0, 1);
    if (onChange) onChange({
      from: iso(start),
      to: iso(end),
      preset: p.id
    });
    setOpen(false);
  };
  const click = d => {
    if (!anchor) {
      setAnchor(d);
      if (onChange) onChange({
        from: iso(d),
        to: iso(d)
      });
      return;
    }
    const a = anchor < d ? anchor : d;
    const b = anchor < d ? d : anchor;
    setAnchor(null);
    if (onChange) onChange({
      from: iso(a),
      to: iso(b)
    });
    setOpen(false);
  };
  const f = parse(from);
  const t = parse(to);
  const label = f && t ? f.toLocaleDateString("es-ES", {
    day: "numeric",
    month: "short"
  }) + " – " + t.toLocaleDateString("es-ES", {
    day: "numeric",
    month: "short",
    year: "numeric"
  }) : "Elegir periodo";
  const days = grid(cursor.getFullYear(), cursor.getMonth());
  return /*#__PURE__*/React.createElement("span", _extends({
    className: ["atl-anchored", className].filter(Boolean).join(" "),
    ref: ref
  }, rest), /*#__PURE__*/React.createElement(__ds_scope.Button, {
    icon: "calendar-days",
    trailingIcon: "chevron-down",
    onClick: () => setOpen(!open)
  }, label), open ? /*#__PURE__*/React.createElement("div", {
    className: "atl-anchored__pop",
    style: {
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-popover atl-datepop"
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-datepop__presets"
  }, list.map(p => /*#__PURE__*/React.createElement("button", {
    key: p.id,
    className: "atl-datepop__preset",
    onClick: () => applyPreset(p)
  }, p.label))), /*#__PURE__*/React.createElement("div", {
    className: "atl-cal"
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-cal__head"
  }, /*#__PURE__*/React.createElement("button", {
    className: "atl-iconbtn atl-iconbtn--sm",
    onClick: () => setCursor(new Date(cursor.getFullYear(), cursor.getMonth() - 1, 1))
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron-left",
    size: 15
  })), /*#__PURE__*/React.createElement("span", {
    className: "atl-cal__month"
  }, MONTHS[cursor.getMonth()], " ", cursor.getFullYear()), /*#__PURE__*/React.createElement("button", {
    className: "atl-iconbtn atl-iconbtn--sm",
    onClick: () => setCursor(new Date(cursor.getFullYear(), cursor.getMonth() + 1, 1))
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron-right",
    size: 15
  }))), /*#__PURE__*/React.createElement("div", {
    className: "atl-cal__grid"
  }, DOW.map(d => /*#__PURE__*/React.createElement("span", {
    className: "atl-cal__dow",
    key: d
  }, d)), days.map((d, i) => {
    if (!d) return /*#__PURE__*/React.createElement("span", {
      key: "e" + i
    });
    const s = iso(d);
    const isEdge = s === from || s === to;
    const inRange = f && t && d > f && d < t;
    const today = s === iso(new Date());
    return /*#__PURE__*/React.createElement("button", {
      key: s,
      className: ["atl-cal__day", inRange ? "atl-cal__day--range" : "", isEdge ? "atl-cal__day--sel" : "", today && !isEdge ? "atl-cal__day--today" : ""].filter(Boolean).join(" "),
      onClick: () => click(d)
    }, d.getDate());
  })), /*#__PURE__*/React.createElement("div", {
    className: "atl-cal__foot"
  }, /*#__PURE__*/React.createElement("span", null, from || "—", " \u2192 ", to || "—"), /*#__PURE__*/React.createElement(__ds_scope.Button, {
    size: "sm",
    variant: "ghost",
    onClick: () => {
      if (onChange) onChange({
        from: "",
        to: ""
      });
      setAnchor(null);
    }
  }, "Limpiar"))))) : null);
}
Object.assign(__ds_scope, { DateRangePicker });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/DateRangePicker.jsx", error: String((e && e.message) || e) }); }

// components/forms/Field.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Field({
  label,
  required = false,
  help,
  error,
  htmlFor,
  children,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `atl-field ${className}`.trim()
  }, rest), label ? /*#__PURE__*/React.createElement("label", {
    className: "atl-field__label",
    htmlFor: htmlFor
  }, label, required ? /*#__PURE__*/React.createElement("span", {
    className: "atl-field__req"
  }, "*") : null) : null, children, error ? /*#__PURE__*/React.createElement("div", {
    className: "atl-field__error"
  }, error) : help ? /*#__PURE__*/React.createElement("div", {
    className: "atl-field__help"
  }, help) : null);
}
Object.assign(__ds_scope, { Field });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Field.jsx", error: String((e && e.message) || e) }); }

// components/forms/Input.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Input({
  icon,
  invalid = false,
  className = "",
  style,
  ...rest
}) {
  const input = /*#__PURE__*/React.createElement("input", _extends({
    className: ["atl-input", invalid ? "atl-input--invalid" : "", className].filter(Boolean).join(" "),
    "aria-invalid": invalid || undefined,
    style: style
  }, rest));
  if (!icon) return input;
  return /*#__PURE__*/React.createElement("span", {
    className: "atl-inputwrap atl-inputwrap--icon"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 14
  }), input);
}
Object.assign(__ds_scope, { Input });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Input.jsx", error: String((e && e.message) || e) }); }

// components/forms/MultiSelect.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const {
  useEffect,
  useMemo,
  useRef,
  useState
} = React;
/**
 * @startingPoint section="Forms" subtitle="Tag-based multi select with type-ahead" viewport="700x340"
 */
function MultiSelect({
  options = [],
  value = [],
  onChange,
  placeholder = "Añadir…",
  max,
  className = "",
  ...rest
}) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState("");
  const ref = useRef(null);
  useEffect(() => {
    const away = e => {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false);
    };
    document.addEventListener("mousedown", away);
    return () => document.removeEventListener("mousedown", away);
  }, []);
  const chosen = options.filter(o => value.includes(o.value));
  const list = useMemo(() => {
    const t = q.trim().toLowerCase();
    return options.filter(o => !value.includes(o.value) && (!t || o.label.toLowerCase().includes(t)));
  }, [options, value, q]);
  const set = next => onChange && onChange(next);
  const shown = max ? chosen.slice(0, max) : chosen;
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-combo", className].filter(Boolean).join(" "),
    ref: ref
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: ["atl-multi", open ? "atl-multi--open" : ""].filter(Boolean).join(" "),
    onClick: () => setOpen(true)
  }, shown.map(o => /*#__PURE__*/React.createElement(__ds_scope.Tag, {
    key: o.value,
    onRemove: () => set(value.filter(v => v !== o.value))
  }, o.label)), max && chosen.length > max ? /*#__PURE__*/React.createElement("span", {
    className: "atl-multi__count"
  }, "+", chosen.length - max) : null, /*#__PURE__*/React.createElement("input", {
    className: "atl-multi__input",
    value: q,
    placeholder: chosen.length ? "" : placeholder,
    onChange: e => {
      setQ(e.target.value);
      setOpen(true);
    },
    onKeyDown: e => {
      if (e.key === "Backspace" && !q && chosen.length) set(value.slice(0, -1));
      if (e.key === "Escape") setOpen(false);
    }
  })), open ? /*#__PURE__*/React.createElement("div", {
    className: "atl-popover atl-combo__pop",
    role: "listbox"
  }, list.length === 0 ? /*#__PURE__*/React.createElement("div", {
    className: "atl-combo__empty"
  }, "Nada m\xE1s que a\xF1adir") : null, list.map(o => /*#__PURE__*/React.createElement("button", {
    key: o.value,
    className: "atl-combo__opt",
    onClick: () => {
      set(value.concat(o.value));
      setQ("");
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "plus",
    size: 13
  }), o.label, o.meta ? /*#__PURE__*/React.createElement("span", {
    className: "atl-combo__opt-meta"
  }, o.meta) : null))) : null);
}
Object.assign(__ds_scope, { MultiSelect });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/MultiSelect.jsx", error: String((e && e.message) || e) }); }

// components/forms/Radio.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Radio({
  label,
  description,
  name,
  value,
  checked,
  defaultChecked,
  disabled = false,
  onChange,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("label", {
    className: ["atl-check", disabled ? "atl-check--disabled" : "", className].filter(Boolean).join(" ")
  }, /*#__PURE__*/React.createElement("input", _extends({
    type: "radio",
    name: name,
    value: value,
    checked: checked,
    defaultChecked: defaultChecked,
    disabled: disabled,
    onChange: onChange
  }, rest)), /*#__PURE__*/React.createElement("span", {
    className: "atl-check__box atl-check__box--radio"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-check__dot"
  })), label || description ? /*#__PURE__*/React.createElement("span", {
    className: "atl-check__text"
  }, /*#__PURE__*/React.createElement("span", null, label), description ? /*#__PURE__*/React.createElement("span", {
    className: "atl-check__desc"
  }, description) : null) : null);
}
Object.assign(__ds_scope, { Radio });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Radio.jsx", error: String((e && e.message) || e) }); }

// components/forms/Select.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Select({
  options = [],
  invalid = false,
  placeholder,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("span", {
    className: "atl-selectwrap"
  }, /*#__PURE__*/React.createElement("select", _extends({
    className: ["atl-select", invalid ? "atl-select--invalid" : "", className].filter(Boolean).join(" "),
    "aria-invalid": invalid || undefined
  }, rest), placeholder ? /*#__PURE__*/React.createElement("option", {
    value: ""
  }, placeholder) : null, options.map(o => {
    const opt = typeof o === "string" ? {
      value: o,
      label: o
    } : o;
    return /*#__PURE__*/React.createElement("option", {
      key: opt.value,
      value: opt.value,
      disabled: opt.disabled
    }, opt.label);
  })), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron-down",
    size: 14
  }));
}
Object.assign(__ds_scope, { Select });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Select.jsx", error: String((e && e.message) || e) }); }

// components/forms/Switch.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Switch({
  label,
  checked,
  defaultChecked,
  disabled = false,
  onChange,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("label", {
    className: ["atl-switch", disabled ? "atl-switch--disabled" : "", className].filter(Boolean).join(" ")
  }, /*#__PURE__*/React.createElement("input", _extends({
    type: "checkbox",
    role: "switch",
    checked: checked,
    defaultChecked: defaultChecked,
    disabled: disabled,
    onChange: onChange
  }, rest)), /*#__PURE__*/React.createElement("span", {
    className: "atl-switch__track"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-switch__knob"
  })), label ? /*#__PURE__*/React.createElement("span", null, label) : null);
}
Object.assign(__ds_scope, { Switch });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Switch.jsx", error: String((e && e.message) || e) }); }

// components/forms/Textarea.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Textarea({
  invalid = false,
  rows = 4,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("textarea", _extends({
    rows: rows,
    className: ["atl-textarea", invalid ? "atl-textarea--invalid" : "", className].filter(Boolean).join(" "),
    "aria-invalid": invalid || undefined
  }, rest));
}
Object.assign(__ds_scope, { Textarea });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Textarea.jsx", error: String((e && e.message) || e) }); }

// components/navigation/Breadcrumbs.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Navigation" subtitle="Record trail above a detail title" viewport="700x100"
 */
function Breadcrumbs({
  items = [],
  onNavigate,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("nav", _extends({
    className: ["atl-crumbs", className].filter(Boolean).join(" "),
    "aria-label": "Ruta"
  }, rest), items.map((it, i) => {
    const last = i === items.length - 1;
    return /*#__PURE__*/React.createElement(React.Fragment, {
      key: it.value || it.label
    }, /*#__PURE__*/React.createElement("button", {
      className: ["atl-crumbs__item", last ? "atl-crumbs__item--current" : ""].filter(Boolean).join(" "),
      "aria-current": last ? "page" : undefined,
      onClick: () => !last && onNavigate && onNavigate(it.value)
    }, it.label), last ? null : /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "chevron-right",
      size: 13
    }));
  }));
}
Object.assign(__ds_scope, { Breadcrumbs });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/Breadcrumbs.jsx", error: String((e && e.message) || e) }); }

// components/navigation/CommandPalette.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const {
  useEffect,
  useMemo,
  useState
} = React;
/**
 * @startingPoint section="Navigation" subtitle="Global ⌘K search across records and actions" viewport="900x520"
 */
function CommandPalette({
  open = false,
  onClose,
  groups = [],
  onSelect,
  placeholder = "Busca registros, acciones o ve a…",
  className = "",
  ...rest
}) {
  const [q, setQ] = useState("");
  const [active, setActive] = useState(0);
  const flat = useMemo(() => {
    const t = q.trim().toLowerCase();
    const out = [];
    groups.forEach(g => {
      const items = t ? g.items.filter(i => (i.label + " " + (i.meta || "")).toLowerCase().includes(t)) : g.items;
      if (items.length) out.push({
        group: g.label,
        items
      });
    });
    return out;
  }, [groups, q]);
  const rows = flat.flatMap(g => g.items);
  useEffect(() => {
    setActive(0);
  }, [q, open]);
  useEffect(() => {
    if (!open) return;
    const key = e => {
      if (e.key === "Escape") onClose && onClose();
      if (e.key === "ArrowDown") {
        setActive(a => Math.min(rows.length - 1, a + 1));
        e.preventDefault();
      }
      if (e.key === "ArrowUp") {
        setActive(a => Math.max(0, a - 1));
        e.preventDefault();
      }
      if (e.key === "Enter" && rows[active]) {
        onSelect && onSelect(rows[active]);
        onClose && onClose();
      }
    };
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  });
  if (!open) return null;
  let idx = -1;
  return /*#__PURE__*/React.createElement("div", {
    className: "atl-scrim",
    style: {
      alignItems: "flex-start",
      paddingTop: "12vh"
    },
    onClick: onClose
  }, /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-palette", className].filter(Boolean).join(" "),
    onClick: e => e.stopPropagation()
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "atl-palette__field"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "search",
    size: 18
  }), /*#__PURE__*/React.createElement("input", {
    className: "atl-palette__input",
    autoFocus: true,
    value: q,
    placeholder: placeholder,
    onChange: e => setQ(e.target.value)
  }), /*#__PURE__*/React.createElement("kbd", {
    className: "atl-kbd"
  }, "esc")), /*#__PURE__*/React.createElement("div", {
    className: "atl-palette__body"
  }, flat.length === 0 ? /*#__PURE__*/React.createElement("div", {
    className: "atl-combo__empty"
  }, "Sin coincidencias para \u201C", q, "\u201D") : null, flat.map(g => /*#__PURE__*/React.createElement("div", {
    key: g.group
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-palette__group"
  }, g.group), g.items.map(it => {
    idx += 1;
    const i = idx;
    return /*#__PURE__*/React.createElement("button", {
      key: it.id || it.label,
      className: ["atl-palette__item", i === active ? "atl-palette__item--active" : ""].filter(Boolean).join(" "),
      onMouseEnter: () => setActive(i),
      onClick: () => {
        onSelect && onSelect(it);
        onClose && onClose();
      }
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: it.icon || "corner-down-right",
      size: 16
    }), it.label, it.meta ? /*#__PURE__*/React.createElement("span", {
      className: "atl-palette__item-meta"
    }, it.meta) : null);
  })))), /*#__PURE__*/React.createElement("div", {
    className: "atl-palette__foot"
  }, /*#__PURE__*/React.createElement("span", null, /*#__PURE__*/React.createElement("kbd", {
    className: "atl-kbd"
  }, "\u2191\u2193"), " moverse"), /*#__PURE__*/React.createElement("span", null, /*#__PURE__*/React.createElement("kbd", {
    className: "atl-kbd"
  }, "\u21B5"), " abrir"), /*#__PURE__*/React.createElement("span", null, /*#__PURE__*/React.createElement("kbd", {
    className: "atl-kbd"
  }, "esc"), " cerrar"))));
}
Object.assign(__ds_scope, { CommandPalette });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/CommandPalette.jsx", error: String((e && e.message) || e) }); }

// components/navigation/FilterBar.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Navigation" subtitle="Filter chips over a record table" viewport="760x140"
 */
function FilterBar({
  filters = [],
  onOpen,
  onClear,
  className = "",
  ...rest
}) {
  const active = filters.filter(f => f.value != null && f.value !== "");
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-filters", className].filter(Boolean).join(" ")
  }, rest), filters.map(f => /*#__PURE__*/React.createElement("button", {
    key: f.id,
    className: ["atl-filterchip", f.value ? "atl-filterchip--on" : ""].filter(Boolean).join(" "),
    onClick: () => onOpen && onOpen(f.id)
  }, f.icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: f.icon,
    size: 13
  }) : null, f.label, f.value ? /*#__PURE__*/React.createElement("span", {
    className: "atl-filterchip__val"
  }, f.value) : null, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron-down",
    size: 13
  }))), active.length ? /*#__PURE__*/React.createElement("button", {
    className: "atl-filters__clear",
    onClick: onClear
  }, "Limpiar ", active.length) : null);
}
Object.assign(__ds_scope, { FilterBar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/FilterBar.jsx", error: String((e && e.message) || e) }); }

// components/navigation/Menu.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const {
  useEffect,
  useRef,
  useState
} = React;
/**
 * @startingPoint section="Navigation" subtitle="Anchored dropdown menu with sections, checks and shortcuts" viewport="700x360"
 */
function Menu({
  trigger,
  items = [],
  align = "left",
  placement = "down",
  open: openProp,
  onOpenChange,
  className = "",
  ...rest
}) {
  const [uncontrolled, setUncontrolled] = useState(false);
  const open = openProp === undefined ? uncontrolled : openProp;
  const setOpen = v => {
    if (openProp === undefined) setUncontrolled(v);
    if (onOpenChange) onOpenChange(v);
  };
  const ref = useRef(null);
  useEffect(() => {
    if (!open) return;
    const away = e => {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false);
    };
    const esc = e => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", away);
    document.addEventListener("keydown", esc);
    return () => {
      document.removeEventListener("mousedown", away);
      document.removeEventListener("keydown", esc);
    };
  });
  return /*#__PURE__*/React.createElement("span", _extends({
    className: ["atl-anchored", className].filter(Boolean).join(" "),
    ref: ref
  }, rest), /*#__PURE__*/React.createElement("span", {
    onClick: () => setOpen(!open)
  }, trigger), open ? /*#__PURE__*/React.createElement("div", {
    className: ["atl-anchored__pop", align === "right" ? "atl-anchored__pop--right" : "", placement === "up" ? "atl-anchored__pop--up" : ""].filter(Boolean).join(" ")
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-popover atl-menu",
    role: "menu"
  }, items.map((it, i) => {
    if (it.type === "separator") return /*#__PURE__*/React.createElement("div", {
      className: "atl-menu__sep",
      key: "s" + i
    });
    if (it.type === "label") return /*#__PURE__*/React.createElement("div", {
      className: "atl-menu__label",
      key: "l" + i
    }, it.label);
    return /*#__PURE__*/React.createElement("button", {
      key: it.value || it.label,
      role: it.checked === undefined ? "menuitem" : "menuitemradio",
      "aria-checked": it.checked === undefined ? undefined : !!it.checked,
      disabled: it.disabled,
      className: ["atl-menu__item", it.danger ? "atl-menu__item--danger" : ""].filter(Boolean).join(" "),
      onClick: () => {
        if (it.onSelect) it.onSelect(it.value);
        setOpen(false);
      }
    }, it.icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: it.icon,
      size: 15
    }) : null, it.label, it.shortcut || it.meta ? /*#__PURE__*/React.createElement("span", {
      className: "atl-menu__trail"
    }, it.meta, it.shortcut ? /*#__PURE__*/React.createElement("kbd", {
      className: "atl-kbd"
    }, it.shortcut) : null) : null);
  }))) : null);
}
Object.assign(__ds_scope, { Menu });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/Menu.jsx", error: String((e && e.message) || e) }); }

// components/ai/ChatPanel.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const {
  useEffect,
  useRef
} = React;
const DEFAULT_THINKING = [{
  value: "bajo",
  label: "Bajo"
}, {
  value: "medio",
  label: "Medio"
}, {
  value: "alto",
  label: "Alto"
}];
const DEFAULT_MODELS = [{
  value: "auto",
  label: "Auto"
}, {
  value: "gpt-5.6-sol",
  label: "Chat GPT 5.6 Sol"
}, {
  value: "minimax-m3",
  label: "Minimax M3"
}, {
  value: "glm-5.2",
  label: "GLM 5.2"
}];

/**
 * @startingPoint section="AI" subtitle="Permission-filtered assistant with citations and actions" viewport="760x560"
 */
function ChatPanel({
  title = "Asistente",
  note = "Solo ve lo que tú puedes ver",
  messages = [],
  streaming = false,
  suggestions = [],
  value = "",
  onValueChange,
  onSend,
  onCite,
  onClose,
  onStop,
  variant = "card",
  footNote = "Las respuestas citan registros. Verifica antes de operar.",
  modelValue = "auto",
  onModelChange,
  modelOptions = DEFAULT_MODELS,
  thinkingMode = "medio",
  onThinkingModeChange,
  thinkingOptions = DEFAULT_THINKING,
  className = "",
  ...rest
}) {
  const logRef = useRef(null);
  useEffect(() => {
    const el = logRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [messages.length, streaming]);
  const send = () => {
    if (onSend && value.trim()) onSend(value.trim());
  };
  const hasStarted = messages.some(m => m.role === "user");
  const faceState = streaming ? "thinking" : value.trim() ? "listening" : "idle";
  const taRef = useRef(null);
  useEffect(() => {
    const el = taRef.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = Math.min(el.scrollHeight, 200) + "px";
  }, [value]);
  return /*#__PURE__*/React.createElement("section", _extends({
    className: ["atl-chat", variant === "panel" ? "atl-chat--panel" : "", className].filter(Boolean).join(" ")
  }, rest), /*#__PURE__*/React.createElement("header", {
    className: "atl-chat__head"
  }, /*#__PURE__*/React.createElement(__ds_scope.AiFace, {
    state: faceState,
    size: 34
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-chat__title"
  }, title), /*#__PURE__*/React.createElement("div", {
    className: "atl-chat__note"
  }, note)), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), onClose ? /*#__PURE__*/React.createElement(__ds_scope.IconButton, {
    icon: "x",
    label: "Cerrar",
    onClick: onClose
  }) : null), /*#__PURE__*/React.createElement("div", {
    className: "atl-chat__log",
    ref: logRef
  }, messages.map((m, i) => {
    const prev = messages[i - 1];
    const next = messages[i + 1];
    const isFirst = !prev || prev.role !== m.role;
    const isLast = !next || next.role !== m.role;
    return /*#__PURE__*/React.createElement("article", {
      className: ["atl-msg", m.role === "user" ? "atl-msg--user" : "atl-msg--ai", isFirst ? "atl-msg--group-start" : ""].join(" "),
      key: m.id || i
    }, /*#__PURE__*/React.createElement("span", {
      className: ["atl-msg__avatar", m.role === "user" ? "atl-msg__avatar--user" : "atl-msg__avatar--ai", isFirst ? "" : "atl-msg__avatar--hidden"].join(" ")
    }, m.role === "user" ? m.initials || "TÚ" : /*#__PURE__*/React.createElement(__ds_scope.AiFace, {
      state: "idle",
      size: 36
    })), /*#__PURE__*/React.createElement("div", {
      className: "atl-msg__col"
    }, /*#__PURE__*/React.createElement("div", {
      className: ["atl-msg__bubble", isFirst ? "" : "atl-msg__bubble--follow"].join(" ")
    }, /*#__PURE__*/React.createElement("span", null, m.text), m.data ? /*#__PURE__*/React.createElement("div", {
      className: "atl-msg__data"
    }, m.data) : null, m.citations && m.citations.length ? /*#__PURE__*/React.createElement("div", {
      className: "atl-msg__cites"
    }, m.citations.map(c => /*#__PURE__*/React.createElement("button", {
      className: "atl-cite",
      key: c.id || c.label,
      onClick: () => onCite && onCite(c)
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: c.icon || "file-text",
      size: 12
    }), c.label))) : null, m.role !== "user" && m.actions ? /*#__PURE__*/React.createElement("div", {
      className: "atl-msg__actions"
    }, m.actions) : null), isLast && m.timestamp ? /*#__PURE__*/React.createElement("span", {
      className: "atl-msg__meta"
    }, m.timestamp) : null));
  }), streaming ? /*#__PURE__*/React.createElement("article", {
    className: "atl-msg atl-msg--ai atl-msg--group-start"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-msg__avatar atl-msg__avatar--ai"
  }, /*#__PURE__*/React.createElement(__ds_scope.AiFace, {
    state: "thinking",
    size: 36
  })), /*#__PURE__*/React.createElement("div", {
    className: "atl-msg__col"
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-msg__bubble"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-morph"
  }, "Pensando")))) : null), suggestions.length && !hasStarted ? /*#__PURE__*/React.createElement("div", {
    className: "atl-chat__suggest"
  }, suggestions.map(s => /*#__PURE__*/React.createElement("button", {
    className: "atl-filterchip",
    key: s,
    onClick: () => onSend && onSend(s)
  }, s))) : null, /*#__PURE__*/React.createElement("div", {
    className: "atl-chat__composer"
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-chat__box"
  }, /*#__PURE__*/React.createElement("textarea", {
    className: "atl-chat__input",
    rows: 1,
    ref: taRef,
    placeholder: "Pregunta sobre saldos, proyectos o registros\u2026",
    value: value,
    onChange: e => onValueChange && onValueChange(e.target.value),
    onKeyDown: e => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        send();
      }
    }
  }), /*#__PURE__*/React.createElement("div", {
    className: "atl-chat__tools"
  }, /*#__PURE__*/React.createElement(__ds_scope.Menu, {
    placement: "up",
    trigger: /*#__PURE__*/React.createElement("button", {
      type: "button",
      className: "atl-chat__toolbtn"
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "sparkles",
      size: 14
    }), (modelOptions.find(o => o.value === modelValue) || {}).label || "Auto", /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "chevron-down",
      size: 12
    })),
    items: modelOptions.map(o => ({
      label: o.label,
      value: o.value,
      onSelect: () => onModelChange && onModelChange(o.value)
    }))
  }), /*#__PURE__*/React.createElement(__ds_scope.Menu, {
    placement: "up",
    trigger: /*#__PURE__*/React.createElement("button", {
      type: "button",
      className: "atl-chat__toolbtn"
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "gauge",
      size: 14
    }), (thinkingOptions.find(o => o.value === thinkingMode) || {}).label || "Medio", /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "chevron-down",
      size: 12
    })),
    items: thinkingOptions.map(o => ({
      label: o.label,
      value: o.value,
      onSelect: () => onThinkingModeChange && onThinkingModeChange(o.value)
    }))
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), streaming ? /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "atl-chat__send atl-chat__send--stop",
    onClick: onStop,
    "aria-label": "Detener"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-chat__stopdot"
  })) : /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "atl-chat__send",
    onClick: send,
    disabled: !value.trim(),
    "aria-label": "Enviar"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "arrow-up",
    size: 24,
    strokeWidth: 3
  })))), /*#__PURE__*/React.createElement("span", {
    className: "atl-chat__foot"
  }, footNote)));
}
Object.assign(__ds_scope, { ChatPanel });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/ai/ChatPanel.jsx", error: String((e && e.message) || e) }); }

// components/navigation/Pagination.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function pageList(page, pages) {
  if (pages <= 7) return Array.from({
    length: pages
  }, (_, i) => i + 1);
  const out = [1];
  const from = Math.max(2, page - 1);
  const to = Math.min(pages - 1, page + 1);
  if (from > 2) out.push("gap-l");
  for (let p = from; p <= to; p++) out.push(p);
  if (to < pages - 1) out.push("gap-r");
  out.push(pages);
  return out;
}

/**
 * @startingPoint section="Navigation" subtitle="Table footer paging with range counter" viewport="700x120"
 */
function Pagination({
  page = 1,
  pageSize = 50,
  total = 0,
  onPageChange,
  className = "",
  ...rest
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  const go = p => onPageChange && onPageChange(Math.min(pages, Math.max(1, p)));
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-pager", className].filter(Boolean).join(" ")
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "atl-pager__info"
  }, from, "\u2013", to, " de ", total.toLocaleString("es-ES")), /*#__PURE__*/React.createElement("span", {
    className: "atl-pager__spacer"
  }), /*#__PURE__*/React.createElement(__ds_scope.IconButton, {
    icon: "chevron-left",
    label: "Anterior",
    size: "sm",
    disabled: page <= 1,
    onClick: () => go(page - 1)
  }), /*#__PURE__*/React.createElement("span", {
    className: "atl-pager__pages"
  }, pageList(page, pages).map(p => typeof p === "number" ? /*#__PURE__*/React.createElement("button", {
    key: p,
    className: ["atl-pager__page", p === page ? "atl-pager__page--on" : ""].filter(Boolean).join(" "),
    onClick: () => go(p)
  }, p) : /*#__PURE__*/React.createElement("span", {
    className: "atl-pager__gap",
    key: p
  }, "\u2026"))), /*#__PURE__*/React.createElement(__ds_scope.IconButton, {
    icon: "chevron-right",
    label: "Siguiente",
    size: "sm",
    disabled: page >= pages,
    onClick: () => go(page + 1)
  }));
}
Object.assign(__ds_scope, { Pagination });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/Pagination.jsx", error: String((e && e.message) || e) }); }

// components/navigation/SegmentedControl.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Navigation" subtitle="Two to four mutually exclusive views" viewport="700x120"
 */
function SegmentedControl({
  options = [],
  value,
  onChange,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-seg", className].filter(Boolean).join(" "),
    role: "tablist"
  }, rest), options.map(o => /*#__PURE__*/React.createElement("button", {
    key: o.value,
    role: "tab",
    "aria-selected": o.value === value,
    className: ["atl-seg__btn", o.value === value ? "atl-seg__btn--on" : ""].filter(Boolean).join(" "),
    onClick: () => onChange && onChange(o.value)
  }, o.icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: o.icon,
    size: 14
  }) : null, o.label)));
}
Object.assign(__ds_scope, { SegmentedControl });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/SegmentedControl.jsx", error: String((e && e.message) || e) }); }

// components/navigation/SideNav.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Navigation" subtitle="App sidebar with brand block and grouped items" viewport="700x420"
 */
function SideNav({
  product,
  maker = "by Atlas Labs",
  appName,
  header,
  groups = [],
  value,
  onChange,
  footer,
  className = "",
  ...rest
}) {
  const name = product || (appName ? `Atlas ${appName}` : "Atlas");
  return /*#__PURE__*/React.createElement("nav", _extends({
    className: `atl-sidenav ${className}`.trim()
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "atl-sidenav__brand"
  }, /*#__PURE__*/React.createElement("span", {
    className: "atl-sidenav__mark"
  }), /*#__PURE__*/React.createElement("span", {
    className: "atl-sidenav__names"
  }, /*#__PURE__*/React.createElement("div", {
    className: "atl-sidenav__wordmark"
  }, name), maker ? /*#__PURE__*/React.createElement("div", {
    className: "atl-sidenav__by"
  }, maker) : null)), header ? /*#__PURE__*/React.createElement("div", {
    className: "atl-sidenav__head"
  }, header) : null, /*#__PURE__*/React.createElement("div", {
    className: "atl-sidenav__groups"
  }, groups.map((g, gi) => /*#__PURE__*/React.createElement("div", {
    className: "atl-sidenav__group",
    key: g.label || gi
  }, g.label ? /*#__PURE__*/React.createElement("div", {
    className: "atl-sidenav__grouplabel"
  }, g.label) : null, g.items.map(it => /*#__PURE__*/React.createElement("button", {
    key: it.value,
    className: `atl-navitem${it.value === value ? " atl-navitem--active" : ""}`,
    onClick: () => onChange && onChange(it.value)
  }, it.icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: it.icon,
    size: 18
  }) : null, /*#__PURE__*/React.createElement("span", {
    className: "atl-navitem__label"
  }, it.label), it.trailing != null ? /*#__PURE__*/React.createElement("span", {
    className: "atl-navitem__trail"
  }, it.trailing) : null))))), footer ? /*#__PURE__*/React.createElement("div", {
    className: "atl-sidenav__foot"
  }, footer) : null);
}
Object.assign(__ds_scope, { SideNav });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/SideNav.jsx", error: String((e && e.message) || e) }); }

// components/navigation/Stepper.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Navigation" subtitle="Multi-step import or onboarding progress" viewport="760x160"
 */
function Stepper({
  steps = [],
  current = 0,
  orientation = "horizontal",
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-stepper", orientation === "vertical" ? "atl-stepper--vertical" : "", className].filter(Boolean).join(" ")
  }, rest), steps.map((s, i) => {
    const state = i < current ? "done" : i === current ? "current" : "todo";
    return /*#__PURE__*/React.createElement("div", {
      className: ["atl-step", "atl-step--" + state].join(" "),
      key: s.label
    }, /*#__PURE__*/React.createElement("span", {
      className: "atl-step__dot"
    }, state === "done" ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "check",
      size: 13
    }) : i + 1), /*#__PURE__*/React.createElement("span", {
      className: "atl-step__body"
    }, /*#__PURE__*/React.createElement("span", {
      className: "atl-step__label"
    }, s.label), s.note ? /*#__PURE__*/React.createElement("span", {
      className: "atl-step__note"
    }, s.note) : null), orientation === "horizontal" && i < steps.length - 1 ? /*#__PURE__*/React.createElement("span", {
      className: ["atl-step__line", i < current ? "atl-step__line--done" : ""].filter(Boolean).join(" ")
    }) : null);
  }));
}
Object.assign(__ds_scope, { Stepper });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/Stepper.jsx", error: String((e && e.message) || e) }); }

// components/navigation/Tabs.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Tabs({
  items = [],
  value,
  onChange,
  variant = "underline",
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    role: "tablist",
    className: ["atl-tabs", variant === "pill" ? "atl-tabs--pill" : "", className].filter(Boolean).join(" ")
  }, rest), items.map(it => {
    const t = typeof it === "string" ? {
      value: it,
      label: it
    } : it;
    const active = t.value === value;
    return /*#__PURE__*/React.createElement("button", {
      key: t.value,
      role: "tab",
      "aria-selected": active,
      className: `atl-tab${active ? " atl-tab--active" : ""}`,
      onClick: () => onChange && onChange(t.value)
    }, t.label, t.count != null ? /*#__PURE__*/React.createElement("span", {
      className: "atl-tab__count"
    }, t.count) : null);
  }));
}
Object.assign(__ds_scope, { Tabs });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/Tabs.jsx", error: String((e && e.message) || e) }); }

// components/navigation/TopBar.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Navigation" subtitle="Sticky topbar with search trigger and actions" viewport="900x120"
 */
function TopBar({
  eyebrow,
  title,
  subtitle,
  onMenu,
  menuLabel = "Menú",
  onSearch,
  searchHint,
  actions,
  left,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("header", _extends({
    className: ["atl-topbar", className].filter(Boolean).join(" ")
  }, rest), onMenu ? /*#__PURE__*/React.createElement("button", {
    className: "atl-topbar__menu",
    onClick: onMenu,
    "aria-label": menuLabel
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "menu",
    size: 20
  })) : null, left, /*#__PURE__*/React.createElement("div", {
    className: "atl-topbar__titles"
  }, eyebrow ? /*#__PURE__*/React.createElement("span", {
    className: "atl-topbar__eyebrow"
  }, eyebrow) : null, title ? /*#__PURE__*/React.createElement("span", {
    className: "atl-topbar__title"
  }, title) : null, subtitle ? /*#__PURE__*/React.createElement("span", {
    className: "atl-topbar__subtitle"
  }, subtitle) : null), /*#__PURE__*/React.createElement("span", {
    className: "atl-topbar__spacer"
  }), onSearch ? /*#__PURE__*/React.createElement("button", {
    className: "atl-search",
    onClick: onSearch
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "search",
    size: 15
  }), "Buscar en todo", searchHint ? /*#__PURE__*/React.createElement("kbd", {
    className: "atl-kbd atl-search__hint"
  }, searchHint) : null) : null, actions ? /*#__PURE__*/React.createElement("div", {
    className: "atl-topbar__actions"
  }, actions) : null);
}
Object.assign(__ds_scope, { TopBar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/TopBar.jsx", error: String((e && e.message) || e) }); }

// components/navigation/AppShell.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * @startingPoint section="Navigation" subtitle="Full app frame: sidebar, topbar, content, optional AI panel" viewport="1180x720"
 */
function AppShell({
  app = "atlas",
  theme = "light",
  appName,
  product,
  maker,
  navHeader,
  groups = [],
  value,
  onChange,
  navFooter,
  collapsed = false,
  topbar,
  aside,
  children,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: ["atl-shell", className].filter(Boolean).join(" "),
    "data-app": app,
    "data-theme": theme
  }, rest), /*#__PURE__*/React.createElement(__ds_scope.SideNav, {
    appName: appName,
    product: product,
    maker: maker,
    header: navHeader,
    groups: groups,
    value: value,
    onChange: onChange,
    footer: navFooter,
    className: collapsed ? "atl-sidenav--collapsed" : ""
  }), /*#__PURE__*/React.createElement("div", {
    className: "atl-shell__main"
  }, topbar || /*#__PURE__*/React.createElement(__ds_scope.TopBar, null), /*#__PURE__*/React.createElement("div", {
    className: "atl-shell__scroll"
  }, children)), aside);
}
Object.assign(__ds_scope, { AppShell });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/AppShell.jsx", error: String((e && e.message) || e) }); }

__ds_ns.AiFace = __ds_scope.AiFace;

__ds_ns.ChatPanel = __ds_scope.ChatPanel;

__ds_ns.Badge = __ds_scope.Badge;

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Card = __ds_scope.Card;

__ds_ns.Icon = __ds_scope.Icon;

__ds_ns.IconButton = __ds_scope.IconButton;

__ds_ns.Tag = __ds_scope.Tag;

__ds_ns.Avatar = __ds_scope.Avatar;

__ds_ns.AvatarStack = __ds_scope.AvatarStack;

__ds_ns.DataGrid = __ds_scope.DataGrid;

__ds_ns.KanbanBoard = __ds_scope.KanbanBoard;

__ds_ns.SignedAmount = __ds_scope.SignedAmount;

__ds_ns.StatCard = __ds_scope.StatCard;

__ds_ns.Table = __ds_scope.Table;

__ds_ns.Timeline = __ds_scope.Timeline;

__ds_ns.Dialog = __ds_scope.Dialog;

__ds_ns.Drawer = __ds_scope.Drawer;

__ds_ns.EmptyState = __ds_scope.EmptyState;

__ds_ns.NotificationList = __ds_scope.NotificationList;

__ds_ns.Toast = __ds_scope.Toast;

__ds_ns.Tooltip = __ds_scope.Tooltip;

__ds_ns.Checkbox = __ds_scope.Checkbox;

__ds_ns.Combobox = __ds_scope.Combobox;

__ds_ns.DateRangePicker = __ds_scope.DateRangePicker;

__ds_ns.Field = __ds_scope.Field;

__ds_ns.Input = __ds_scope.Input;

__ds_ns.MultiSelect = __ds_scope.MultiSelect;

__ds_ns.Radio = __ds_scope.Radio;

__ds_ns.Select = __ds_scope.Select;

__ds_ns.Switch = __ds_scope.Switch;

__ds_ns.Textarea = __ds_scope.Textarea;

__ds_ns.AppShell = __ds_scope.AppShell;

__ds_ns.Breadcrumbs = __ds_scope.Breadcrumbs;

__ds_ns.CommandPalette = __ds_scope.CommandPalette;

__ds_ns.FilterBar = __ds_scope.FilterBar;

__ds_ns.Menu = __ds_scope.Menu;

__ds_ns.Pagination = __ds_scope.Pagination;

__ds_ns.SegmentedControl = __ds_scope.SegmentedControl;

__ds_ns.SideNav = __ds_scope.SideNav;

__ds_ns.Stepper = __ds_scope.Stepper;

__ds_ns.Tabs = __ds_scope.Tabs;

__ds_ns.TopBar = __ds_scope.TopBar;

})();
