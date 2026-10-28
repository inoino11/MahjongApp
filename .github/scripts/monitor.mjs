// 毎日の運用確認（.github/workflows/monitor.yml から実行）。
// KV の書き込み回数と、過去24時間に届いた問い合わせの件数を調べ、知らせるべきことがあれば標準出力に Markdown で書く。
// 問い合わせの中身は暗号化されていて読めないので、件数だけを扱う
const token = process.env.CLOUDFLARE_MONITOR_TOKEN;
const accountId = process.env.CLOUDFLARE_ACCOUNT_ID;
const namespaceId = process.env.KV_NAMESPACE_ID;
// 無料枠は書き込み 1 日 1,000 回。その 70% で知らせる
const writeAlert = Number(process.env.WRITE_ALERT ?? 700);

if (!token || !accountId || !namespaceId) {
  console.error("CLOUDFLARE_MONITOR_TOKEN / CLOUDFLARE_ACCOUNT_ID / KV_NAMESPACE_ID が設定されていないため、確認を飛ばします");
  process.exit(0);
}

const api = "https://api.cloudflare.com/client/v4";
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const utcDate = (d) => d.toISOString().slice(0, 10);
// 通知は日本時間で書く。KV の無料枠は UTC の日ごと（日本時間 9 時から翌日 9 時まで）に数えられる
const jst = (d) => d.toLocaleString("ja-JP", { timeZone: "Asia/Tokyo", month: "numeric", day: "numeric", hour: "2-digit", minute: "2-digit" });
const jstRangeOfUtcDate = (date) => {
  const start = new Date(`${date}T00:00:00Z`);
  return `日本時間 ${jst(start)}〜${jst(new Date(start.getTime() + 24 * 3600e3))}`;
};

// UTC の日ごとの KV の書き込み回数（前日と当日）
async function kvWritesByDate() {
  const today = new Date();
  const yesterday = new Date(today.getTime() - 24 * 3600e3);
  const query = `query ($accountTag: string!, $namespaceId: string!, $start: Date!, $end: Date!) {
    viewer { accounts(filter: { accountTag: $accountTag }) {
      kvOperationsAdaptiveGroups(limit: 100, filter: { namespaceId: $namespaceId, date_geq: $start, date_leq: $end }) {
        sum { requests }
        dimensions { date actionType }
      }
    } }
  }`;
  const res = await fetch(`${api}/graphql`, {
    method: "POST",
    headers,
    body: JSON.stringify({ query, variables: { accountTag: accountId, namespaceId, start: utcDate(yesterday), end: utcDate(today) } }),
  });
  const json = await res.json();
  if (json.errors?.length) throw new Error("GraphQL: " + JSON.stringify(json.errors));
  const groups = json.data?.viewer?.accounts?.[0]?.kvOperationsAdaptiveGroups ?? [];
  const writes = { [utcDate(yesterday)]: 0, [utcDate(today)]: 0 };
  for (const g of groups) {
    if (/write/i.test(g.dimensions.actionType)) writes[g.dimensions.date] = (writes[g.dimensions.date] ?? 0) + g.sum.requests;
  }
  return writes;
}

// 過去24時間に届いた問い合わせの件数（キー名の先頭に受信日時が入っている）
async function recentFeedbackCount() {
  const since = new Date(Date.now() - 24 * 3600e3).toISOString();
  let count = 0;
  let cursor = "";
  do {
    const url = `${api}/accounts/${accountId}/storage/kv/namespaces/${namespaceId}/keys?prefix=feedback:&limit=1000${cursor ? `&cursor=${cursor}` : ""}`;
    const json = await (await fetch(url, { headers })).json();
    if (!json.success) throw new Error("KV keys: " + JSON.stringify(json.errors));
    count += json.result.filter(k => k.name.slice("feedback:".length, "feedback:".length + 24) >= since).length;
    cursor = json.result_info?.cursor ?? "";
  } while (cursor);
  return count;
}

const lines = [];
const writes = await kvWritesByDate();
const feedback = await recentFeedbackCount();
console.error(`KV の書き込み: ${JSON.stringify(writes)} / 過去24時間の問い合わせ: ${feedback}件`);

for (const [date, count] of Object.entries(writes)) {
  if (count >= writeAlert) lines.push(`- ⚠️ ${jstRangeOfUtcDate(date)} の KV の書き込みが **${count} 回**です（無料枠は 1 日 1,000 回、通知のしきい値は ${writeAlert} 回）。README の「運用」にある有料プランへの切り替えの目安を確認してください。`);
}
if (feedback > 0) lines.push(`- ✉️ 過去 24 時間に問い合わせが **${feedback} 件**届いています。Cloudflare のダッシュボードで \`feedback:\` のキーを開き、tools/feedback-reader.html で読んでください。`);

if (lines.length > 0) {
  console.log(`### ${jst(new Date())}（日本時間）の運用確認\n\n${lines.join("\n")}`);
}
