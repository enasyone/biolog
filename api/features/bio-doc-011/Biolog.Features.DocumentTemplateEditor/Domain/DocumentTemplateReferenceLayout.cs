namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>
/// AC01's "upload reference layout": the canvas a tenant draws named field/graphic regions
/// against. No image-decoding library exists anywhere in this repo (BIO-DOC-004-OCR-ENGINE.md
/// "Why no OCR library") - this is the same boundary BIO-CRD-008 draws for signature assets
/// (referenced by hash/id, never stored as an inline blob): <see cref="ContentHash"/> is a
/// SHA-256 of the uploaded bytes, computed once at upload time
/// (<see cref="DocumentTemplateDraftService"/>), never re-derivable from this record alone.
/// <see cref="Width"/>/<see cref="Height"/> are the pixel canvas every region's <c>Left</c>/
/// <c>Top</c> is drawn against.
/// </summary>
public sealed record DocumentTemplateReferenceLayout(int Width, int Height, string ContentHash, string UploadedAt);
