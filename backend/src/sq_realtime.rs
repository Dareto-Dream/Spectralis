//! Streamer Queue realtime: a push channel so the dashboard, the OBS overlay and the Discord bot stop
//! polling. Same handshake as Shared Play (see `protocol.rs`), different room kind.
//!
//! The socket carries *change notifications*, never private data: every frame is safe to show any
//! viewer. Clients that need more (the owner's dashboard wants the submission list) react to
//! `sq.changed` by fetching over REST with their own credentials, so permissions stay in one place.
//!
//! Frames from the server:
//!   `welcome`        snapshot of the public status plus what was negotiated
//!   `sq.changed`     the room changed (queue, now playing, open/closed); carries the new public status
//!   `sq.submission`  a webhook/chat-bot submission was accepted (only with the `sq.webhooks` feature)
//!   `pong`           reply to a client `ping`
//!
//! Channel: `sq:ev:<roomId>`, fanned out to every replica's local sockets like Shared Play's `sp:ev:`.

use std::sync::atomic::{AtomicU64, Ordering};

use axum::extract::ws::{CloseFrame, Message, WebSocket, WebSocketUpgrade};
use axum::extract::{Path as AxumPath, State};
use axum::response::Response;
use futures_util::{SinkExt, StreamExt};
use serde_json::{json, Value};
use tokio::sync::mpsc;

use crate::protocol;
use crate::AppState;

static NEXT_CONN_ID: AtomicU64 = AtomicU64::new(1);

pub fn ev_channel(room_id: &str) -> String {
    format!("sq:ev:{room_id}")
}

/// What anyone may know about a room right now.
pub fn public_status(room: &Value) -> Value {
    let now_playing = room
        .get("nowPlayingId")
        .and_then(Value::as_str)
        .and_then(|id| {
            room.get("submissions")
                .and_then(Value::as_array)
                .and_then(|subs| subs.iter().find(|s| s.get("id").and_then(Value::as_str) == Some(id)))
        })
        .map(|s| json!({ "title": s.get("title"), "artist": s.get("artist") }));

    json!({
        "roomId": room.get("roomId"),
        "enabled": room.get("enabled").and_then(Value::as_bool).unwrap_or(false),
        "acceptingSubmissions": room.get("acceptingSubmissions").and_then(Value::as_bool).unwrap_or(true),
        "queueLength": crate::sq_ordered_queue(room).len(),
        "nowPlaying": now_playing,
    })
}

pub fn changed_frame(room: &Value) -> Value {
    json!({
        "v": protocol::PROTOCOL_VERSION,
        "t": "sq.changed",
        "rev": room.get("updatedAtUtc"),
        "status": public_status(room),
    })
}

/// Best effort: a failed notification must never fail the write that caused it.
pub async fn notify_changed(state: &AppState, room_id: &str, room: &Value) {
    let _ = state
        .store
        .publish(&ev_channel(room_id), &changed_frame(room).to_string())
        .await;
}

pub async fn notify_submission(
    state: &AppState,
    room_id: &str,
    source: &str,
    display_name: &str,
    submission_id: &str,
    status: &str,
) {
    let frame = json!({
        "v": protocol::PROTOCOL_VERSION,
        "t": "sq.submission",
        "source": source,
        "displayName": display_name,
        "submissionId": submission_id,
        "status": status,
    });
    let _ = state.store.publish(&ev_channel(room_id), &frame.to_string()).await;
}

pub async fn ws_handler(
    ws: WebSocketUpgrade,
    AxumPath(room_id): AxumPath<String>,
    State(state): State<AppState>,
) -> Response {
    ws.on_upgrade(move |socket| handle_socket(socket, room_id, state))
}

fn error_frame(code: &str, message: &str) -> String {
    json!({ "v": protocol::PROTOCOL_VERSION, "t": "error", "code": code, "message": message }).to_string()
}

async fn handle_socket(socket: WebSocket, room_id_raw: String, state: AppState) {
    let (mut sender, mut receiver) = socket.split();
    let conn_id = NEXT_CONN_ID.fetch_add(1, Ordering::Relaxed);

    let room = match crate::read_sq_room_file(&state, &room_id_raw).await {
        Ok(room) => room,
        Err(_) => {
            let _ = sender
                .send(Message::Text(error_frame("no_room", "Streamer queue room not found")))
                .await;
            return;
        }
    };
    let room_id = room
        .get("roomId")
        .and_then(Value::as_str)
        .unwrap_or(&room_id_raw)
        .to_string();

    // Handshake: the first frame must be a `hello` from a client new enough to talk to us.
    let hello_frame = match receiver.next().await {
        Some(Ok(Message::Text(t))) => serde_json::from_str::<Value>(&t).unwrap_or(Value::Null),
        _ => return,
    };
    let hello = match protocol::parse_hello(&hello_frame) {
        Ok(h) => h,
        Err(protocol::HelloError::NotHello) => {
            let _ = sender
                .send(Message::Text(error_frame("no_hello", "Expected hello")))
                .await;
            return;
        }
        Err(protocol::HelloError::UpdateRequired { client_proto }) => {
            let _ = sender
                .send(Message::Text(protocol::update_required_frame(client_proto)))
                .await;
            let _ = sender
                .send(Message::Close(Some(CloseFrame {
                    code: protocol::CLOSE_UPDATE_REQUIRED,
                    reason: "update required".into(),
                })))
                .await;
            return;
        }
    };

    let features = protocol::negotiate(&hello.features);
    let wants_submissions = features.contains(&"sq.webhooks");

    let (out_tx, mut out_rx) = mpsc::unbounded_channel::<String>();
    state.sq_rooms.add(&room_id, conn_id, out_tx.clone(), false).await;

    let mut welcome = json!({
        "v": protocol::PROTOCOL_VERSION,
        "t": "welcome",
        "roomKind": "streamer-queue",
        "status": public_status(&room),
    });
    if let (Some(frame), Some(agreed)) = (
        welcome.as_object_mut(),
        protocol::welcome_fields(&hello).as_object().cloned(),
    ) {
        frame.extend(agreed);
    }
    let _ = out_tx.send(welcome.to_string());

    // Everything the client receives goes through the channel, so fan-out and replies share one writer.
    let mut pump = tokio::spawn(async move {
        while let Some(text) = out_rx.recv().await {
            if !wants_submissions && text.contains("\"sq.submission\"") {
                continue;
            }
            if sender.send(Message::Text(text)).await.is_err() {
                break;
            }
        }
    });

    loop {
        tokio::select! {
            _ = &mut pump => break,
            incoming = receiver.next() => match incoming {
                Some(Ok(Message::Text(t))) => {
                    let frame = serde_json::from_str::<Value>(&t).unwrap_or(Value::Null);
                    if frame.get("t").and_then(Value::as_str) == Some("ping") {
                        let _ = out_tx.send(json!({ "v": protocol::PROTOCOL_VERSION, "t": "pong" }).to_string());
                    }
                }
                Some(Ok(Message::Close(_))) | None | Some(Err(_)) => break,
                _ => {}
            },
        }
    }

    pump.abort();
    state.sq_rooms.remove(&room_id, conn_id).await;
}

#[cfg(test)]
mod tests {
    use super::*;

    fn room() -> Value {
        json!({
            "roomId": "r1",
            "enabled": true,
            "acceptingSubmissions": false,
            "updatedAtUtc": "2026-10-03T12:00:00Z",
            "nowPlayingId": "n",
            "submissions": [
                { "id": "n", "status": "playing", "title": "Song", "artist": "Band", "_fp": { "cookie": "secret" } },
                { "id": "a", "status": "queued", "tier": "normal", "submittedAtUtc": "2026-10-03T11:00:00Z", "tierChangedAtUtc": "2026-10-03T11:00:00Z" },
                { "id": "b", "status": "rejected" }
            ],
        })
    }

    #[test]
    fn public_status_reports_queue_and_now_playing() {
        let status = public_status(&room());

        assert_eq!(status["roomId"], "r1");
        assert_eq!(status["enabled"], true);
        assert_eq!(status["acceptingSubmissions"], false);
        assert_eq!(status["queueLength"], 1); // only the queued one counts
        assert_eq!(status["nowPlaying"]["title"], "Song");
        assert_eq!(status["nowPlaying"]["artist"], "Band");
    }

    #[test]
    fn nothing_playing_is_null() {
        let mut r = room();
        r["nowPlayingId"] = Value::Null;

        assert!(public_status(&r)["nowPlaying"].is_null());
    }

    #[test]
    fn notifications_never_carry_private_data() {
        let text = changed_frame(&room()).to_string();

        assert!(!text.contains("secret"));
        assert!(!text.contains("_fp"));
        assert!(!text.contains("ownerToken"));
        assert!(text.contains("\"sq.changed\""));
        assert!(text.contains("2026-10-03T12:00:00Z"));
    }

    #[test]
    fn channel_name_is_per_room() {
        assert_eq!(ev_channel("abc"), "sq:ev:abc");
    }
}
