use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::io::{BufRead, BufReader, Read};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{mpsc, Arc, Mutex};
use std::time::Duration;
use thiserror::Error;

use crate::project_store::{
    claim_proxy_job, complete_proxy_job, fail_proxy_job, request_proxy_job_cancel,
    update_proxy_progress, ProjectStoreError, ProxyCompletion, ProxyJobRequest, ProxyJobStatus,
};

#[derive(Clone, Debug, PartialEq)]
pub struct ProbeMetadata {
    pub duration_seconds: f64,
    pub detected_start: String,
}

#[derive(Debug, Error)]
pub enum ProxyWorkerError {
    #[error("ffprobe output is invalid: {0}")]
    InvalidProbe(String),
    #[error("Could not launch media tool: {0}")]
    ProcessLaunch(String),
    #[error("Media tool failed: {0}")]
    ProcessFailed(String),
    #[error("Proxy job was cancelled.")]
    Cancelled,
    #[error("Proxy output filesystem failed: {0}")]
    Io(#[from] std::io::Error),
    #[error("Proxy job state failed: {0}")]
    Store(#[from] ProjectStoreError),
    #[error("Proxy job {0} is already active.")]
    AlreadyActive(String),
    #[error("Proxy job {0} is not active.")]
    NotActive(String),
    #[error("Proxy worker synchronization failed.")]
    Synchronization,
}

#[derive(Clone, Debug, PartialEq)]
pub struct ProcessOutput {
    pub success: bool,
    pub stdout: String,
    pub stderr: String,
}

#[cfg(test)]
impl ProcessOutput {
    pub fn success(stdout: &str) -> Self {
        Self {
            success: true,
            stdout: stdout.to_string(),
            stderr: String::new(),
        }
    }

    pub fn failure(stderr: &str) -> Self {
        Self {
            success: false,
            stdout: String::new(),
            stderr: stderr.to_string(),
        }
    }
}

pub trait ProcessRunner: Send + Sync + 'static {
    fn output(&self, program: &Path, args: &[String]) -> Result<ProcessOutput, ProxyWorkerError>;

    fn spawn_progress(
        &self,
        program: &Path,
        args: &[String],
        cancel: Arc<AtomicBool>,
        on_line: &mut dyn FnMut(&str),
    ) -> Result<ProcessOutput, ProxyWorkerError>;
}

#[derive(Default)]
pub struct SystemProcessRunner;

impl ProcessRunner for SystemProcessRunner {
    fn output(&self, program: &Path, args: &[String]) -> Result<ProcessOutput, ProxyWorkerError> {
        let output = Command::new(program).args(args).output().map_err(|error| {
            ProxyWorkerError::ProcessLaunch(format!("{}: {error}", program.display()))
        })?;
        Ok(ProcessOutput {
            success: output.status.success(),
            stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
            stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
        })
    }

    fn spawn_progress(
        &self,
        program: &Path,
        args: &[String],
        cancel: Arc<AtomicBool>,
        on_line: &mut dyn FnMut(&str),
    ) -> Result<ProcessOutput, ProxyWorkerError> {
        let mut child = Command::new(program)
            .args(args)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|error| {
                ProxyWorkerError::ProcessLaunch(format!("{}: {error}", program.display()))
            })?;
        let stdout = child.stdout.take().expect("piped stdout");
        let stderr = child.stderr.take().expect("piped stderr");
        let (line_sender, line_receiver) = mpsc::channel();
        let stdout_thread = std::thread::spawn(move || {
            for line in BufReader::new(stdout).lines().map_while(Result::ok) {
                if line_sender.send(line).is_err() {
                    break;
                }
            }
        });
        let stderr_thread = std::thread::spawn(move || {
            let mut reader = BufReader::new(stderr);
            let mut value = String::new();
            let _ = reader.read_to_string(&mut value);
            value
        });

        let status = loop {
            while let Ok(line) = line_receiver.try_recv() {
                on_line(&line);
            }
            if cancel.load(Ordering::SeqCst) {
                let _ = child.kill();
                let _ = child.wait();
                let _ = stdout_thread.join();
                let _ = stderr_thread.join();
                return Err(ProxyWorkerError::Cancelled);
            }
            if let Some(status) = child.try_wait()? {
                break status;
            }
            std::thread::sleep(Duration::from_millis(50));
        };
        while let Ok(line) = line_receiver.try_recv() {
            on_line(&line);
        }
        let _ = stdout_thread.join();
        let stderr = stderr_thread.join().unwrap_or_default();
        Ok(ProcessOutput {
            success: status.success(),
            stdout: String::new(),
            stderr,
        })
    }
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProxyStartResponse {
    pub job_id: String,
    pub status: String,
}

pub struct ProxyWorkerManager<R: ProcessRunner = SystemProcessRunner> {
    runner: Arc<R>,
    execution_lock: Arc<Mutex<()>>,
    cancellations: Arc<Mutex<HashMap<String, Arc<AtomicBool>>>>,
}

impl Default for ProxyWorkerManager<SystemProcessRunner> {
    fn default() -> Self {
        Self::new(Arc::new(SystemProcessRunner))
    }
}

impl<R: ProcessRunner> ProxyWorkerManager<R> {
    pub fn new(runner: Arc<R>) -> Self {
        Self {
            runner,
            execution_lock: Arc::new(Mutex::new(())),
            cancellations: Arc::new(Mutex::new(HashMap::new())),
        }
    }

    pub fn start(&self, request: ProxyJobRequest) -> Result<ProxyStartResponse, ProxyWorkerError> {
        let current = crate::project_store::read_proxy_job_status(
            &request.sqlite_path,
            &request.project_id,
            &request.job_id,
        )?;
        let cancel = Arc::new(AtomicBool::new(false));
        {
            let mut cancellations = self
                .cancellations
                .lock()
                .map_err(|_| ProxyWorkerError::Synchronization)?;
            if cancellations.contains_key(&request.job_id) {
                return Err(ProxyWorkerError::AlreadyActive(request.job_id));
            }
            cancellations.insert(request.job_id.clone(), cancel.clone());
        }

        let runner = self.runner.clone();
        let execution_lock = self.execution_lock.clone();
        let cancellations = self.cancellations.clone();
        let thread_request = request.clone();
        std::thread::spawn(move || {
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                let _execution = execution_lock
                    .lock()
                    .map_err(|_| ProxyWorkerError::Synchronization)?;
                run_proxy_job(runner.as_ref(), &thread_request, cancel)
            }));
            if result.is_err() {
                let _ = fail_proxy_job(&thread_request, "failed", "Proxy worker panicked.");
            }
            if let Ok(mut active) = cancellations.lock() {
                active.remove(&thread_request.job_id);
            }
        });

        Ok(ProxyStartResponse {
            job_id: request.job_id,
            status: current.status,
        })
    }

    pub fn cancel(&self, request: &ProxyJobRequest) -> Result<ProxyJobStatus, ProxyWorkerError> {
        let token = self
            .cancellations
            .lock()
            .map_err(|_| ProxyWorkerError::Synchronization)?
            .get(&request.job_id)
            .cloned()
            .ok_or_else(|| ProxyWorkerError::NotActive(request.job_id.clone()))?;
        token.store(true, Ordering::SeqCst);
        Ok(request_proxy_job_cancel(request)?)
    }
}

#[derive(Deserialize)]
struct ProbeDocument {
    format: ProbeFormat,
}

#[derive(Deserialize)]
struct ProbeFormat {
    duration: String,
    #[serde(default)]
    tags: ProbeTags,
}

#[derive(Default, Deserialize)]
struct ProbeTags {
    #[serde(default)]
    creation_time: String,
}

pub fn parse_probe_json(value: &str) -> Result<ProbeMetadata, ProxyWorkerError> {
    let document: ProbeDocument = serde_json::from_str(value)
        .map_err(|error| ProxyWorkerError::InvalidProbe(error.to_string()))?;
    let duration_seconds = document
        .format
        .duration
        .parse::<f64>()
        .map_err(|error| ProxyWorkerError::InvalidProbe(error.to_string()))?;
    if !duration_seconds.is_finite() || duration_seconds <= 0.0 {
        return Err(ProxyWorkerError::InvalidProbe(
            "duration must be a positive finite number".to_string(),
        ));
    }
    Ok(ProbeMetadata {
        duration_seconds,
        detected_start: document.format.tags.creation_time,
    })
}

pub fn progress_percent(line: &str, duration_seconds: f64) -> Option<f64> {
    let microseconds = line.strip_prefix("out_time_us=")?.parse::<f64>().ok()?;
    if !duration_seconds.is_finite() || duration_seconds <= 0.0 || !microseconds.is_finite() {
        return None;
    }
    Some((5.0 + (microseconds / 1_000_000.0 / duration_seconds) * 85.0).clamp(5.0, 90.0))
}

pub fn select_encoder(listing: &str) -> &'static str {
    for encoder in ["h264_nvenc", "h264_qsv", "h264_amf"] {
        if listing.split_whitespace().any(|token| token == encoder) {
            return encoder;
        }
    }
    "libx264"
}

pub fn run_proxy_job<R: ProcessRunner>(
    runner: &R,
    request: &ProxyJobRequest,
    cancel: Arc<AtomicBool>,
) -> Result<(), ProxyWorkerError> {
    let claimed = claim_proxy_job(request)?;
    let output_directory = claimed
        .project_directory
        .join("proxies")
        .join(&request.media_id);
    let partial_proxy_path = output_directory.join("review-proxy.partial.mp4");
    let final_proxy_path = output_directory.join("review-proxy.mp4");
    let temporary_thumbnails = output_directory.join("thumbnails.partial");
    let final_thumbnails = output_directory.join("thumbnails");
    let cleanup = || {
        let _ = fs::remove_file(&partial_proxy_path);
        let _ = fs::remove_dir_all(&temporary_thumbnails);
    };

    let result = (|| {
        fs::create_dir_all(&output_directory)?;
        cleanup();
        if cancel.load(Ordering::SeqCst) {
            return Err(ProxyWorkerError::Cancelled);
        }
        let ffprobe = binary_path(&request.binary_directory, "ffprobe");
        let ffmpeg = binary_path(&request.binary_directory, "ffmpeg");
        let probe = runner
            .output(&ffprobe, &probe_arguments(&claimed.source_path))
            .map_err(|error| match error {
                ProxyWorkerError::ProcessLaunch(message) => {
                    ProxyWorkerError::ProcessLaunch(format!("ffprobe unavailable: {message}"))
                }
                other => other,
            })?;
        if !probe.success {
            return Err(ProxyWorkerError::ProcessFailed(stderr_tail(&probe.stderr)));
        }
        let metadata = parse_probe_json(&probe.stdout)?;
        update_proxy_progress(request, 5.0, "source metadata ready")?;

        let encoders = runner.output(
            &ffmpeg,
            &["-hide_banner".to_string(), "-encoders".to_string()],
        )?;
        if !encoders.success {
            return Err(ProxyWorkerError::ProcessFailed(stderr_tail(
                &encoders.stderr,
            )));
        }
        let preferred_encoder = select_encoder(&encoders.stdout);
        let mut selected_encoder = preferred_encoder;
        let first_render = render_proxy(
            runner,
            request,
            &ffmpeg,
            &claimed.source_path,
            &partial_proxy_path,
            preferred_encoder,
            metadata.duration_seconds,
            cancel.clone(),
        )?;
        if !first_render.success {
            if preferred_encoder == "libx264" {
                return Err(ProxyWorkerError::ProcessFailed(stderr_tail(
                    &first_render.stderr,
                )));
            }
            if cancel.load(Ordering::SeqCst) {
                return Err(ProxyWorkerError::Cancelled);
            }
            let _ = fs::remove_file(&partial_proxy_path);
            update_proxy_progress(
                request,
                5.0,
                "hardware encoder failed; retrying with libx264",
            )?;
            let cpu_render = render_proxy(
                runner,
                request,
                &ffmpeg,
                &claimed.source_path,
                &partial_proxy_path,
                "libx264",
                metadata.duration_seconds,
                cancel.clone(),
            )?;
            if !cpu_render.success {
                return Err(ProxyWorkerError::ProcessFailed(stderr_tail(
                    &cpu_render.stderr,
                )));
            }
            selected_encoder = "libx264";
        }
        if cancel.load(Ordering::SeqCst) {
            return Err(ProxyWorkerError::Cancelled);
        }

        update_proxy_progress(request, 92.0, "generating thumbnails")?;
        fs::create_dir_all(&temporary_thumbnails)?;
        let thumbnail_pattern = temporary_thumbnails.join("%06d.jpg");
        let thumbnail_output = runner.output(
            &ffmpeg,
            &thumbnail_arguments(&claimed.source_path, &thumbnail_pattern),
        )?;
        if !thumbnail_output.success {
            return Err(ProxyWorkerError::ProcessFailed(stderr_tail(
                &thumbnail_output.stderr,
            )));
        }
        if !partial_proxy_path.is_file() {
            return Err(ProxyWorkerError::ProcessFailed(
                "FFmpeg did not create a proxy file".to_string(),
            ));
        }
        if !temporary_thumbnails.is_dir() || fs::read_dir(&temporary_thumbnails)?.next().is_none() {
            return Err(ProxyWorkerError::ProcessFailed(
                "FFmpeg did not create thumbnails".to_string(),
            ));
        }
        let _ = fs::remove_file(&final_proxy_path);
        let _ = fs::remove_dir_all(&final_thumbnails);
        fs::rename(&partial_proxy_path, &final_proxy_path)?;
        fs::rename(&temporary_thumbnails, &final_thumbnails)?;

        complete_proxy_job(
            request,
            ProxyCompletion {
                duration_seconds: metadata.duration_seconds,
                detected_start: metadata.detected_start,
                proxy_path: final_proxy_path.to_string_lossy().into_owned(),
                thumbnail_directory: final_thumbnails.to_string_lossy().into_owned(),
                video_codec: selected_encoder.to_string(),
            },
        )?;
        Ok(())
    })();

    if let Err(error) = &result {
        cleanup();
        let (status, detail) = match error {
            ProxyWorkerError::Cancelled => ("cancelled", "Proxy job cancelled.".to_string()),
            ProxyWorkerError::ProcessLaunch(message) => ("blocked", message.clone()),
            other => ("failed", stderr_tail(&other.to_string())),
        };
        let _ = fail_proxy_job(request, status, &detail);
    }
    result
}

fn render_proxy<R: ProcessRunner>(
    runner: &R,
    request: &ProxyJobRequest,
    ffmpeg: &Path,
    source_path: &Path,
    output_path: &Path,
    encoder: &str,
    duration_seconds: f64,
    cancel: Arc<AtomicBool>,
) -> Result<ProcessOutput, ProxyWorkerError> {
    let mut last_progress = 5.0;
    let mut progress_error = None;
    let result = runner.spawn_progress(
        ffmpeg,
        &render_arguments(source_path, output_path, encoder),
        cancel,
        &mut |line| {
            if let Some(progress) = progress_percent(line, duration_seconds) {
                if progress - last_progress >= 1.0 || progress >= 90.0 {
                    if let Err(error) = update_proxy_progress(
                        request,
                        progress,
                        &format!("rendering with {encoder}"),
                    ) {
                        progress_error = Some(error);
                    }
                    last_progress = progress;
                }
            }
        },
    )?;
    if let Some(error) = progress_error {
        return Err(error.into());
    }
    Ok(result)
}

fn binary_path(directory: &str, name: &str) -> PathBuf {
    let directory = directory.trim();
    if directory.is_empty() || directory.starts_with("slot:") {
        return PathBuf::from(name);
    }
    let executable = if cfg!(windows) {
        format!("{name}.exe")
    } else {
        name.to_string()
    };
    PathBuf::from(directory).join(executable)
}

fn probe_arguments(source_path: &Path) -> Vec<String> {
    vec![
        "-v".to_string(),
        "error".to_string(),
        "-show_format".to_string(),
        "-of".to_string(),
        "json".to_string(),
        source_path.to_string_lossy().into_owned(),
    ]
}

fn render_arguments(source_path: &Path, output_path: &Path, encoder: &str) -> Vec<String> {
    vec![
        "-y".to_string(),
        "-i".to_string(),
        source_path.to_string_lossy().into_owned(),
        "-vf".to_string(),
        "scale=1280:-2:force_original_aspect_ratio=decrease".to_string(),
        "-c:v".to_string(),
        encoder.to_string(),
        "-pix_fmt".to_string(),
        "yuv420p".to_string(),
        "-c:a".to_string(),
        "aac".to_string(),
        "-movflags".to_string(),
        "+faststart".to_string(),
        "-progress".to_string(),
        "pipe:1".to_string(),
        "-nostats".to_string(),
        output_path.to_string_lossy().into_owned(),
    ]
}

fn thumbnail_arguments(source_path: &Path, output_pattern: &Path) -> Vec<String> {
    vec![
        "-y".to_string(),
        "-i".to_string(),
        source_path.to_string_lossy().into_owned(),
        "-vf".to_string(),
        "fps=fps=1/5:start_time=0:eof_action=pass,scale=320:-2".to_string(),
        "-pix_fmt".to_string(),
        "yuvj420p".to_string(),
        output_pattern.to_string_lossy().into_owned(),
    ]
}

fn stderr_tail(value: &str) -> String {
    value
        .chars()
        .rev()
        .take(2_000)
        .collect::<String>()
        .chars()
        .rev()
        .collect()
}

#[cfg(test)]
mod tests {
    use super::{
        parse_probe_json, progress_percent, run_proxy_job, select_encoder, ProcessOutput,
        ProcessRunner, ProxyWorkerError, ProxyWorkerManager, SystemProcessRunner,
    };
    use crate::project_store::{
        create_project_at, import_media_at, read_proxy_job_status, MediaImportRequest,
        ProjectCreateRequest, ProxyJobRequest,
    };
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::{Arc, Mutex};
    use uuid::Uuid;

    #[test]
    fn parses_ffprobe_duration_and_creation_time() {
        let metadata = parse_probe_json(
            r#"{"format":{"duration":"12.5","tags":{"creation_time":"2026-07-10T12:00:00Z"}}}"#,
        )
        .unwrap();

        assert_eq!(metadata.duration_seconds, 12.5);
        assert_eq!(metadata.detected_start, "2026-07-10T12:00:00Z");
    }

    #[test]
    fn rejects_probe_output_without_positive_finite_duration() {
        assert!(parse_probe_json(r#"{"format":{"duration":"0"}}"#).is_err());
        assert!(parse_probe_json(r#"{"format":{"duration":"NaN"}}"#).is_err());
        assert!(parse_probe_json("not json").is_err());
    }

    #[test]
    fn maps_ffmpeg_progress_into_the_render_band() {
        assert_eq!(progress_percent("out_time_us=6250000", 12.5), Some(47.5));
        assert_eq!(progress_percent("out_time_us=12500000", 12.5), Some(90.0));
        assert_eq!(progress_percent("progress=continue", 12.5), None);
    }

    #[test]
    fn selects_hardware_encoder_priority_then_cpu() {
        let listing = " V..... h264_amf\n V..... h264_qsv\n V..... h264_nvenc\n V..... libx264";
        assert_eq!(select_encoder(listing), "h264_nvenc");
        assert_eq!(
            select_encoder(" V..... h264_qsv\n V..... libx264"),
            "h264_qsv"
        );
        assert_eq!(
            select_encoder(" V..... h264_amf\n V..... libx264"),
            "h264_amf"
        );
        assert_eq!(select_encoder(" V..... libx264"), "libx264");
    }

    #[test]
    fn retries_failed_hardware_with_cpu_and_completes_durable_outputs() {
        let fixture = ProxyFixture::new();
        let runner = FakeRunner::hardware_failure_then_cpu();
        let cancel = Arc::new(AtomicBool::new(false));

        run_proxy_job(&runner, &fixture.request, cancel).unwrap();

        let status = read_proxy_job_status(
            &fixture.request.sqlite_path,
            &fixture.request.project_id,
            &fixture.request.job_id,
        )
        .unwrap();
        assert_eq!(status.status, "complete");
        assert_eq!(status.progress, 100.0);
        assert_eq!(status.duration_seconds, 12.5);
        assert_eq!(status.detected_start, "2026-07-10T12:00:00Z");
        assert_eq!(status.video_codec, "libx264");
        assert!(Path::new(&status.proxy_path).is_file());
        assert!(Path::new(&status.thumbnail_directory)
            .join("000001.jpg")
            .is_file());
        let calls = runner.calls();
        assert!(calls.iter().any(|call| call.contains("h264_nvenc")));
        assert!(calls.iter().any(|call| call.contains("libx264")));
    }

    #[test]
    fn cancellation_stops_without_cpu_retry_and_cleans_partial_output() {
        let fixture = ProxyFixture::new();
        let runner = FakeRunner::cancelled_render();
        let cancel = Arc::new(AtomicBool::new(false));

        let error = run_proxy_job(&runner, &fixture.request, cancel.clone()).unwrap_err();

        assert!(matches!(error, ProxyWorkerError::Cancelled));
        assert!(cancel.load(Ordering::SeqCst));
        let status = read_proxy_job_status(
            &fixture.request.sqlite_path,
            &fixture.request.project_id,
            &fixture.request.job_id,
        )
        .unwrap();
        assert_eq!(status.status, "cancelled");
        assert_eq!(status.proxy_status, "blocked");
        assert!(!runner.calls().iter().any(|call| call.contains("libx264")));
        assert!(!fixture.partial_proxy_path().exists());
    }

    #[test]
    fn missing_ffprobe_marks_the_job_blocked() {
        let fixture = ProxyFixture::new();
        let runner = FakeRunner::missing_binary();

        let error =
            run_proxy_job(&runner, &fixture.request, Arc::new(AtomicBool::new(false))).unwrap_err();

        assert!(matches!(error, ProxyWorkerError::ProcessLaunch(_)));
        let status = read_proxy_job_status(
            &fixture.request.sqlite_path,
            &fixture.request.project_id,
            &fixture.request.job_id,
        )
        .unwrap();
        assert_eq!(status.status, "blocked");
        assert!(status.detail.contains("ffprobe unavailable"));
    }

    #[test]
    fn manager_returns_promptly_and_completes_the_durable_job_in_background() {
        let fixture = ProxyFixture::new();
        let runner = Arc::new(FakeRunner::hardware_failure_then_cpu());
        let manager = ProxyWorkerManager::new(runner);

        let started = manager.start(fixture.request.clone()).unwrap();

        assert_eq!(started.job_id, fixture.request.job_id);
        assert!(matches!(started.status.as_str(), "queued" | "running"));
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(2);
        loop {
            let status = read_proxy_job_status(
                &fixture.request.sqlite_path,
                &fixture.request.project_id,
                &fixture.request.job_id,
            )
            .unwrap();
            if status.status == "complete" {
                break;
            }
            assert!(
                std::time::Instant::now() < deadline,
                "worker did not complete"
            );
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
    }

    #[test]
    fn manager_cancel_signals_the_active_child_and_persists_cancelled_state() {
        let fixture = ProxyFixture::new();
        let manager = ProxyWorkerManager::new(Arc::new(FakeRunner::wait_for_cancel()));
        manager.start(fixture.request.clone()).unwrap();
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(2);
        while read_proxy_job_status(
            &fixture.request.sqlite_path,
            &fixture.request.project_id,
            &fixture.request.job_id,
        )
        .unwrap()
        .status
            != "running"
        {
            assert!(std::time::Instant::now() < deadline, "worker did not start");
            std::thread::sleep(std::time::Duration::from_millis(10));
        }

        manager.cancel(&fixture.request).unwrap();

        loop {
            let status = read_proxy_job_status(
                &fixture.request.sqlite_path,
                &fixture.request.project_id,
                &fixture.request.job_id,
            )
            .unwrap();
            if status.status == "cancelled" {
                break;
            }
            assert!(
                std::time::Instant::now() < deadline,
                "worker did not cancel"
            );
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
    }

    #[test]
    #[ignore = "requires installed ffmpeg and ffprobe binaries"]
    fn real_ffmpeg_smoke_creates_proxy_thumbnails_and_terminal_metadata() {
        let fixture = ProxyFixture::new_empty_source();
        let generated = std::process::Command::new("ffmpeg")
            .args([
                "-y",
                "-f",
                "lavfi",
                "-i",
                "color=c=blue:s=320x180:r=24",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=440:sample_rate=48000",
                "-t",
                "0.5",
                "-c:v",
                "libx264",
                "-c:a",
                "aac",
                fixture.source_path().to_str().unwrap(),
            ])
            .status()
            .unwrap();
        assert!(generated.success());
        fixture.import_source();

        run_proxy_job(
            &SystemProcessRunner,
            &fixture.request,
            Arc::new(AtomicBool::new(false)),
        )
        .unwrap();

        let status = read_proxy_job_status(
            &fixture.request.sqlite_path,
            &fixture.request.project_id,
            &fixture.request.job_id,
        )
        .unwrap();
        assert_eq!(status.status, "complete");
        assert!(status.duration_seconds > 0.0);
        assert!(!status.video_codec.is_empty());
        eprintln!("real FFmpeg smoke encoder: {}", status.video_codec);
        assert!(Path::new(&status.proxy_path).is_file());
        assert!(fs::read_dir(&status.thumbnail_directory)
            .unwrap()
            .next()
            .is_some());
    }

    struct FakeRunner {
        mode: FakeMode,
        calls: Mutex<Vec<String>>,
    }

    #[derive(Clone, Copy)]
    enum FakeMode {
        HardwareFailureThenCpu,
        Cancelled,
        MissingBinary,
        WaitForCancel,
    }

    impl FakeRunner {
        fn hardware_failure_then_cpu() -> Self {
            Self {
                mode: FakeMode::HardwareFailureThenCpu,
                calls: Mutex::new(Vec::new()),
            }
        }

        fn cancelled_render() -> Self {
            Self {
                mode: FakeMode::Cancelled,
                calls: Mutex::new(Vec::new()),
            }
        }

        fn missing_binary() -> Self {
            Self {
                mode: FakeMode::MissingBinary,
                calls: Mutex::new(Vec::new()),
            }
        }

        fn wait_for_cancel() -> Self {
            Self {
                mode: FakeMode::WaitForCancel,
                calls: Mutex::new(Vec::new()),
            }
        }

        fn calls(&self) -> Vec<String> {
            self.calls.lock().unwrap().clone()
        }

        fn record(&self, program: &Path, args: &[String]) {
            self.calls
                .lock()
                .unwrap()
                .push(format!("{} {}", program.display(), args.join(" ")));
        }
    }

    impl ProcessRunner for FakeRunner {
        fn output(
            &self,
            program: &Path,
            args: &[String],
        ) -> Result<ProcessOutput, ProxyWorkerError> {
            self.record(program, args);
            if matches!(self.mode, FakeMode::MissingBinary) {
                return Err(ProxyWorkerError::ProcessLaunch(
                    "ffprobe unavailable".to_string(),
                ));
            }
            if args.iter().any(|argument| argument == "-show_format") {
                return Ok(ProcessOutput::success(
                    r#"{"format":{"duration":"12.5","tags":{"creation_time":"2026-07-10T12:00:00Z"}}}"#,
                ));
            }
            if args.iter().any(|argument| argument == "-encoders") {
                return Ok(ProcessOutput::success(
                    " V..... h264_nvenc\n V..... libx264",
                ));
            }

            let output_pattern = PathBuf::from(args.last().unwrap());
            fs::create_dir_all(output_pattern.parent().unwrap()).unwrap();
            fs::write(output_pattern.parent().unwrap().join("000001.jpg"), b"jpeg").unwrap();
            Ok(ProcessOutput::success(""))
        }

        fn spawn_progress(
            &self,
            program: &Path,
            args: &[String],
            cancel: Arc<AtomicBool>,
            on_line: &mut dyn FnMut(&str),
        ) -> Result<ProcessOutput, ProxyWorkerError> {
            self.record(program, args);
            let output_path = PathBuf::from(args.last().unwrap());
            fs::create_dir_all(output_path.parent().unwrap()).unwrap();
            fs::write(&output_path, b"partial").unwrap();
            if matches!(self.mode, FakeMode::Cancelled) {
                cancel.store(true, Ordering::SeqCst);
                return Err(ProxyWorkerError::Cancelled);
            }
            if matches!(self.mode, FakeMode::WaitForCancel) {
                let deadline = std::time::Instant::now() + std::time::Duration::from_secs(2);
                while !cancel.load(Ordering::SeqCst) {
                    assert!(
                        std::time::Instant::now() < deadline,
                        "manager did not signal cancellation"
                    );
                    std::thread::sleep(std::time::Duration::from_millis(10));
                }
                return Err(ProxyWorkerError::Cancelled);
            }
            if matches!(self.mode, FakeMode::HardwareFailureThenCpu)
                && args.iter().any(|argument| argument == "h264_nvenc")
            {
                return Ok(ProcessOutput::failure("hardware unavailable"));
            }
            on_line("out_time_us=6250000");
            on_line("out_time_us=12500000");
            Ok(ProcessOutput::success(""))
        }
    }

    struct ProxyFixture {
        root: PathBuf,
        request: ProxyJobRequest,
    }

    impl ProxyFixture {
        fn new() -> Self {
            let fixture = Self::new_empty_source();
            fs::write(fixture.source_path(), b"source").unwrap();
            fixture.import_source();
            fixture
        }

        fn new_empty_source() -> Self {
            let root = std::env::temp_dir()
                .join(format!("roadwatcher-proxy-worker-test-{}", Uuid::new_v4()));
            fs::create_dir_all(&root).unwrap();
            let project_id = Uuid::new_v4();
            let media_id = Uuid::new_v4();
            let job_id = Uuid::new_v4();
            let created = create_project_at(
                ProjectCreateRequest {
                    project_name: "Proxy worker".to_string(),
                    root_directory: root.clone(),
                },
                project_id,
                1_788_000_000,
            )
            .unwrap();
            Self {
                root,
                request: ProxyJobRequest {
                    sqlite_path: PathBuf::from(created.sqlite_path),
                    project_id: project_id.to_string(),
                    media_id: media_id.to_string(),
                    job_id: job_id.to_string(),
                    profile: "review-proxy".to_string(),
                    binary_directory: String::new(),
                },
            }
        }

        fn source_path(&self) -> PathBuf {
            self.root.join("source.mp4")
        }

        fn import_source(&self) {
            import_media_at(
                MediaImportRequest {
                    sqlite_path: self.request.sqlite_path.clone(),
                    project_id: self.request.project_id.clone(),
                    source_path: self.source_path(),
                },
                Uuid::parse_str(&self.request.media_id).unwrap(),
                Uuid::parse_str(&self.request.job_id).unwrap(),
            )
            .unwrap();
        }

        fn partial_proxy_path(&self) -> PathBuf {
            self.request
                .sqlite_path
                .parent()
                .unwrap()
                .join("proxies")
                .join(&self.request.media_id)
                .join("review-proxy.partial.mp4")
        }
    }

    impl Drop for ProxyFixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.root);
        }
    }
}
