# Hardware acceleration

Which GPU decodes and encodes video, how Vora finds it, and what happens when it fails. Covers playback transcodes, the marker scan, scrub-bar thumbnails and DVR post-processing. The playback path itself is in [`docs/streaming.md`](streaming.md).

## The setting

`ServerSetting.HardwareAccelerationType` (`Auto`, `Nvidia`, `QuickSync`, `Vaapi`, `Off`; stored as an integer, sent as a string) picks the kind of GPU. `HardwareTranscodingDevice` picks which one: `Auto`, a CUDA index (`"0"`) for NVIDIA, or a render node (`/dev/dri/renderD128`) for Intel and AMD. The older switches still apply on top:

| Switch | Gates |
|---|---|
| `UseHardwareAcceleration` | everything below; off means no GPU at all |
| `UseHardwareEncoding` | GPU encoding of playback and DVR transcodes (decode can stay on the GPU) |
| `AnalyzeUseHardwareDecode` | GPU decode in the marker scan |
| `VideoThumbnailUseHardwareDecode` | GPU decode for scrub-bar sprites |
| `MaxGpuTranscodes` | GPU encodes at once; past it a stream decodes on the GPU and encodes on the CPU |

Every caller turns the setting into a concrete `HardwareAccelerator` (type, device, vendor, Vulkan interop) through `IHardwareCapabilityService.ResolveAsync`. **Never hard-code `-hwaccel cuda` or a device path**; build the flags with `HardwareArguments` (`InitDevices`, `Decode`, `InitAndDecode`, `DownloadScaled`).

## Finding the GPU

`GpuDeviceDiscovery` lists what the container can see, once per process:

- **NVIDIA**: `nvidia-smi --query-gpu=index,name`; failing that the `/dev/nvidiaN` nodes; failing that a single GPU `0` when `libcuda.so.1` is present (WSL2 has neither `nvidia-smi` nor `/dev/nvidia0`, only `/dev/dxg` and the driver library).
- **Intel and AMD**: each `/dev/dri/renderD*`, named by the PCI vendor in `/sys/class/drm/<node>/device/vendor` (`0x8086` Intel, `0x1002` AMD). NVIDIA render nodes are left out: VAAPI can't drive them and CUDA wants an index, not a path.

`HardwareCapabilityService` then **test-encodes a 2-second `testsrc2` clip** (`HardwareProbe`) rather than trusting what exists. A probe proves the driver loads and the container user can open the device, which is what actually fails in the field.

| Probe | Arguments |
|---|---|
| NVIDIA | `-c:v h264_nvenc [-gpu N]` |
| Quick Sync | VAAPI device, `qsv=qs@va`, `format=nv12,hwupload,hwmap=derive_device=qsv,format=qsv`, `h264_qsv` |
| VAAPI | VAAPI device, `format=nv12,hwupload`, `h264_vaapi` |
| Vulkan tone mapping (AMD only) | the transcode's `libplacebo` chain on an uploaded P010 frame |

**Auto** tries NVIDIA (only when an NVIDIA GPU was found), then Quick Sync on each Intel node, then VAAPI on each node, and keeps the first that passes. A pinned device narrows it: a CUDA index tries only NVIDIA, a render node only that node. Results are cached per probe and per device; `HardwareDetectionWorker` resolves the saved setting at startup so the first stream doesn't wait, and logs the outcome. **Test hardware** (`POST /api/settings/hardware/test`) re-runs the probe fresh and replaces the cached result, so fixing permissions and pressing Test is enough.

## Transcode pipelines

`FFmpegTranscodeService.BuildFFmpegArguments` takes a `TranscodeHardware` (the accelerator plus whether it encodes). `TranscodeArgumentTests` pins the full argument list for every pipeline.

| | Decode | Filters | Encode |
|---|---|---|---|
| NVIDIA | `-hwaccel cuda -hwaccel_output_format cuda` | `scale_cuda`, `tonemap_cuda` (jellyfin-ffmpeg) | `h264_nvenc` / `hevc_nvenc` with `-forced-idr 1` |
| Quick Sync | `-hwaccel vaapi -hwaccel_output_format vaapi` | `scale_vaapi`, `tonemap_vaapi`, then `hwmap=derive_device=qsv` | `h264_qsv` / `hevc_qsv` with `-forced_idr 1` |
| VAAPI | same as Quick Sync | `scale_vaapi`, tone mapping per vendor | `h264_vaapi` / `hevc_vaapi` |
| CPU | none | `scale`, `zscale` + `tonemap` | `libx264` / `libx265` |

Quick Sync decodes and scales through VAAPI and maps to QSV only for the encoder; that is jellyfin-ffmpeg's default Linux path and lets both Intel pipelines share one filter chain. `-forced_idr 1` (QSV) and `-forced-idr 1` (NVENC) matter: without them the encoder turns the segment-boundary keyframes into plain I-frames that aren't flagged as keyframes, so the HLS muxer can only cut at the encoder's own GOP (250 frames, about 10 s) under a playlist that promises 4 s segments. NVENC ran that way until the flag was added; players coped by following the timestamps, but the playlist's timing was wrong. VAAPI and x264 turn a forced keyframe into an IDR frame on their own.

**HDR tone mapping** depends on the GPU:

- **NVIDIA**: `tonemap_cuda`, or a download and CPU `zscale` chain on a stock FFmpeg.
- **Intel**: `tonemap_vaapi` on the GPU.
- **AMD**: `tonemap_vaapi` is an Intel driver feature. AMD maps the frame to Vulkan and tone-maps with `libplacebo`, then maps back to VAAPI. The startup probe checks that this works on the card (`HardwareAccelerator.HasVulkanInterop`); if not, the frame is downloaded, tone-mapped on the CPU and uploaded again, still decoding and encoding on the GPU.

`HdrTonemapQuality` / `HdrTranscodeDownscale` on Auto follow where the tone map runs: on the GPU they mean Quality and no downscale; on the CPU, Fast and a 1080p downscale.

**What gets tone-mapped** is decided by the video track's `HdrType`, which `HdrClassifier` (`Vora.Infrastructure/Analysis/`) sets when a file is scanned. It looks at the stream's transfer and, for Dolby Vision, at the configuration record's base layer (`dv_bl_signal_compatibility_id`):

| Stream | `HdrType` |
|---|---|
| PQ (`smpte2084`) | `HDR10`, or `DoVi/HDR10` with Dolby Vision (profile 8.1, 7) |
| HLG (`arib-std-b67`) | `HLG`, or `DoVi/HLG` with Dolby Vision (profile 8.4, e.g. iPhone video) |
| Tagged SDR (`bt709`, `smpte170m`, …) with a Dolby Vision base layer | none: the picture is SDR, nothing to tone-map |
| Dolby Vision profile 5 (compatibility 0), or untagged | `DoVi` |

The SDR row is issue #145. A YTS re-encode kept the Dolby Vision record but not the Dolby Vision data, and its picture is tagged `bt709`. Labelled `DoVi`, it was sent to `tonemap_cuda`, which refused it ("No DOVI metadata and unsupported input transfer characteristic: bt709"), and no frame came out. Because a file is only re-read when it changes, a source still labelled plain `DoVi` is probed again (`HdrClassifier.ProbeAsync`) when a transcode starts, and that run uses what the probe finds. A `DoVi/HLG` file used to be labelled `DoVi`, so screens that show HLG but not Dolby Vision transcoded it for nothing.

**Burned-in subtitles** keep decode and encode on the GPU but draw on the CPU: the decoder hands frames to system memory, a `-filter_complex` graph scales, tone-maps and overlays the subtitle (`docs/subtitles.md`), and VAAPI gets `format=nv12,hwupload` back while QSV takes the CPU frames directly.

A **remux** (`-c:v copy`) never touches the GPU.

## When the GPU fails

A GPU transcode that exits with an error now restarts on the CPU instead of leaving the session waiting for a segment that never comes. In `OnFFmpegExited`, a non-zero exit of the session's current launch (not one Vora killed for a seek or a stop) clears `TranscodeContext.Hardware` and sets `RetryingOnCpu` before the process leaves `_activeTranscodes`, so segment waiters keep waiting. `RetryOnCpuAsync` then relaunches on the CPU from the first segment not yet on disk. The session stays on the CPU for later seeks. A CPU failure is not retried.

Every launch gets an id from a process-wide counter, and the exit handler removes only its own process from `_activeTranscodes` (`TryRemove` by key and value). A killed process's late `Exited` event therefore can't remove the launch that replaced it.

The marker scan, thumbnails and DVR post-processing already retried a failed hardware pass on the CPU; they now decode on the resolved GPU instead of always CUDA (thumbnails used `-hwaccel auto`), and the DVR worker uses the resolved encoder instead of the first one FFmpeg was built with (it picked NVENC on Intel machines) and retries on the CPU too.

## The container

jellyfin-ffmpeg ships the Intel (`iHD`, `i965`) and AMD (`radeonsi`) VA-API drivers, Intel's oneVPL runtime, and Mesa's Vulkan drivers, so the image needs nothing extra. The host passes the render nodes in (`--device=/dev/dri`). `entrypoint.sh` adds `vora` to the group that owns each render node (creating `vora-gpu-<gid>` when the GID has no name), runs the app with `gosu vora` so those supplementary groups apply (`gosu vora:vora` drops them), and warns at startup about a node `vora` still can't open, such as one owned by root's group.

## Admin UI

System Settings → Core → Transcoding and the Setup Guide's Playback step share `HardwareAccelerationPicker`: a type select whose Auto option says what was detected (`GET /api/settings/hardware`), a device select filtered to what the chosen type can drive, each GPU named by maker, and **Test hardware**, which tests the unsaved choice. The Setup Guide leaves Off out because its switch covers it.

## Testing on real hardware

The argument tests run in CI, but Quick Sync, VAAPI and the AMD Vulkan path only run on a real Intel or AMD GPU. Before relying on a change here, run **Test hardware** and play an HDR file on such a machine, and read the `Hardware test:` and `FFmpeg transcode launched` log lines.
