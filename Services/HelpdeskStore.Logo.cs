using System.Buffers.Binary;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The school's own logo, shown in the header, on printed tickets and on report print-outs. Kept as a file next to the
// database (App_Data\logo.png), like the print template, rather than in a table - it is one picture, not a record.
public sealed partial class HelpdeskStore
{
    public const long MaxLogoBytes = 1024 * 1024;
    public const int MaxLogoPixels = 4000;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private string LogoPath => Path.Combine(Path.GetDirectoryName(_path)!, "logo.png");

    // Changes whenever the logo does, so it can go on the end of the logo's address and browsers fetch the new one
    // straight away instead of showing a cached copy. Null when there is no logo.
    public string? LogoVersion
    {
        get
        {
            lock (_sync) return File.Exists(LogoPath) ? File.GetLastWriteTimeUtc(LogoPath).Ticks.ToString("x") : null;
        }
    }

    public string? LogoFile
    {
        get { lock (_sync) return File.Exists(LogoPath) ? LogoPath : null; }
    }

    // PNG only, checked by content rather than by the file name: the logo is served to anyone who can reach the sign-in
    // page, so a renamed HTML or script file must never get as far as being stored.
    public (bool Ok, string Message) SaveLogo(Stream content, long length)
    {
        if (length <= 0) return (false, "Choose a PNG file to upload.");
        if (length > MaxLogoBytes) return (false, $"The logo must be {MaxLogoBytes / 1024} KB or smaller. Try exporting it at a smaller size.");

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length > MaxLogoBytes) return (false, $"The logo must be {MaxLogoBytes / 1024} KB or smaller. Try exporting it at a smaller size.");
        // Signature, then the IHDR chunk: 4-byte length, "IHDR", then width and height as big-endian integers.
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return (false, "That file isn't a PNG image. Save the logo as a .png and try again.");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width < 1 || height < 1) return (false, "That file isn't a PNG image. Save the logo as a .png and try again.");
        if (width > MaxLogoPixels || height > MaxLogoPixels)
            return (false, $"The logo is {width} × {height} pixels. Keep it to {MaxLogoPixels} pixels or fewer on each side.");

        lock (_sync)
        {
            var replacing = File.Exists(LogoPath);
            // Written beside the old one and then swapped in, so a failed write never leaves a half-saved logo.
            var temporary = LogoPath + ".uploading";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, LogoPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return (false, $"The logo couldn't be saved ({ex.Message}).");
            }
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "School logo", replacing ? "Replaced" : "Uploaded",
                $"A {width} × {height} pixel PNG logo was {(replacing ? "uploaded in place of the old one" : "uploaded")}."));
            Save();
            return (true, replacing ? "Logo replaced." : "Logo uploaded.");
        }
    }

    public (bool Ok, string Message) RemoveLogo()
    {
        lock (_sync)
        {
            if (!File.Exists(LogoPath)) return (false, "There is no logo to remove.");
            try { File.Delete(LogoPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (false, $"The logo couldn't be removed ({ex.Message})."); }
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "School logo", "Removed", "The logo was removed; the header shows the initials badge again."));
            Save();
            return (true, "Logo removed.");
        }
    }
}
