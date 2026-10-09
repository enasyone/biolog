using Biolog.Features.OcrEngine.Model;
using Xunit;

namespace Biolog.Features.OcrEngine.Tests;

/// <summary>Developer tests for BIO-DOC-004-AC01's "configured" template registry.</summary>
public sealed class OcrTemplateRegistryTests
{
    [Fact]
    public void Register_then_Get_round_trips_a_template()
    {
        var registry = new OcrTemplateRegistry();
        var template = new OcrDocumentTemplate("national-id-v1", [
            new OcrTemplateField("surname", OcrFieldType.Surname, 0, 0, 8, Critical: true),
        ]);

        registry.Register(template);

        Assert.Same(template, registry.Get("national-id-v1"));
    }

    [Fact]
    public void Get_returns_null_for_an_unregistered_template()
    {
        var registry = new OcrTemplateRegistry();
        Assert.Null(registry.Get("does-not-exist"));
    }

    [Fact]
    public void Register_replaces_a_prior_template_with_the_same_id()
    {
        var registry = new OcrTemplateRegistry();
        var v1 = new OcrDocumentTemplate("id", []);
        var v2 = new OcrDocumentTemplate("id", [new OcrTemplateField("f", OcrFieldType.FreeText, 0, 0, 1, false)]);

        registry.Register(v1);
        registry.Register(v2);

        Assert.Same(v2, registry.Get("id"));
    }
}
