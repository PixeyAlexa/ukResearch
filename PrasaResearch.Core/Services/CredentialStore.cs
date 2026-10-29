using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PrasaResarch.Services;
public interface ICredentialStore { string Read(); void Write(string value); }
public sealed class WindowsCredentialStore(string root) : ICredentialStore
{
    public string Read()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path = Path.Combine(root, "credential.bin");
        return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : "";
    }
    public void Write(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "credential.bin"), ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }
}
public sealed class MacKeychainCredentialStore(string serviceName = "com.prasaresearch.desktop") : ICredentialStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private readonly byte[] Service = Encoding.UTF8.GetBytes(serviceName);
    private static readonly byte[] Account = Encoding.UTF8.GetBytes(Environment.UserName);
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint length, out IntPtr data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint length, byte[] data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);
    public string Read()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)Account.Length, Account, out var length, out var data, out var item);
        if (status == -25300) return "";
        if (status != 0) throw new InvalidOperationException($"macOS Keychain could not read the credential (status {status}).");
        try { var bytes = new byte[length]; Marshal.Copy(data, bytes, 0, bytes.Length); return Encoding.UTF8.GetString(bytes); }
        finally { SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
    }
    public void Write(string value)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var bytes = Encoding.UTF8.GetBytes(value);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)Account.Length, Account, out _, out var data, out var item);
        if (status == 0)
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            try { status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes); } finally { CFRelease(item); }
        }
        else if (status == -25300)
        { status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)Account.Length, Account, (uint)bytes.Length, bytes, out item); if (item != IntPtr.Zero) CFRelease(item); }
        if (status != 0) throw new InvalidOperationException($"macOS Keychain could not save the credential (status {status}).");
    }
    public void DeleteTestCredential()
    {
        if (!OperatingSystem.IsMacOS() || !serviceName.StartsWith("com.prasaresearch.test.")) throw new InvalidOperationException("Test cleanup is restricted to isolated test services.");
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)Account.Length, Account, out _, out var data, out var item);
        if (status == -25300) return;
        if (status != 0) throw new InvalidOperationException($"Keychain test cleanup failed: {status}");
        SecKeychainItemFreeContent(IntPtr.Zero, data);
        try { SecKeychainItemDelete(item); } finally { CFRelease(item); }
    }
}
