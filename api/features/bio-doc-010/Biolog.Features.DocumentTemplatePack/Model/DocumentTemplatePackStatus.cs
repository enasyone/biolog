namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>A pack's own lifecycle (AC02) - deliberately just two states, not the fuller
/// draft/test/approval/publish workflow BIO-DOC-011 (tenant template editor) adds later: AC02 only
/// requires immutability once published, not a multi-step approval pipeline.</summary>
public enum DocumentTemplatePackStatus
{
    Draft,
    Published,
}
