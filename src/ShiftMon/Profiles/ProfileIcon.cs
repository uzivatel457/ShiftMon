using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ShiftMon.App;

namespace ShiftMon.Profiles;

internal static class ProfileIcon
{
    public static string Write(DisplayProfile profile)
    {
        Directory.CreateDirectory(AppPaths.IconsDirectory);
        var enabled = EnabledOf(profile);
        var stamp = Stamp(enabled);
        var path = Path.Combine(AppPaths.IconsDirectory, profile.Id + "-" + stamp + ".ico");
        var temp = path + ".tmp";
        WriteIco(temp, enabled);
        File.Move(temp, path, overwrite: true);

        if (Directory.Exists(AppPaths.IconsDirectory))
        {
            foreach (var old in Directory.EnumerateFiles(AppPaths.IconsDirectory, profile.Id + "-*.ico"))
            {
                if (!old.Equals(path, StringComparison.OrdinalIgnoreCase))
                    TryDelete(old);
            }
        }

        return path;
    }

    public static void Delete(string profileId)
    {
        if (!Directory.Exists(AppPaths.IconsDirectory))
            return;
        foreach (var old in Directory.EnumerateFiles(AppPaths.IconsDirectory, profileId + "-*.ico"))
            TryDelete(old);
    }

    static List<MonitorProfileEntry> EnabledOf(DisplayProfile profile) =>
        profile.Monitors.Where(monitor => monitor.Enabled && monitor.Width > 0 && monitor.Height > 0).ToList();

    static string Stamp(IReadOnlyList<MonitorProfileEntry> enabled)
    {
        var raw = string.Join("|", enabled
            .OrderBy(monitor => monitor.X)
            .ThenBy(monitor => monitor.Y)
            .Select(monitor => $"{monitor.X},{monitor.Y},{monitor.Width},{monitor.Height},{(monitor.Primary ? 1 : 0)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..10].ToLowerInvariant();
    }

    static void WriteIco(string path, IReadOnlyList<MonitorProfileEntry> enabled)
    {
        int[] sizes = [16, 32, 48, 256];
        var images = new List<byte[]>(sizes.Length);
        foreach (var size in sizes)
        {
            using var bitmap = Draw(enabled, size);
            images.Add(ToDib(bitmap));
        }

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            var side = sizes[i] >= 256 ? 0 : sizes[i];
            writer.Write((byte)side);
            writer.Write((byte)side);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
            writer.Write(image);
    }

    static byte[] ToDib(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var header = new byte[40];
        BitConverter.GetBytes(40).CopyTo(header, 0);
        BitConverter.GetBytes(width).CopyTo(header, 4);
        BitConverter.GetBytes(height * 2).CopyTo(header, 8);
        BitConverter.GetBytes((short)1).CopyTo(header, 12);
        BitConverter.GetBytes((short)32).CopyTo(header, 14);

        var xor = new byte[width * height * 4];
        var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bits.Stride];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(bits.Scan0 + (height - 1 - y) * bits.Stride, row, 0, bits.Stride);
                for (var x = 0; x < width; x++)
                {
                    var source = x * 4;
                    var alpha = row[source + 3];
                    var target = (y * width + x) * 4;
                    xor[target] = (byte)(row[source] * alpha / 255);
                    xor[target + 1] = (byte)(row[source + 1] * alpha / 255);
                    xor[target + 2] = (byte)(row[source + 2] * alpha / 255);
                    xor[target + 3] = alpha;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        var maskStride = ((width + 31) / 32) * 4;
        var mask = new byte[maskStride * height];
        var result = new byte[header.Length + xor.Length + mask.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(xor, 0, result, header.Length, xor.Length);
        Buffer.BlockCopy(mask, 0, result, header.Length + xor.Length, mask.Length);
        return result;
    }

    static Bitmap Draw(IReadOnlyList<MonitorProfileEntry> enabled, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        if (enabled.Count == 0)
            return bitmap;

        var minX = enabled.Min(monitor => monitor.X);
        var minY = enabled.Min(monitor => monitor.Y);
        var spanX = Math.Max(1, enabled.Max(monitor => monitor.X + monitor.Width) - minX);
        var spanY = Math.Max(1, enabled.Max(monitor => monitor.Y + monitor.Height) - minY);
        var pad = size * 0.10f;
        var scale = Math.Min((size - pad * 2) / spanX, (size - pad * 2) / spanY);
        var usedW = spanX * scale;
        var usedH = spanY * scale;
        var originX = (size - usedW) / 2f;
        var originY = (size - usedH) / 2f;
        var order = enabled.OrderBy(monitor => monitor.X).ThenBy(monitor => monitor.Y).ToList();

        for (var index = 0; index < order.Count; index++)
        {
            var monitor = order[index];
            var rect = new RectangleF(
                originX + (monitor.X - minX) * scale,
                originY + (monitor.Y - minY) * scale,
                Math.Max(1, monitor.Width * scale),
                Math.Max(1, monitor.Height * scale));
            var inset = Math.Max(0.6f, size * 0.012f);
            rect.Inflate(-inset, -inset);
            if (rect.Width < 1 || rect.Height < 1)
                continue;

            using var fill = new SolidBrush(monitor.Primary ? Color.FromArgb(0, 120, 212) : Color.FromArgb(54, 58, 66));
            using var pen = new Pen(Color.FromArgb(198, 214, 230), Math.Max(1f, size / 64f));
            g.FillRectangle(fill, rect);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            if (rect.Width < 14 || rect.Height < 14)
                continue;

            using var font = new Font("Segoe UI", Math.Max(7f, rect.Height * 0.46f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new SolidBrush(Color.White);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString((index + 1).ToString(), font, text, rect, format);
        }

        return bitmap;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* Starou ikonu drží plocha. Nový zástupce už míří na nový soubor. */ }
    }
}
