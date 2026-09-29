using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GfmStudioOS.SecureCredentialHelper;

public interface ICredentialProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

/// <summary>Windows CurrentUser DPAPI adapter. It deliberately fails closed on non-Windows hosts.</summary>
public sealed class DpapiCurrentUserProtector : ICredentialProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GFM StudioOS Secure Credential Helper v1");

    public byte[] Protect(ReadOnlySpan<byte> plaintext) => Transform(plaintext, protect: true);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData) => Transform(protectedData, protect: false);

    private static byte[] Transform(ReadOnlySpan<byte> input, bool protect)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Credential protection requires Windows DPAPI CurrentUser.");
        if (input.IsEmpty)
            throw new ArgumentException("Credential payload must not be empty.", nameof(input));

        var inputBytes = input.ToArray();
        var inputBlob = AllocateBlob(inputBytes);
        var entropyBlob = AllocateBlob(Entropy);
        DataBlob output = default;
        IntPtr description = IntPtr.Zero;
        try
        {
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref inputBlob, out description, ref entropyBlob, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output);

            if (!succeeded)
                throw new CryptographicException(Marshal.GetLastWin32Error());

            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            ZeroAndFree(inputBlob);
            ZeroAndFree(entropyBlob);
            ZeroAndLocalFree(output);
            if (description != IntPtr.Zero)
                _ = LocalFree(description);
            CryptographicOperations.ZeroMemory(inputBytes);
        }
    }

    private static DataBlob AllocateBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = pointer };
    }

    private static void ZeroAndFree(DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
            return;
        var zeroes = new byte[blob.Length];
        Marshal.Copy(zeroes, 0, blob.Data, zeroes.Length);
        Marshal.FreeHGlobal(blob.Data);
    }

    private static void ZeroAndLocalFree(DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
            return;
        var zeroes = new byte[blob.Length];
        Marshal.Copy(zeroes, 0, blob.Data, zeroes.Length);
        _ = LocalFree(blob.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        out IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
