# ---------------------------------------------------------------------------------------
# Разбор C# на POSIX awk. Никаких расширений gawk: 2-аргументный match, substr, split,
# SUBSEP-массивы, ENVIRON. Проверено на mawk 1.3.4 и GNU Awk 5.0 (тот, что стоит с Git
# for Windows) — прибор обязан считать одинаково там, где стоят ворота, и в CI.
BEGIN {
  FS = "\n"
  nord = split(ENVIRON["QUARP_ORDER"], ORDER, "\n")
  n2 = split(ENVIRON["QUARP_LAYERS"], LL, "\n")
  for (i = 1; i <= n2; i++) {
    if (LL[i] == "") continue
    p = index(LL[i], "|")
    LAYER[substr(LL[i], 1, p - 1)] = substr(LL[i], p + 1) + 0
  }
  DECLRE = "^[ \t]*((public|internal|private|protected|static|sealed|abstract|partial|readonly|ref|file|new|unsafe)[ \t]+)*(record[ \t]+)?(class|struct|record|enum|interface)[ \t]+[A-Za-z_]"
  KW["class"] = 1; KW["struct"] = 1; KW["record"] = 1; KW["enum"] = 1; KW["interface"] = 1
}

function strip(line,   i, n, c, d, j, k, cc, ch, depth, out, cut) {
  out = ""; cut = ""
  i = 1; n = length(line)
  while (i <= n) {
    c = substr(line, i, 1); d = substr(line, i + 1, 1)
    if (ST == 1) {                       # блочный комментарий тянется с прошлой строки
      j = index(substr(line, i), "*/")
      if (j == 0) { cut = cut substr(line, i); i = n + 1 }
      else { cut = cut substr(line, i, j + 1); i = i + j + 1; ST = 0 }
      continue
    }
    if (ST == 2) {                       # verbatim-строка тянется с прошлой строки
      j = i
      while (j <= n) {
        if (substr(line, j, 1) == "\"") {
          if (substr(line, j + 1, 1) == "\"") { j = j + 2; continue }
          break
        }
        j++
      }
      if (j > n) { cut = cut substr(line, i); i = n + 1 }
      else { cut = cut substr(line, i, j - i + 1); i = j + 1; ST = 0 }
      continue
    }
    if (c == "/" && d == "/") { cut = cut substr(line, i); i = n + 1; continue }
    if (c == "/" && d == "*") {
      j = index(substr(line, i + 2), "*/")
      if (j == 0) { cut = cut substr(line, i); ST = 1; i = n + 1 }
      else { cut = cut substr(line, i, j + 3); i = i + j + 3 }
      continue
    }
    if (c == "@" && d == "\"") { ST = 2; cut = cut "@\""; i = i + 2; continue }
    if (c == "$" && d == "\"") {         # интерполяция: текст в мусор, дырки {...} — код
      i = i + 2
      while (i <= n) {
        cc = substr(line, i, 1)
        if (cc == "\\") { i = i + 2; continue }
        if (cc == "\"") { i++; break }
        if (cc == "{") {
          if (substr(line, i + 1, 1) == "{") { i = i + 2; continue }
          depth = 1; k = i + 1
          while (k <= n && depth > 0) {
            ch = substr(line, k, 1)
            if (ch == "{") depth++
            else if (ch == "}") depth--
            k++
          }
          out = out " " substr(line, i + 1, k - i - 2)
          i = k; continue
        }
        cut = cut cc; i++
      }
      continue
    }
    if (c == "\"" || c == "'") {
      j = i + 1
      while (j <= n) {
        ch = substr(line, j, 1)
        if (ch == "\\") { j = j + 2; continue }
        if (ch == c) { j++; break }
        j++
      }
      if (j > n + 1) j = n + 1
      cut = cut substr(line, i, j - i); i = j; continue
    }
    out = out c; i++
  }
  CODE = out; CUT = cut
}

FNR == 1 { ST = 0; nf = split(FILENAME, _fp, "/"); FILE = _fp[nf] }

{
  strip($0)
  # объявления типов — только там, где строка и вправду похожа на объявление
  if (match(CODE, DECLRE)) {
    m = split(CODE, tk, "[^A-Za-z0-9_]+")
    lastkw = 0
    for (t = 1; t <= m; t++) if (tk[t] in KW) lastkw = t
    if (lastkw > 0 && lastkw < m) {
      name = tk[lastkw + 1]
      if (name != "" && name !~ /^[0-9]/) {
        if ((name in OWNER) && OWNER[name] != FILE) {
          printf "тип %s объявлен и в %s, и в %s — граф ссылок стал догадкой\n", \
            name, OWNER[name], FILE > "/dev/stderr"
          DUP = 1
        }
        OWNER[name] = FILE
      }
    }
  }
  # токены кода: имя + номер строки; токены мусора: только счётчик
  m = split(CODE, tk, "[^A-Za-z0-9_]+")
  for (t = 1; t <= m; t++) {
    if (tk[t] == "" || tk[t] ~ /^[0-9]/) continue
    key = FILE SUBSEP tk[t]
    if (key in HITS) HITS[key] = HITS[key] "," FNR; else { HITS[key] = FNR; SEEN[FILE] = SEEN[FILE] " " tk[t] }
  }
  m = split(CUT, tk, "[^A-Za-z0-9_]+")
  for (t = 1; t <= m; t++) {
    if (tk[t] == "" || tk[t] ~ /^[0-9]/) continue
    CUTC[FILE SUBSEP tk[t]]++
    if (!((FILE SUBSEP tk[t]) in CUTSEEN)) { CUTSEEN[FILE SUBSEP tk[t]] = 1; CSEEN[FILE] = CSEEN[FILE] " " tk[t] }
  }
}

function ssort(arr, n,   a, b, tmp) {
  for (a = 2; a <= n; a++) {
    tmp = arr[a]; b = a - 1
    while (b >= 1 && arr[b] > tmp) { arr[b + 1] = arr[b]; b-- }
    arr[b + 1] = tmp
  }
}

END {
  if (DUP) exit 2
  # самопроверка: сколько упоминаний чужих типов ушло вместе с комментариями и строками
  cuthits = 0
  for (f = 1; f <= nord; f++) {
    file = ORDER[f]
    m = split(CSEEN[file], names, " ")
    for (t = 1; t <= m; t++) {
      nm = names[t]
      if (!(nm in OWNER)) continue
      if (OWNER[nm] == file) continue
      cuthits += CUTC[file SUBSEP nm]
    }
  }
  printf "C\t%d\n", cuthits

  for (f = 1; f <= nord; f++) {
    file = ORDER[f]
    src = LAYER[file]
    m = split(SEEN[file], names, " ")
    for (lv = 1; lv <= 4; lv++) bucket[lv] = ""
    nv = 0
    for (t = 1; t <= m; t++) {
      nm = names[t]
      if (!(nm in OWNER)) continue
      home = OWNER[nm]
      if (home == file) continue
      hl = LAYER[home]
      bucket[hl] = bucket[hl] " " nm
      if (hl > src) { nv++; VIO[nv] = nm "\t" home "\t" hl "\t" HITS[file SUBSEP nm] }
    }
    cell = ""
    for (lv = 1; lv <= 4; lv++) {
      if (bucket[lv] == "") continue
      k = split(bucket[lv], bb, " ")
      ssort(bb, k)
      s = ""
      for (t = 1; t <= k; t++) s = (s == "" ? bb[t] : s "," bb[t])
      cell = (cell == "" ? "" : cell " ") lv ":" s
    }
    if (cell == "") cell = "—"
    printf "R\t%s\t%d\t%s\n", file, src, cell
    for (t = 1; t <= nv; t++) {
      split(VIO[t], vv, "\t")
      printf "V\t%s\t%d\t%s\t%s\t%s\t%s\n", file, src, vv[1], vv[2], vv[3], vv[4]
    }
  }
}
