use serde::Serialize;
use uuid::Uuid;

mod project_store;

#[derive(Serialize)]
struct ProjectSlot {
    id: String,
    label: &'static str,
    status: &'static str,
    detail: &'static str,
}

#[tauri::command]
fn create_project_slot_manifest() -> Vec<ProjectSlot> {
    vec![
        ProjectSlot {
            id: Uuid::new_v4().to_string(),
            label: "SQLite project database",
            status: "planned",
            detail: "Rust command will create project.sqlite in the chosen project folder.",
        },
        ProjectSlot {
            id: Uuid::new_v4().to_string(),
            label: "Native FFmpeg render jobs",
            status: "planned",
            detail: "Rust job queue will probe GPU encoders and fall back to CPU.",
        },
        ProjectSlot {
            id: Uuid::new_v4().to_string(),
            label: "Python GPStitch/CV sidecars",
            status: "blocked",
            detail: "Needs vendored GPStitch fork and BYO CV model slots filled.",
        },
    ]
}

pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![create_project_slot_manifest])
        .run(tauri::generate_context!())
        .expect("error while running RoadWatcher Tauri app");
}
