using System.Xml;
using System.Xml.Linq;

namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// SVG structure and active-content validation (BIO-CRD-001-AC04), per Card Designer v0.3's
/// adversarial case "invalid SVG or active-content upload". SVG is XML that a browser or
/// design tool can execute script inside, so "is this a well-formed image" and "does this
/// try to run code" are both content-validation questions this module answers before a
/// tenant's brand asset is ever rendered anywhere else in the platform.
/// </summary>
public static class SvgValidator
{
    public static ContentValidationResult Validate(ReadOnlySpan<byte> content)
    {
        string text;
        try
        {
            text = System.Text.Encoding.UTF8.GetString(content);
        }
        catch (Exception)
        {
            return new ContentValidationResult(false, ["file is not valid UTF-8 text"]);
        }

        XDocument document;
        try
        {
            // DtdProcessing.Prohibit rejects any DOCTYPE outright - including a benign one -
            // which is the correct fail-closed posture here: an SVG brand asset has no
            // legitimate reason to declare a DTD, and DOCTYPE is exactly the vector an XXE
            // (external entity) attack uses. XmlResolver = null additionally refuses to
            // dereference any external reference this parser is ever asked to resolve.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var stringReader = new StringReader(text);
            using var xmlReader = XmlReader.Create(stringReader, settings);
            document = XDocument.Load(xmlReader, LoadOptions.None);
        }
        catch (XmlException error)
        {
            return new ContentValidationResult(false, [$"malformed XML: {error.Message}"]);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "svg")
        {
            return new ContentValidationResult(false, ["root element is not <svg>"]);
        }

        var findings = new List<string>();
        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("active content: <script> element is not permitted");
            }
            if (element.Name.LocalName.Equals("foreignObject", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("active content: <foreignObject> element is not permitted");
            }
            foreach (var attribute in element.Attributes())
            {
                var localName = attribute.Name.LocalName;
                if (localName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add($"active content: event-handler attribute '{localName}' is not permitted");
                }
                if (attribute.Value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add($"active content: 'javascript:' URI in '{localName}' is not permitted");
                }
            }
        }

        return findings.Count == 0
            ? new ContentValidationResult(true, [])
            : new ContentValidationResult(false, findings);
    }
}
