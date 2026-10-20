// Il tema dello schermo del cliente, per /cliente e per il back office: le otto palette del design (le scrive qui
// l'Agent, da Aspetto/Palettes.cs), il contrasto WCAG, i colori del locale sopra il tema e le variabili CSS. È lo stesso
// calcolo di display/src/theme/theme.ts del design e di Aspetto/ThemeColors.cs: il back office rifiuta quello che il
// display rifiuterebbe, e il display ripiega su bluetech-notte se un tema illeggibile arriva lo stesso.
(function () {
  "use strict";
  var PALETTES = /*PALETTES*/{};
  var FONTS = /*FONTS*/{};
  var MIN = 4.5;
  var FALLBACK = "bluetech-notte";
  var DAY = { start: 7, end: 19 };

  function parseHex(color) {
    var match = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(String(color || "").trim());
    if (!match) return null;
    var hex = match[1].length === 3 ? match[1].replace(/./g, "$&$&") : match[1];
    return [0, 2, 4].map(function (i) { return parseInt(hex.slice(i, i + 2), 16); });
  }

  function parseColor(color) {
    var hex = parseHex(color);
    if (hex) return { rgb: hex, alpha: 1 };
    var match = /^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*(\d*\.?\d+)\s*)?\)$/i.exec(String(color || "").trim());
    if (!match) return null;
    var rgb = [match[1], match[2], match[3]].map(Number);
    var alpha = match[4] === undefined ? 1 : Number(match[4]);
    if (rgb.some(function (c) { return c > 255; }) || alpha > 1) return null;
    return { rgb: rgb, alpha: alpha };
  }

  function toHex(rgb) {
    return ("#" + rgb.map(function (c) { return Math.round(c).toString(16).padStart(2, "0"); }).join("")).toUpperCase();
  }

  function composite(top, bottom) {
    var over = parseColor(top), under = parseHex(bottom);
    if (!over || !under) return null;
    return toHex(over.rgb.map(function (c, i) { return c * over.alpha + under[i] * (1 - over.alpha); }));
  }

  function mix(a, b, amount) {
    var from = parseHex(a), to = parseHex(b);
    if (!from || !to) return null;
    return toHex(from.map(function (c, i) { return c + (to[i] - c) * amount; }));
  }

  function alphaOf(color) { var c = parseColor(color); return c ? c.alpha : null; }

  function withAlpha(color, alpha) { var rgb = parseHex(color); return rgb ? "rgba(" + rgb.join(",") + "," + alpha + ")" : null; }

  function recolor(value, map) {
    return value.replace(/#[0-9a-f]{6}\b|(rgba?\(\s*)(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})/gi, function (match, prefix, r, g, b) {
      var rgb = prefix === undefined ? parseHex(match) : [r, g, b].map(Number);
      var target = rgb && map.get(rgb.join(","));
      if (!target) return match;
      return prefix === undefined ? toHex(target) : prefix + target.join(",");
    });
  }

  function luminance(color) {
    var rgb = parseHex(color);
    if (!rgb) return null;
    var c = rgb.map(function (v) { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
  }

  function contrast(fg, bg) {
    var a = luminance(fg), b = luminance(bg);
    if (a === null || b === null) return null;
    return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
  }

  var ORDER = ["primary", "onPrimary", "text", "secondary", "accent", "bg", "muted"];
  var LOCALE = ["bg", "surface", "text", "muted", "primary", "onPrimary", "accent", "secondary"];

  /** I colori del locale sopra il tema; quelli che il tema ricava dagli altri si ricalcolano (theme.ts). */
  function withLocaleColors(base, colors) {
    var map = new Map();
    ORDER.forEach(function (key) {
      var from = parseHex(base[key]), to = parseHex(colors[key]);
      if (from && to && colors[key] !== base[key] && !map.has(from.join(","))) map.set(from.join(","), to);
    });
    function unchanged() { return Array.prototype.every.call(arguments, function (k) { return colors[k] === base[k]; }); }
    var p = Object.assign({}, base);
    LOCALE.forEach(function (key) { p[key] = colors[key]; });
    p.surfaceSolid = unchanged("surface", "bg") ? base.surfaceSolid : (composite(colors.surface, colors.bg) || recolor(base.surfaceSolid, map));
    var lineAlpha = alphaOf(base.line);
    p.line = unchanged("text") ? base.line : (withAlpha(colors.text, lineAlpha === null ? 0.12 : lineAlpha) || recolor(base.line, map));
    p.faint = unchanged("muted", "bg") ? base.faint : (mix(colors.muted, colors.bg, 0.35) || recolor(base.faint, map));
    ["totalFill", "totalBorder", "totalText", "glow", "glow2"].forEach(function (key) { p[key] = recolor(base[key], map); });
    return p;
  }

  /** Le coppie che il cliente deve poter leggere (contrastProblems del design). */
  function pairs(p) {
    var behind = parseHex(p.totalFill) ? p.totalFill : p.surfaceSolid;
    return [
      ["il testo sul fondo", p.text, p.bg],
      ["il testo sulla card del conto", p.text, p.surfaceSolid],
      ["il testo sopra il colore primario", p.onPrimary, p.primary],
      ["la cifra del totale", p.totalText, behind],
    ].map(function (pair) {
      var ratio = contrast(pair[1], pair[2]);
      return { label: pair[0], fg: pair[1], bg: pair[2], ratio: ratio, ok: ratio !== null && ratio >= MIN };
    });
  }

  function hsl(rgb) {
    var r = rgb[0] / 255, g = rgb[1] / 255, b = rgb[2] / 255;
    var max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2;
    if (max === min) return [0, 0, l];
    var d = max - min, s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    var h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return [h / 6, s, l];
  }

  function fromHsl(h, s, l) {
    if (s === 0) return toHex([l * 255, l * 255, l * 255]);
    function hue(p, q, t) {
      if (t < 0) t += 1;
      if (t > 1) t -= 1;
      return t < 1 / 6 ? p + (q - p) * 6 * t : t < 1 / 2 ? q : t < 2 / 3 ? p + (q - p) * (2 / 3 - t) * 6 : p;
    }
    var q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    return toHex([hue(p, q, h + 1 / 3) * 255, hue(p, q, h) * 255, hue(p, q, h - 1 / 3) * 255]);
  }

  /** Stessa tinta, la luminosità più vicina che si legge su tutti i fondi (come ThemeColors.Suggest dell'Agent). */
  function nearest(color, backgrounds) {
    var rgb = parseHex(color);
    if (!rgb) return null;
    var c = hsl(rgb);
    for (var step = 1; step <= 100; step++) {
      var tries = [c[2] - step / 100, c[2] + step / 100];
      for (var i = 0; i < 2; i++) {
        if (tries[i] < 0 || tries[i] > 1) continue;
        var candidate = fromHsl(c[0], c[1], tries[i]);
        if (backgrounds.every(function (b) { return contrast(candidate, b) >= MIN + 0.05; })) return candidate;
      }
    }
    return null;
  }

  function same(a, b) { var x = parseHex(a), y = parseHex(b); return !!x && !!y && x.join(",") === y.join(","); }

  /** I colori più vicini che passano il controllo: cambia solo il testo o il testo sopra il primario. Null se vanno già. */
  function suggest(base, colors) {
    var current = Object.assign({}, colors);
    for (var round = 0; round < 4; round++) {
      var p = withLocaleColors(base, current);
      var failing = pairs(p).filter(function (x) { return !x.ok; });
      if (!failing.length) return round === 0 ? null : current;
      var pair = failing[0];
      if (pair.ratio === null) return null;
      if (same(pair.fg, p.onPrimary) && same(pair.bg, p.primary)) current.onPrimary = nearest(current.onPrimary, [p.primary]) || current.onPrimary;
      else current.text = nearest(current.text, [p.bg, p.surfaceSolid]) || current.text;
    }
    return pairs(withLocaleColors(base, current)).every(function (x) { return x.ok; }) ? current : null;
  }

  function modeAt(date, hours) {
    var h = hours || DAY;
    var hour = date.getHours() + date.getMinutes() / 60;
    return hour >= h.start && hour < h.end ? "giorno" : "notte";
  }

  /** Il tema da applicare adesso, dal messaggio cfg ({ preset, theme, dayHours }): resolveTheme del design. */
  function resolve(cfg, date) {
    var t = (cfg && cfg.theme) || {};
    var preset = cfg && PALETTES[cfg.preset + "-notte"] ? cfg.preset : "bluetech";
    var mode = t.mode === "auto" ? modeAt(date || new Date(), cfg && cfg.dayHours) : t.mode === "giorno" ? "giorno" : "notte";
    var base = PALETTES[preset + "-" + mode];
    var shape = {};
    if (t.fontDisplay) shape.fontDisplay = t.fontDisplay;
    if (t.fontText) shape.fontText = t.fontText;
    if (typeof t.radius === "number") shape.radius = t.radius;
    var palette;
    if (t.mode === "auto" || !t.cBg) {
      palette = Object.assign({}, base, shape);
    } else {
      palette = Object.assign(withLocaleColors(base, {
        bg: t.cBg, surface: t.cSurface, text: t.cText, muted: t.cMuted,
        primary: t.cPrimary, onPrimary: t.cOnPrimary, accent: t.cAccent, secondary: t.cSecondary,
      }), shape);
      if (typeof t.glass === "number") palette.glass = t.glass;
      if (t.footerVariant) palette.footerVariant = t.footerVariant;
    }
    var problems = pairs(palette).filter(function (p) { return !p.ok; });
    if (problems.length) return { palette: PALETTES[FALLBACK], mode: "notte", fallback: true, problems: problems };
    return { palette: palette, mode: mode, fallback: false, problems: [] };
  }

  function clamp(v, lo, hi) { return Math.min(hi, Math.max(lo, v)); }

  /** paletteToCssVars del design. */
  function cssVars(p) {
    return {
      "--c-bg": p.bg, "--c-surface": p.surface, "--c-surface-solid": p.surfaceSolid, "--c-line": p.line,
      "--c-text": p.text, "--c-muted": p.muted, "--c-faint": p.faint, "--c-primary": p.primary,
      "--c-on-primary": p.onPrimary, "--c-accent": p.accent, "--c-secondary": p.secondary,
      "--font-display": p.fontDisplay, "--font-text": p.fontText, "--radius": clamp(p.radius, 0, 32) + "px",
      "--glass-alpha": String(clamp(p.glass, 0, 100) * 0.0012), "--glass-blur": clamp(p.glass, 0, 100) * 0.28 + "px",
      "--total-fill": p.totalFill, "--total-border": p.totalBorder, "--total-text": p.totalText,
      "--glow": p.glow, "--glow-2": p.glow2,
    };
  }

  function apply(element, palette) {
    var vars = cssVars(palette);
    Object.keys(vars).forEach(function (name) { element.style.setProperty(name, vars[name]); });
  }

  window.BluetechTema = {
    palettes: PALETTES, fonts: FONTS, min: MIN, fallback: FALLBACK,
    parseHex: parseHex, parseColor: parseColor, toHex: toHex, contrast: contrast,
    withLocaleColors: withLocaleColors, pairs: pairs, suggest: suggest, modeAt: modeAt,
    resolve: resolve, cssVars: cssVars, apply: apply,
  };
})();
