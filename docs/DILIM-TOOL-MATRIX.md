# Dilim tool delivery

All 26 reference tool entries now dispatch real operations. Availability still depends on the input format and
engine route. This table defines the delivered operation, including its limits.

| Tool | Implementation / output |
|---|---|
| Compress | Image → WEBP quality 70; PDF 150 dpi compression; media smaller encoding |
| Remove metadata | Image EXIF/XMP/ICC, PDF document info, media tags; no visible content redaction |
| Edit photo | Rotate, mirror, resize, brightness/contrast/saturation → PNG |
| Annotate photo | Positioned text and colour baked into PNG |
| Add background | Solid colour behind transparent pixels → PNG |
| Crop image | Existing interactive crop overlay and real conversion |
| Normalize volume | FFmpeg loudnorm, -16 LUFS / -1.5 dBTP |
| Remove audio | Silent MP4 copy |
| Trim video | Start/end seconds → MP4 |
| Split PDF | One PDF per page through existing queue |
| Organize PDF | Reorder/delete/repeat pages by ordered range list |
| Extract archive | Existing archive route; bundled 7-Zip |
| Create PDF | Existing image/document conversion routes |
| Merge PDFs | Ordered input list, native PDF merge |
| Crop video | Source-pixel rectangle → MP4 |
| Change video speed | 0.25–4× video and matching audio timing |
| Join videos | Reordered inputs normalized to 1280×720 MP4; all clips with audio or all silent |
| Video snapshot | One PNG at the entered second |
| Split video | Entered segment duration; final segment may be shorter |
| Audio visualizer | 1280×720 orange waveform MP4 with source audio |
| Trim audio | Start/end seconds → M4A |
| Audio channels | Mono/stereo → M4A |
| Create collage | Reordered photo inputs, 1–8 columns, 640×480 white cells → PNG |
| Redact photo | Opaque black pixel rectangle, metadata stripped → PNG |
| Redact video | Fixed opaque rectangle during entered interval → MP4; no tracking |
| Bleep audio | Original sound gated out and 1 kHz bleep in entered interval → M4A |

## File actions

| Action | Behavior |
|---|---|
| Share nearby | Windows native share sheet via desktop DataTransferManager interop; user chooses recipient; folders must be zipped |
| Float | Always-on-top shelf with open, drag-out and remove-reference; current session only |
| Copy | Native file clipboard, ready for Explorer paste |
| Move | Folder picker; preflight rejects conflicting names/self destinations; originals relocate |
| Copy path | Full paths as newline-separated clipboard text |
| Terminal | Windows Terminal at first folder/containing folder |

The operation editors use explicit fields rather than timelines/layers. Sources stay unchanged for edits;
Move relocates originals. Completed parts of a cancelled batch remain completed. Missing engines/routes report a
reason instead of a simulated success. The catalogue and global pie dispatch the same presets/tools.
