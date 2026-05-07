// MemLeakInspector Dashboard v2.x

let currentSnapshotData = null;
let threadChart = null;

function showTab(tabId) {
  document.querySelectorAll(".tab-content").forEach(t => t.style.display = "none");
  document.getElementById(tabId).style.display = "block";
  document.querySelectorAll(".buttons button").forEach(b => b.classList.remove("active"));
  document.getElementById("btn-" + tabId)?.classList.add("active");
}

function setStatus(msg) {
  document.getElementById("status").textContent = msg;
}

document.getElementById("fileInput").addEventListener("change", function(event) {
  const file = event.target.files[0];
  if (!file) return;

  document.getElementById("fileName").textContent = file.name;
  const reader = new FileReader();

  reader.onload = function(e) {
    const text = e.target.result;
    try {
      if (file.name.endsWith(".json")) {
        const data = JSON.parse(text);

        // Snapshot data
        if (data.TypeCounts || data.TrackedInstancesByType) {
          currentSnapshotData = data;
          renderSnapshots(data);
          showTab("snapshots");
          setStatus(`Loaded snapshot: ${data.Timestamp || "unknown time"}, ${Object.keys(data.TypeCounts || {}).length} types`);
        }

        // Thread history (array of entries)
        if (Array.isArray(data) && data.length > 0 && data[0]?.Threads) {
          renderThreads(data);
          renderThreadChart(data);
          showTab("threads");
          setStatus(`Loaded ${data.length} thread snapshots`);
        }
      } else if (file.name.endsWith(".csv")) {
        renderCsv(text);
        showTab("snapshots");
        setStatus(`Loaded CSV: ${file.name}`);
      }
    } catch (err) {
      setStatus("Parse error: " + err.message);
    }
  };
  reader.readAsText(file);
});

// -- Snapshot rendering --

function renderSnapshots(data) {
  const container = document.getElementById("snapshots");
  const entries = [];

  // Support both TypeCounts and TrackedInstancesByType
  if (data.TypeCounts) {
    for (const [type, count] of Object.entries(data.TypeCounts))
      entries.push({ type, count, memMB: data.EstimatedMemoryBytesPerType?.[type] ? (data.EstimatedMemoryBytesPerType[type] / 1048576).toFixed(1) : "-" });
  } else if (data.TrackedInstancesByType) {
    for (const [type, instances] of Object.entries(data.TrackedInstancesByType))
      entries.push({ type, count: instances.length, memMB: "-" });
  }

  // Store for filtering/sorting
  currentSnapshotData._entries = entries;
  renderSnapshotTable(entries);
}

function renderSnapshotTable(entries) {
  const container = document.getElementById("snapshots");
  let html = `<h2>Snapshot Overview (${entries.length} types)</h2>`;
  html += `<table><thead><tr><th>Type</th><th class="num">Count</th><th class="num">Est. MB</th></tr></thead><tbody>`;
  for (const e of entries)
    html += `<tr><td>${esc(e.type)}</td><td class="num">${e.count}</td><td class="num">${e.memMB}</td></tr>`;
  html += `</tbody></table>`;
  container.innerHTML = html;
}

function applyFilter() {
  if (!currentSnapshotData?._entries) return;
  const q = document.getElementById("filterInput").value.toLowerCase();
  const filtered = currentSnapshotData._entries.filter(e => e.type.toLowerCase().includes(q));
  renderSnapshotTable(applySortTo(filtered));
}

function reSort() {
  applyFilter();
}

function applySortTo(entries) {
  const mode = document.getElementById("sortSelect").value;
  const copy = [...entries];
  switch (mode) {
    case "count-desc": copy.sort((a, b) => b.count - a.count); break;
    case "count-asc":  copy.sort((a, b) => a.count - b.count); break;
    case "name-asc":   copy.sort((a, b) => a.type.localeCompare(b.type)); break;
    case "name-desc":  copy.sort((a, b) => b.type.localeCompare(a.type)); break;
  }
  return copy;
}

// -- Thread rendering --

function renderThreads(data) {
  const container = document.getElementById("threadTable");
  let html = `<h2>Thread Activity (${data.length} snapshots)</h2>`;
  html += `<table><thead><tr><th>Timestamp</th><th class="num">ID</th><th>State</th><th>Wait</th><th class="num">CPU ms</th></tr></thead><tbody>`;

  // Show last 500 rows max
  const rows = [];
  for (const entry of data)
    for (const t of entry.Threads)
      rows.push({ ts: entry.Timestamp, ...t });

  for (const r of rows.slice(-500))
    html += `<tr><td>${esc(r.ts)}</td><td class="num">${r.Id}</td><td>${esc(r.State)}</td><td>${esc(r.WaitReason || "")}</td><td class="num">${r.CpuTimeMs}</td></tr>`;

  html += `</tbody></table>`;
  container.innerHTML = html;
}

function renderThreadChart(data) {
  const canvas = document.getElementById("threadChart");
  if (threadChart) threadChart.destroy();

  const labels = data.map(e => {
    const d = new Date(e.Timestamp);
    return d.toLocaleTimeString();
  });
  const counts = data.map(e => e.Threads.length);

  threadChart = new Chart(canvas.getContext("2d"), {
    type: "line",
    data: {
      labels,
      datasets: [{
        label: "Thread Count",
        data: counts,
        borderColor: "#33faff",
        backgroundColor: "#33faff22",
        fill: true,
        tension: 0.3,
        pointRadius: 1,
      }]
    },
    options: {
      responsive: true,
      plugins: { legend: { labels: { color: "#888" } } },
      scales: {
        x: { ticks: { color: "#555", maxTicksLimit: 20 } },
        y: { ticks: { color: "#555" }, beginAtZero: true }
      }
    }
  });
}

// -- CSV rendering --

function renderCsv(csvText) {
  const container = document.getElementById("snapshots");
  const lines = csvText.trim().split("\n");
  if (lines.length === 0) { container.innerHTML = "<p>Empty CSV</p>"; return; }

  const header = parseCSVLine(lines[0]);
  let html = `<h2>CSV Data (${lines.length - 1} rows)</h2>`;
  html += `<table><thead><tr>${header.map(h => `<th>${esc(h)}</th>`).join("")}</tr></thead><tbody>`;

  for (let i = 1; i < Math.min(lines.length, 5001); i++) {
    const cells = parseCSVLine(lines[i]);
    html += `<tr>${cells.map(c => `<td>${esc(c)}</td>`).join("")}</tr>`;
  }

  html += `</tbody></table>`;
  if (lines.length > 5001) html += `<p class="status">Showing first 5000 rows</p>`;
  container.innerHTML = html;
}

// Basic CSV line parser (handles quoted fields)
function parseCSVLine(line) {
  const result = [];
  let current = "";
  let inQuotes = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (inQuotes) {
      if (c === '"' && line[i + 1] === '"') { current += '"'; i++; }
      else if (c === '"') inQuotes = false;
      else current += c;
    } else {
      if (c === '"') inQuotes = true;
      else if (c === ',') { result.push(current.trim()); current = ""; }
      else current += c;
    }
  }
  result.push(current.trim());
  return result;
}

function esc(s) {
  if (s == null) return "";
  return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
