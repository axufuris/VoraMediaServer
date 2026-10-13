using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Vora.Plugins.Providers.Local;

public sealed class LocalMediaFolders(ILogger logger)
{
    private const int MaxDocumentBytes = 4 * 1024 * 1024;
    private const int SniffBytes = 512;

    private readonly Dictionary<string, string[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<XElement>> _documents = new(StringComparer.Ordinal);

    public int Listings { get; private set; }

    public string[] Files(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return [];
        if (_files.TryGetValue(directory, out var known)) return known;

        string[] files;
        try
        {
            Listings++;
            files = Directory.Exists(directory)
                ? Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read local assets in {Directory}.", directory);
            files = [];
        }

        _files[directory] = files;
        return files;
    }

    public IReadOnlyList<XElement> Document(string path)
    {
        if (_documents.TryGetValue(path, out var known)) return known;

        var roots = Read(path);
        _documents[path] = roots;
        return roots;
    }

    private IReadOnlyList<XElement> Read(string path)
    {
        var roots = new List<XElement>();
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaxDocumentBytes || !LooksLikeXml(stream))
            {
                logger.LogDebug("Skipping {NfoPath}: it is not an XML metadata file.", path);
                return roots;
            }

            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                ConformanceLevel = ConformanceLevel.Fragment,
                MaxCharactersInDocument = MaxDocumentBytes
            });

            reader.MoveToContent();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    roots.Add((XElement)XNode.ReadFrom(reader));
                }
                else
                {
                    reader.Read();
                }
            }
        }
        catch (XmlException ex)
        {
            if (roots.Count == 0)
            {
                logger.LogWarning(ex, "Could not import local metadata from {NfoPath}.", path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read local metadata from {NfoPath}.", path);
        }

        return roots;
    }

    private static bool LooksLikeXml(Stream stream)
    {
        var buffer = new byte[SniffBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        stream.Position = 0;
        if (read >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF))) return true;

        var text = Encoding.UTF8.GetString(buffer, 0, read).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith('<');
    }
}
