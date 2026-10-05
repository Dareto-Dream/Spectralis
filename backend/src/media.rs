use super::*;
use std::io::{Cursor, Read, Write};

pub fn basic_track(track: &Value) -> Value {
    json!({"displayName":track["displayName"],"title":track["title"],"artist":track["artist"],"durationSeconds":track["durationSeconds"]})
}

fn audio_entry(name: &str) -> bool {
    name.starts_with("audio/track.") && matches!(name.rsplit('.').next(),Some("mp3"|"wav"|"flac"|"ogg"|"opus"|"m4a"|"aac"))
}

pub fn guest_package(bytes: Bytes) -> Result<Bytes,AppError> {
    let mut archive = zip::ZipArchive::new(Cursor::new(bytes)).map_err(AppError::internal)?;
    let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
    let options = zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Stored);
    let mut found = false;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index).map_err(AppError::internal)?;
        if !audio_entry(entry.name()) { continue; }
        if entry.size() > MAX_PACKAGE_BYTES as u64 { return Err(AppError::payload_too_large("Audio exceeds the package limit.")); }
        writer.start_file(entry.name(),options).map_err(AppError::internal)?;
        let copied = std::io::copy(&mut entry.by_ref().take(MAX_PACKAGE_BYTES as u64+1),&mut writer).map_err(AppError::internal)?;
        if copied > MAX_PACKAGE_BYTES as u64 { return Err(AppError::payload_too_large("Audio exceeds the package limit.")); }
        found = true;
        break;
    }
    if !found { return Err(AppError::bad_request("This package contains no browser-playable audio.")); }
    Ok(Bytes::from(writer.finish().map_err(AppError::internal)?.into_inner()))
}

pub async fn package(state: &AppState, headers: &HeaderMap, code: &str, track_key: &str) -> Result<Response,AppError> {
    let key = sess_blob_key(code,&format!("tracks/{track_key}.zip"));
    let trusted = headers.contains_key("x-session-key") || player::optional_subject(state,headers).await?.is_some();
    if trusted { return send_blob(state,&key,Some("application/vnd.spectralis.shared-play+zip")).await; }
    let public_key = sess_blob_key(code,&format!("tracks/{track_key}.audio.zip"));
    if !state.store.blob_exists(&public_key).await.map_err(AppError::internal)? {
        let original = state.store.get_blob(&key).await.map_err(AppError::internal)?.ok_or_else(|| AppError::not_found("Audio is not available yet."))?;
        let audio = tokio::task::spawn_blocking(move || guest_package(original.bytes)).await.map_err(AppError::internal)??;
        state.store.put_blob(&public_key,audio,"application/zip").await.map_err(AppError::internal)?;
    }
    send_blob(state,&public_key,Some("application/zip")).await
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn guest_zip_excludes_host_metadata() {
        let mut writer=zip::ZipWriter::new(Cursor::new(Vec::new()));
        for name in ["manifest.json","audio/track.lrc","artwork/cover.jpg","audio/track.wav"] {
            writer.start_file(name,zip::write::SimpleFileOptions::default()).unwrap();
            writer.write_all(b"test").unwrap();
        }
        let result=guest_package(Bytes::from(writer.finish().unwrap().into_inner())).unwrap();
        let mut archive=zip::ZipArchive::new(Cursor::new(result)).unwrap();
        assert_eq!(archive.len(),1);
        assert_eq!(archive.by_index(0).unwrap().name(),"audio/track.wav");
    }
}
