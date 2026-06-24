import librosa
import numpy as np
import json

def analyze_mp3_with_lyrics(filepath, lrc_filepath, output_file="analysis.json"):
    """
    Combine music analysis with LRC lyrics timing.
    """
    
    # Music analysis (same as before)
    y, sr = librosa.load(filepath, sr=22050)
    onset_env = librosa.onset.onset_strength(y=y, sr=sr)
    beats = librosa.beat.beat_track(onset_envelope=onset_env, sr=sr)[1]
    beat_times = librosa.frames_to_time(beats, sr=sr)
    onsets = librosa.onset.onset_detect(onset_envelope=onset_env, sr=sr, units='time')
    
    S = librosa.feature.melspectrogram(y=y, sr=sr)
    energy = np.sqrt(np.mean(S**2, axis=0))
    energy = librosa.util.normalize(energy)
    rms = librosa.feature.rms(y=y)[0]
    rms = librosa.util.normalize(rms)
    cent = librosa.feature.spectral_centroid(y=y, sr=sr)[0]
    cent = librosa.util.normalize(cent)
    
    frames = np.arange(len(energy))
    times = librosa.frames_to_time(frames, sr=sr)
    
    # Parse LRC
    lyrics = []
    with open(lrc_filepath, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if line.startswith('[') and ']:' in line:
                # [00:12.34]Lyric text
                time_str = line[1:line.index(']')]
                lyric_text = line[line.index(']')+1:]
                
                # Convert MM:SS.MS to seconds
                parts = time_str.split(':')
                timestamp = int(parts[0]) * 60 + float(parts[1])
                
                lyrics.append({
                    "time": timestamp,
                    "text": lyric_text
                })
    
    # Detect drops
    energy_diff = np.diff(np.concatenate([[0], energy]))
    is_drop = (energy_diff > 0.3) & (cent < np.percentile(cent, 40))
    drop_frames = np.where(is_drop)[0]
    drop_times = librosa.frames_to_time(drop_frames, sr=sr)
    
    output = {
        "metadata": {
            "file": filepath,
            "duration": librosa.get_duration(y=y, sr=sr),
            "sr": sr
        },
        "beats": beat_times.tolist(),
        "onsets": onsets.tolist(),
        "drops": drop_times.tolist(),
        "lyrics": lyrics,
        "frame_data": [
            {
                "time": float(times[i]),
                "energy": float(energy[i]),
                "rms": float(rms[i]),
                "spectral_centroid": float(cent[i])
            }
            for i in range(len(energy))
        ]
    }
    
    with open(output_file, 'w') as f:
        json.dump(output, f, indent=2)
    
    print(f"Analysis saved to {output_file}")
    return output

analyze_mp3_with_lyrics("reveal.mp3", "reveal.lrc", "song_analysis.json")