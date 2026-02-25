using Controledu.Common.Security;
using Controledu.Storage.Models;
using Controledu.Storage.Stores;
using Controledu.Student.Agent.Models;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Resolves stored student binding and decrypts token.
/// </summary>
public interface IBindingResolver
{
    /// <summary>
    /// Gets resolved binding if paired.
    /// </summary>
    Task<ResolvedStudentBinding?> GetAsync(CancellationToken cancellationToken = default);
}

internal sealed class BindingResolver(IStudentBindingStore bindingStore, ISecretProtector protector) : IBindingResolver
{
    public async Task<ResolvedStudentBinding?> GetAsync(CancellationToken cancellationToken = default)
    {
        var binding = await bindingStore.GetAsync(cancellationToken);
        if (binding is null)
        {
            return null;
        }

        var tokenBytes = Convert.FromBase64String(binding.ProtectedTokenBase64);
        var token = System.Text.Encoding.UTF8.GetString(protector.Unprotect(tokenBytes));

        return new ResolvedStudentBinding(
            binding.ServerId,
            binding.ServerName,
            binding.ServerBaseUrl,
            binding.ServerFingerprint,
            binding.ClientId,
            token);
    }
}
