using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrismCast.Security;

/// <summary>
/// Small Windows-only secret store backed by DPAPI CurrentUser. The encrypted
/// file is useless to a different Windows account and never contains plaintext
/// service tokens on disk.
/// </summary>
internal sealed class ProtectedSecretStore
{
    internal const string PlexServerTokenKey = "plex.server-token";
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string>? _cache;

    internal ProtectedSecretStore(string configDirectory)
    {
        _path = Path.Combine(configDirectory, "PrismCastSecrets.dat");
    }

    internal string Get(string key)
    {
        lock (_gate)
            return Load().TryGetValue(key, out var value) ? value : "";
    }

    internal void Set(string key, string value)
    {
        lock (_gate)
        {
            var values = Load();
            if (string.IsNullOrWhiteSpace(value))
                values.Remove(key);
            else
                values[key] = value;
            Save(values);
        }
    }

    internal void Delete(string key) => Set(key, "");

    private Dictionary<string, string> Load()
    {
        if (_cache is not null)
            return _cache;

        _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(_path))
            return _cache;

        try
        {
            var clear = Unprotect(File.ReadAllBytes(_path));
            _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(clear)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
            CryptographicOperations.ZeroMemory(clear);
        }
        catch
        {
            // A damaged or copied DPAPI file must fail closed. Callers see a
            // disconnected service and can authorize it again.
            _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return _cache;
    }

    private void Save(Dictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            if (File.Exists(_path))
                File.Delete(_path);
            return;
        }

        var clear = JsonSerializer.SerializeToUtf8Bytes(values);
        try
        {
            var encrypted = Protect(clear);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, _path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    private static byte[] Protect(byte[] clear) => Crypt(true, clear);
    private static byte[] Unprotect(byte[] encrypted) => Crypt(false, encrypted);

    private static byte[] Crypt(bool protect, byte[] input)
    {
        var inputBlob = new DataBlob();
        var outputBlob = new DataBlob();
        try
        {
            inputBlob.Data = Marshal.AllocHGlobal(input.Length);
            inputBlob.Size = input.Length;
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);

            var ok = protect
                ? CryptProtectData(ref inputBlob, "PrismCast local secrets", IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outputBlob);
            if (!ok)
                throw new InvalidOperationException("Windows could not protect PrismCast's local secret store.");

            var result = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, result, 0, outputBlob.Size);
            return result;
        }
        finally
        {
            if (inputBlob.Data != IntPtr.Zero)
            {
                for (var i = 0; i < inputBlob.Size; i++)
                    Marshal.WriteByte(inputBlob.Data, i, 0);
                Marshal.FreeHGlobal(inputBlob.Data);
            }
            if (outputBlob.Data != IntPtr.Zero)
                LocalFree(outputBlob.Data);
        }
    }

    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description,
        IntPtr optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description,
        IntPtr optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
