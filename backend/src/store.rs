//! Storage abstraction: hot JSON in Redis, blobs in S3-compatible object storage,
//! pub/sub for cross-replica fan-out. Falls back to an in-process implementation
//! (memory + temp dir) when `REDIS_URL` is unset, so tests and `cargo run` work
//! without live infrastructure.
//!
//! Keys are flat strings. JSON keys look like `sp:sess:ABC123:state`; blob keys
//! look like `sessions/ABC123/tracks/<key>.zip`. Pub/sub channels look like
//! `sp:ev:ABC123`.

use std::collections::{HashMap, HashSet};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use anyhow::{Context, Result};
use bytes::Bytes;
use futures_util::StreamExt;
use serde_json::Value;
use tokio::sync::{broadcast, mpsc};

use object_store::aws::AmazonS3Builder;
use object_store::{Attribute, AttributeValue, Attributes, ObjectStore, PutOptions, PutPayload};
use redis::aio::ConnectionManager;
use redis::AsyncCommands;

/// A blob streamed back out of storage.
pub struct BlobRead {
    pub bytes: Bytes,
    pub content_type: String,
}

enum Backend {
    Live {
        redis: ConnectionManager,
        redis_client: redis::Client,
        s3: Box<dyn ObjectStore>,
    },
    Local {
        json: Mutex<HashMap<String, (Value, Option<Instant>)>>,
        blobs: Mutex<HashMap<String, (Bytes, String)>>,
        hashes: Mutex<HashMap<String, HashMap<String, String>>>,
        sets: Mutex<HashMap<String, HashSet<String>>>,
        bus: broadcast::Sender<(String, String)>,
    },
}

pub struct Store {
    backend: Backend,
}

/// A live subscription to one or more pub/sub channel patterns. Poll `recv()`.
pub struct Subscription {
    rx: mpsc::Receiver<(String, String)>,
    _task: tokio::task::JoinHandle<()>,
}

impl Subscription {
    pub async fn recv(&mut self) -> Option<(String, String)> {
        self.rx.recv().await
    }
}

impl Store {
    /// Build a `Store` from the environment. Uses Redis + S3 when `REDIS_URL` is
    /// set; otherwise an in-process fallback (never use that in production).
    pub async fn from_env() -> Result<Self> {
        match std::env::var("REDIS_URL").ok().filter(|v| !v.trim().is_empty()) {
            Some(redis_url) => {
                let redis_client =
                    redis::Client::open(redis_url).context("REDIS_URL is not a valid Redis URL")?;
                let redis = redis_client
                    .get_connection_manager()
                    .await
                    .context("could not connect to Redis")?;
                let s3 = build_s3().context("could not configure S3 object storage")?;
                eprintln!("store: live backend (Redis + S3)");
                Ok(Self {
                    backend: Backend::Live {
                        redis,
                        redis_client,
                        s3,
                    },
                })
            }
            None => {
                eprintln!("store: in-process fallback (no REDIS_URL) — DEV ONLY");
                let (bus, _) = broadcast::channel(1024);
                Ok(Self {
                    backend: Backend::Local {
                        json: Mutex::new(HashMap::new()),
                        blobs: Mutex::new(HashMap::new()),
                        hashes: Mutex::new(HashMap::new()),
                        sets: Mutex::new(HashMap::new()),
                        bus,
                    },
                })
            }
        }
    }

    #[allow(dead_code)]
    pub fn is_live(&self) -> bool {
        matches!(self.backend, Backend::Live { .. })
    }

    // ── JSON ──────────────────────────────────────────────────────────────────

    pub async fn get_json(&self, key: &str) -> Result<Option<Value>> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                let raw: Option<String> = conn.get(key).await?;
                match raw {
                    Some(s) => Ok(Some(serde_json::from_str(&s)?)),
                    None => Ok(None),
                }
            }
            Backend::Local { json, .. } => {
                let mut map = json.lock().unwrap();
                match map.get(key) {
                    Some((_, Some(exp))) if *exp <= Instant::now() => {
                        map.remove(key);
                        Ok(None)
                    }
                    Some((v, _)) => Ok(Some(v.clone())),
                    None => Ok(None),
                }
            }
        }
    }

    pub async fn put_json(&self, key: &str, value: &Value, ttl: Option<Duration>) -> Result<()> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                let s = serde_json::to_string(value)?;
                match ttl {
                    Some(ttl) => {
                        conn.set_ex::<_, _, ()>(key, s, ttl.as_secs().max(1)).await?;
                    }
                    None => {
                        conn.set::<_, _, ()>(key, s).await?;
                    }
                }
                Ok(())
            }
            Backend::Local { json, .. } => {
                let exp = ttl.map(|t| Instant::now() + t);
                json.lock()
                    .unwrap()
                    .insert(key.to_string(), (value.clone(), exp));
                Ok(())
            }
        }
    }

    pub async fn del(&self, key: &str) -> Result<()> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                conn.del::<_, ()>(key).await?;
                Ok(())
            }
            Backend::Local {
                json,
                blobs,
                hashes,
                sets,
                ..
            } => {
                json.lock().unwrap().remove(key);
                blobs.lock().unwrap().remove(key);
                hashes.lock().unwrap().remove(key);
                sets.lock().unwrap().remove(key);
                Ok(())
            }
        }
    }

    pub async fn exists(&self, key: &str) -> Result<bool> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                Ok(conn.exists(key).await?)
            }
            Backend::Local { json, .. } => {
                let mut map = json.lock().unwrap();
                match map.get(key) {
                    Some((_, Some(exp))) if *exp <= Instant::now() => {
                        map.remove(key);
                        Ok(false)
                    }
                    Some(_) => Ok(true),
                    None => Ok(false),
                }
            }
        }
    }

    /// Refresh the TTL on an existing key. No-op if the key is gone.
    pub async fn expire(&self, key: &str, ttl: Duration) -> Result<()> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                conn.expire::<_, ()>(key, ttl.as_secs().max(1) as i64).await?;
                Ok(())
            }
            Backend::Local { json, .. } => {
                if let Some(entry) = json.lock().unwrap().get_mut(key) {
                    entry.1 = Some(Instant::now() + ttl);
                }
                Ok(())
            }
        }
    }

    /// All keys matching a glob-style pattern (`SCAN MATCH` semantics).
    pub async fn scan_keys(&self, pattern: &str) -> Result<Vec<String>> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                let mut out = Vec::new();
                let mut it = conn.scan_match::<_, String>(pattern).await?;
                while let Some(k) = it.next_item().await {
                    out.push(k);
                }
                Ok(out)
            }
            Backend::Local { json, .. } => {
                let re = glob_to_prefix(pattern);
                let map = json.lock().unwrap();
                Ok(map
                    .keys()
                    .filter(|k| glob_match(pattern, k, &re))
                    .cloned()
                    .collect())
            }
        }
    }

    // ── Hashes (roster) ───────────────────────────────────────────────────────

    pub async fn hset(&self, key: &str, field: &str, value: &str, ttl: Option<Duration>) -> Result<()> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                conn.hset::<_, _, _, ()>(key, field, value).await?;
                if let Some(ttl) = ttl {
                    let _ = conn.expire::<_, ()>(key, ttl.as_secs().max(1) as i64).await;
                }
                Ok(())
            }
            Backend::Local { hashes, .. } => {
                hashes
                    .lock()
                    .unwrap()
                    .entry(key.to_string())
                    .or_default()
                    .insert(field.to_string(), value.to_string());
                Ok(())
            }
        }
    }

    pub async fn hdel(&self, key: &str, field: &str) -> Result<()> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                conn.hdel::<_, _, ()>(key, field).await?;
                Ok(())
            }
            Backend::Local { hashes, .. } => {
                if let Some(h) = hashes.lock().unwrap().get_mut(key) {
                    h.remove(field);
                }
                Ok(())
            }
        }
    }

    pub async fn hgetall(&self, key: &str) -> Result<HashMap<String, String>> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                Ok(conn.hgetall(key).await?)
            }
            Backend::Local { hashes, .. } => {
                Ok(hashes.lock().unwrap().get(key).cloned().unwrap_or_default())
            }
        }
    }

    // ── Sets (vote-skip tally) ────────────────────────────────────────────────

    /// Add a member; returns the set's cardinality afterwards.
    pub async fn sadd_count(&self, key: &str, member: &str, ttl: Option<Duration>) -> Result<u64> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                conn.sadd::<_, _, ()>(key, member).await?;
                if let Some(ttl) = ttl {
                    let _ = conn.expire::<_, ()>(key, ttl.as_secs().max(1) as i64).await;
                }
                Ok(conn.scard(key).await?)
            }
            Backend::Local { sets, .. } => {
                let mut map = sets.lock().unwrap();
                let s = map.entry(key.to_string()).or_default();
                s.insert(member.to_string());
                Ok(s.len() as u64)
            }
        }
    }

    // ── Blobs ─────────────────────────────────────────────────────────────────

    pub async fn get_blob(&self, key: &str) -> Result<Option<BlobRead>> {
        match &self.backend {
            Backend::Live { s3, .. } => {
                let path = object_store::path::Path::from(key);
                match s3.get(&path).await {
                    Ok(res) => {
                        let content_type = res
                            .attributes
                            .get(&Attribute::ContentType)
                            .map(|v| v.to_string())
                            .unwrap_or_else(|| "application/octet-stream".to_string());
                        let bytes = res.bytes().await?;
                        Ok(Some(BlobRead {
                            bytes,
                            content_type,
                        }))
                    }
                    Err(object_store::Error::NotFound { .. }) => Ok(None),
                    Err(e) => Err(e.into()),
                }
            }
            Backend::Local { blobs, .. } => Ok(blobs.lock().unwrap().get(key).map(|(b, ct)| {
                BlobRead {
                    bytes: b.clone(),
                    content_type: ct.clone(),
                }
            })),
        }
    }

    pub async fn put_blob(&self, key: &str, bytes: Bytes, content_type: &str) -> Result<()> {
        match &self.backend {
            Backend::Live { s3, .. } => {
                let path = object_store::path::Path::from(key);
                let mut attrs = Attributes::new();
                attrs.insert(
                    Attribute::ContentType,
                    AttributeValue::from(content_type.to_string()),
                );
                let opts = PutOptions {
                    attributes: attrs,
                    ..Default::default()
                };
                s3.put_opts(&path, PutPayload::from_bytes(bytes), opts).await?;
                Ok(())
            }
            Backend::Local { blobs, .. } => {
                blobs
                    .lock()
                    .unwrap()
                    .insert(key.to_string(), (bytes, content_type.to_string()));
                Ok(())
            }
        }
    }

    pub async fn blob_exists(&self, key: &str) -> Result<bool> {
        match &self.backend {
            Backend::Live { s3, .. } => {
                let path = object_store::path::Path::from(key);
                match s3.head(&path).await {
                    Ok(_) => Ok(true),
                    Err(object_store::Error::NotFound { .. }) => Ok(false),
                    Err(e) => Err(e.into()),
                }
            }
            Backend::Local { blobs, .. } => Ok(blobs.lock().unwrap().contains_key(key)),
        }
    }

    pub async fn delete_blob(&self, key: &str) -> Result<()> {
        match &self.backend {
            Backend::Live { s3, .. } => {
                let path = object_store::path::Path::from(key);
                match s3.delete(&path).await {
                    Ok(()) | Err(object_store::Error::NotFound { .. }) => Ok(()),
                    Err(e) => Err(e.into()),
                }
            }
            Backend::Local { blobs, .. } => {
                blobs.lock().unwrap().remove(key);
                Ok(())
            }
        }
    }

    /// Delete every blob whose key starts with `prefix`. Used by session GC.
    #[allow(dead_code)]
    pub async fn delete_blob_prefix(&self, prefix: &str) -> Result<()> {
        match &self.backend {
            Backend::Live { s3, .. } => {
                let path = object_store::path::Path::from(prefix);
                let mut stream = s3.list(Some(&path));
                let mut keys = Vec::new();
                while let Some(meta) = stream.next().await {
                    keys.push(meta?.location);
                }
                for k in keys {
                    let _ = s3.delete(&k).await;
                }
                Ok(())
            }
            Backend::Local { blobs, .. } => {
                blobs.lock().unwrap().retain(|k, _| !k.starts_with(prefix));
                Ok(())
            }
        }
    }

    // ── Pub/sub ───────────────────────────────────────────────────────────────

    pub async fn publish(&self, channel: &str, message: &str) -> Result<()> {
        match &self.backend {
            Backend::Live { redis, .. } => {
                let mut conn = redis.clone();
                conn.publish::<_, _, ()>(channel, message).await?;
                Ok(())
            }
            Backend::Local { bus, .. } => {
                let _ = bus.send((channel.to_string(), message.to_string()));
                Ok(())
            }
        }
    }

    /// Subscribe to channel name patterns (`PSUBSCRIBE` semantics). The returned
    /// `Subscription` yields `(channel, payload)` pairs until dropped.
    pub async fn subscribe(&self, patterns: Vec<String>) -> Result<Subscription> {
        let (tx, rx) = mpsc::channel(1024);
        match &self.backend {
            Backend::Live { redis_client, .. } => {
                let mut pubsub = redis_client
                    .get_async_pubsub()
                    .await
                    .context("could not open a Redis pub/sub connection")?;
                for p in &patterns {
                    pubsub.psubscribe(p).await?;
                }
                let task = tokio::spawn(async move {
                    let mut stream = pubsub.on_message();
                    while let Some(msg) = stream.next().await {
                        let channel = msg.get_channel_name().to_string();
                        let payload: String = match msg.get_payload() {
                            Ok(p) => p,
                            Err(_) => continue,
                        };
                        if tx.send((channel, payload)).await.is_err() {
                            break;
                        }
                    }
                });
                Ok(Subscription { rx, _task: task })
            }
            Backend::Local { bus, .. } => {
                let mut sub = bus.subscribe();
                let task = tokio::spawn(async move {
                    loop {
                        match sub.recv().await {
                            Ok((channel, payload)) => {
                                if patterns.iter().any(|p| glob_match(p, &channel, &glob_to_prefix(p)))
                                    && tx.send((channel, payload)).await.is_err()
                                {
                                    break;
                                }
                            }
                            Err(broadcast::error::RecvError::Lagged(_)) => continue,
                            Err(broadcast::error::RecvError::Closed) => break,
                        }
                    }
                });
                Ok(Subscription { rx, _task: task })
            }
        }
    }
}

fn build_s3() -> Result<Box<dyn ObjectStore>> {
    let raw_endpoint = std::env::var("S3_ENDPOINT").context("S3_ENDPOINT is required with REDIS_URL")?;
    let bucket = std::env::var("S3_BUCKET").context("S3_BUCKET is required")?;
    let access_key = std::env::var("S3_ACCESS_KEY_ID").context("S3_ACCESS_KEY_ID is required")?;
    let secret_key =
        std::env::var("S3_SECRET_ACCESS_KEY").context("S3_SECRET_ACCESS_KEY is required")?;
    let region = std::env::var("S3_REGION").unwrap_or_else(|_| "auto".to_string());
    // Allow forcing path-style if a bucket needs it (Railway's legacy buckets).
    let path_style = std::env::var("S3_PATH_STYLE")
        .map(|v| v == "1" || v.eq_ignore_ascii_case("true"))
        .unwrap_or(false);

    // object_store's virtual-hosted mode uses the endpoint verbatim (it does NOT
    // prepend the bucket as a subdomain), so we build the bucket host ourselves:
    // https://<bucket>.<endpoint-host>
    let endpoint = if path_style {
        raw_endpoint.clone()
    } else {
        let scheme_split: Vec<&str> = raw_endpoint.splitn(2, "://").collect();
        match scheme_split.as_slice() {
            [scheme, host] => format!("{scheme}://{bucket}.{}", host.trim_end_matches('/')),
            _ => format!("https://{bucket}.{}", raw_endpoint.trim_end_matches('/')),
        }
    };

    eprintln!(
        "store: s3 endpoint={endpoint} bucket={bucket} region={region} path_style={path_style}"
    );

    let mut builder = AmazonS3Builder::new()
        .with_endpoint(endpoint)
        .with_bucket_name(bucket)
        .with_access_key_id(access_key)
        .with_secret_access_key(secret_key)
        .with_region(region)
        .with_allow_http(false);
    builder = builder.with_virtual_hosted_style_request(!path_style);
    Ok(Box::new(builder.build()?))
}

/// Very small glob matcher for the in-process fallback: supports a single
/// trailing `*` (which is all the codebase uses) and exact matches.
fn glob_to_prefix(pattern: &str) -> String {
    pattern.strip_suffix('*').unwrap_or(pattern).to_string()
}

fn glob_match(pattern: &str, candidate: &str, prefix: &str) -> bool {
    if pattern.ends_with('*') {
        candidate.starts_with(prefix)
    } else {
        candidate == pattern
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    async fn local_store() -> Store {
        // Ensure the fallback path even if the test host has REDIS_URL set.
        std::env::remove_var("REDIS_URL");
        Store::from_env().await.unwrap()
    }

    #[tokio::test]
    async fn json_round_trip_and_delete() {
        let s = local_store().await;
        assert!(s.get_json("k1").await.unwrap().is_none());
        s.put_json("k1", &json!({"a": 1}), None).await.unwrap();
        assert_eq!(s.get_json("k1").await.unwrap().unwrap(), json!({"a": 1}));
        assert!(s.exists("k1").await.unwrap());
        s.del("k1").await.unwrap();
        assert!(!s.exists("k1").await.unwrap());
    }

    #[tokio::test]
    async fn json_ttl_expires() {
        let s = local_store().await;
        s.put_json("k2", &json!(true), Some(Duration::from_millis(30)))
            .await
            .unwrap();
        assert!(s.exists("k2").await.unwrap());
        tokio::time::sleep(Duration::from_millis(60)).await;
        assert!(s.get_json("k2").await.unwrap().is_none());
    }

    #[tokio::test]
    async fn blob_put_get() {
        let s = local_store().await;
        assert!(s.get_blob("b/1.zip").await.unwrap().is_none());
        s.put_blob("b/1.zip", Bytes::from_static(b"hello"), "application/zip")
            .await
            .unwrap();
        let read = s.get_blob("b/1.zip").await.unwrap().unwrap();
        assert_eq!(&read.bytes[..], b"hello");
        assert_eq!(read.content_type, "application/zip");
        assert!(s.blob_exists("b/1.zip").await.unwrap());
        s.delete_blob_prefix("b/").await.unwrap();
        assert!(!s.blob_exists("b/1.zip").await.unwrap());
    }

    #[tokio::test]
    async fn scan_keys_prefix() {
        let s = local_store().await;
        s.put_json("sp:sess:AAA:state", &json!(1), None).await.unwrap();
        s.put_json("sp:sess:BBB:state", &json!(2), None).await.unwrap();
        s.put_json("sq:room:xyz", &json!(3), None).await.unwrap();
        let mut hit = s.scan_keys("sp:sess:*").await.unwrap();
        hit.sort();
        assert_eq!(hit, vec!["sp:sess:AAA:state", "sp:sess:BBB:state"]);
    }

    #[tokio::test]
    async fn pubsub_delivers_matching_pattern() {
        let s = local_store().await;
        let mut sub = s.subscribe(vec!["sp:ev:*".to_string()]).await.unwrap();
        tokio::time::sleep(Duration::from_millis(20)).await;
        s.publish("sp:ev:ROOM1", "payload-a").await.unwrap();
        s.publish("sp:other:ROOM1", "nope").await.unwrap();
        s.publish("sp:ev:ROOM2", "payload-b").await.unwrap();
        let first = tokio::time::timeout(Duration::from_millis(200), sub.recv())
            .await
            .unwrap()
            .unwrap();
        assert_eq!(first, ("sp:ev:ROOM1".to_string(), "payload-a".to_string()));
        let second = tokio::time::timeout(Duration::from_millis(200), sub.recv())
            .await
            .unwrap()
            .unwrap();
        assert_eq!(second, ("sp:ev:ROOM2".to_string(), "payload-b".to_string()));
    }
}
