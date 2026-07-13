use std::io::Read;
use std::process::{Command, ExitStatus, Stdio};
use std::thread;
use std::time::{Duration, Instant};
use thiserror::Error;

#[derive(Debug)]
pub struct BoundedProcessOutput {
    pub status: ExitStatus,
    pub stdout: Vec<u8>,
    pub stderr: Vec<u8>,
}

#[derive(Debug, Error)]
pub enum BoundedProcessError {
    #[error("could not launch or monitor process: {0}")]
    Io(#[from] std::io::Error),
    #[error("process exceeded its {0:?} execution limit")]
    Timeout(Duration),
    #[error("process {stream} exceeded {limit} bytes")]
    OutputLimit { stream: &'static str, limit: u64 },
    #[error("process {0} reader thread failed")]
    Reader(&'static str),
    #[error("process was cancelled")]
    Cancelled,
}

pub fn run_bounded_process(
    command: &mut Command,
    timeout: Duration,
    output_limit: u64,
) -> Result<BoundedProcessOutput, BoundedProcessError> {
    run_bounded_process_cancellable(command, timeout, output_limit, || false)
}

pub fn run_bounded_process_cancellable(
    command: &mut Command,
    timeout: Duration,
    output_limit: u64,
    should_cancel: impl Fn() -> bool,
) -> Result<BoundedProcessOutput, BoundedProcessError> {
    let mut child = command
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()?;
    let stdout = child
        .stdout
        .take()
        .expect("bounded process stdout is piped");
    let stderr = child
        .stderr
        .take()
        .expect("bounded process stderr is piped");
    let stdout_reader = thread::spawn(move || read_bounded(stdout, "stdout", output_limit));
    let stderr_reader = thread::spawn(move || read_bounded(stderr, "stderr", output_limit));
    let started = Instant::now();
    let status = loop {
        match child.try_wait() {
            Ok(Some(status)) => break Ok(status),
            Ok(None) if should_cancel() => break Err(BoundedProcessError::Cancelled),
            Ok(None) if started.elapsed() < timeout => thread::sleep(Duration::from_millis(25)),
            Ok(None) => break Err(BoundedProcessError::Timeout(timeout)),
            Err(error) => break Err(BoundedProcessError::Io(error)),
        }
    };
    if status.is_err() {
        let _ = child.kill();
        let _ = child.wait();
    }
    let stdout = join_reader(stdout_reader, "stdout")?;
    let stderr = join_reader(stderr_reader, "stderr")?;
    Ok(BoundedProcessOutput {
        status: status?,
        stdout,
        stderr,
    })
}

fn join_reader(
    reader: thread::JoinHandle<Result<Vec<u8>, BoundedProcessError>>,
    stream: &'static str,
) -> Result<Vec<u8>, BoundedProcessError> {
    reader
        .join()
        .map_err(|_| BoundedProcessError::Reader(stream))?
}

fn read_bounded(
    reader: impl Read,
    stream: &'static str,
    limit: u64,
) -> Result<Vec<u8>, BoundedProcessError> {
    let mut bytes = Vec::new();
    reader.take(limit + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 > limit {
        return Err(BoundedProcessError::OutputLimit { stream, limit });
    }
    Ok(bytes)
}

#[cfg(test)]
mod tests {
    use super::{run_bounded_process, run_bounded_process_cancellable, BoundedProcessError};
    use std::process::Command;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::time::Duration;

    #[test]
    fn captures_test_harness_output_with_a_bounded_process() {
        let mut command = Command::new(std::env::current_exe().unwrap());
        command.arg("--help");
        let output =
            run_bounded_process(&mut command, Duration::from_secs(10), 256 * 1024).unwrap();
        assert!(output.status.success());
        assert!(!output.stdout.is_empty());
        assert!(output.stderr.len() < 256 * 1024);
    }

    #[test]
    fn cancellation_stops_the_exact_spawned_process() {
        let mut command = Command::new(std::env::current_exe().unwrap());
        command
            .arg("--exact")
            .arg("bounded_process::tests::cancellable_child_fixture")
            .arg("--nocapture")
            .env("ROADWATCHER_BOUNDED_CHILD_SLEEP", "1");
        let checks = AtomicUsize::new(0);
        let error = run_bounded_process_cancellable(
            &mut command,
            Duration::from_secs(10),
            256 * 1024,
            || checks.fetch_add(1, Ordering::SeqCst) > 1,
        )
        .unwrap_err();
        assert!(matches!(error, BoundedProcessError::Cancelled));
    }

    #[test]
    fn cancellable_child_fixture() {
        if std::env::var_os("ROADWATCHER_BOUNDED_CHILD_SLEEP").is_some() {
            std::thread::sleep(Duration::from_secs(30));
        }
    }
}
