//! Account identity, room admission and the access boundary shared by HTTP and sockets.
use super::*;
use axum::{extract::Request, middleware::Next};

fn token_hash(token: &str) -> String { bytes_to_hex(&Sha256::digest(token.as_bytes())) }
fn bearer(headers: &HeaderMap) -> Option<&str> {
    headers.get(header::AUTHORIZATION)?.to_str().ok()?.strip_prefix("Bearer ").filter(|v| !v.is_empty())
}

pub async fn identity(state: &AppState, headers: &HeaderMap) -> Result<Value, AppError> {
    let token = bearer(headers).ok_or_else(|| AppError::unauthorized("Sign in with Ward to do that."))?;
    if token.starts_with("sp_app_") {
        return read_json_opt(state, &format!("player:device:{}", token_hash(token))).await
            .ok_or_else(|| AppError::unauthorized("Reconnect your Ward account."));
    }
    let cache = format!("player:identity:{}", token_hash(token));
    if let Some(user) = read_json_opt(state, &cache).await { return Ok(user); }
    let response = reqwest::Client::new().get(format!("{}/oauth/userinfo", state.ward_issuer))
        .timeout(std::time::Duration::from_secs(8)).bearer_auth(token).send().await.map_err(AppError::internal)?;
    if !response.status().is_success() { return Err(AppError::unauthorized("Ward sign-in expired. Please sign in again.")); }
    let user: Value = response.json().await.map_err(AppError::internal)?;
    if user.get("sub").and_then(Value::as_str).filter(|s| !s.is_empty()).is_none() {
        return Err(AppError::unauthorized("Ward did not identify this account."));
    }
    state.store.put_json(&cache, &user, Some(std::time::Duration::from_secs(30))).await.map_err(AppError::internal)?;
    Ok(user)
}

pub async fn optional_subject(state: &AppState, headers: &HeaderMap) -> Result<Option<String>, AppError> {
    if bearer(headers).is_none() { return Ok(None); }
    Ok(Some(ward_subject(state, headers).await?))
}

pub fn owns(room: &Value, subject: &str) -> bool {
    room.get("wardOwnerId").and_then(Value::as_str) == Some(subject)
}

async fn admission(state: &AppState, room: &Value, subject: Option<&str>) -> Result<(), AppError> {
    if subject.is_some_and(|s| owns(room, s)) { return Ok(()); }
    match room.get("accessPolicy").and_then(Value::as_str).unwrap_or("anyone") {
        "anyone" => Ok(()),
        "ward" if subject.is_some() => Ok(()),
        "approval" if subject.is_some() => {
            let id = room["channelId"].as_str().unwrap_or("");
            let member = read_json_opt(state, &member_key(id, subject.unwrap())).await;
            if member.as_ref().and_then(|m| m["status"].as_str()) == Some("allowed") { Ok(()) }
            else { Err(AppError::forbidden("Ask the host to let you in.")) }
        },
        _ => Err(AppError::unauthorized("This room requires Ward sign-in.")),
    }
}

pub async fn session_access(state: &AppState, code: &str, headers: &HeaderMap) -> Result<(), AppError> {
    let manifest = read_json(state, &sess_key(code, "manifest")).await?;
    if manifest["ended"].as_bool() == Some(true) { return Err(AppError::not_found("This listening party has ended.")); }
    if let Some(key) = headers.get("x-session-key").and_then(|h| h.to_str().ok()) {
        validate_session_key(state, code, key).await?;
        return Ok(());
    }
    if let Some(id) = manifest["channelId"].as_str() {
        let room = read_json(state, &channel_key_checked(id)?).await?;
        let subject = optional_subject(state, headers).await?;
        admission(state, &room, subject.as_deref()).await?;
    }
    Ok(())
}

async fn guard(state: &AppState, uri: &axum::http::Uri, method: &str, headers: &HeaderMap) -> Result<(), AppError> {
    let path = uri.path();
    if let Some(rest) = path.strip_prefix("/shared-play/v2/sessions/") {
        let (raw_code, tail) = rest.split_once('/').unwrap_or((rest, ""));
        let code = clean_room_code(raw_code)?;
        // The socket authenticates inside its first frame, never in a logged URL.
        if tail == "socket" { return Ok(()); }
        session_access(state, &code, headers).await?;
        let host_write = (method == "POST" && matches!(tail, "state" | "queue" | "tracks" | "end"))
            || (method == "PUT" && tail.ends_with("package")) || tail.ends_with("/activate");
        if host_write {
            validate_session_key(state, &code, headers.get("x-session-key")
                .and_then(|v| v.to_str().ok()).unwrap_or("")).await?;
        }
        if method == "POST" && tail == "queue/items" {
            let caps = read_json_opt(state, &sess_key(&code, "room:caps")).await.unwrap_or(json!({}));
            if caps["queueAdd"].as_str().unwrap_or("everyone") != "everyone" {
                validate_session_key(state, &code, headers.get("x-session-key")
                    .and_then(|v| v.to_str().ok()).unwrap_or("")).await?;
            }
        }
    }
    if let Some(rest) = path.strip_prefix("/streamer-queue/v1/rooms/") {
        let id = rest.split('/').next().unwrap_or("");
        if id == "discord-pin" { return Ok(()); }
        if let Some(room) = read_json_opt(state, &sq_room_key(id)).await {
            if let Some(channel) = room["publicRoomId"].as_str() {
                let profile = read_json(state, &channel_key_checked(channel)?).await?;
                let subject = optional_subject(state, headers).await?;
                // Owner/bot endpoints authenticate their own credentials. Public reads/submits
                // must not bypass admission just because they use the standalone queue URL.
                let tail = rest.strip_prefix(id).unwrap_or("");
                let query: HashMap<String,String> = uri.query().and_then(|q| reqwest::Url::parse(&format!("https://local/?{q}")).ok()).map(|u| u.query_pairs().into_owned().collect()).unwrap_or_default();
                let privileged = query.get("ownerToken").is_some_and(|t| sq_owner_token_valid(&room,t)) || query.get("botToken").is_some_and(|t| sq_bot_token_valid(&room,t));
                if matches!(tail, "/submit" | "/upload") || (tail.is_empty() && !privileged) {
                    admission(state, &profile, subject.as_deref()).await?;
                }
            }
        }
    }
    Ok(())
}

pub async fn boundary(State(state): State<AppState>, request: Request, next: Next) -> Response {
    let (parts, body) = request.into_parts();
    match guard(&state, &parts.uri, parts.method.as_str(), &parts.headers).await {
        Ok(()) => {
            let mut response = next.run(Request::from_parts(parts,body)).await;
            // Private and room data is never cached; public content (warnings, creator keys) sets its own policy.
            if !response.headers().contains_key(header::CACHE_CONTROL) {
                response.headers_mut().insert(header::CACHE_CONTROL, HeaderValue::from_static("no-store"));
            }
            response
        },
        Err(error) => error.into_response(),
    }
}

fn member_key(id: &str, subject: &str) -> String { format!("player:member:{id}:{}", token_hash(subject)) }

pub async fn access(State(state): State<AppState>, headers: HeaderMap, AxumPath(id): AxumPath<String>) -> Result<Json<Value>, AppError> {
    let room = read_json(&state, &channel_key_checked(&id)?).await?;
    let subject = optional_subject(&state, &headers).await?;
    let status = if admission(&state, &room, subject.as_deref()).await.is_ok() { "allowed".to_string() }
        else if let Some(subject) = &subject {
            read_json_opt(&state, &member_key(&id, subject)).await.and_then(|m| m["status"].as_str().map(str::to_string)).unwrap_or("none".into())
        } else { "signin".into() };
    Ok(Json(json!({"status": status, "isOwner": subject.as_deref().is_some_and(|s| owns(&room,s))})))
}

pub async fn request_access(State(state): State<AppState>, headers: HeaderMap, AxumPath(id): AxumPath<String>) -> Result<Json<Value>, AppError> {
    let room = read_json(&state, &channel_key_checked(&id)?).await?;
    let user = identity(&state, &headers).await?;
    let subject = user["sub"].as_str().unwrap();
    let key = member_key(&id, subject);
    let existing = read_json_opt(&state, &key).await;
    // Denial is sticky until the host changes it; repeated clicking can't erase a decision.
    let member = existing.unwrap_or(json!({"subject":subject,"name":user["name"],"username":user["preferred_username"],"status":"pending","requestedAtUtc":Utc::now().to_rfc3339()}));
    if room["accessPolicy"] == "approval" { write_json(&state,&key,&member).await?; }
    access(State(state),headers,AxumPath(id)).await
}

pub async fn members(State(state): State<AppState>, headers: HeaderMap, AxumPath(id): AxumPath<String>) -> Result<Json<Value>, AppError> {
    let room = read_json(&state, &channel_key_checked(&id)?).await?;
    if !owns(&room, &ward_subject(&state,&headers).await?) { return Err(AppError::forbidden("Only the host can manage admission.")); }
    let keys = state.store.scan_keys(&format!("player:member:{id}:*")).await.map_err(AppError::internal)?;
    let mut members = Vec::new();
    for key in keys { if let Some(member) = read_json_opt(&state,&key).await { members.push(member); } }
    Ok(Json(json!({"members":members})))
}

pub async fn decide(State(state): State<AppState>, headers: HeaderMap, AxumPath((id,subject)): AxumPath<(String,String)>, Json(payload): Json<Value>) -> Result<Json<Value>, AppError> {
    let room = read_json(&state, &channel_key_checked(&id)?).await?;
    if !owns(&room, &ward_subject(&state,&headers).await?) { return Err(AppError::forbidden("Only the host can manage admission.")); }
    let status = payload["status"].as_str().filter(|s| matches!(*s,"allowed"|"denied")).ok_or_else(|| AppError::bad_request("Choose allowed or denied."))?;
    let key = member_key(&id,&subject);
    let mut member = read_json(&state,&key).await?;
    member["status"] = json!(status);
    write_json(&state,&key,&member).await?;
    Ok(Json(member))
}

pub async fn my_rooms(State(state): State<AppState>, headers: HeaderMap) -> Result<Json<Value>, AppError> {
    let subject = ward_subject(&state,&headers).await?;
    let keys = state.store.scan_keys("sp:chan:*").await.map_err(AppError::internal)?;
    let mut rooms = Vec::new();
    for key in keys { if let Some(room) = read_json_opt(&state,&key).await { if owns(&room,&subject) { rooms.push(room_card(&room)); } } }
    Ok(Json(json!({"rooms":rooms})))
}

pub fn room_card(room: &Value) -> Value {
    let live = room["isLive"].as_bool().unwrap_or(false) && room["updatedAtUtc"].as_str().and_then(parse_utc)
        .is_some_and(|t| Utc::now().signed_duration_since(t).num_seconds() < CHANNEL_HEARTBEAT_TTL_SECONDS);
    let id = room["channelId"].as_str().unwrap_or("");
    let mut result = json!({
        "id":id, "name":room.get("publicName").or_else(||room.get("displayName")),
        "description":room["publicDescription"], "kind":room.get("roomKind").unwrap_or(&json!("channel")),
        "tags":room.get("tags").unwrap_or(&json!([])), "security":room.get("accessPolicy").unwrap_or(&json!("anyone")),
        "host":room.get("hostName").or_else(||room.get("displayName")), "isPublic":room["isPublic"],
        "isLive":live, "listeners":if live { room["listenerCount"].as_u64().unwrap_or(0) } else {0},
        "roomCode":if live { room["roomCode"].clone() } else {Value::Null},
        "streamerQueueId":room["streamerQueueId"], "joinUrl":format!("https://player.deltavdevs.com/rooms/{id}"),
        "nowPlaying":if live {json!({"title":room["track"].get("displayName").or_else(||room["track"].get("title")),"artist":room["track"]["artist"]})} else {Value::Null}
    });
    for field in ["bannerUrl","iconUrl","ogImageUrl","seoTitle","seoDescription"] { result[field] = room[field].clone(); }
    result
}

pub async fn end_session(State(state): State<AppState>, AxumPath(code): AxumPath<String>) -> Result<Json<Value>,AppError> {
    let key = sess_key(&clean_room_code(&code)?,"manifest");
    let mut manifest = read_json(&state,&key).await?;
    manifest["ended"] = json!(true);
    write_json(&state,&key,&manifest).await?;
    Ok(Json(json!({"ok":true})))
}

pub async fn me(State(state): State<AppState>, headers: HeaderMap) -> Result<Json<Value>,AppError> { Ok(Json(identity(&state,&headers).await?)) }

pub async fn start_link(State(state): State<AppState>) -> Result<Json<Value>,AppError> {
    let code = bytes_to_hex(&rand::thread_rng().gen::<[u8;16]>());
    let secret = bytes_to_hex(&rand::thread_rng().gen::<[u8;32]>());
    state.store.put_json(&format!("player:link:{code}"),&json!({"secretHash":token_hash(&secret)}),Some(std::time::Duration::from_secs(600))).await.map_err(AppError::internal)?;
    Ok(Json(json!({"code":code,"secret":secret,"verificationUrl":format!("https://player.deltavdevs.com/connect/{code}"),"expiresIn":600})))
}

pub async fn approve_link(State(state): State<AppState>, headers: HeaderMap, AxumPath(code): AxumPath<String>) -> Result<Json<Value>,AppError> {
    let user = identity(&state,&headers).await?;
    let key = format!("player:link:{code}");
    let mut link = read_json(&state,&key).await?;
    if link.get("user").is_some() { return Err(AppError::bad_request("This connection has already been approved.")); }
    link["user"] = user;
    state.store.put_json(&key,&link,Some(std::time::Duration::from_secs(120))).await.map_err(AppError::internal)?;
    Ok(Json(json!({"ok":true})))
}

pub async fn poll_link(State(state): State<AppState>, AxumPath(code): AxumPath<String>, Json(payload): Json<Value>) -> Result<Json<Value>,AppError> {
    let key = format!("player:link:{code}");
    let link = read_json(&state,&key).await?;
    if link["secretHash"].as_str() != Some(&token_hash(payload["secret"].as_str().unwrap_or(""))) { return Err(AppError::forbidden("Invalid connection proof.")); }
    let Some(user) = link.get("user") else { return Ok(Json(json!({"status":"pending"}))); };
    let token = format!("sp_app_{}",bytes_to_hex(&rand::thread_rng().gen::<[u8;32]>()));
    state.store.put_json(&format!("player:device:{}",token_hash(&token)),user,Some(std::time::Duration::from_secs(30*86400))).await.map_err(AppError::internal)?;
    state.store.del(&key).await.map_err(AppError::internal)?;
    Ok(Json(json!({"status":"connected","token":token,"user":user})))
}

pub async fn disconnect(State(state): State<AppState>, headers: HeaderMap) -> Result<Json<Value>,AppError> {
    let token = bearer(&headers).ok_or_else(||AppError::unauthorized("Not connected."))?;
    if token.starts_with("sp_app_") { state.store.del(&format!("player:device:{}",token_hash(token))).await.map_err(AppError::internal)?; }
    Ok(Json(json!({"ok":true})))
}

pub fn pending_submission(room: &Value, sub: &Value) -> bool {
    matches!(sub["status"].as_str(),Some("pending"|"queued"|"approved")) && room["nowPlayingId"].as_str() != sub["id"].as_str()
}

pub async fn authorize_submission(state: &AppState, headers: &HeaderMap, payload: &Value, room: &Value, sub: &Value) -> Result<bool,AppError> {
    let host = sq_owner_token_valid(room,payload["ownerToken"].as_str().unwrap_or(""));
    if host { return Ok(true); }
    let subject = ward_subject(state,headers).await?;
    if owns(room,&subject) { return Ok(true); }
    if sub["wardOwnerId"].as_str() != Some(&subject) { return Err(AppError::forbidden("You can only change your own requests.")); }
    if !pending_submission(room,sub) { return Err(AppError::forbidden("Only pending songs can be edited or promoted.")); }
    Ok(false)
}

pub async fn claim_queue(State(state): State<AppState>, headers: HeaderMap, AxumPath(id): AxumPath<String>, Json(payload): Json<Value>) -> Result<Json<Value>,AppError> {
    let user = identity(&state,&headers).await?;
    let subject = user["sub"].as_str().unwrap();
    let mut queue = read_sq_room_file(&state,&id).await?;
    if !owns(&queue,subject) {
        if queue["wardOwnerId"].as_str().is_some() || !sq_owner_token_valid(&queue,payload["ownerToken"].as_str().unwrap_or("")) { return Err(AppError::forbidden("Only this queue's host can link it.")); }
        queue["wardOwnerId"] = json!(subject);
    }
    let profile_key = channel_key_checked(&id)?;
    let profile = read_json_opt(&state,&profile_key).await.unwrap_or(json!({
        "channelId":id,"wardOwnerId":subject,"ownerToken":queue["ownerToken"],"streamerQueueId":id,
        "roomKind":"streamer_queue","publicName":user.get("name").unwrap_or(&json!("My queue")),
        "hostName":user["name"],"isPublic":false,"accessPolicy":"anyone","tags":[],"isLive":false,
        "stats":empty_channel_stats(),"createdAtUtc":Utc::now().to_rfc3339()
    }));
    if !owns(&profile,subject) { return Err(AppError::forbidden("This room belongs to another host.")); }
    queue["publicRoomId"] = json!(&id);
    write_json(&state,&profile_key,&profile).await?;
    write_sq_room_file(&state,&id,&queue).await?;
    Ok(Json(room_card(&profile)))
}

pub async fn host_credentials(State(state): State<AppState>, headers: HeaderMap, AxumPath(id): AxumPath<String>) -> Result<Json<Value>,AppError> {
    let room = read_json(&state,&channel_key_checked(&id)?).await?;
    if !owns(&room,&ward_subject(&state,&headers).await?) { return Err(AppError::forbidden("Only the host can use this room.")); }
    Ok(Json(json!({"ownerToken":room["ownerToken"],"streamerQueueId":room["streamerQueueId"]})))
}

pub async fn upload_image(State(state):State<AppState>, headers:HeaderMap, AxumPath((id,kind)):AxumPath<(String,String)>, bytes:Bytes)->Result<Json<Value>,AppError>{
    let field=match kind.as_str(){"banner"=>"bannerUrl","icon"=>"iconUrl","social"=>"ogImageUrl",_=>return Err(AppError::bad_request("Unknown image slot."))};
    let key=channel_key_checked(&id)?;
    let mut room=read_json(&state,&key).await?;
    if !owns(&room,&ward_subject(&state,&headers).await?){return Err(AppError::forbidden("Only the host can upload room artwork."));}
    if bytes.len()>5*1024*1024{return Err(AppError::payload_too_large("Room images must be under 5 MB."));}
    let mime=if bytes.starts_with(b"\x89PNG\r\n\x1a\n"){"image/png"}else if bytes.starts_with(b"\xff\xd8\xff"){"image/jpeg"}else if bytes.starts_with(b"RIFF")&&bytes.get(8..12)==Some(b"WEBP"){"image/webp"}else{return Err(AppError::bad_request("Upload a PNG, JPEG or WebP image."));};
    state.store.put_blob(&format!("room-images/{id}/{kind}"),bytes,mime).await.map_err(AppError::internal)?;
    let url=format!("{}/player/v1/rooms/{id}/images/{kind}",base_url(&state,&headers));
    room[field]=json!(&url);write_json(&state,&key,&room).await?;
    Ok(Json(json!({"url":url})))
}

pub async fn get_image(State(state):State<AppState>,AxumPath((id,kind)):AxumPath<(String,String)>)->Result<Response,AppError>{
    let id=clean_channel_id(&id)?;
    if !matches!(kind.as_str(),"banner"|"icon"|"social"){return Err(AppError::not_found("Image not found."));}
    send_blob(&state,&format!("room-images/{id}/{kind}"),None).await
}

#[cfg(test)]
mod tests {
    use super::*;
    async fn state() -> AppState {
        std::env::remove_var("REDIS_URL");
        AppState {store:Arc::new(store::Store::from_env().await.unwrap()),rooms:collab::Rooms::default(),sq_rooms:collab::Rooms::default(),web_share_root:Arc::new(PathBuf::from("web-share")),public_base_url:None,stripe_secret_key:None,stripe_webhook_secret:None,stripe_connect_client_id:None,stripe_publishable_key:None,ward_issuer:Arc::new("http://127.0.0.1:1".into())}
    }
    async fn user(state:&AppState,subject:&str)->HeaderMap {
        let token=format!("sp_app_{subject}");
        state.store.put_json(&format!("player:device:{}",token_hash(&token)),&json!({"sub":subject,"name":subject}),None).await.unwrap();
        let mut headers=HeaderMap::new();headers.insert(header::AUTHORIZATION,HeaderValue::from_str(&format!("Bearer {token}")).unwrap());headers
    }
    async fn fixtures(state:&AppState,security:&str) {
        write_json(state,&channel_key("room-test"),&json!({"channelId":"room-test","wardOwnerId":"host","ownerToken":"abcdefgh12345678","accessPolicy":security,"publicName":"Keep my name","tags":["ambient"],"isPublic":true,"roomKind":"channel","createdAtUtc":Utc::now().to_rfc3339()})).await.unwrap();
        write_json(state,&sess_key("ABC123","manifest"),&json!({"roomCode":"ABC123","channelId":"room-test","sessionKey":"abcdefgh12345678","wardOwnerId":"host","expiresAtUtc":(Utc::now()+Duration::hours(12)).to_rfc3339()})).await.unwrap();
    }
    #[tokio::test]
    async fn admission_gates_every_session_http_surface() {
        let state=state().await;fixtures(&state,"approval").await;
        let listener=user(&state,"alice").await;
        for path in ["","/manifest","/state","/queue","/presence","/reactions","/package","/tracks/track/package","/streamer-queue"] {
            let uri=format!("/shared-play/v2/sessions/ABC123{path}").parse().unwrap();
            assert!(guard(&state,&uri,"GET",&HeaderMap::new()).await.is_err(),"anonymous bypass: {path}");
            assert!(guard(&state,&uri,"GET",&listener).await.is_err(),"unapproved bypass: {path}");
        }
        request_access(State(state.clone()),listener.clone(),AxumPath("room-test".into())).await.unwrap();
        let host=user(&state,"host").await;
        decide(State(state.clone()),host,AxumPath(("room-test".into(),"alice".into())),Json(json!({"status":"allowed"}))).await.unwrap();
        assert!(session_access(&state,"ABC123",&listener).await.is_ok());
        let host=user(&state,"host").await;
        decide(State(state.clone()),host,AxumPath(("room-test".into(),"alice".into())),Json(json!({"status":"denied"}))).await.unwrap();
        assert!(session_access(&state,"ABC123",&listener).await.is_err());
        request_access(State(state.clone()),listener.clone(),AxumPath("room-test".into())).await.unwrap();
        assert!(session_access(&state,"ABC123",&listener).await.is_err());
    }
    #[tokio::test]
    async fn host_writes_need_host_key_even_in_an_open_room() {
        let state=state().await;fixtures(&state,"anyone").await;
        for (path,method) in [("state","POST"),("queue","POST"),("tracks","POST"),("package","PUT"),("tracks/x/activate","POST")] {
            let uri=format!("/shared-play/v2/sessions/ABC123/{path}").parse().unwrap();
            assert!(guard(&state,&uri,method,&HeaderMap::new()).await.is_err());
            let mut host=HeaderMap::new();host.insert("x-session-key",HeaderValue::from_static("abcdefgh12345678"));
            assert!(guard(&state,&uri,method,&host).await.is_ok());
        }
        // Released clients send the key twice on the package upload ("key, key"); that must still be the host.
        let uri="/shared-play/v2/sessions/ABC123/package".parse().unwrap();
        let mut doubled=HeaderMap::new();doubled.insert("x-session-key",HeaderValue::from_static("abcdefgh12345678, abcdefgh12345678"));
        assert!(guard(&state,&uri,"PUT",&doubled).await.is_ok());
        let mut wrong=HeaderMap::new();wrong.insert("x-session-key",HeaderValue::from_static("abcdefgh12345678, someoneelses123456"));
        assert!(guard(&state,&uri,"PUT",&wrong).await.is_ok(), "only the first value is the key");
        let mut other=HeaderMap::new();other.insert("x-session-key",HeaderValue::from_static("someoneelses123456, abcdefgh12345678"));
        assert!(guard(&state,&uri,"PUT",&other).await.is_err());
        let uri="/shared-play/v2/sessions/ABC123/queue/items".parse().unwrap();
        assert!(guard(&state,&uri,"POST",&HeaderMap::new()).await.is_ok());
        write_json(&state,&sess_key("ABC123","room:caps"),&json!({"queueAdd":"host"})).await.unwrap();
        assert!(guard(&state,&uri,"POST",&HeaderMap::new()).await.is_err());
    }
    #[tokio::test]
    async fn only_own_pending_requests_can_be_changed() {
        let state=state().await;let alice=user(&state,"alice").await;let bob=user(&state,"bob").await;
        let mut room=json!({"wardOwnerId":"host","ownerToken":"secret","nowPlayingId":null});
        let mut sub=json!({"id":"s1","wardOwnerId":"alice","status":"queued"});
        assert!(authorize_submission(&state,&alice,&json!({}),&room,&sub).await.is_ok());
        assert!(authorize_submission(&state,&bob,&json!({}),&room,&sub).await.is_err());
        assert!(authorize_submission(&state,&HeaderMap::new(),&json!({}),&room,&sub).await.is_err());
        room["nowPlayingId"]=json!("s1");
        assert!(authorize_submission(&state,&alice,&json!({}),&room,&sub).await.is_err());
        room["nowPlayingId"]=Value::Null;
        for status in ["played","skipped","rejected","awaiting_payment","payment_failed"] {sub["status"]=json!(status);assert!(authorize_submission(&state,&alice,&json!({}),&room,&sub).await.is_err());}
        assert!(authorize_submission(&state,&HeaderMap::new(),&json!({"ownerToken":"secret"}),&room,&sub).await.is_ok());
    }
    #[tokio::test]
    async fn heartbeat_preserves_profile_and_rejects_another_hosts_session() {
        let state=state().await;fixtures(&state,"approval").await;let host=user(&state,"host").await;
        put_channel(State(state.clone()),host.clone(),AxumPath("room-test".into()),Json(json!({"roomCode":"ABC123","isLive":true}))).await.unwrap();
        let room=read_json(&state,&channel_key("room-test")).await.unwrap();
        assert_eq!(room["publicName"],"Keep my name");assert_eq!(room["accessPolicy"],"approval");assert_eq!(room["isPublic"],true);assert_eq!(room["tags"],json!(["ambient"]));
        write_json(&state,&sess_key("XYZ123","manifest"),&json!({"wardOwnerId":"other"})).await.unwrap();
        assert!(put_channel(State(state.clone()),host,AxumPath("room-test".into()),Json(json!({"roomCode":"XYZ123","isLive":true}))).await.is_err());
        assert!(put_channel(State(state.clone()),HeaderMap::new(),AxumPath("new-room".into()),Json(json!({"ownerToken":"abcdefgh12345678"}))).await.is_err());
    }
    #[tokio::test]
    async fn device_link_requires_proof_and_can_only_be_redeemed_once() {
        let state=state().await;let host=user(&state,"host").await;
        let Json(link)=start_link(State(state.clone())).await.unwrap();let code=link["code"].as_str().unwrap().to_string();
        approve_link(State(state.clone()),host,AxumPath(code.clone())).await.unwrap();
        assert!(poll_link(State(state.clone()),AxumPath(code.clone()),Json(json!({"secret":"wrong"}))).await.is_err());
        let Json(result)=poll_link(State(state.clone()),AxumPath(code.clone()),Json(json!({"secret":link["secret"]}))).await.unwrap();
        assert_eq!(result["user"]["sub"],"host");
        assert!(poll_link(State(state.clone()),AxumPath(code),Json(json!({"secret":link["secret"]}))).await.is_err());
        let mut headers=HeaderMap::new();headers.insert(header::AUTHORIZATION,HeaderValue::from_str(&format!("Bearer {}",result["token"].as_str().unwrap())).unwrap());
        assert!(identity(&state,&headers).await.is_ok());disconnect(State(state.clone()),headers.clone()).await.unwrap();assert!(identity(&state,&headers).await.is_err());
    }
    #[tokio::test]
    async fn query_parameters_do_not_bypass_queue_admission() {
        let state=state().await;fixtures(&state,"ward").await;
        write_sq_room_file(&state,"queue-test",&json!({"roomId":"queue-test","publicRoomId":"room-test","ownerToken":"secret"})).await.unwrap();
        for query in ["","?x=1","?ownerToken=wrong","?botToken=wrong"] {let uri=format!("/streamer-queue/v1/rooms/queue-test{query}").parse().unwrap();assert!(guard(&state,&uri,"GET",&HeaderMap::new()).await.is_err());}
    }
    #[test]
    fn tags_and_offline_cards_are_not_silent_guesses() {
        assert!(public_room_fields(&json!({"tags":["a","b","c","d"]})).is_err());
        assert!(public_room_fields(&json!({"security":"open-ish"})).is_err());
        assert_eq!(public_room_fields(&json!({"tags":[" Ambient ","ambient"]})).unwrap().2,vec!["ambient"]);
        let card=room_card(&json!({"channelId":"test","isLive":true,"updatedAtUtc":"2020-01-01T00:00:00Z","roomCode":"ABC123","listenerCount":100}));
        assert_eq!(card["isLive"],false);assert_eq!(card["listeners"],0);assert!(card["roomCode"].is_null());
    }
}
