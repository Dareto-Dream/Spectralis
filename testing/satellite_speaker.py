#!/usr/bin/env python3
"""Small LAN speaker for the Spectralis Satellite protocol.

Run this on the machine that should play the audio:
    python testing/satellite_speaker.py --source 192.168.1.20 --satellite-port 41234

The source app shows the six-digit pairing PIN on its Satellite window. Supply it with
--pin (or SATELLITE_PIN) on first connection. The HTTP status page is available on the
LAN at http://<this-machine>:8765/.

Audio output is live when the optional ``sounddevice`` package is available. Without it,
the receiver still validates the complete Satellite connection and writes the latest PCM
to testing/.satellite-speaker-last.wav for inspection.
"""

from __future__ import annotations

import argparse
import base64
import json
import math
import os
import queue
import secrets
import socket
import struct
import threading
import time
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any


PROTOCOL_VERSION = 1
FRAME_CONTROL = 1
FRAME_AUDIO = 2
MAX_FRAME_BYTES = 4 * 1024 * 1024
IDENTITY_FILE = Path(__file__).with_name(".satellite-speaker-device-id")
LAST_WAV = Path(__file__).with_name(".satellite-speaker-last.wav")


def monotonic_ms() -> float:
    return time.monotonic_ns() / 1_000_000


def read_exact(sock: socket.socket, size: int) -> bytes:
    result = bytearray()
    while len(result) < size:
        chunk = sock.recv(size - len(result))
        if not chunk:
            raise ConnectionError("source closed the connection")
        result.extend(chunk)
    return bytes(result)


def read_frame(sock: socket.socket) -> tuple[int, bytes]:
    length, frame_type = struct.unpack(">IB", read_exact(sock, 5))
    if length > MAX_FRAME_BYTES:
        raise ValueError(f"Satellite frame is too large: {length} bytes")
    return frame_type, read_exact(sock, length)


def write_frame(sock: socket.socket, frame_type: int, payload: bytes) -> None:
    if len(payload) > MAX_FRAME_BYTES:
        raise ValueError("Satellite frame is too large")
    sock.sendall(struct.pack(">IB", len(payload), frame_type) + payload)


def control(message_type: str, **fields: Any) -> bytes:
    return json.dumps({"v": PROTOCOL_VERSION, "t": message_type, **fields}, separators=(",", ":")).encode()


def load_device_id() -> str:
    try:
        device_id = IDENTITY_FILE.read_text(encoding="utf-8").strip()
        if device_id:
            return device_id
    except OSError:
        pass
    device_id = "python-speaker-" + secrets.token_hex(8)
    try:
        IDENTITY_FILE.write_text(device_id + "\n", encoding="utf-8")
    except OSError:
        pass
    return device_id


class Speaker:
    def __init__(self, args: argparse.Namespace):
        self.args = args
        self.device_id = load_device_id()
        self.state_lock = threading.Lock()
        self.state: dict[str, Any] = {
            "connected": False,
            "source": f"{args.source}:{args.satellite_port}",
            "frames": 0,
            "samples": 0,
            "sample_rate": None,
            "channels": None,
            "clock_offset_ms": None,
            "last_error": None,
            "audio_backend": "wav fallback",
        }
        self.stop_event = threading.Event()
        self.audio_queue: queue.Queue[tuple[int, int, list[float]]] = queue.Queue(maxsize=12)
        self.output_thread = threading.Thread(target=self._audio_loop, daemon=True)
        self.output_thread.start()

    def update(self, **values: Any) -> None:
        with self.state_lock:
            self.state.update(values)

    def snapshot(self) -> dict[str, Any]:
        with self.state_lock:
            return dict(self.state)

    def connect_forever(self) -> None:
        while not self.stop_event.is_set():
            try:
                self._connect_once()
            except (ConnectionError, OSError, ValueError, json.JSONDecodeError) as exc:
                self.update(connected=False, last_error=str(exc))
                if not self.stop_event.wait(2):
                    print(f"[speaker] {exc}; retrying...")

    def _connect_once(self) -> None:
        print(f"[speaker] connecting to {self.state['source']}")
        with socket.create_connection((self.args.source, self.args.satellite_port), timeout=10) as sock:
            sock.settimeout(None)
            write_frame(sock, FRAME_CONTROL, control("hello", deviceId=self.device_id, displayName=self.args.name))
            frame_type, payload = read_frame(sock)
            if frame_type != FRAME_CONTROL:
                raise ValueError("source did not send a control response")
            message = json.loads(payload)
            if message.get("t") == "pairingRequired":
                pin = self.args.pin or os.environ.get("SATELLITE_PIN")
                if not pin:
                    pin = input("Satellite pairing PIN: ").strip()
                write_frame(sock, FRAME_CONTROL, control("pairRequest", Pin=pin))
                _, payload = read_frame(sock)
                message = json.loads(payload)
                if message.get("t") != "pairResult" or not message.get("ok"):
                    raise ValueError("pairing rejected")
            elif message.get("t") != "pairResult" or not message.get("ok"):
                raise ValueError("unexpected handshake response")

            write_frame(sock, FRAME_CONTROL, control("capabilities", codec="pcm", display="none"))
            self.update(connected=True, last_error=None)
            print("[speaker] connected; waiting for PCM frames")
            next_ping = 0.0
            while not self.stop_event.is_set():
                sock.settimeout(max(0.1, next_ping - time.monotonic()) if next_ping else 0.1)
                try:
                    frame_type, payload = read_frame(sock)
                except socket.timeout:
                    write_frame(sock, FRAME_CONTROL, control("clockPing", T0=monotonic_ms()))
                    next_ping = time.monotonic() + 2
                    continue
                if frame_type == FRAME_AUDIO:
                    self._receive_audio(payload)
                elif frame_type == FRAME_CONTROL:
                    message = json.loads(payload)
                    if message.get("t") == "clockPong":
                        t3 = monotonic_ms()
                        offset = ((message["t1"] - message["t0"]) + (message["t2"] - t3)) / 2
                        self.update(clock_offset_ms=round(offset, 3))
            self.update(connected=False)

    def _receive_audio(self, payload: bytes) -> None:
        # Wire shape: [8]sourceClockMs(double) [4]sampleRate(i32) [4]channels(i32)
        # [1]encoding(byte: 0=pcm, 1=opus) then, for pcm, [4]pcmCount(i32) + pcmCount*4 floats.
        # This reference speaker always negotiates codec="pcm" (see _connect_once), so an opus
        # frame here would mean the source ignored that — treated as a protocol mismatch, not
        # decoded (this script has no Opus decoder).
        if len(payload) < 17:
            return
        source_clock, sample_rate, channels, encoding = struct.unpack(">diiB", payload[:17])
        if encoding != 0:
            return
        if len(payload) < 21:
            return
        (pcm_count,) = struct.unpack(">i", payload[17:21])
        expected = 21 + pcm_count * 4 + 4
        if pcm_count < 0 or channels < 1 or sample_rate < 1 or len(payload) < expected:
            return
        pcm = list(struct.unpack(f">{pcm_count}f", payload[21:21 + pcm_count * 4]))
        self.update(frames=self.state["frames"] + 1, samples=self.state["samples"] + pcm_count,
                    sample_rate=sample_rate, channels=channels)
        try:
            self.audio_queue.put_nowait((sample_rate, channels, pcm))
        except queue.Full:
            try:
                self.audio_queue.get_nowait()
                self.audio_queue.put_nowait((sample_rate, channels, pcm))
            except queue.Empty:
                pass

    def _audio_loop(self) -> None:
        try:
            import sounddevice as sd  # type: ignore
            stream = None
            while not self.stop_event.is_set():
                rate, channels, pcm = self.audio_queue.get()
                if stream is None or stream.samplerate != rate or stream.channels != channels:
                    if stream is not None:
                        stream.stop(); stream.close()
                    stream = sd.RawOutputStream(samplerate=rate, channels=channels, dtype="float32", blocksize=0)
                    stream.start()
                    self.update(audio_backend="sounddevice")
                stream.write(struct.pack(f"={len(pcm)}f", *pcm))
        except (ImportError, OSError) as exc:
            self.update(audio_backend="wav fallback")
            self._wav_fallback()

    def _wav_fallback(self) -> None:
        with wave.open(str(LAST_WAV), "wb") as output:
            output.setnchannels(2)
            output.setsampwidth(2)
            output.setframerate(48_000)
            while not self.stop_event.is_set():
                rate, channels, pcm = self.audio_queue.get()
                if rate != 48_000 or channels not in (1, 2):
                    continue
                samples = pcm if channels == 2 else [v for sample in pcm for v in (sample, sample)]
                output.writeframes(struct.pack("<" + "h" * len(samples), *(max(-1, min(1, v)) * 32767 for v in samples)))


class StatusHandler(BaseHTTPRequestHandler):
    speaker: Speaker

    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/":
            try:
                body = (Path(__file__).with_name("index.html")).read_bytes()
            except OSError:
                self.send_error(500, "dashboard is missing")
                return
            content_type = "text/html; charset=utf-8"
        elif self.path == "/status":
            body = json.dumps(self.speaker.snapshot(), indent=2).encode()
            content_type = "application/json; charset=utf-8"
        else:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format: str, *args: Any) -> None:
        print(f"[http] {format % args}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Spectralis Satellite PCM speaker")
    parser.add_argument("--source", default="127.0.0.1", help="Satellite source host")
    parser.add_argument("--satellite-port", type=int, default=41234, help="Satellite source TCP port (default: 41234)")
    parser.add_argument("--pin", help="six-digit pairing PIN; SATELLITE_PIN also works")
    parser.add_argument("--name", default="Python LAN Speaker")
    parser.add_argument("--http-host", default="0.0.0.0")
    parser.add_argument("--http-port", type=int, default=8765)
    args = parser.parse_args()

    speaker = Speaker(args)
    StatusHandler.speaker = speaker
    http = ThreadingHTTPServer((args.http_host, args.http_port), StatusHandler)
    threading.Thread(target=http.serve_forever, daemon=True).start()
    print(f"[speaker] status page: http://<this-machine>:{args.http_port}/")
    try:
        speaker.connect_forever()
    except KeyboardInterrupt:
        pass
    finally:
        speaker.stop_event.set()
        http.shutdown()


if __name__ == "__main__":
    main()
