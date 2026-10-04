//! Spectralis realtime protocol, version 2.
//!
//! One websocket protocol for every room kind (Shared Play, Streamer Queue) and every client
//! (desktop app, Discord bot, OBS overlay, web player). The first client frame is a `hello`
//! carrying the protocol version it speaks and the optional features it understands; the server
//! answers `welcome` with the protocol version and the features both sides share. A client that
//! doesn't say it speaks at least `MIN_PROTOCOL` gets one `error` frame with code
//! `update_required` and the socket is closed with `CLOSE_UPDATE_REQUIRED`, so an old build shows
//! "please update" instead of silently misbehaving.
//!
//! Frame envelope (unchanged shape, new version): `{ "v": 2, "t": "<type>", ... }`.
//!
//! Hello:   `{ "v": 2, "t": "hello", "proto": 2, "client": { "kind": "app", "version": "6.2.0" },
//!             "features": ["queue.v2", ...], ...room-specific fields }`
//! Welcome: `{ "v": 2, "t": "welcome", "proto": 2, "features": [<intersection>], ... }`
//!
//! Features are additive: a feature never changes the meaning of existing frames, it only adds new
//! ones, so a client can ignore any feature it didn't ask for. Anything that *would* change existing
//! frames bumps `PROTOCOL_VERSION` instead.

use serde_json::{json, Value};

/// The version this server speaks.
pub const PROTOCOL_VERSION: u64 = 2;

/// Oldest client protocol the server still accepts. Raise this to cut off a generation of clients.
pub const MIN_PROTOCOL: u64 = 2;

/// WebSocket close code sent after `update_required` (private-use range, mirrors HTTP 426).
pub const CLOSE_UPDATE_REQUIRED: u16 = 4426;

/// Everything this server can do beyond the baseline. A feature is active for a connection when
/// the client listed it in `hello.features` and it appears here.
///
/// Only list a feature once something real sits behind it. `roster`, `reactions` and `commands`
/// describe frames the Shared Play hub already sends to everyone, so announcing them is purely
/// informational today; `sq.webhooks` is the first feature the server actually gates on.
pub const SERVER_FEATURES: &[&str] = &[
    "roster",      // live listener list + presence (Shared Play)
    "reactions",   // reaction frames (Shared Play)
    "commands",    // permitted listener commands relayed to the host (Shared Play)
    "sq.webhooks", // `sq.submission` frames for chat-bot/webhook submissions (Streamer Queue)
];

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ClientHello {
    pub proto: u64,
    pub kind: String,
    pub version: String,
    pub features: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum HelloError {
    /// The client didn't say it speaks `MIN_PROTOCOL` or newer (or didn't say at all).
    UpdateRequired { client_proto: Option<u64> },
    /// The first frame wasn't a hello.
    NotHello,
}

/// Reads the protocol-level fields of a `hello` frame. Room-specific fields (role, clientId, key…)
/// are the caller's business.
pub fn parse_hello(frame: &Value) -> Result<ClientHello, HelloError> {
    if frame.get("t").and_then(Value::as_str) != Some("hello") {
        return Err(HelloError::NotHello);
    }

    let proto = frame.get("proto").and_then(Value::as_u64);
    match proto {
        Some(p) if p >= MIN_PROTOCOL => {}
        other => return Err(HelloError::UpdateRequired { client_proto: other }),
    }

    let client = frame.get("client");
    let text = |key: &str| {
        client
            .and_then(|c| c.get(key))
            .and_then(Value::as_str)
            .map(|s| s.chars().filter(|c| !c.is_control()).take(32).collect::<String>())
            .filter(|s| !s.is_empty())
    };

    Ok(ClientHello {
        proto: proto.unwrap_or(MIN_PROTOCOL),
        kind: text("kind").unwrap_or_else(|| "other".to_string()),
        version: text("version").unwrap_or_default(),
        features: frame
            .get("features")
            .and_then(Value::as_array)
            .map(|a| a.iter().filter_map(Value::as_str).map(str::to_string).collect())
            .unwrap_or_default(),
    })
}

/// Features active for a connection: what the client asked for that the server also offers, in the
/// server's order (stable, so welcome frames are deterministic).
pub fn negotiate(client_features: &[String]) -> Vec<&'static str> {
    SERVER_FEATURES
        .iter()
        .copied()
        .filter(|f| client_features.iter().any(|c| c == f))
        .collect()
}

/// The version the connection will actually use: the lower of what both sides speak.
pub fn negotiated_version(client_proto: u64) -> u64 {
    client_proto.min(PROTOCOL_VERSION)
}

/// The single frame an outdated client receives before the socket closes.
pub fn update_required_frame(client_proto: Option<u64>) -> String {
    json!({
        "v": PROTOCOL_VERSION,
        "t": "error",
        "code": "update_required",
        "message": format!(
            "This version of Spectralis is too old to join. Update to the latest version to continue (needs protocol {MIN_PROTOCOL} or newer)."
        ),
        "minProto": MIN_PROTOCOL,
        "proto": PROTOCOL_VERSION,
        "yourProto": client_proto,
    })
    .to_string()
}

/// Fields every `welcome` carries so clients can confirm what was agreed.
pub fn welcome_fields(hello: &ClientHello) -> Value {
    json!({
        "proto": negotiated_version(hello.proto),
        "features": negotiate(&hello.features),
        "server": { "protocol": PROTOCOL_VERSION, "minProtocol": MIN_PROTOCOL },
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn hello(extra: Value) -> Value {
        let mut base = json!({ "v": 2, "t": "hello" });
        base.as_object_mut()
            .unwrap()
            .extend(extra.as_object().unwrap().clone());
        base
    }

    #[test]
    fn current_clients_are_accepted_with_their_details() {
        let frame = hello(json!({
            "proto": 2,
            "client": { "kind": "obs", "version": "1.4.0" },
            "features": ["sq.webhooks", "roster"],
        }));

        let parsed = parse_hello(&frame).unwrap();

        assert_eq!(parsed.proto, 2);
        assert_eq!(parsed.kind, "obs");
        assert_eq!(parsed.version, "1.4.0");
        assert_eq!(parsed.features, vec!["sq.webhooks", "roster"]);
    }

    #[test]
    fn a_hello_without_proto_is_an_old_client() {
        // This is exactly what a protocol-1 app sends.
        let frame = json!({ "v": 1, "t": "hello", "role": "listener", "clientId": "abc" });

        assert_eq!(
            parse_hello(&frame),
            Err(HelloError::UpdateRequired { client_proto: None })
        );
    }

    #[test]
    fn an_explicit_old_proto_is_rejected_with_its_version() {
        let frame = hello(json!({ "proto": 1 }));

        assert_eq!(
            parse_hello(&frame),
            Err(HelloError::UpdateRequired { client_proto: Some(1) })
        );
    }

    #[test]
    fn a_newer_client_is_accepted_and_runs_at_our_version() {
        let parsed = parse_hello(&hello(json!({ "proto": 7 }))).unwrap();

        assert_eq!(negotiated_version(parsed.proto), PROTOCOL_VERSION);
    }

    #[test]
    fn non_hello_frames_are_not_hellos() {
        assert_eq!(parse_hello(&json!({ "t": "ping" })), Err(HelloError::NotHello));
        assert_eq!(parse_hello(&json!({})), Err(HelloError::NotHello));
    }

    #[test]
    fn negotiation_is_the_intersection_in_server_order() {
        let asked: Vec<String> = ["sq.webhooks", "bogus", "roster", "queue.v2"]
            .iter()
            .map(|s| s.to_string())
            .collect();

        // "queue.v2" and "bogus" aren't offered, so they drop out; order follows the server's list.
        assert_eq!(negotiate(&asked), vec!["roster", "sq.webhooks"]);
    }

    #[test]
    fn no_features_asked_means_baseline_only() {
        assert!(negotiate(&[]).is_empty());
    }

    #[test]
    fn client_text_fields_are_sanitised() {
        let frame = hello(json!({
            "proto": 2,
            "client": { "kind": "app\u{0007}\n", "version": "x".repeat(100) },
        }));

        let parsed = parse_hello(&frame).unwrap();

        assert_eq!(parsed.kind, "app");
        assert_eq!(parsed.version.len(), 32);
    }

    #[test]
    fn update_required_frame_tells_the_client_what_to_do() {
        let frame: Value = serde_json::from_str(&update_required_frame(None)).unwrap();

        assert_eq!(frame["t"], "error");
        assert_eq!(frame["code"], "update_required");
        assert_eq!(frame["minProto"], MIN_PROTOCOL);
        assert!(frame["message"].as_str().unwrap().contains("Update"));
        assert!(frame["yourProto"].is_null());
    }

    #[test]
    fn welcome_fields_report_what_was_agreed() {
        let parsed = parse_hello(&hello(json!({ "proto": 2, "features": ["roster", "nope"] }))).unwrap();

        let fields = welcome_fields(&parsed);

        assert_eq!(fields["proto"], 2);
        assert_eq!(fields["features"], json!(["roster"]));
        assert_eq!(fields["server"]["minProtocol"], MIN_PROTOCOL);
    }
}
