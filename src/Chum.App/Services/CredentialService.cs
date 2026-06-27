using System.Net;
using AdysTech.CredentialManager;

namespace Chum.App.Services;

/// <summary>
/// Stores API keys in Windows Credential Manager (DPAPI-encrypted).
/// Keys are NEVER written to settings.json or log files.
/// </summary>
public sealed class CredentialService
{
    private const string AnthropicKey = "Chum_Anthropic_ApiKey";
    private const string OpenAiKey = "Chum_OpenAI_ApiKey";
    private const string AzureSpeechKey = "Chum_Azure_SpeechKey";
    private const string AzureSpeechRegion = "Chum_Azure_SpeechRegion";

    public void SaveAnthropicKey(string key) => Save(AnthropicKey, key);
    public void SaveOpenAiKey(string key) => Save(OpenAiKey, key);
    public void SaveAzureSpeechKey(string key, string region)
    {
        Save(AzureSpeechKey, key);
        Save(AzureSpeechRegion, region);
    }

    public string? GetAnthropicKey() => Load(AnthropicKey);
    public string? GetOpenAiKey() => Load(OpenAiKey);
    public string? GetAzureSpeechKey() => Load(AzureSpeechKey);
    public string? GetAzureSpeechRegion() => Load(AzureSpeechRegion);

    public void DeleteAnthropicKey() => Delete(AnthropicKey);
    public void DeleteOpenAiKey() => Delete(OpenAiKey);

    private static void Save(string target, string secret)
    {
        var cred = new NetworkCredential(target, secret);
        CredentialManager.SaveCredentials(target, cred);
    }

    private static string? Load(string target)
    {
        try
        {
            var cred = CredentialManager.GetCredentials(target);
            return cred?.SecurePassword?.Length > 0
                ? new System.Net.NetworkCredential(string.Empty, cred.SecurePassword).Password
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Delete(string target)
    {
        try { CredentialManager.RemoveCredentials(target); }
        catch { /* not stored — ignore */ }
    }
}
