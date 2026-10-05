//! The room model.
//!
//! Shared Play has two kinds of room:
//!  - **temporary**: made by an app that isn't signed in to Ward. It is a session, private, reachable only
//!    through the invite link, and gone when the host stops or it expires. It is never listed anywhere.
//!  - **permanent**: tied to one Ward account (at most one each). It can be public, which lists it in the room
//!    finder, or private. What used to be called a channel is just this.
//!
//! Streamer-queue rooms are a separate product and don't count toward the one-room limit.

use super::*;

/// The permanent Shared Play room owned by this account, if it has one. When an account somehow ended up with
/// more than one, the oldest wins so the answer is stable.
pub async fn room_of(state: &AppState, subject: &str) -> Result<Option<Value>, AppError> {
    let keys = state.store.scan_keys("sp:chan:*").await.map_err(AppError::internal)?;
    let mut found: Option<Value> = None;
    for key in keys {
        let Some(room) = read_json_opt(state, &key).await else { continue };
        if !player::owns(&room, subject) || room.get("roomKind").and_then(Value::as_str) == Some("streamer_queue") {
            continue;
        }
        let created = |r: &Value| r.get("createdAtUtc").and_then(Value::as_str).unwrap_or("").to_string();
        if found.as_ref().is_none_or(|current| created(&room) < created(current)) {
            found = Some(room);
        }
    }
    Ok(found)
}

pub fn already_has_room(room: &Value) -> AppError {
    let id = room.get("channelId").and_then(Value::as_str).unwrap_or("");
    AppError {
        status: StatusCode::CONFLICT,
        message: format!("You already have a room ({id}). Update it instead of making another."),
    }
}

/// `GET /player/v1/me/room`: the signed-in account's permanent room, or `null` if it hasn't made one yet.
pub async fn my_room(State(state): State<AppState>, headers: HeaderMap) -> Result<Json<Value>, AppError> {
    let subject = ward_subject(&state, &headers).await?;
    let room = room_of(&state, &subject).await?.map(|r| player::room_card(&r));
    Ok(Json(json!({ "room": room })))
}

const WIPE_PHRASE: &str = "wipe all rooms";

/// `POST /spectralis/v1/admin/rooms/wipe`: deletes every permanent room, its members and its artwork, and hands
/// the deleted records back so they can be kept. Admin token and a typed phrase are both required, and nothing
/// is deleted unless everything could first be read. The backup holds owner tokens, so treat it as a secret.
pub async fn wipe_rooms(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(body): Json<Value>,
) -> Result<Json<Value>, AppError> {
    content::require_admin(&headers)?;
    if body.get("confirm").and_then(Value::as_str) != Some(WIPE_PHRASE) {
        return Err(AppError::bad_request(&format!("Send {{\"confirm\":\"{WIPE_PHRASE}\"}} to wipe every room.")));
    }

    let room_keys = state.store.scan_keys("sp:chan:*").await.map_err(AppError::internal)?;
    let member_keys = state.store.scan_keys("player:member:*").await.map_err(AppError::internal)?;
    let mut rooms = Vec::new();
    for key in &room_keys {
        if let Some(room) = state.store.get_json(key).await.map_err(AppError::internal)? {
            rooms.push(room);
        }
    }
    let mut members = Vec::new();
    for key in &member_keys {
        if let Some(member) = state.store.get_json(key).await.map_err(AppError::internal)? {
            members.push(json!({ "key": key, "member": member }));
        }
    }

    // Everything is in hand; only now start deleting.
    for room in &rooms {
        if let Some(id) = room.get("channelId").and_then(Value::as_str) {
            for kind in ["banner", "icon", "social"] {
                state.store.delete_blob(&format!("room-images/{id}/{kind}")).await.map_err(AppError::internal)?;
            }
        }
    }
    for key in room_keys.iter().chain(member_keys.iter()) {
        state.store.del(key).await.map_err(AppError::internal)?;
    }

    Ok(Json(json!({
        "ok": true,
        "deleted": { "rooms": rooms.len(), "members": members.len() },
        "backup": { "rooms": rooms, "members": members },
    })))
}

#[cfg(test)]
mod tests {
    use super::*;

    const TOKEN: &str = "test-admin-token-0123456789abcdef";

    async fn state() -> AppState {
        std::env::remove_var("REDIS_URL");
        std::env::set_var("SPECTRALIS_ADMIN_TOKEN", TOKEN);
        AppState {
            store: Arc::new(store::Store::from_env().await.unwrap()),
            rooms: collab::Rooms::default(),
            sq_rooms: collab::Rooms::default(),
            web_share_root: Arc::new(PathBuf::from("web-share")),
            public_base_url: Some("https://api.test".into()),
            stripe_secret_key: None,
            stripe_webhook_secret: None,
            stripe_connect_client_id: None,
            stripe_publishable_key: None,
            ward_issuer: Arc::new("http://127.0.0.1:1".into()),
        }
    }

    /// Headers for an app that is signed in as `subject`.
    async fn signed_in(state: &AppState, subject: &str) -> HeaderMap {
        let token = format!("sp_app_{subject}");
        let hash = bytes_to_hex(&Sha256::digest(token.as_bytes()));
        state.store.put_json(&format!("player:device:{hash}"), &json!({ "sub": subject, "name": subject }), None).await.unwrap();
        let mut h = HeaderMap::new();
        h.insert(header::AUTHORIZATION, HeaderValue::from_str(&format!("Bearer {token}")).unwrap());
        h
    }

    fn admin() -> HeaderMap {
        let mut h = HeaderMap::new();
        h.insert(header::AUTHORIZATION, HeaderValue::from_str(&format!("Bearer {TOKEN}")).unwrap());
        h
    }

    async fn json_of(response: Response) -> Value {
        serde_json::from_slice(&axum::body::to_bytes(response.into_body(), 1 << 20).await.unwrap()).unwrap()
    }

    async fn make_room(state: &AppState, who: &HeaderMap, extra: Value) -> Result<Value, AppError> {
        let mut body = json!({ "name": "My room", "kind": "channel", "security": "anyone", "tags": [], "isPublic": false });
        for (k, v) in extra.as_object().unwrap() {
            body[k] = v.clone();
        }
        let response = create_public_room(State(state.clone()), who.clone(), Json(body)).await?.into_response();
        Ok(json_of(response).await)
    }

    #[tokio::test]
    async fn an_account_can_make_one_permanent_room_and_a_second_is_refused() {
        let state = state().await;
        let alice = signed_in(&state, "alice").await;
        let first = make_room(&state, &alice, json!({})).await.unwrap();
        assert!(first["id"].as_str().unwrap().starts_with("room-"));

        let again = make_room(&state, &alice, json!({ "name": "Another" })).await.unwrap_err();
        assert_eq!(again.status, StatusCode::CONFLICT);
        assert!(again.message.contains(first["id"].as_str().unwrap()), "the refusal says which room they already have");
    }

    #[tokio::test]
    async fn the_limit_is_per_account_and_does_not_count_streamer_queue_rooms() {
        let state = state().await;
        let alice = signed_in(&state, "alice").await;
        let bob = signed_in(&state, "bob").await;

        make_room(&state, &alice, json!({})).await.unwrap();
        assert!(make_room(&state, &bob, json!({})).await.is_ok(), "another account is unaffected");
        assert!(make_room(&state, &alice, json!({ "kind": "streamer_queue", "name": "Queue" })).await.is_ok(), "a queue is a different product");
        let still = make_room(&state, &alice, json!({ "name": "Third" })).await.unwrap_err();
        assert_eq!(still.status, StatusCode::CONFLICT, "queues don't free up the slot either");
    }

    #[tokio::test]
    async fn my_room_is_null_until_one_exists_and_then_is_the_accounts_own() {
        let state = state().await;
        let alice = signed_in(&state, "alice").await;
        let bob = signed_in(&state, "bob").await;

        assert_eq!(json_of(my_room(State(state.clone()), alice.clone()).await.unwrap().into_response()).await["room"], Value::Null);
        let made = make_room(&state, &alice, json!({ "name": "Alice's room" })).await.unwrap();
        let mine = json_of(my_room(State(state.clone()), alice.clone()).await.unwrap().into_response()).await;
        assert_eq!(mine["room"]["id"], made["id"]);
        assert_eq!(mine["room"]["name"], "Alice's room");
        assert_eq!(json_of(my_room(State(state.clone()), bob).await.unwrap().into_response()).await["room"], Value::Null, "never someone else's");
        assert_eq!(my_room(State(state.clone()), HeaderMap::new()).await.unwrap_err().status, StatusCode::UNAUTHORIZED);
    }

    #[tokio::test]
    async fn only_public_permanent_rooms_appear_in_the_room_finder() {
        let state = state().await;
        let alice = signed_in(&state, "alice").await;
        let bob = signed_in(&state, "bob").await;
        let private = make_room(&state, &alice, json!({ "name": "Private one" })).await.unwrap();
        let public = make_room(&state, &bob, json!({ "name": "Public one", "isPublic": true })).await.unwrap();

        let listed = json_of(list_public_rooms(State(state.clone())).await.unwrap().into_response()).await;
        let ids: Vec<_> = listed["rooms"].as_array().unwrap().iter().map(|r| r["id"].as_str().unwrap().to_string()).collect();
        assert!(ids.contains(&public["id"].as_str().unwrap().to_string()));
        assert!(!ids.contains(&private["id"].as_str().unwrap().to_string()));

        update_public_room_profile(State(state.clone()), alice.clone(), AxumPath(private["id"].as_str().unwrap().to_string()), Json(json!({ "isPublic": true }))).await.unwrap();
        let after = json_of(list_public_rooms(State(state.clone())).await.unwrap().into_response()).await;
        assert!(after["rooms"].as_array().unwrap().iter().any(|r| r["id"] == private["id"]), "going public lists it");
    }

    #[tokio::test]
    async fn a_session_from_an_app_that_is_not_signed_in_is_temporary_and_unlisted() {
        let state = state().await;
        let response = create_session(State(state.clone()), HeaderMap::new(), Json(json!({ "track": { "title": "x" } }))).await.unwrap().into_response();
        let created = json_of(response).await;
        assert_eq!(created["temporary"], true);
        let manifest = read_json(&state, &sess_key(created["roomCode"].as_str().unwrap(), "manifest")).await.unwrap();
        assert_eq!(manifest["temporary"], true);
        assert_eq!(manifest["wardOwnerId"], Value::Null);

        let listed = json_of(list_public_rooms(State(state.clone())).await.unwrap().into_response()).await;
        assert!(listed["rooms"].as_array().unwrap().iter().all(|r| r["roomCode"] != created["roomCode"]), "a temporary room is never in the finder");
    }

    #[tokio::test]
    async fn a_session_from_a_signed_in_app_belongs_to_the_account_and_is_not_temporary() {
        let state = state().await;
        let alice = signed_in(&state, "alice").await;
        let created = json_of(create_session(State(state.clone()), alice, Json(json!({}))).await.unwrap().into_response()).await;
        assert_eq!(created["temporary"], false);
        let manifest = read_json(&state, &sess_key(created["roomCode"].as_str().unwrap(), "manifest")).await.unwrap();
        assert_eq!(manifest["wardOwnerId"], "alice");
        assert_eq!(manifest["temporary"], false);
    }

    #[tokio::test]
    async fn wiping_needs_the_admin_token_and_the_typed_phrase() {
        let state = state().await;
        let alice = signed_in(&state, "alice").await;
        make_room(&state, &alice, json!({})).await.unwrap();

        let no_token = wipe_rooms(State(state.clone()), HeaderMap::new(), Json(json!({ "confirm": WIPE_PHRASE }))).await.unwrap_err();
        assert_eq!(no_token.status, StatusCode::UNAUTHORIZED);
        let no_phrase = wipe_rooms(State(state.clone()), admin(), Json(json!({}))).await.unwrap_err();
        assert_eq!(no_phrase.status, StatusCode::BAD_REQUEST);
        let wrong = wipe_rooms(State(state.clone()), admin(), Json(json!({ "confirm": "yes" }))).await.unwrap_err();
        assert_eq!(wrong.status, StatusCode::BAD_REQUEST);
        assert!(room_of(&state, "alice").await.unwrap().is_some(), "nothing was deleted by the refused attempts");
    }

    #[tokio::test]
    async fn wiping_removes_rooms_members_and_artwork_and_returns_a_backup() {
        let state = state().await;
        let alice = signed_in(&state, "alice").await;
        let bob = signed_in(&state, "bob").await;
        let a = make_room(&state, &alice, json!({ "name": "Alice's room" })).await.unwrap();
        make_room(&state, &bob, json!({ "name": "Bob's room" })).await.unwrap();
        let a_id = a["id"].as_str().unwrap();
        state.store.put_blob(&format!("room-images/{a_id}/banner"), Bytes::from_static(b"png"), "image/png").await.unwrap();
        state.store.put_json("player:member:somewhere:abc", &json!({ "status": "allowed" }), None).await.unwrap();
        state.store.put_json("sp:other:untouched", &json!({ "keep": true }), None).await.unwrap();

        let result = json_of(wipe_rooms(State(state.clone()), admin(), Json(json!({ "confirm": WIPE_PHRASE }))).await.unwrap().into_response()).await;
        assert_eq!(result["deleted"]["rooms"], 2);
        assert_eq!(result["deleted"]["members"], 1);
        let backed_up: Vec<_> = result["backup"]["rooms"].as_array().unwrap().iter().map(|r| r["displayName"].as_str().unwrap().to_string()).collect();
        assert!(backed_up.contains(&"Alice's room".to_string()) && backed_up.contains(&"Bob's room".to_string()));
        assert!(result["backup"]["rooms"][0]["ownerToken"].is_string(), "the backup is complete, so it can restore a room");

        assert!(room_of(&state, "alice").await.unwrap().is_none());
        assert!(!state.store.blob_exists(&format!("room-images/{a_id}/banner")).await.unwrap(), "artwork is gone too");
        assert!(state.store.exists("sp:other:untouched").await.unwrap(), "unrelated data is left alone");
        assert!(make_room(&state, &alice, json!({ "name": "Fresh start" })).await.is_ok(), "the slot is free again");
    }
}
