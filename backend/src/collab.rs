//! Collaborative Shared Play rooms: a per-room WebSocket hub with a permission
//! model (room-wide capability toggles + per-listener co-DJ roles), vote-skip
//! tallying, and cross-replica fan-out over Redis pub/sub.
//!
//! The backend never plays audio. It authenticates connections, enforces
//! permissions, keeps the live roster, and relays permitted listener commands to
//! the host, whose desktop app applies them and republishes authoritative state.
//!
//! Channels:
//!   `sp:ev:<code>`  — room events (state / queue / roster / caps / reaction /
//!                     skip / kicked), fanned to every socket on every replica.
//!   `sp:cmd:<code>` — permitted listener commands, delivered only to the replica
//!                     holding the host socket, then to the host.

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;
use std::time::{Duration, Instant};

use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::{Path as AxumPath, Query, State};
use axum::response::Response;
use futures_util::{SinkExt, StreamExt};
use serde_json::{json, Value};
use tokio::sync::{mpsc, Mutex};

use crate::{sess_key, AppState};

const ENVELOPE_V: u64 = 1;
const ROOM_TTL: Duration = Duration::from_secs(12 * 3600);
const MAX_ROSTER: usize = 300;
const REACTION_MIN_INTERVAL: Duration = Duration::from_millis(250);
const COMMAND_MIN_INTERVAL: Duration = Duration::from_millis(120);
const MAX_QUEUE_ADDS_PER_CONN: u32 = 200;

static NEXT_CONN_ID: AtomicU64 = AtomicU64::new(1);

// ── Roles & capabilities ────────────────────────────────────────────────────

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Role {
    Follower,
    CoDj,
    Host,
}

impl Role {
    fn rank(self) -> u8 {
        match self {
            Role::Follower => 0,
            Role::CoDj => 1,
            Role::Host => 2,
        }
    }
    fn as_str(self) -> &'static str {
        match self {
            Role::Follower => "follower",
            Role::CoDj => "codj",
            Role::Host => "host",
        }
    }
    fn parse(s: &str) -> Role {
        match s {
            "host" => Role::Host,
            "codj" => Role::CoDj,
            _ => Role::Follower,
        }
    }
}

/// Minimum role a capability string names.
fn cap_min_rank(cap: &str) -> u8 {
    match cap {
        "everyone" => 0,
        "codj" => 1,
        _ => 2, // "host" or anything unrecognised
    }
}

/// Whether a member holding `role` may perform an action gated by `cap`.
pub fn allows(cap: &str, role: Role) -> bool {
    role.rank() >= cap_min_rank(cap)
}

/// Room capabilities. Stored as a JSON object at `sp:sess:<code>:room:caps`.
#[derive(Clone, Debug)]
pub struct Caps {
    pub queue_add: String,
    pub queue_remove: String,
    pub queue_reorder: String,
    pub transport: String,
    pub vote_skip: bool,
    pub skip_votes_required: u32,
}

impl Default for Caps {
    fn default() -> Self {
        Caps {
            queue_add: "everyone".into(),
            queue_remove: "host".into(),
            queue_reorder: "host".into(),
            transport: "host".into(),
            vote_skip: true,
            skip_votes_required: 3,
        }
    }
}

impl Caps {
    fn from_json(v: &Value) -> Caps {
        let d = Caps::default();
        let s = |k: &str, dflt: &str| {
            v.get(k)
                .and_then(Value::as_str)
                .map(sanitize_cap)
                .unwrap_or_else(|| dflt.to_string())
        };
        Caps {
            queue_add: s("queueAdd", &d.queue_add),
            queue_remove: s("queueRemove", &d.queue_remove),
            queue_reorder: s("queueReorder", &d.queue_reorder),
            transport: s("transport", &d.transport),
            vote_skip: v.get("voteSkip").and_then(Value::as_bool).unwrap_or(d.vote_skip),
            skip_votes_required: v
                .get("skipVotesRequired")
                .and_then(Value::as_u64)
                .map(|n| n.clamp(1, 20) as u32)
                .unwrap_or(d.skip_votes_required),
        }
    }

    fn to_json(&self) -> Value {
        json!({
            "queueAdd": self.queue_add,
            "queueRemove": self.queue_remove,
            "queueReorder": self.queue_reorder,
            "transport": self.transport,
            "voteSkip": self.vote_skip,
            "skipVotesRequired": self.skip_votes_required,
        })
    }

    /// Apply a partial `caps.set` payload, keeping unknown/invalid fields.
    fn merged_with(&self, patch: &Value) -> Caps {
        let mut next = self.clone();
        if let Some(v) = patch.get("queueAdd").and_then(Value::as_str) {
            next.queue_add = sanitize_cap(v);
        }
        if let Some(v) = patch.get("queueRemove").and_then(Value::as_str) {
            next.queue_remove = sanitize_cap(v);
        }
        if let Some(v) = patch.get("queueReorder").and_then(Value::as_str) {
            next.queue_reorder = sanitize_cap(v);
        }
        if let Some(v) = patch.get("transport").and_then(Value::as_str) {
            next.transport = sanitize_cap(v);
        }
        if let Some(v) = patch.get("voteSkip").and_then(Value::as_bool) {
            next.vote_skip = v;
        }
        if let Some(v) = patch.get("skipVotesRequired").and_then(Value::as_u64) {
            next.skip_votes_required = v.clamp(1, 20) as u32;
        }
        next
    }
}

fn sanitize_cap(raw: &str) -> String {
    match raw {
        "everyone" | "codj" | "host" => raw.to_string(),
        _ => "host".to_string(),
    }
}

// ── In-process room registry (one per replica) ──────────────────────────────

#[derive(Default)]
struct RoomLocal {
    sockets: HashMap<u64, mpsc::UnboundedSender<String>>,
    host_conn: Option<u64>,
}

#[derive(Clone, Default)]
pub struct Rooms(Arc<Mutex<HashMap<String, RoomLocal>>>);

impl Rooms {
    async fn add(&self, code: &str, conn_id: u64, tx: mpsc::UnboundedSender<String>, is_host: bool) {
        let mut g = self.0.lock().await;
        let room = g.entry(code.to_string()).or_default();
        room.sockets.insert(conn_id, tx);
        if is_host {
            room.host_conn = Some(conn_id);
        }
    }

    async fn remove(&self, code: &str, conn_id: u64) {
        let mut g = self.0.lock().await;
        if let Some(room) = g.get_mut(code) {
            room.sockets.remove(&conn_id);
            if room.host_conn == Some(conn_id) {
                room.host_conn = None;
            }
            if room.sockets.is_empty() {
                g.remove(code);
            }
        }
    }

    async fn fanout_all(&self, code: &str, payload: &str) {
        let g = self.0.lock().await;
        if let Some(room) = g.get(code) {
            for tx in room.sockets.values() {
                let _ = tx.send(payload.to_string());
            }
        }
    }

    async fn fanout_host(&self, code: &str, payload: &str) {
        let g = self.0.lock().await;
        if let Some(room) = g.get(code) {
            if let Some(hc) = room.host_conn.and_then(|id| room.sockets.get(&id)) {
                let _ = hc.send(payload.to_string());
            }
        }
    }
}

/// One task per replica: subscribe to every room's event/command channels and
/// fan them out to this process's local sockets. Reconnects on stream drop.
pub fn spawn_replica_fanout(state: AppState) {
    tokio::spawn(async move {
        loop {
            let mut sub = match state
                .store
                .subscribe(vec!["sp:ev:*".to_string(), "sp:cmd:*".to_string()])
                .await
            {
                Ok(s) => s,
                Err(e) => {
                    eprintln!("collab: pub/sub subscribe failed: {e}");
                    tokio::time::sleep(Duration::from_secs(2)).await;
                    continue;
                }
            };
            while let Some((channel, payload)) = sub.recv().await {
                if let Some(code) = channel.strip_prefix("sp:ev:") {
                    state.rooms.fanout_all(code, &payload).await;
                } else if let Some(code) = channel.strip_prefix("sp:cmd:") {
                    state.rooms.fanout_host(code, &payload).await;
                }
            }
            eprintln!("collab: pub/sub stream ended — reconnecting");
        }
    });
}

// ── Redis-backed room state helpers ─────────────────────────────────────────

fn caps_key(code: &str) -> String {
    sess_key(code, "room:caps")
}
fn roster_key(code: &str) -> String {
    sess_key(code, "room:roster")
}
fn votes_key(code: &str) -> String {
    sess_key(code, "room:votes")
}
fn ev_channel(code: &str) -> String {
    format!("sp:ev:{code}")
}
fn cmd_channel(code: &str) -> String {
    format!("sp:cmd:{code}")
}

async fn load_caps(state: &AppState, code: &str) -> Caps {
    match state.store.get_json(&caps_key(code)).await {
        Ok(Some(v)) => Caps::from_json(&v),
        _ => Caps::default(),
    }
}

async fn save_caps(state: &AppState, code: &str, caps: &Caps) {
    let _ = state
        .store
        .put_json(&caps_key(code), &caps.to_json(), Some(ROOM_TTL))
        .await;
}

/// The roster as a list of `{clientId,name,role,isHost}` objects.
async fn roster_members(state: &AppState, code: &str) -> Vec<Value> {
    let raw = state.store.hgetall(&roster_key(code)).await.unwrap_or_default();
    let mut out: Vec<Value> = raw
        .into_iter()
        .filter_map(|(client_id, entry)| {
            let v: Value = serde_json::from_str(&entry).ok()?;
            let role = v.get("role").and_then(Value::as_str).unwrap_or("follower");
            Some(json!({
                "clientId": client_id,
                "name": v.get("name").and_then(Value::as_str).unwrap_or("Listener"),
                "role": role,
                "isHost": role == "host",
            }))
        })
        .collect();
    out.sort_by(|a, b| {
        a.get("name")
            .and_then(Value::as_str)
            .unwrap_or("")
            .cmp(b.get("name").and_then(Value::as_str).unwrap_or(""))
    });
    out
}

async fn roster_role(state: &AppState, code: &str, client_id: &str) -> Option<Role> {
    let raw = state.store.hgetall(&roster_key(code)).await.ok()?;
    let entry = raw.get(client_id)?;
    let v: Value = serde_json::from_str(entry).ok()?;
    Some(Role::parse(v.get("role").and_then(Value::as_str).unwrap_or("follower")))
}

async fn roster_upsert(state: &AppState, code: &str, client_id: &str, name: &str, role: Role) {
    let entry = json!({ "name": name, "role": role.as_str() });
    let _ = state
        .store
        .hset(&roster_key(code), client_id, &entry.to_string(), Some(ROOM_TTL))
        .await;
}

async fn roster_remove(state: &AppState, code: &str, client_id: &str) {
    let _ = state.store.hdel(&roster_key(code), client_id).await;
}

async fn publish_roster(state: &AppState, code: &str) {
    let members = roster_members(state, code).await;
    let envelope = json!({
        "v": ENVELOPE_V, "t": "roster",
        "count": members.len(),
        "members": members,
    });
    let _ = state.store.publish(&ev_channel(code), &envelope.to_string()).await;
}

async fn publish_caps(state: &AppState, code: &str, caps: &Caps) {
    let envelope = json!({ "v": ENVELOPE_V, "t": "caps", "caps": caps.to_json() });
    let _ = state.store.publish(&ev_channel(code), &envelope.to_string()).await;
}

pub async fn broadcast_state(
    state: &AppState,
    room_code: &str,
    playback: &Value,
) -> anyhow::Result<()> {
    let envelope = json!({ "v": ENVELOPE_V, "t": "state", "playback": playback });
    state
        .store
        .publish(&ev_channel(room_code), &envelope.to_string())
        .await
}

pub async fn broadcast_queue(state: &AppState, room_code: &str, queue: &Value) -> anyhow::Result<()> {
    let envelope = json!({ "v": ENVELOPE_V, "t": "queue", "queue": queue });
    state
        .store
        .publish(&ev_channel(room_code), &envelope.to_string())
        .await
}

// ── Connection handling ─────────────────────────────────────────────────────

pub async fn ws_handler(
    ws: WebSocketUpgrade,
    AxumPath(code): AxumPath<String>,
    Query(query): Query<HashMap<String, String>>,
    State(state): State<AppState>,
) -> Response {
    ws.on_upgrade(move |socket| handle_socket(socket, code, query, state))
}

struct ConnCtx {
    code: String,
    client_id: String,
    name: String,
    role: Role,
    caps: Caps,
    last_reaction: Instant,
    last_command: Instant,
    queue_adds: u32,
}

async fn handle_socket(
    socket: WebSocket,
    code_raw: String,
    query: HashMap<String, String>,
    state: AppState,
) {
    let (mut sender, mut receiver) = socket.split();
    let conn_id = NEXT_CONN_ID.fetch_add(1, Ordering::Relaxed);

    // Outbound pump: everything the client receives goes through this channel so
    // the fan-out task and command handlers never touch the socket directly.
    let (out_tx, mut out_rx) = mpsc::unbounded_channel::<String>();

    let code = match crate::clean_room_code(&code_raw) {
        Ok(c) => c,
        Err(_) => {
            let _ = sender
                .send(Message::Text(error_frame("bad_room", "Invalid room code")))
                .await;
            return;
        }
    };

    if !state
        .store
        .exists(&sess_key(&code, "manifest"))
        .await
        .unwrap_or(false)
    {
        let _ = sender
            .send(Message::Text(error_frame("no_room", "Room not found")))
            .await;
        return;
    }

    // ── Handshake: first frame must be `hello` ──────────────────────────────
    let hello = match receiver.next().await {
        Some(Ok(Message::Text(t))) => serde_json::from_str::<Value>(&t).unwrap_or(Value::Null),
        _ => {
            let _ = sender
                .send(Message::Text(error_frame("no_hello", "Expected hello")))
                .await;
            return;
        }
    };
    if hello.get("t").and_then(Value::as_str) != Some("hello") {
        let _ = sender
            .send(Message::Text(error_frame("no_hello", "Expected hello")))
            .await;
        return;
    }

    let requested_role = hello.get("role").and_then(Value::as_str).unwrap_or("listener");
    let client_id = crate::clean_client_id(hello.get("clientId").and_then(Value::as_str).unwrap_or(""))
        .or_else(|| query.get("clientId").and_then(|c| crate::clean_client_id(c)))
        .unwrap_or_else(|| format!("anon-{conn_id}"));
    let name = crate::clean_short_text(
        hello
            .get("name")
            .and_then(Value::as_str)
            .or_else(|| query.get("name").map(|s| s.as_str()))
            .unwrap_or(""),
        32,
    )
    .unwrap_or_else(|| "Listener".to_string());

    let is_host = requested_role == "host";
    if is_host {
        let key = hello.get("key").and_then(Value::as_str).unwrap_or("");
        if crate::validate_session_key(&state, &code, key).await.is_err() {
            let _ = sender
                .send(Message::Text(error_frame("bad_key", "Session key rejected")))
                .await;
            return;
        }
    }

    // Roster cap (never for the host).
    if !is_host {
        let current = roster_members(&state, &code).await;
        if current.len() >= MAX_ROSTER
            && !current
                .iter()
                .any(|m| m.get("clientId").and_then(Value::as_str) == Some(client_id.as_str()))
        {
            let _ = sender
                .send(Message::Text(error_frame("room_full", "Room is full")))
                .await;
            return;
        }
    }

    // Resolve effective role: host is host; otherwise keep an existing co-DJ
    // grant across reconnects, else follower.
    let role = if is_host {
        Role::Host
    } else {
        match roster_role(&state, &code, &client_id).await {
            Some(Role::CoDj) => Role::CoDj,
            _ => Role::Follower,
        }
    };

    let caps = load_caps(&state, &code).await;

    // Register.
    state.rooms.add(&code, conn_id, out_tx.clone(), is_host).await;
    roster_upsert(&state, &code, &client_id, &name, role).await;
    if is_host {
        let _ = state
            .store
            .put_json(
                &sess_key(&code, "room:host"),
                &json!({ "connId": conn_id }),
                Some(ROOM_TTL),
            )
            .await;
    }

    // Welcome frame with a full snapshot.
    let snapshot_state = crate::read_json_opt(&state, &sess_key(&code, "state")).await;
    let snapshot_queue = crate::read_json_opt(&state, &sess_key(&code, "queue")).await;
    let welcome = json!({
        "v": ENVELOPE_V,
        "t": "welcome",
        "you": { "clientId": client_id, "name": name, "role": role.as_str(), "isHost": is_host },
        "caps": caps.to_json(),
        "roster": roster_members(&state, &code).await,
        "state": snapshot_state,
        "queue": snapshot_queue,
    });
    let _ = out_tx.send(welcome.to_string());
    publish_roster(&state, &code).await;

    let mut ctx = ConnCtx {
        code: code.clone(),
        client_id: client_id.clone(),
        name: name.clone(),
        role,
        caps,
        last_reaction: Instant::now() - REACTION_MIN_INTERVAL,
        last_command: Instant::now() - COMMAND_MIN_INTERVAL,
        queue_adds: 0,
    };

    // Outbound pump task. Some room-channel frames are point-to-point (`kicked`,
    // `error`): they carry a target client id and are dropped for everyone else.
    let pump_client_id = client_id.clone();
    let mut pump = tokio::spawn(async move {
        while let Some(text) = out_rx.recv().await {
            if text.contains("\"kicked\"") || text.contains("toClientId") {
                if let Ok(v) = serde_json::from_str::<Value>(&text) {
                    let tag = v.get("t").and_then(Value::as_str);
                    if tag == Some("kicked") {
                        match v.get("clientId").and_then(Value::as_str) {
                            Some(target) if target == pump_client_id => {
                                let _ = sender
                                    .send(Message::Text(
                                        json!({ "v": ENVELOPE_V, "t": "kicked" }).to_string(),
                                    ))
                                    .await;
                                break;
                            }
                            _ => continue,
                        }
                    }
                    if let Some(target) = v.get("toClientId").and_then(Value::as_str) {
                        if target != pump_client_id {
                            continue;
                        }
                    }
                }
            }
            if sender.send(Message::Text(text)).await.is_err() {
                break;
            }
        }
        let _ = sender.close().await;
    });

    // Inbound loop.
    loop {
        tokio::select! {
            _ = &mut pump => break,
            msg = receiver.next() => {
                match msg {
                    Some(Ok(Message::Text(t))) => {
                        if let Ok(v) = serde_json::from_str::<Value>(&t) {
                            handle_client_msg(&state, &mut ctx, v).await;
                        }
                    }
                    Some(Ok(Message::Close(_))) | None => break,
                    Some(Ok(_)) => {}
                    Some(Err(_)) => break,
                }
            }
        }
    }

    // Teardown.
    pump.abort();
    state.rooms.remove(&code, conn_id).await;
    roster_remove(&state, &code, &client_id).await;
    if is_host {
        let _ = state.store.del(&sess_key(&code, "room:host")).await;
    }
    publish_roster(&state, &code).await;
}

async fn handle_client_msg(state: &AppState, ctx: &mut ConnCtx, v: Value) {
    let t = v.get("t").and_then(Value::as_str).unwrap_or("");
    // Keep this connection's view of the caps fresh (another socket may have
    // changed them).
    ctx.caps = load_caps(state, &ctx.code).await;

    match t {
        "ping" => {}

        "reaction" => {
            if ctx.last_reaction.elapsed() < REACTION_MIN_INTERVAL {
                return;
            }
            ctx.last_reaction = Instant::now();
            let kind = v
                .get("kind")
                .and_then(Value::as_str)
                .unwrap_or("spark")
                .chars()
                .take(16)
                .collect::<String>();
            let envelope = json!({
                "v": ENVELOPE_V, "t": "reaction",
                "kind": kind, "by": ctx.name,
            });
            let _ = state
                .store
                .publish(&ev_channel(&ctx.code), &envelope.to_string())
                .await;
        }

        "queue.add" => {
            if !allows(&ctx.caps.queue_add, ctx.role) {
                send_err(ctx, state, "forbidden", "Adding to the queue is not allowed").await;
                return;
            }
            if ctx.queue_adds >= MAX_QUEUE_ADDS_PER_CONN {
                return;
            }
            if !command_ok(ctx) {
                return;
            }
            let item = match v.get("item").cloned() {
                Some(raw) => match crate::normalize_queue_item(raw) {
                    Ok(item) => item,
                    Err(_) => {
                        send_err(ctx, state, "bad_item", "That link could not be added").await;
                        return;
                    }
                },
                None => return,
            };
            ctx.queue_adds += 1;
            relay_command(state, ctx, "queue.add", json!({ "item": item })).await;
        }

        "queue.remove" => {
            if !allows(&ctx.caps.queue_remove, ctx.role) {
                send_err(ctx, state, "forbidden", "Removing tracks is not allowed").await;
                return;
            }
            if !command_ok(ctx) {
                return;
            }
            let id = v.get("id").and_then(Value::as_str).unwrap_or("").to_string();
            if id.is_empty() {
                return;
            }
            relay_command(state, ctx, "queue.remove", json!({ "id": id })).await;
        }

        "queue.move" => {
            if !allows(&ctx.caps.queue_reorder, ctx.role) {
                send_err(ctx, state, "forbidden", "Reordering is not allowed").await;
                return;
            }
            if !command_ok(ctx) {
                return;
            }
            let id = v.get("id").and_then(Value::as_str).unwrap_or("").to_string();
            let to_index = v.get("toIndex").and_then(Value::as_i64).unwrap_or(-1);
            if id.is_empty() || to_index < 0 {
                return;
            }
            relay_command(state, ctx, "queue.move", json!({ "id": id, "toIndex": to_index })).await;
        }

        "transport" => {
            let action = v.get("action").and_then(Value::as_str).unwrap_or("");
            if !matches!(action, "play" | "pause" | "seek" | "next" | "prev") {
                return;
            }
            let allowed = allows(&ctx.caps.transport, ctx.role);
            if action == "next" && !allowed {
                // Fall through to a vote when direct transport is not granted.
                if command_ok(ctx) {
                    handle_skip_vote(state, ctx).await;
                }
                return;
            }
            if !allowed {
                send_err(ctx, state, "forbidden", "Playback control is not allowed").await;
                return;
            }
            if !command_ok(ctx) {
                return;
            }
            let mut payload = json!({ "action": action });
            if action == "seek" {
                payload["position"] = json!(v.get("position").and_then(Value::as_f64).unwrap_or(0.0));
            }
            relay_command(state, ctx, "transport", payload).await;
        }

        "skip.vote" => {
            if !command_ok(ctx) {
                return;
            }
            handle_skip_vote(state, ctx).await;
        }

        // ── Host-only ──────────────────────────────────────────────────────
        "state.publish" if ctx.role == Role::Host => {
            if let Some(playback) = v.get("playback").cloned() {
                let _ = crate::write_playback_state(state, &ctx.code, json!({ "playback": playback }))
                    .await;
            }
        }

        "queue.publish" if ctx.role == Role::Host => {
            if let Some(queue) = v.get("queue").cloned() {
                let _ = crate::write_json(state, &sess_key(&ctx.code, "queue"), &queue).await;
                let _ = broadcast_queue(state, &ctx.code, &queue).await;
            }
        }

        "caps.set" if ctx.role == Role::Host => {
            let patch = v.get("caps").cloned().unwrap_or(Value::Null);
            let next = ctx.caps.merged_with(&patch);
            save_caps(state, &ctx.code, &next).await;
            ctx.caps = next.clone();
            publish_caps(state, &ctx.code, &next).await;
        }

        "member.role" if ctx.role == Role::Host => {
            let target = v.get("clientId").and_then(Value::as_str).unwrap_or("");
            let new_role = match v.get("role").and_then(Value::as_str).unwrap_or("follower") {
                "codj" => Role::CoDj,
                _ => Role::Follower,
            };
            if target.is_empty() || target == ctx.client_id {
                return;
            }
            let raw = state.store.hgetall(&roster_key(&ctx.code)).await.unwrap_or_default();
            if let Some(entry) = raw.get(target) {
                let name = serde_json::from_str::<Value>(entry)
                    .ok()
                    .and_then(|v| v.get("name").and_then(Value::as_str).map(str::to_string))
                    .unwrap_or_else(|| "Listener".to_string());
                roster_upsert(state, &ctx.code, target, &name, new_role).await;
                publish_roster(state, &ctx.code).await;
            }
        }

        "member.kick" if ctx.role == Role::Host => {
            let target = v.get("clientId").and_then(Value::as_str).unwrap_or("");
            if target.is_empty() || target == ctx.client_id {
                return;
            }
            roster_remove(state, &ctx.code, target).await;
            let envelope = json!({ "v": ENVELOPE_V, "t": "kicked", "clientId": target });
            let _ = state
                .store
                .publish(&ev_channel(&ctx.code), &envelope.to_string())
                .await;
            publish_roster(state, &ctx.code).await;
        }

        _ => {}
    }
}

/// Rate-limit gate for listener commands.
fn command_ok(ctx: &mut ConnCtx) -> bool {
    if ctx.last_command.elapsed() < COMMAND_MIN_INTERVAL {
        return false;
    }
    ctx.last_command = Instant::now();
    true
}

async fn relay_command(state: &AppState, ctx: &ConnCtx, cmd: &str, payload: Value) {
    let envelope = json!({
        "v": ENVELOPE_V, "t": "command",
        "cmd": cmd,
        "payload": payload,
        "by": { "clientId": ctx.client_id, "name": ctx.name },
    });
    let _ = state
        .store
        .publish(&cmd_channel(&ctx.code), &envelope.to_string())
        .await;
}

async fn handle_skip_vote(state: &AppState, ctx: &ConnCtx) {
    if !ctx.caps.vote_skip {
        send_err(ctx, state, "no_vote_skip", "Vote-skip is off for this room").await;
        return;
    }
    let required = ctx.caps.skip_votes_required.max(1);
    let votes = state
        .store
        .sadd_count(&votes_key(&ctx.code), &ctx.client_id, Some(Duration::from_secs(600)))
        .await
        .unwrap_or(1) as u32;

    let progress = json!({ "v": ENVELOPE_V, "t": "skip", "votes": votes, "required": required });
    let _ = state
        .store
        .publish(&ev_channel(&ctx.code), &progress.to_string())
        .await;

    if votes >= required {
        let _ = state.store.del(&votes_key(&ctx.code)).await;
        let envelope = json!({
            "v": ENVELOPE_V, "t": "command", "cmd": "transport",
            "payload": { "action": "next" },
            "by": { "clientId": "room", "name": "Vote skip" },
        });
        let _ = state
            .store
            .publish(&cmd_channel(&ctx.code), &envelope.to_string())
            .await;
        let reset = json!({ "v": ENVELOPE_V, "t": "skip", "votes": 0, "required": required });
        let _ = state
            .store
            .publish(&ev_channel(&ctx.code), &reset.to_string())
            .await;
    }
}

async fn send_err(ctx: &ConnCtx, state: &AppState, code: &str, message: &str) {
    // Errors are point-to-point: publish on the room channel tagged with the
    // recipient; their pump keeps it, everyone else's drops it.
    let envelope = json!({
        "v": ENVELOPE_V, "t": "error",
        "code": code, "message": message,
        "toClientId": ctx.client_id,
    });
    let _ = state
        .store
        .publish(&ev_channel(&ctx.code), &envelope.to_string())
        .await;
}

fn error_frame(code: &str, message: &str) -> String {
    json!({ "v": ENVELOPE_V, "t": "error", "code": code, "message": message }).to_string()
}

#[cfg(test)]
mod collab_tests {
    use super::*;

    #[test]
    fn permission_matrix() {
        // everyone
        assert!(allows("everyone", Role::Follower));
        assert!(allows("everyone", Role::CoDj));
        assert!(allows("everyone", Role::Host));
        // codj
        assert!(!allows("codj", Role::Follower));
        assert!(allows("codj", Role::CoDj));
        assert!(allows("codj", Role::Host));
        // host
        assert!(!allows("host", Role::Follower));
        assert!(!allows("host", Role::CoDj));
        assert!(allows("host", Role::Host));
        // unknown cap string is treated as host-only
        assert!(!allows("garbage", Role::CoDj));
        assert!(allows("garbage", Role::Host));
    }

    #[test]
    fn caps_default_and_roundtrip() {
        let d = Caps::default();
        assert_eq!(d.queue_add, "everyone");
        assert_eq!(d.transport, "host");
        assert!(d.vote_skip);
        assert_eq!(d.skip_votes_required, 3);

        let j = d.to_json();
        let back = Caps::from_json(&j);
        assert_eq!(back.queue_add, "everyone");
        assert_eq!(back.skip_votes_required, 3);
    }

    #[test]
    fn caps_merge_keeps_untouched_fields_and_clamps() {
        let base = Caps::default();
        let merged = base.merged_with(&json!({ "transport": "everyone", "skipVotesRequired": 99 }));
        assert_eq!(merged.transport, "everyone");
        assert_eq!(merged.queue_add, "everyone"); // untouched
        assert_eq!(merged.skip_votes_required, 20); // clamped
        // invalid cap string falls back to host
        let merged2 = base.merged_with(&json!({ "queueAdd": "bogus" }));
        assert_eq!(merged2.queue_add, "host");
    }

    #[tokio::test]
    async fn vote_skip_tally_reaches_threshold_once_then_resets() {
        std::env::remove_var("REDIS_URL");
        let store = crate::store::Store::from_env().await.unwrap();

        // 2 distinct voters, threshold 2.
        let key = "sp:sess:ROOM:room:votes";
        assert_eq!(store.sadd_count(key, "alice", None).await.unwrap(), 1);
        assert_eq!(store.sadd_count(key, "alice", None).await.unwrap(), 1); // dup ignored
        assert_eq!(store.sadd_count(key, "bob", None).await.unwrap(), 2);

        store.del(key).await.unwrap();
        assert_eq!(store.sadd_count(key, "carol", None).await.unwrap(), 1);
    }

    #[tokio::test]
    async fn roster_upsert_and_role_lookup() {
        std::env::remove_var("REDIS_URL");
        let store = crate::store::Store::from_env().await.unwrap();
        let state = test_state(store);

        roster_upsert(&state, "ROOMX", "c1", "Alice", Role::Follower).await;
        roster_upsert(&state, "ROOMX", "c2", "Bob", Role::CoDj).await;

        let members = roster_members(&state, "ROOMX").await;
        assert_eq!(members.len(), 2);
        assert_eq!(members[0].get("name").unwrap(), "Alice");

        assert_eq!(roster_role(&state, "ROOMX", "c2").await, Some(Role::CoDj));
        assert_eq!(roster_role(&state, "ROOMX", "c1").await, Some(Role::Follower));

        roster_remove(&state, "ROOMX", "c1").await;
        assert_eq!(roster_members(&state, "ROOMX").await.len(), 1);
    }

    fn test_state(store: crate::store::Store) -> AppState {
        AppState {
            store: std::sync::Arc::new(store),
            rooms: Rooms::default(),
            web_share_root: std::sync::Arc::new(std::path::PathBuf::from("web-share")),
            public_base_url: None,
            stripe_secret_key: None,
            stripe_webhook_secret: None,
            stripe_connect_client_id: None,
            stripe_publishable_key: None,
        }
    }
}
